using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class CaptionProducedEventArgs(CaptionEvent caption) : EventArgs
{
    public CaptionEvent Caption { get; } = caption;
}

