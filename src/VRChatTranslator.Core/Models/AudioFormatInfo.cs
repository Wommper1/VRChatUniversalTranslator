namespace VRChatTranslator.Core.Models;

public sealed record AudioFormatInfo(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string Encoding)
{
    public override string ToString() => $"{SampleRate:N0} Hz · {Channels} ch · {BitsPerSample}-bit {Encoding}";
}

