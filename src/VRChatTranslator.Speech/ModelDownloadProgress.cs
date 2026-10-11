namespace VRChatTranslator.Speech;

public sealed record ModelDownloadProgress(long BytesDownloaded, long? ExpectedBytes)
{
    public double? Percentage => ExpectedBytes is > 0
        ? Math.Clamp((double)BytesDownloaded / ExpectedBytes.Value * 100, 0, 100)
        : null;
}

