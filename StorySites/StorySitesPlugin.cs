using CherryBox.Plugins.Abstractions;

namespace StorySites.Plugin;

public sealed class StorySitesPlugin : ICherryBoxPlugin
{
    public string Id => "story-sites";
    public string Name => "Story site importers";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
