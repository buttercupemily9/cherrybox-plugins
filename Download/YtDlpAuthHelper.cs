using CherryBox.Core.Configuration;
using CherryBox.Core.Platform;

namespace CherryBox.Download.Plugin;

internal static class YtDlpAuthHelper
{
    public static SiteAuthEntryConfig? MatchSiteAuth(string url, IEnumerable<SiteAuthEntryConfig> entries) =>
        SiteAuthHelper.MatchSiteAuth(url, entries);

    public static void AppendAuthArguments(
        ICollection<string> arguments,
        SiteAuthEntryConfig entry,
        IPlatformPaths paths)
    {
        if (string.Equals(entry.AuthMode, SiteAuthModes.Cookies, StringComparison.OrdinalIgnoreCase))
        {
            var cookiesPath = SiteAuthHelper.ResolveCookiesFilePath(paths, entry.SiteKey);
            if (cookiesPath is null || !File.Exists(cookiesPath))
                throw new InvalidOperationException($"Cookie file not found for {entry.SiteKey}.");

            arguments.Add("--cookies");
            arguments.Add(cookiesPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Username) || string.IsNullOrWhiteSpace(entry.Password))
            throw new InvalidOperationException($"Username and password are required for {entry.SiteKey}.");

        arguments.Add("-u");
        arguments.Add(entry.Username);
        arguments.Add("-p");
        arguments.Add(entry.Password);
    }

    public static async Task<YtDlpRunResult> RunAsync(
        string ytDlpPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new YtDlpRunResult(process.ExitCode, stdout, stderr);
    }

    public sealed record YtDlpRunResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput =>
            string.Join(Environment.NewLine, new[] { StdOut, StdErr }
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim()));
    }
}
