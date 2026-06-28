namespace CherryBox.StoryCovers.Plugin;

internal static class StoryCoverImageValidator
{
    private const int MinCoverBytes = 12_000;

    private static readonly string[] RejectedTextMarkers =
    [
        "violates our terms of service",
        "violates our terms",
        "please try changing your prompt",
        "support@venice.ai",
        "content policy",
        "content moderation",
        "safety filter",
        "not allowed",
    ];

    public static bool IsAcceptableCover(byte[] data, string? mimeType)
    {
        if (data.Length == 0)
            return false;

        if (ContainsRejectedMarker(data))
            return false;

        if (!HasKnownImageMagic(data))
            return false;

        if (data.Length < MinCoverBytes)
            return false;

        return true;
    }

    private static bool ContainsRejectedMarker(byte[] data)
    {
        var sample = System.Text.Encoding.UTF8.GetString(
            data.Length <= 16_384 ? data : data.AsSpan(0, 16_384));
        foreach (var marker in RejectedTextMarkers)
        {
            if (sample.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasKnownImageMagic(byte[] data) =>
        data.Length >= 12 && (
            (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) ||
            (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) ||
            (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
             data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50));
}
