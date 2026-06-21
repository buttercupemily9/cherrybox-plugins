using CherryBox.Plugins.Abstractions;

namespace ViewTime.Plugin;

public sealed class ViewTimePlugin : ICherryBoxPlugin
{
    public string Id => "view-time";
    public string Name => "View time limits";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
