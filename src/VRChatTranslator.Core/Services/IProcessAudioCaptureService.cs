using System.Threading.Channels;
using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public interface IProcessAudioCaptureService : IAsyncDisposable
{
    event EventHandler<AudioCaptureChangedEventArgs>? CaptureChanged;
    event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

    AudioCaptureSnapshot Current { get; }
    ChannelReader<AudioChunk> AudioChunks { get; }

    Task StartAsync(int processId, CancellationToken cancellationToken = default);
    Task StopAsync();
}
