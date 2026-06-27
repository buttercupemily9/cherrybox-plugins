using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Media;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryCovers.Plugin;

internal sealed class StoryCoverExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StoryCoverSettingsStore _settings;
    private readonly IPluginServiceRegistry _plugins;
    private readonly ILogger<StoryCoverExecutor> _logger;

    public StoryCoverExecutor(
        IServiceScopeFactory scopeFactory,
        StoryCoverSettingsStore settings,
        IPluginServiceRegistry plugins,
        ILogger<StoryCoverExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _plugins = plugins;
        _logger = logger;
    }

    public async Task<StoryCoverJobDto> ExecuteAsync(StoryCoverJobDto job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ai = _plugins.Resolve<IAiService>(scope.ServiceProvider);
        if (ai is null)
            return Fail(job, "AI plugin is not loaded. Install and enable the AI plugin.");

        var images = _plugins.Resolve<IAiImageService>(scope.ServiceProvider);
        if (images is null)
            return Fail(job, "Story cover art requires AI plugin 1.2.0 or later with image generation support.");

        AiSettingsDto aiSettings;
        try
        {
            aiSettings = await ai.GetSettingsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(job, ex.Message);
        }

        if (!aiSettings.HasApiKey)
            return Fail(job, "Venice API key is not configured. Open Settings → AI.");

        var pluginSettings = _settings.Get();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IMediaBlobService>();

        var story = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == job.StoryMediaItemId && m.MediaType == MediaType.Story, cancellationToken);
        if (story is null)
            return Fail(job, "Story media item was not found.");

        if (pluginSettings.SkipWhenCoverExists
            && await blobs.ExistsAsync(story.Id, MediaBlobKind.PrimaryImage, cancellationToken))
        {
            await SyncCoverToLinkedAudioAsync(db, blobs, story.Id, cancellationToken);
            return Complete(job, "Cover already exists; synced to linked audio if needed.");
        }

        string plainText;
        try
        {
            plainText = await StoryCoverTextExtractor.ExtractPlainTextAsync(db, story.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(job, ex.Message);
        }

        var excerpt = StoryCoverTextExtractor.TruncateForContext(
            plainText,
            Math.Clamp(pluginSettings.ContextCharLimit, 500, 8000));

        var title = string.IsNullOrWhiteSpace(story.Title) ? story.FileName : story.Title!;
        string imagePrompt;
        try
        {
            imagePrompt = await StoryCoverPromptBuilder.BuildImagePromptAsync(
                ai,
                title,
                story.Author,
                excerpt,
                pluginSettings.UseChatPromptRefinement,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(job, ex.Message);
        }

        AiImageResult image;
        try
        {
            image = await images.GenerateImageAsync(
                new AiImageRequest(
                    imagePrompt,
                    Width: Math.Clamp(pluginSettings.ImageWidth, 256, 2048),
                    Height: Math.Clamp(pluginSettings.ImageHeight, 256, 2048),
                    Format: "webp"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(job, ex.Message);
        }

        try
        {
            await blobs.UpsertAsync(story.Id, MediaBlobKind.PrimaryImage, image.MimeType, image.Data, cancellationToken);
            await SyncCoverToLinkedAudioAsync(db, blobs, story.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(job, ex.Message);
        }

        _logger.LogInformation("Generated story cover for {StoryId} ({Title})", story.Id, title);
        return Complete(job, null);
    }

    public async Task SyncCoverFromLinkedStoryAsync(Guid audioMediaItemId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IMediaBlobService>();

        var audio = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == audioMediaItemId && m.MediaType == MediaType.Audio, cancellationToken);
        if (audio is null)
            return;

        if (await blobs.ExistsAsync(audio.Id, MediaBlobKind.PrimaryImage, cancellationToken))
            return;

        var link = await db.StoryLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.AudioMediaItemId == audioMediaItemId, cancellationToken);
        if (link is null)
            return;

        await CopyStoryCoverToAudioAsync(blobs, link.StoryMediaItemId, audio.Id, cancellationToken);
    }

    private async Task SyncCoverToLinkedAudioAsync(
        CherryBoxDbContext db,
        IMediaBlobService blobs,
        Guid storyMediaItemId,
        CancellationToken cancellationToken)
    {
        var audioIds = await db.StoryLinks.AsNoTracking()
            .Where(l => l.StoryMediaItemId == storyMediaItemId)
            .Select(l => l.AudioMediaItemId)
            .ToListAsync(cancellationToken);

        foreach (var audioId in audioIds)
            await CopyStoryCoverToAudioAsync(blobs, storyMediaItemId, audioId, cancellationToken);
    }

    private static async Task CopyStoryCoverToAudioAsync(
        IMediaBlobService blobs,
        Guid storyMediaItemId,
        Guid audioMediaItemId,
        CancellationToken cancellationToken)
    {
        var cover = await blobs.GetAsync(storyMediaItemId, MediaBlobKind.PrimaryImage, cancellationToken);
        if (cover is null)
            return;

        await blobs.UpsertAsync(
            audioMediaItemId,
            MediaBlobKind.PrimaryImage,
            cover.MimeType,
            cover.Data,
            cancellationToken);
    }

    private static StoryCoverJobDto Complete(StoryCoverJobDto job, string? message) =>
        job with
        {
            Status = StoryCoverJobStatus.Completed,
            ErrorMessage = message,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private StoryCoverJobDto Fail(StoryCoverJobDto job, string message)
    {
        _logger.LogWarning("Story cover job {JobId} failed: {Message}", job.Id, message);
        return job with
        {
            Status = StoryCoverJobStatus.Failed,
            ErrorMessage = message,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
