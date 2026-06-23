using CherryBox.Core.Entities;

namespace CherryBox.Download.Plugin;

internal static class DownloadJobFileCleanup
{
    public static void RemoveCover(DownloadJob job)
    {
        if (string.IsNullOrWhiteSpace(job.CoverImagePath))
            return;

        try
        {
            if (File.Exists(job.CoverImagePath))
                File.Delete(job.CoverImagePath);
        }
        catch
        {
            // Best-effort cleanup; job row is still removed.
        }
    }
}
