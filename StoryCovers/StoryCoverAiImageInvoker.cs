using System.Reflection;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.StoryCovers.Plugin;

/// <summary>
/// Invokes AI image generation via reflection so Story covers loads on hosts whose
/// shared <see cref="IAiService"/> does not declare <c>GenerateImageAsync</c> and
/// that omit the legacy <c>IAiImageService</c> type entirely.
/// </summary>
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

        var task = method.Invoke(aiService, [request, cancellationToken]);
        if (task is not Task taskObj)
            return null;

        await taskObj.ConfigureAwait(false);

        var resultProperty = taskObj.GetType().GetProperty("Result");
        return resultProperty?.GetValue(taskObj) as AiImageResult;
    }
}
