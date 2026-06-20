namespace CherryBox.Plugins.Abstractions;

public interface ICherryBoxPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
}

public interface IPluginContext
{
    IServiceProvider Services { get; }
    string DataDirectory { get; }
}

public interface ILibraryHook
{
    Task OnMediaIndexedAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
}

public interface IMetadataProvider
{
    string ProviderName { get; }
    Task<object?> LookupAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class PluginManifest
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string EntryAssembly { get; set; }
    public required string EntryType { get; set; }
}
