using CherryBox.Core.Enums;
using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

public static partial class NewsletterWeeklyComposer
{
    public static async Task<(string Html, string Plain, IReadOnlyList<EmailEmbeddedImage> EmbeddedImages, string NarratorDisplayName)> BuildAsync(
        CherryBox.Data.CherryBoxDbContext db,
        IPluginServiceRegistry plugins,
        IServiceProvider services,
        string username,
        string? skinId,
        string baseUrl,
        DateTimeOffset since,
        ILogger? logger,
        CancellationToken cancellationToken) =>
        await BuildForTestAsync(
            db,
            plugins,
            services,
            username,
            skinId,
            baseUrl,
            since,
            logger,
            audienceGender: null,
            audienceOrientation: null,
            voiceOverride: null,
            cancellationToken);

    public static async Task<(string Html, string Plain, IReadOnlyList<EmailEmbeddedImage> EmbeddedImages, string NarratorDisplayName)> BuildForTestAsync(
        CherryBox.Data.CherryBoxDbContext db,
        IPluginServiceRegistry plugins,
        IServiceProvider services,
        string username,
        string? skinId,
        string baseUrl,
        DateTimeOffset since,
        ILogger? logger,
        UserGender? audienceGender,
        SexualOrientation? audienceOrientation,
        NewsletterNarratorVoice? voiceOverride,
        CancellationToken cancellationToken)
    {
        var (items, embeddedImages) = await LoadDigestItemsAsync(db, baseUrl, since, cancellationToken);
        var voice = voiceOverride
            ?? (audienceGender.HasValue && audienceOrientation.HasValue
                ? NewsletterVoiceSelector.Resolve(audienceGender, audienceOrientation)
                : NewsletterNarratorVoice.Female);
        var audience = audienceGender ?? UserGender.Female;
        var orientation = audienceOrientation ?? SexualOrientation.Straight;
        var variant = NewsletterAiVariant.ResolveForUser(voice, audience, orientation);
        var aiIntro = await TryGenerateAiIntroAsync(
            plugins,
            services,
            username,
            items,
            variant.Voice,
            variant.Audience,
            variant.Orientation,
            logger,
            cancellationToken);
        return RenderForUser(username, skinId, baseUrl, items, embeddedImages, voice, aiIntro);
    }

    public static (string Html, string Plain, IReadOnlyList<EmailEmbeddedImage> EmbeddedImages, string NarratorDisplayName) RenderForUser(
        string username,
        string? skinId,
        string baseUrl,
        IReadOnlyList<NewsletterDigestItem> items,
        IReadOnlyList<EmailEmbeddedImage> embeddedImages,
        NewsletterNarratorVoice voice,
        string? aiIntro)
    {
        var narratorName = NewsletterVoiceSelector.DisplayName(voice);
        var theme = NewsletterTemplates.GetTheme(skinId);
        var html = NewsletterTemplates.RenderWeeklyDigest(username, baseUrl, theme, items, narratorName, aiIntro);
        var plain = NewsletterTemplates.WeeklyPlainText(username, baseUrl, items, narratorName, aiIntro);
        return (html, plain, embeddedImages, narratorName);
    }

    internal static async Task<string?> TryGenerateAiIntroAsync(
        IPluginServiceRegistry plugins,
        IServiceProvider services,
        string username,
        IReadOnlyList<NewsletterDigestItem> items,
        NewsletterNarratorVoice voice,
        UserGender audience,
        SexualOrientation orientation,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var ai = plugins.Resolve<IAiService>(services);
        if (ai is null)
            return null;

        try
        {
            var settings = await ai.GetSettingsAsync(cancellationToken);
            if (!settings.HasApiKey)
                return null;

            var prompt = NewsletterAiPrompts.BuildUserPrompt(username, items, audience, orientation);
            var text = await ai.CompleteChatAsync(
                new AiChatRequest(prompt, NewsletterAiPrompts.SystemPromptFor(voice, audience, orientation), MaxTokens: 850),
                cancellationToken);

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI newsletter intro generation failed for {Voice}/{Audience}/{Orientation}; using default copy.", voice, audience, orientation);
            return null;
        }
    }
}

