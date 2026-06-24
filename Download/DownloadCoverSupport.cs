using CherryBox.Core.Entities;
using CherryBox.Core.Platform;

namespace CherryBox.Download.Plugin;

internal static class DownloadCoverSupport
{
    private static readonly HashSet<string> ImageCoverExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif"
    };

    private static readonly HashSet<string> VideoCoverExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".mp4"
    };

    public static string BuildCoverPath(string coversDir, Guid jobId, string? mediaUrl)
    {
        var ext = GuessCoverExtension(mediaUrl);
        return Path.Combine(coversDir, $"{jobId:N}{ext}");
    }

    public static string? ResolveCoverPath(DownloadJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.CoverImagePath) && File.Exists(job.CoverImagePath))
            return job.CoverImagePath;

        if (!string.IsNullOrWhiteSpace(job.OutputPath) && File.Exists(job.OutputPath) &&
            IsPreviewableExtension(Path.GetExtension(job.OutputPath)))
        {
            return job.OutputPath;
        }

        return null;
    }

    public static bool HasCover(DownloadJob job) => ResolveCoverPath(job) is not null;

    public static bool CoverIsVideo(DownloadJob job)
    {
        var path = ResolveCoverPath(job);
        return path is not null && VideoCoverExtensions.Contains(Path.GetExtension(path));
    }

    public static void TrySetCoverFromOutput(DownloadJob job, IPlatformPaths paths, string outputPath)
    {
        if (!File.Exists(outputPath) || !IsPreviewableExtension(Path.GetExtension(outputPath)))
            return;

        var ext = Path.GetExtension(outputPath);
        if (!string.IsNullOrWhiteSpace(job.CoverImagePath) && File.Exists(job.CoverImagePath) &&
            string.Equals(Path.GetExtension(job.CoverImagePath), ext, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var coversDir = Path.Combine(paths.ProgramDataDirectory, "download-covers");
        Directory.CreateDirectory(coversDir);
        var coverPath = Path.Combine(coversDir, $"{job.Id:N}{ext.ToLowerInvariant()}");
        try
        {
            File.Copy(outputPath, coverPath, overwrite: true);
            job.CoverImagePath = coverPath;
        }
        catch
        {
            // Preview can still fall back to OutputPath.
        }
    }

    private static bool IsPreviewableExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return ImageCoverExtensions.Contains(extension) || VideoCoverExtensions.Contains(extension);
    }

    private static string GuessCoverExtension(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return ".jpg";

        try
        {
            var ext = Path.GetExtension(new Uri(mediaUrl).AbsolutePath);
            if (IsPreviewableExtension(ext))
                return ext.ToLowerInvariant();
        }
        catch
        {
            // fall through
        }

        return ".jpg";
    }
}
