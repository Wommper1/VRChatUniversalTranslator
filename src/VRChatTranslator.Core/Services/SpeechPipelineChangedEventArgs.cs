using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class SpeechPipelineChangedEventArgs(SpeechPipelineSnapshot snapshot) : EventArgs
{
    public SpeechPipelineSnapshot Snapshot { get; } = snapshot;
}

