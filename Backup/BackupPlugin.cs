using CherryBox.Plugins.Abstractions;

namespace Backup.Plugin;

public sealed class BackupPlugin : ICherryBoxPlugin
{
    public string Id => "backup";
    public string Name => "Backup";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(context.DataDirectory);
        return Task.CompletedTask;
    }
}
