using CherryBox.Plugins.Abstractions;

namespace HelloCherryBox.Plugin;

public sealed class HelloPlugin : ICherryBoxPlugin
{
    public string Id => "hello-cherrybox";
    public string Name => "Hello CherryBox";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(context.DataDirectory);
        File.WriteAllText(Path.Combine(context.DataDirectory, "hello.txt"), $"Hello from {Name} v{Version} at {DateTimeOffset.UtcNow:O}");
        return Task.CompletedTask;
    }
}
