using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CherryBox.Core.Platform;
using CherryBox.Core.SupportApps;
using Microsoft.Extensions.Logging;

namespace CherryBox.Download.Plugin;

public interface IYtDlpToolInstaller : ISupportAppUpdater
{
}

public sealed class YtDlpToolInstaller : IYtDlpToolInstaller
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPlatformPaths _paths;
    private readonly ISupportAppManifestStore _manifest;
    private readonly ILogger<YtDlpToolInstaller> _logger;

    public string AppId => SupportAppIds.YtDlp;
    public string DisplayName => "yt-dlp";

    public YtDlpToolInstaller(
        IHttpClientFactory httpClientFactory,
        IPlatformPaths paths,
        ISupportAppManifestStore manifest,
        ILogger<YtDlpToolInstaller> logger)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
        _manifest = manifest;
        _logger = logger;
    }

    public async Task<SupportAppStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveTargetPath();
        var isInstalled = IsManagedInstallPresent(targetPath);
        var installedVersion = await ResolveInstalledVersionAsync(targetPath, isInstalled, cancellationToken);
        var latestTag = await TryGetLatestTagAsync(cancellationToken);
        var latestVersion = latestTag is null ? null : SupportAppVersionComparer.Normalize(latestTag);
        var updateAvailable = latestVersion is not null &&
            (!isInstalled || SupportAppVersionComparer.IsNewer(latestVersion, installedVersion));

        return new SupportAppStatus(
            AppId,
            DisplayName,
            isInstalled,
            installedVersion,
            latestVersion,
            updateAvailable,
            isInstalled ? targetPath : null,
            _manifest.GetApp(AppId)?.InstalledAt);
    }

    public async Task<bool> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveTargetPath();
        if (IsManagedInstallPresent(targetPath))
        {
            await BackfillManifestIfNeededAsync(targetPath, cancellationToken);
            return true;
        }

        if (HasExternalConfiguredPath())
            return true;

        var release = await FindReleaseAsync(cancellationToken);
        if (release is null)
        {
            _logger.LogWarning("No yt-dlp release found for this platform.");
            return false;
        }

        try
        {
            return await InstallReleaseAsync(release.Value.Tag, release.Value.Asset, targetPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install yt-dlp.");
            return false;
        }
    }

    public async Task<SupportAppUpdateResult> UpdateIfNewerAsync(
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        if (HasExternalConfiguredPath())
        {
            return new SupportAppUpdateResult(
                AppId,
                true,
                false,
                null,
                "yt-dlp is provided via CHERRYBOX_YTDLP_PATH; automatic updates are disabled.");
        }

        var targetPath = ResolveTargetPath();
        var release = await FindReleaseAsync(cancellationToken);
        if (release is null)
        {
            return new SupportAppUpdateResult(AppId, false, false, null, "Could not find a yt-dlp release for this platform.");
        }

        var latestVersion = SupportAppVersionComparer.Normalize(release.Value.Tag);
        var installedVersion = await ResolveInstalledVersionAsync(
            targetPath,
            IsManagedInstallPresent(targetPath),
            cancellationToken);

        if (!force && IsManagedInstallPresent(targetPath) &&
            !SupportAppVersionComparer.IsNewer(latestVersion, installedVersion))
        {
            return new SupportAppUpdateResult(AppId, true, false, installedVersion, "Already up to date.");
        }

        try
        {
            var installed = await InstallReleaseAsync(release.Value.Tag, release.Value.Asset, targetPath, cancellationToken);
            return installed
                ? new SupportAppUpdateResult(AppId, true, true, latestVersion, $"Updated to {latestVersion}.")
                : new SupportAppUpdateResult(AppId, false, false, installedVersion, "Update failed.");
        }
        catch (Exception ex)
        {
            return new SupportAppUpdateResult(AppId, false, false, installedVersion, ex.Message);
        }
    }

    private bool HasExternalConfiguredPath()
    {
        var configured = Environment.GetEnvironmentVariable("CHERRYBOX_YTDLP_PATH");
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured);
    }

    private static bool IsManagedInstallPresent(string targetPath) => File.Exists(targetPath);

    private async Task<bool> InstallReleaseAsync(
        string tag,
        GitHubReleaseAssetInfo asset,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.ToolsDirectory);

        try
        {
            _logger.LogInformation("Downloading yt-dlp {Version} ({Asset})…", tag, asset.Name);

            var client = CreateGitHubClient();
            await using var stream = await client.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken);
            await ManagedToolFile.WriteFromStreamAsync(targetPath, stream, cancellationToken);

            if (!File.Exists(targetPath))
            {
                _logger.LogWarning("yt-dlp download completed but binary was not found at {Path}.", targetPath);
                return false;
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    targetPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var version = SupportAppVersionComparer.Normalize(tag);
            _manifest.SetAppVersion(AppId, version);
            _logger.LogInformation("yt-dlp {Version} installed to {Path}", version, targetPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download yt-dlp.");
            throw;
        }
    }

    private string ResolveTargetPath() => _paths.GetToolPath("yt-dlp");

    private async Task<string?> TryGetLatestTagAsync(CancellationToken cancellationToken)
    {
        var release = await FindReleaseAsync(cancellationToken);
        return release?.Tag;
    }

    private async Task<(string Tag, GitHubReleaseAssetInfo Asset)?> FindReleaseAsync(CancellationToken cancellationToken)
    {
        var client = CreateGitHubClient();
        var release = await client.GetFromJsonAsync<GitHubReleaseInfo>(LatestReleaseUrl, cancellationToken);
        if (release?.Assets is null || release.Assets.Count == 0 || string.IsNullOrWhiteSpace(release.TagName))
            return null;

        var patterns = GetAssetPatterns();
        foreach (var pattern in patterns)
        {
            var match = release.Assets.FirstOrDefault(a => pattern.IsMatch(a.Name));
            if (match is not null)
                return (release.TagName, match);
        }

        return null;
    }

    private async Task<string?> ResolveInstalledVersionAsync(
        string targetPath,
        bool isInstalled,
        CancellationToken cancellationToken)
    {
        var entry = _manifest.GetApp(AppId);
        if (!string.IsNullOrWhiteSpace(entry?.Version))
            return entry.Version;

        if (!isInstalled)
            return null;

        var probed = await ProbeBinaryVersionAsync(targetPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(probed))
            _manifest.SetAppVersion(AppId, probed);

        return probed;
    }

    private async Task BackfillManifestIfNeededAsync(string targetPath, CancellationToken cancellationToken)
    {
        if (_manifest.GetApp(AppId) is not null)
            return;

        var probed = await ProbeBinaryVersionAsync(targetPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(probed))
            _manifest.SetAppVersion(AppId, probed);
    }

    private static async Task<string?> ProbeBinaryVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
            await process.WaitForExitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(output) ? null : output.Split('\n', '\r')[0].Trim();
        }
        catch
        {
            return null;
        }
    }

    private static Regex[] GetAssetPatterns()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.Is64BitProcess
                ? [new Regex(@"^yt-dlp\.exe$", RegexOptions.IgnoreCase)]
                : [new Regex(@"^yt-dlp_x86\.exe$", RegexOptions.IgnoreCase)];
        }

        if (OperatingSystem.IsLinux())
            return
            [
                new Regex(@"^yt-dlp_linux$", RegexOptions.IgnoreCase),
                new Regex(@"^yt-dlp$", RegexOptions.IgnoreCase),
            ];

        if (OperatingSystem.IsMacOS())
            return
            [
                new Regex(@"^yt-dlp_macos$", RegexOptions.IgnoreCase),
                new Regex(@"^yt-dlp_macos_legacy$", RegexOptions.IgnoreCase),
            ];

        return [new Regex(@"^yt-dlp\.exe$", RegexOptions.IgnoreCase)];
    }

    private HttpClient CreateGitHubClient()
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CherryBox", "1.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
