using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Speech;

public static class CaptionPresentation
{
    /// <summary>
    /// The language route shown beside a caption. The source language is marked "locked" when the user chose it
    /// and "auto" when it was detected, so a wrongly detected language is never mistaken for a deliberate choice.
    /// Placeholder captions with no language ("und") carry no marker.
    /// </summary>
    public static string Route(CaptionEvent caption)
    {
        var source = caption.DetectedLanguage == "und" ? caption.LanguageName
            : $"{caption.LanguageName} ({(caption.LanguageLocked ? "locked" : "auto")})";
        return caption.DetectedLanguage == caption.TargetLanguage
            ? source : $"{source} → {LanguageCatalog.GetName(caption.TargetLanguage)}";
    }
}
