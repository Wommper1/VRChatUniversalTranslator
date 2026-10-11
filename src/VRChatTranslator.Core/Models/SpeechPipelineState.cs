namespace VRChatTranslator.Core.Models;

public enum SpeechPipelineState
{
    Stopped,
    ModelMissing,
    LoadingModel,
    Listening,
    Transcribing,
    Faulted
}

