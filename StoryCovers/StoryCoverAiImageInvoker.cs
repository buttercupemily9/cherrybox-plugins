using System.Reflection;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.StoryCovers.Plugin;

internal static class StoryCoverAiImageInvoker
{
    public static async Task<AiImageResult?> TryGenerateAsync(
        object aiService,
        AiImageRequest request,
        CancellationToken cancellationToken)
    {
        var method = aiService.GetType().GetMethod(
            "GenerateImageAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(AiImageRequest), typeof(CancellationToken)],
            modifiers: null);
        if (method is null)
            return null;

        var task = method.Invoke(aiService, [request, cancellationToken]) as Task<AiImageResult>;
        return task is null ? null : await task.ConfigureAwait(false);
    }
}
