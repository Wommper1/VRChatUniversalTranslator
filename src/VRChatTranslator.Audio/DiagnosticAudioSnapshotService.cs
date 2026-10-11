using System.Threading.Channels;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Audio;

public sealed class DiagnosticAudioSnapshotService
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(10);

    public async Task SaveAsync(
        IProcessAudioCaptureService capture,
        string path,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var snapshot = capture.Current;
        if (snapshot.State != AudioCaptureState.Capturing || snapshot.Format is null)
        {
            throw new InvalidOperationException("Start VRChat audio capture before saving a diagnostic snapshot.");
        }

        var requestedDuration = duration ?? DefaultDuration;
        var chunks = Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

        void OnChunk(object? sender, AudioChunkAvailableEventArgs eventArgs) =>
            chunks.Writer.TryWrite(eventArgs.Chunk);

        capture.AudioChunkAvailable += OnChunk;
        try
        {
            await Task.Delay(requestedDuration, cancellationToken);
        }
        finally
        {
            capture.AudioChunkAvailable -= OnChunk;
            chunks.Writer.TryComplete();
        }

        using var captured = new MemoryStream();
        await foreach (var chunk in chunks.Reader.ReadAllAsync(CancellationToken.None))
        {
            await captured.WriteAsync(chunk.Data, CancellationToken.None);
        }

        await FloatWaveFile.WriteExactDurationAsync(
            path,
            snapshot.Format,
            captured.ToArray(),
            requestedDuration,
            cancellationToken);
    }
}

