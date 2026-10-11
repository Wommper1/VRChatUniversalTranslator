using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public interface ICaptionHub
{
    event EventHandler<CaptionProducedEventArgs>? CaptionPublished;

    IReadOnlyList<CaptionEvent> History { get; }

    void Publish(CaptionEvent caption);
    void Clear();
}
