using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Media;
using CherryBox.Plugins.Abstractions;
using CherryBox.Stories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryTts.Plugin;

internal sealed class StoryTtsExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StoryTtsSettingsStore _settings;
    private readonly VeniceTtsClient _venice;
    private readonly ILogger<StoryTtsExecutor> _logger;

    public StoryTtsExecutor(
        IServiceScopeFactory scopeFactory,
        StoryTtsSettingsStore settings,
        VeniceTtsClient venice,
        ILogger<StoryTtsExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _venice = venice;
        _logger = logger;
    }

    public async Task<StoryTtsJobDto> ExecuteAsync(StoryTtsJobDto job, CancellationToken cancellationToken)
    {
        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return Fail(job, "Venice API key is not configured.");
        if (settings.AudioLibraryFolderId is null)
            return Fail(job, "Select an audio library folder in Settings → AI.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var folder = await db.LibraryFolders.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == settings.AudioLibraryFolderId.Value, cancellationToken);
        if (folder is null)
            return Fail(job, "Configured audio library folder was not found.");
        if (folder.ContentKind is not (ContentKind.Audio or ContentKind.Mix))
            return Fail(job, "Configured library folder must be Audio or Mix content kind.");

        var story = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == job.StoryMediaItemId && m.MediaType == MediaType.Story, cancellationToken);
        if (story is null)
            return Fail(job, "Story media item was not found.");

        string plainText;
        try
        {
            plainText = await StoryTextExtractor.ExtractPlainTextAsync(db, job.StoryMediaItemId, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(job, ex.Message);
        }

        var chunks = StoryTextExtractor.ChunkText(plainText, settings.MaxCharsPerRequest);
        job = job with
        {
            ChunksTotal = chunks.Count,
            ChunksCompleted = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var audioParts = new List<byte[]>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var audio = await _venice.SynthesizeAsync(settings.ApiKey!, chunks[i], settings, cancellationToken);
            audioParts.Add(audio);
            job = job with
            {
                ChunksCompleted = i + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        Directory.CreateDirectory(folder.Path);
        var extension = NormalizeExtension(settings.ResponseFormat);
        var outputFileName = BuildOutputFileName(story, extension);
        var outputPath = GetUniqueOutputPath(folder.Path, outputFileName);
        await File.WriteAllBytesAsync(outputPath, ConcatenateAudio(audioParts), cancellationToken);

        job = job with
        {
            OutputPath = outputPath,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();
        await scanner.ScanFolderAsync(folder.Id, cancellationToken);

        var audioItem = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.FilePath == outputPath && m.MediaType == MediaType.Audio, cancellationToken);
        if (audioItem is null)
            return Fail(job, "Audio file was created but library scan did not index it.");

        job = job with
        {
            AudioMediaItemId = audioItem.Id,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (settings.AutoLinkOnComplete)
        {
            var stories = scope.ServiceProvider.GetRequiredService<IStoryService>();
            var linked = await stories.LinkAudioAsync(
                job.StoryMediaItemId,
                new LinkAudioRequest(audioItem.Id),
                cancellationToken);
            if (!linked)
                return Fail(job, "Audio was generated but linking to the story failed.");
        }

        return job with
        {
            Status = StoryTtsJobStatus.Completed,
            ErrorMessage = null,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private StoryTtsJobDto Fail(StoryTtsJobDto job, string message)
    {
        _logger.LogWarning("Story TTS job {JobId} failed: {Message}", job.Id, message);
        return job with
        {
            Status = StoryTtsJobStatus.Failed,
            ErrorMessage = message,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeExtension(string responseFormat) =>
        string.IsNullOrWhiteSpace(responseFormat)
            ? ".mp3"
            : "." + responseFormat.Trim().TrimStart('.').ToLowerInvariant();

    private static string BuildOutputFileName(Core.Entities.MediaItem story, string extension)
    {
        var baseName = story.Title;
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = Path.GetFileNameWithoutExtension(story.FileName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "story";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalid, '_');

        baseName = baseName.Trim();
        if (baseName.EndsWith(".love", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^5];

        return baseName + extension;
    }

    private static string GetUniqueOutputPath(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (!File.Exists(path))
            return path;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(folderPath, $"{name}-{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(folderPath, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private static byte[] ConcatenateAudio(IReadOnlyList<byte[]> parts)
    {
        if (parts.Count == 1)
            return parts[0];

        using var stream = new MemoryStream();
        foreach (var part in parts)
            stream.Write(part, 0, part.Length);
        return stream.ToArray();
    }
}
