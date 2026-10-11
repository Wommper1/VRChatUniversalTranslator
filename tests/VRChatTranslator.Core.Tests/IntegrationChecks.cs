using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NAudio.Wave;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static async Task<int> RunSpeakerTest(string folder)
    {
        Directory.CreateDirectory(folder);
        await SessionSpeakerLabeler.DownloadModelAsync();
        using var labeler = new SessionSpeakerLabeler();
        using var http = new HttpClient();
        foreach (var name in new[] { "fangjun-sr-1", "fangjun-sr-2", "leijun-sr-1" })
        {
            var path = Path.Combine(folder, name + ".wav");
            if (!File.Exists(path)) await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync("https://raw.githubusercontent.com/csukuangfj/sr-data/master/enroll/" + name + ".wav"));
            using var wave = new WaveFileReader(path);
            var samples = ReadMono(wave);
            var watch = Stopwatch.StartNew();
            var label = labeler.Label(samples);
            Console.WriteLine($"Speaker fixture {name}: {label}; analysis {watch.Elapsed.TotalMilliseconds:0} ms; duration {samples.Length / 16000d:0.0}s");
            AssertTrue(label.StartsWith("Speaker ", StringComparison.Ordinal) || label == "Unknown speaker", "Only session labels are allowed.");
        }
        return 0;
    }

    private static async Task<int> RunPipelineTest(string folder)
    {
        Directory.CreateDirectory(folder);
        var fixture = Path.Combine(folder, "jfk.wav");
        if (!File.Exists(fixture))
        {
            using var http = new HttpClient();
            await File.WriteAllBytesAsync(fixture, await http.GetByteArrayAsync("https://raw.githubusercontent.com/ggml-org/whisper.cpp/master/samples/jfk.wav"));
        }
        using var wave = new WaveFileReader(fixture);
        var samples = ReadMono(wave);
        foreach (var original in new[] { false, true })
        {
            await using var capture = new FixtureCapture();
            await using var speech = new LocalWhisperSpeechService();
            var received = new List<CaptionEvent>();
            var gate = new object();
            speech.CaptionProduced += (_, e) => { lock (gate) received.Add(e.Caption); Console.WriteLine($"Bilingual={original}; queue={e.Caption.QueueDelay.TotalMilliseconds:0}ms; end-to-caption={e.Caption.EndToCaptionDelay.TotalMilliseconds:0}ms; inference={e.Caption.Latency.TotalMilliseconds:0}ms; source={e.Caption.DetectedLanguage}"); };
            await speech.StartAsync(capture, new LocalWhisperModelManager().BalancedModelPath, 4, original, maximumSegmentSeconds: 6);
            await capture.FeedAsync(new float[16000]);
            lock (gate) AssertEqual(0, received.Count, "Silence must not create captions.");
            await capture.FeedAsync(samples);
            await capture.FeedAsync(new float[16000]);
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30) && speech.Current.State != SpeechPipelineState.Listening) await Task.Delay(100);
            await Task.Delay(1000);
            await speech.StopAsync();
            lock (gate) AssertTrue(received.Count > 0, "Real speech must produce captions.");
            var count = received.Count;
            await Task.Delay(100);
            AssertEqual(count, received.Count, "Stop must prevent late captions.");
        }
        return 0;
    }

    private static float[] ReadMono(WaveFileReader wave)
    {
        if (wave.WaveFormat.SampleRate != 16000 || wave.WaveFormat.Channels != 1) throw new InvalidDataException("Fixture must be 16 kHz mono.");
        var provider = wave.ToSampleProvider();
        var samples = new List<float>(); var buffer = new float[16000]; int read;
        while ((read = provider.Read(buffer.AsSpan())) > 0) samples.AddRange(buffer.AsSpan(0, read).ToArray());
        return samples.ToArray();
    }

    private sealed class FixtureCapture : IProcessAudioCaptureService
    {
        public event EventHandler<AudioCaptureChangedEventArgs>? CaptureChanged { add { } remove { } }
        public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;
        public AudioCaptureSnapshot Current => new() { State = AudioCaptureState.Capturing };
        public ChannelReader<AudioChunk> AudioChunks => Channel.CreateUnbounded<AudioChunk>().Reader;
        public Task StartAsync(int processId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public async Task FeedAsync(float[] samples)
        {
            for (var offset = 0; offset < samples.Length; offset += 800)
            {
                var count = Math.Min(800, samples.Length - offset);
                var stereo = new float[count * 6];
                for (var i = 0; i < count; i++) Array.Fill(stereo, samples[offset + i], i * 6, 6);
                await Task.Delay(50);
                AudioChunkAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(new AudioChunk(DateTimeOffset.UtcNow,
                    MemoryMarshal.AsBytes(stereo.AsSpan()).ToArray(), new AudioFormatInfo(48000, 2, 32, "IEEE float"), 0, 0)));
            }
        }
    }
}
