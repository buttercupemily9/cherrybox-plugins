using System.Collections.Concurrent;
using System.Diagnostics;

namespace CherryBox.Download.Plugin;

public interface IDownloadJobTracker
{
    void Register(Guid jobId, Process process);
    void Unregister(Guid jobId);
    bool IsTracked(Guid jobId);
    bool TryCancel(Guid jobId);
}

public sealed class DownloadJobTracker : IDownloadJobTracker
{
    private readonly ConcurrentDictionary<Guid, Process> _processes = new();

    public void Register(Guid jobId, Process process) => _processes[jobId] = process;

    public void Unregister(Guid jobId) => _processes.TryRemove(jobId, out _);

    public bool IsTracked(Guid jobId) => _processes.ContainsKey(jobId);

    public bool TryCancel(Guid jobId)
    {
        if (!_processes.TryGetValue(jobId, out var process))
            return false;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
