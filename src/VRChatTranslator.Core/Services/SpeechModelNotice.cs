namespace VRChatTranslator.Core.Services;

/// <summary>Plain-language text for the one thing a first-time user must do: download the speech model.</summary>
public static class SpeechModelNotice
{
    /// <summary>Headline while VRChat is found but the model is missing and listening has not started.</summary>
    public const string ReadyHeadline = "VRChat detected · download the speech model first";

    /// <summary>Headline while audio is being captured but nothing can be captioned yet.</summary>
    public const string ListeningHeadline = "Listening, but no captions until the speech model is downloaded";

    /// <summary>Where to click and what to expect. <paramref name="expectedBytes"/> of 0 leaves the size out.</summary>
    public static string NeededDetail(string modelFileName, long expectedBytes)
    {
        var size = expectedBytes > 0 ? $", about {Math.Round(expectedBytes / 1_000_000d):0} MB" : "";
        return $"Captions need a speech model on this PC ({modelFileName}{size}). Open the Settings tab and click DOWNLOAD SELECTED MODEL once. "
            + "It is checked before use, and captions start by themselves when it finishes.";
    }
}
