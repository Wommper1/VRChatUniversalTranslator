using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Speech;

public static class CaptionOutputTranslation
{
    /// <summary>
    /// Converts a caption to the requested output language. The caption is never dropped: when translation fails
    /// the original route and text are kept and a visible notice explains why the output language is unavailable.
    /// </summary>
    public static async Task<CaptionEvent> ApplyAsync(
        CaptionEvent caption,
        string outputLanguage,
        Func<string, string, string, CancellationToken, Task<string>> translate,
        CancellationToken token)
    {
        if (caption.TargetLanguage == outputLanguage) return caption;
        if (caption.DetectedLanguage == outputLanguage && !string.IsNullOrWhiteSpace(caption.OriginalText))
            return caption with { TranslatedText = caption.OriginalText, TargetLanguage = outputLanguage };
        try
        {
            var translated = await translate(caption.TranslatedText, caption.TargetLanguage, outputLanguage, token).ConfigureAwait(false);
            return caption with { TranslatedText = translated, TargetLanguage = outputLanguage };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Preserve the actual route; do not label fallback English as the requested language.
            return caption with { Notice = "Output language unavailable: " + ex.Message };
        }
    }
}
