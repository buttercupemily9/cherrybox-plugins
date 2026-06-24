using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using CherryBox.Core.Configuration;
using CherryBox.Core.Entities;
using CherryBox.Core.Enums;
using CherryBox.Core.Platform;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.Download.Plugin;

public sealed class ImageDownloadExecutor
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif"
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly CherryBoxDbContext _db;
    private readonly IPlatformPaths _paths;
    private readonly IConfigManager _config;
    private readonly YtDlpToolInstaller? _ytDlpInstaller;
    private readonly IDownloadJobTracker _jobTracker;

    public ImageDownloadExecutor(
        CherryBoxDbContext db,
        IPlatformPaths paths,
        IConfigManager config,
        IDownloadJobTracker jobTracker,
        YtDlpToolInstaller? ytDlpInstaller = null)
    {
        _db = db;
        _paths = paths;
        _config = config;
        _jobTracker = jobTracker;
        _ytDlpInstaller = ytDlpInstaller;
    }

    public async Task<ImageDownloadResult> ExecuteAsync(
        DownloadJob job,
        ImageDownloadPlan plan,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "cherrybox-image-dl",
            job.Id.ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var files = plan.Mode switch
            {
                ImageDownloadExecutionMode.YtDlp => await DownloadViaYtDlpAsync(job, plan, tempDir, cancellationToken),
                _ => await DownloadDirectAsync(plan, tempDir, cancellationToken),
            };

            if (files.Count == 0)
                throw new InvalidOperationException("No images were downloaded.");

            var (outputPath, folderId) = await RouteDownloadedImagesAsync(files, plan.Title, cancellationToken);
            var metadataJson = BuildMetadataJson(plan);
            return new ImageDownloadResult(outputPath, folderId, metadataJson, plan.Title);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    internal static bool AreImageFiles(IEnumerable<string> filePaths) =>
        filePaths.All(f => ImageExtensions.Contains(Path.GetExtension(f)));

    public async Task<(string OutputPath, Guid FolderId)> RouteDownloadedImagesAsync(
        IReadOnlyList<string> sourceFiles,
        string title,
        CancellationToken cancellationToken)
    {
        if (sourceFiles.Count == 0)
            throw new InvalidOperationException("No images were downloaded.");

        var isGallery = sourceFiles.Count > 1;
        var contentKind = isGallery ? ContentKind.Gallery : ContentKind.Picture;
        var folder = await ResolveLibraryFolderAsync(contentKind, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No enabled library folder configured for {(isGallery ? "galleries" : "pictures")}.");

        string outputPath;
        if (isGallery)
            outputPath = await PackageGalleryAsync(title, sourceFiles, folder.Path, cancellationToken);
        else
            outputPath = await MoveSingleImageAsync(title, sourceFiles[0], folder.Path, cancellationToken);

        return (outputPath, folder.Id);
    }

    private async Task<IReadOnlyList<string>> DownloadDirectAsync(
        ImageDownloadPlan plan,
        string tempDir,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var index = 0;
        foreach (var item in plan.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            var ext = GuessExtension(item.Url, item.FileName);
            var fileName = SanitizeFileName(item.FileName ?? $"image-{index}{ext}");
            var dest = Path.Combine(tempDir, fileName);
            await DownloadFileAsync(item.Url, dest, cancellationToken);
            if (File.Exists(dest))
                files.Add(dest);
        }

        return files;
    }

    private async Task<IReadOnlyList<string>> DownloadViaYtDlpAsync(
        DownloadJob job,
        ImageDownloadPlan plan,
        string tempDir,
        CancellationToken cancellationToken)
    {
        await EnsureYtDlpAvailableAsync(cancellationToken);
        var ytDlp = ResolveYtDlp();
        var outputTemplate = Path.Combine(tempDir, "%(title)s [%(id)s].%(ext)s");
        var args = new List<string>
        {
            "-o", outputTemplate,
            "--write-info-json",
            "--progress",
            "--newline",
            plan.SourceUrl,
        };

        var ffmpegDir = ResolveFfmpegDirectory();
        if (!string.IsNullOrWhiteSpace(ffmpegDir))
        {
            args.Add("--ffmpeg-location");
            args.Add(ffmpegDir);
        }

        var siteAuth = YtDlpAuthHelper.MatchSiteAuth(plan.SourceUrl, _config.Current.Download.SiteAuth);
        if (siteAuth is not null)
            YtDlpAuthHelper.AppendAuthArguments(args, siteAuth, _paths);

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ytDlp,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in args)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        _jobTracker.Register(job.Id, process);
        try
        {
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"yt-dlp image download failed (exit {process.ExitCode}).");
        }
        finally
        {
            _jobTracker.Unregister(job.Id);
        }

        return Directory.EnumerateFiles(tempDir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string> MoveSingleImageAsync(
        string title,
        string sourceFile,
        string folderPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var ext = Path.GetExtension(sourceFile);
        var baseName = SanitizeFileName(title);
        var dest = UniquePath(Path.Combine(folderPath, baseName + ext));
        File.Move(sourceFile, dest, overwrite: false);
        await Task.CompletedTask;
        return dest;
    }

    private async Task<string> PackageGalleryAsync(
        string title,
        IReadOnlyList<string> files,
        string folderPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var zipPath = UniquePath(Path.Combine(folderPath, SanitizeFileName(title) + ".zip"));
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var index = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                var entryName = $"{index:0000}{Path.GetExtension(file)}";
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
            }
        }

        return zipPath;
    }

    private async Task<LibraryFolder?> ResolveLibraryFolderAsync(
        ContentKind contentKind,
        CancellationToken cancellationToken)
    {
        var folder = await _db.LibraryFolders
            .Where(f => f.Enabled && f.ContentKind == contentKind)
            .OrderBy(f => f.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        return folder;
    }

    private static string BuildMetadataJson(ImageDownloadPlan plan)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = plan.Title,
            ["url"] = plan.SourceUrl,
            ["webpage_url"] = plan.SourceUrl,
            ["sourceSite"] = plan.SourceSite,
            ["description"] = plan.Description,
            ["downloader"] = "image-download",
            ["imageCount"] = plan.Images.Count,
        };
        if (plan.Tags is { Count: > 0 })
            payload["tags"] = plan.Tags;

        return JsonSerializer.Serialize(payload);
    }

    private static async Task DownloadFileAsync(string url, string destPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("imagefap.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Referrer = new Uri("https://www.imagefap.com/");
        }

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destPath);
        await stream.CopyToAsync(file, cancellationToken);
    }

    private async Task EnsureYtDlpAvailableAsync(CancellationToken cancellationToken)
    {
        if (_ytDlpInstaller is not null)
            await _ytDlpInstaller.EnsureInstalledAsync(cancellationToken);
    }

    private string ResolveYtDlp()
    {
        var configured = _config.Current.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var bundled = Path.Combine(_paths.ProgramDataDirectory, "tools", "yt-dlp.exe");
        if (File.Exists(bundled))
            return bundled;

        return "yt-dlp";
    }

    private static string? ResolveFfmpegDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CHERRYBOX_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        var trimmed = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (trimmed.Length > 120)
            trimmed = trimmed[..120];
        return string.IsNullOrWhiteSpace(trimmed) ? "download" : trimmed;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private static string GuessExtension(string url, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrWhiteSpace(ext))
                return ext;
        }

        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path);
            if (ImageExtensions.Contains(ext))
                return ext;
        }
        catch
        {
        }

        return ".jpg";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }
}

public sealed record ImageDownloadResult(
    string OutputPath,
    Guid FolderId,
    string MetadataJson,
    string? Title);
