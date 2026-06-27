using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.Newsletter.Plugin;

internal static class NewsletterImageEmbedder
{
    private const int MaxImageBytes = 512_000;

    internal static string ContentIdFor(Guid mediaItemId) => $"item-{mediaItemId:N}";

    internal static async Task<IReadOnlyList<EmailEmbeddedImage>> BuildEmbeddedImagesAsync(
        CherryBoxDbContext db,
        IReadOnlyList<(Guid MediaItemId, MediaType MediaType)> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return [];

        var mediaIds = items.Select(i => i.MediaItemId).Distinct().ToList();
        var blobs = await db.MediaBlobs.AsNoTracking()
            .Where(b => mediaIds.Contains(b.MediaItemId))
            .Select(b => new BlobRow(b.MediaItemId, b.Kind, b.MimeType, b.Data))
            .ToListAsync(cancellationToken);

        var blobsByMedia = blobs
            .GroupBy(b => b.MediaItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var embedded = new List<EmailEmbeddedImage>();
        foreach (var (mediaItemId, mediaType) in items)
        {
            if (!blobsByMedia.TryGetValue(mediaItemId, out var mediaBlobs))
                continue;

            var blob = PickPreviewBlob(mediaType, mediaBlobs);
            if (blob is null || !IsSupportedImage(blob.MimeType) || blob.Data.Length == 0)
                continue;
            if (blob.Data.Length > MaxImageBytes)
                continue;

            embedded.Add(new EmailEmbeddedImage(
                ContentIdFor(mediaItemId),
                blob.Data,
                blob.MimeType,
                $"{mediaItemId:N}{ExtensionFor(blob.MimeType)}"));
        }

        return embedded;
    }

    private static BlobRow? PickPreviewBlob(MediaType mediaType, IReadOnlyList<BlobRow> blobs)
    {
        var kinds = PreferredKinds(mediaType);
        foreach (var kind in kinds)
        {
            var match = blobs.FirstOrDefault(b => b.Kind == kind);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static MediaBlobKind[] PreferredKinds(MediaType mediaType) => mediaType switch
    {
        MediaType.Video => [MediaBlobKind.Thumbnail, MediaBlobKind.Poster, MediaBlobKind.PrimaryImage],
        MediaType.Image => [MediaBlobKind.PrimaryImage, MediaBlobKind.Thumbnail],
        MediaType.Audio => [MediaBlobKind.Thumbnail, MediaBlobKind.PrimaryImage],
        _ => [MediaBlobKind.Thumbnail, MediaBlobKind.PrimaryImage]
    };

    private static bool IsSupportedImage(string mimeType) =>
        mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".jpg"
    };

    private sealed record BlobRow(Guid MediaItemId, MediaBlobKind Kind, string MimeType, byte[] Data);
}
