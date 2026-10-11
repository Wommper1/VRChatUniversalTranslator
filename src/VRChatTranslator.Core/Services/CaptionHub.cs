using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class CaptionHub(int maximumHistory = 100) : ICaptionHub
{
    private readonly object _gate = new();
    private readonly Queue<CaptionEvent> _history = new();
    private readonly int _maximumHistory = maximumHistory > 0
        ? maximumHistory
        : throw new ArgumentOutOfRangeException(nameof(maximumHistory));

    public event EventHandler<CaptionProducedEventArgs>? CaptionPublished;

    public IReadOnlyList<CaptionEvent> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public void Publish(CaptionEvent caption)
    {
        ArgumentNullException.ThrowIfNull(caption);

        lock (_gate)
        {
            _history.Enqueue(caption);
            while (_history.Count > _maximumHistory)
            {
                _history.Dequeue();
            }
        }

        CaptionPublished?.Invoke(this, new CaptionProducedEventArgs(caption));
    }

    public void Clear()
    {
        lock (_gate) _history.Clear();
    }
}
