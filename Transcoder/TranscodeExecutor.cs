using CherryBox.Core.Enums;
using CherryBox.Core.Platform;
using CherryBox.Data;
using CherryBox.Encoding;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Transcoder.Plugin;

internal sealed class TranscodeExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscodeProfileStore _profiles;
    private readonly ILogger<TranscodeExecutor> _logger;

    public TranscodeExecutor(
        IServiceScopeFactory scopeFactory,
        TranscodeProfileStore profiles,
        ILogger<TranscodeExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _profiles = profiles;
        _logger = logger;
    }

    public async Task<TranscodeJobDto> ExecuteAsync(TranscodeJobDto job, CancellationToken cancellationToken)
    {
        var profile = _profiles.Get(job.ProfileId)
            ?? throw new InvalidOperationException("Transcode profile not found.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var encoder = scope.ServiceProvider.GetRequiredService<IMediaEncoder>();
        var refresh = scope.ServiceProvider.GetRequiredService<IVideoMediaRefreshService>();

        var media = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == job.MediaItemId && m.MediaType == MediaType.Video, cancellationToken);
        if (media is null || !File.Exists(media.FilePath))
            return Fail(job, "Video file not found.");

        var sourcePath = media.FilePath;
        var bytesBefore = new FileInfo(sourcePath).Length;
        var probe = await encoder.ProbeAsync(sourcePath, cancellationToken);

        if (TranscodeCompatibilityChecker.IsCompatible(probe, profile, sourcePath))
        {
            return Complete(job with
            {
                Status = TranscodeJobStatus.Skipped,
                SourcePath = sourcePath,
                BytesBefore = bytesBefore,
                BytesAfter = bytesBefore,
                ErrorMessage = "Already compatible with profile.",
                UpdatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
        }

        var targetExt = TranscodeCompatibilityChecker.TargetExtension(profile.Container);
        var targetPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            Path.GetFileNameWithoutExtension(sourcePath) + targetExt);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".{Path.GetFileNameWithoutExtension(sourcePath)}.transcoding{targetExt}");

        if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not delete stale temp transcode file"); }
        }

        var spec = TranscodeCompatibilityChecker.ToSpec(profile, probe?.DurationSeconds);
        var success = await encoder.TranscodeToFileAsync(spec, sourcePath, tempPath, cancellationToken);
        if (!success || !File.Exists(tempPath))
            return Fail(job, "FFmpeg transcode failed.");

        try
        {
            if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
                File.Delete(targetPath);

            ManagedToolFile.CopyReplace(tempPath, targetPath);

            if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(sourcePath))
                File.Delete(sourcePath);

            var entity = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == job.MediaItemId, cancellationToken);
            if (entity is not null)
            {
                entity.FilePath = targetPath;
                entity.FileName = Path.GetFileName(targetPath);
                entity.FileSizeBytes = new FileInfo(targetPath).Length;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            await refresh.RefreshAfterFileChangeAsync(job.MediaItemId, cancellationToken);

            var bytesAfter = new FileInfo(targetPath).Length;
            return Complete(job with
            {
                Status = TranscodeJobStatus.Completed,
                SourcePath = sourcePath,
                OutputPath = targetPath,
                BytesBefore = bytesBefore,
                BytesAfter = bytesAfter,
                UpdatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace transcoded file for {MediaId}", job.MediaItemId);
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
            return Fail(job, ex.Message);
        }
    }

    private static TranscodeJobDto Fail(TranscodeJobDto job, string error) =>
        job with
        {
            Status = TranscodeJobStatus.Failed,
            ErrorMessage = error,
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };

    private static TranscodeJobDto Complete(TranscodeJobDto job) => job;
}
