using System.Security.Cryptography;
using SherpaOnnx;

namespace VRChatTranslator.Speech;

public enum VoiceDetectionMode
{
    /// <summary>Use the Silero voice detector when its model file is present and intact, otherwise the energy gate.</summary>
    Auto,
    /// <summary>Always use the plain loudness gate (the behaviour of versions up to 1.1.2).</summary>
    Energy,
    /// <summary>Require the Silero voice detector; fail if its model is missing or damaged.</summary>
    Silero
}

/// <summary>Decides, one short stretch of 16 kHz mono audio at a time, whether someone is speaking.</summary>
public interface ISpeechActivity : IDisposable
{
    string Name { get; }
    bool IsSpeech(ReadOnlySpan<float> samples16k);
}

/// <summary>The original detector: anything louder than a fixed level counts as speech. Music, game sounds and noise count too.</summary>
public sealed class EnergySpeechActivity : ISpeechActivity
{
    private readonly double _rmsThreshold;

    public EnergySpeechActivity(double rmsThreshold = 0.008) => _rmsThreshold = Math.Clamp(rmsThreshold, 0.001, 0.08);

    public string Name => "Energy gate";

    public bool IsSpeech(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return false;
        double sum = 0;
        foreach (var sample in samples)
        {
            if (float.IsFinite(sample)) sum += sample * sample;
        }

        return Math.Sqrt(sum / samples.Length) >= _rmsThreshold;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Silero VAD (MIT licence) through the sherpa-onnx package the app already ships. It is a small neural network that tells speech
/// from other sound, so music, keyboards and game effects no longer open the gate the way a loudness threshold does.
/// </summary>
public sealed class SileroSpeechActivity : ISpeechActivity
{
    private readonly object _gate = new();
    private readonly VoiceActivityDetector _detector;
    private bool _disposed;

    public SileroSpeechActivity(string modelPath, float threshold = 0.5f)
    {
        var config = new VadModelConfig
        {
            SileroVad = new SileroVadModelConfig
            {
                Model = modelPath,
                Threshold = threshold,
                // The segmenter applies its own end-of-speech rule, so the detector only needs to smooth out single noisy windows.
                MinSilenceDuration = 0.1f,
                MinSpeechDuration = 0.05f,
                WindowSize = 512,
                MaxSpeechDuration = 20f
            },
            SampleRate = 16_000,
            NumThreads = 1,
            Provider = "cpu",
            Debug = 0
        };
        _detector = new VoiceActivityDetector(config, 60f);
    }

    public string Name => "Silero VAD";

    public bool IsSpeech(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return false;
        lock (_gate)
        {
            if (_disposed) return false;
            _detector.AcceptWaveform(samples.ToArray());
            var speaking = _detector.IsSpeechDetected();
            // Only the live "is someone speaking now" state is used; finished segments are discarded so the buffer cannot grow.
            while (!_detector.IsEmpty()) _detector.Pop();
            return speaking;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _detector.Dispose();
        }
    }
}

public static class SpeechActivityFactory
{
    public const string ModelFileName = "silero_vad.onnx";
    public const long ExpectedBytes = 643_854;
    // SHA-256 of silero_vad.onnx: silero-vad v4 exported to ONNX by k2-fsa (16 kHz branch only), from the sherpa-onnx "asr-models"
    // release. GitHub publishes no digest for it, so this is the value recorded when the file was inspected and added.
    public const string ExpectedSha256 = "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6";

    /// <summary>The detector model ships inside the app folder, so no download is needed.</summary>
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "vad", ModelFileName);

    /// <summary>True only for exactly the published file, so a partial copy or a replaced file is never loaded.</summary>
    public static bool ModelIsIntact(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != ExpectedBytes) return false;
            var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            return string.Equals(digest, ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static ISpeechActivity Create(VoiceDetectionMode mode, double energyThreshold, string? modelPath = null, float sileroThreshold = 0.5f)
    {
        if (mode != VoiceDetectionMode.Energy)
        {
            var path = modelPath ?? ModelPath;
            if (ModelIsIntact(path))
            {
                if (mode == VoiceDetectionMode.Silero) return new SileroSpeechActivity(path, sileroThreshold);
                try
                {
                    return new SileroSpeechActivity(path, sileroThreshold);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Auto mode never stops captions over a detector problem; the energy gate takes over.
                }
            }
            else if (mode == VoiceDetectionMode.Silero)
            {
                throw new FileNotFoundException("The Silero voice-detection model is missing or damaged.", path);
            }
        }

        return new EnergySpeechActivity(energyThreshold);
    }
}
