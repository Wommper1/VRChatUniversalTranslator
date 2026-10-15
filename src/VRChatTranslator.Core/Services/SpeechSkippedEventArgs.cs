using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class SpeechSkippedEventArgs(SkippedSpeech skipped) : EventArgs
{
    public SkippedSpeech Skipped { get; } = skipped;
}
