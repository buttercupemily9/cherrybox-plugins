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

/// <summary>Optional: plugins implement this to register main nav or settings tabs at runtime.</summary>
public interface IPluginUiContributor
{
    IReadOnlyList<PluginUiTabDefinition> GetMainTabs();
    IReadOnlyList<PluginUiTabDefinition> GetSettingsTabs();
}

/// <summary>Optional: plugins implement this to register services and background workers with the host.</summary>
public interface IPluginServiceContributor
{
    void RegisterServices(IPluginServiceRegistry registry, IPluginContext context);
    Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IPluginServiceRegistry
{
    void Clear();
    void RegisterSingleton<T>(T instance) where T : class;
    void RegisterScoped<T>(Func<IServiceProvider, T> factory) where T : class;
    void RegisterSupportAppUpdater(object updater);
    void RegisterStopCallback(Func<CancellationToken, Task> callback);
    T? Resolve<T>(IServiceProvider services) where T : class;
    bool IsRegistered<T>() where T : class;
    IReadOnlyList<object> GetSupportAppUpdaters();
    Task StopAllAsync(CancellationToken cancellationToken = default);
}

public sealed class PluginUiTabDefinition
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public string? Icon { get; set; }
    public string? Path { get; set; }
    public string? Page { get; set; }
    public int Order { get; set; }
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

public sealed class PluginSkinDefinition
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    /// <summary>CSS file under the plugin web/ folder.</summary>
    public string? Stylesheet { get; set; }
}

public sealed class PluginSkinsManifest
{
    /// <summary>Per core skin override stylesheets (keys: girl, boy, trans, pride).</summary>
    public Dictionary<string, string>? Overrides { get; set; }
    /// <summary>Plugin-only skins selectable inside the plugin UI (not in Account skin picker).</summary>
    public List<PluginSkinDefinition>? PluginSkins { get; set; }
}

public sealed class PluginManifest
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string EntryAssembly { get; set; }
    public required string EntryType { get; set; }
    public List<PluginUiTabDefinition>? MainTabs { get; set; }
    public List<PluginUiTabDefinition>? SettingsTabs { get; set; }
    public PluginSkinsManifest? Skins { get; set; }
}
