namespace VRChatTranslator.Core.Services;

/// <summary>Plain-language text for the optional fast English recogniser in Settings.</summary>
public static class FastEnglishNotice
{
    public const string Credit = "Model: NVIDIA Parakeet TDT-CTC 110M (CC-BY-4.0), converted by the sherpa-onnx project. Runs on this PC only.";

    public const string Explanation =
        "Captions English speech about half a second to a second after it ends, instead of about two seconds, and usually with fewer mistakes. " +
        "Other languages and all translation still use Whisper. English is recognised first by a quick language check, so another language is never captioned as English.";

    /// <summary>One line saying what the switch does right now. Sizes of 0 leave the size out.</summary>
    public static string Status(bool enabled, bool installed, string sourceLanguageCode, long downloadBytes)
    {
        var size = downloadBytes > 0 ? $" (about {Math.Round(downloadBytes / 1_000_000d):0} MB)" : "";
        if (!enabled)
        {
            return installed
                ? "Installed but off. English speech is recognised by Whisper like every other language."
                : "Off. English speech is recognised by Whisper like every other language.";
        }

        if (!installed)
        {
            return $"On, but the one-time download{size} is needed first. Click the button below; until then Whisper handles everything.";
        }

        if (!string.Equals(sourceLanguageCode, "auto", StringComparison.OrdinalIgnoreCase) && !string.Equals(sourceLanguageCode, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "On, but your spoken language is locked to a language other than English, so Whisper is used. Set the spoken language to Auto-detect or English to use the fast engine.";
        }

        return string.Equals(sourceLanguageCode, "en", StringComparison.OrdinalIgnoreCase)
            ? "On. English speech is captioned by the fast engine. Spoken language is locked to English, so there is no language check and it is fastest."
            : "On. English speech is captioned by the fast engine; other languages and translation use Whisper.";
    }

    public static string ButtonText(bool installed, bool busy, long downloadBytes)
    {
        if (busy) return "DOWNLOADING…";
        if (installed) return "FAST ENGLISH MODEL INSTALLED";
        return downloadBytes > 0
            ? $"DOWNLOAD FAST ENGLISH MODEL (about {Math.Round(downloadBytes / 1_000_000d):0} MB)"
            : "DOWNLOAD FAST ENGLISH MODEL";
    }
}
