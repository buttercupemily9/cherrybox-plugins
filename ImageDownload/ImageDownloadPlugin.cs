using CherryBox.Plugins.Abstractions;

namespace ImageDownload.Plugin;

public sealed class ImageDownloadPlugin : ICherryBoxPlugin
{
    public string Id => "image-download";
    public string Name => "Image downloader";
    public string Version => "1.0.4";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
