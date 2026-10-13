using System.Runtime.InteropServices;
using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Speech;

public sealed class PcmSpeechSegmenter : IDisposable
{
    public const int OutputSampleRate = 16_000;
    private const int PreRollSamples = OutputSampleRate * 250 / 1_000;
    private const int DefaultEndSilenceMilliseconds = 350;
    private const int MinimumSpeechSamples = OutputSampleRate * 300 / 1_000;

    private readonly object _gate = new();
    private readonly Queue<float> _preRoll = new(PreRollSamples);
    private readonly List<float> _active = new(OutputSampleRate * 4);
    private readonly ISpeechActivity _activity;
    private readonly int _maximumSegmentSamples;
    private readonly int _endSilenceSamples;
    private bool _inSpeech;
    private int _consecutiveSpeechChunks;
    private int _speechSamples;
    private int _silenceSamples;

    /// <summary>The original behaviour: a loudness threshold decides what is speech.</summary>
    public PcmSpeechSegmenter(double rmsThreshold = 0.008, int maximumSegmentSeconds = 12)
        : this(new EnergySpeechActivity(rmsThreshold), maximumSegmentSeconds)
    {
    }

    /// <summary>The segmenter owns <paramref name="activity"/> and disposes it.</summary>
    /// <param name="endSilenceMilliseconds">How long speech must stop before a segment is handed over; shorter is faster but can split sentences.</param>
    public PcmSpeechSegmenter(ISpeechActivity activity, int maximumSegmentSeconds = 12, int endSilenceMilliseconds = DefaultEndSilenceMilliseconds)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _maximumSegmentSamples = OutputSampleRate * Math.Clamp(maximumSegmentSeconds, 4, 20);
        _endSilenceSamples = OutputSampleRate * Math.Clamp(endSilenceMilliseconds, 100, 1_000) / 1_000;
    }

    public string VoiceDetection => _activity.Name;

    public void Dispose() => _activity.Dispose();

    public SpeechSegmenterResult Accept(AudioChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var mono = ConvertTo16KhzMono(chunk);
        if (mono.Length == 0)
        {
            return new SpeechSegmenterResult(_inSpeech, null);
        }

        var speechChunk = _activity.IsSpeech(mono);

        lock (_gate)
        {
            if (!_inSpeech)
            {
                AppendPreRoll(mono);
                _consecutiveSpeechChunks = speechChunk ? _consecutiveSpeechChunks + 1 : 0;
                if (_consecutiveSpeechChunks < 2)
                {
                    return new SpeechSegmenterResult(false, null);
                }

                _inSpeech = true;
                _active.AddRange(_preRoll);
                _preRoll.Clear();
                _speechSamples = mono.Length * _consecutiveSpeechChunks;
                _silenceSamples = 0;
                return new SpeechSegmenterResult(true, null);
            }

            _active.AddRange(mono);
            if (speechChunk)
            {
                _speechSamples += mono.Length;
                _silenceSamples = 0;
            }
            else
            {
                _silenceSamples += mono.Length;
            }

            if (_silenceSamples < _endSilenceSamples && _active.Count < _maximumSegmentSamples)
            {
                return new SpeechSegmenterResult(true, null);
            }

            var completed = _speechSamples >= MinimumSpeechSamples ? _active.ToArray() : null;
            var trailingSilence = _silenceSamples;
            ResetActive();
            return new SpeechSegmenterResult(false, completed, trailingSilence);
        }
    }

    public float[]? Flush()
    {
        lock (_gate)
        {
            var completed = _speechSamples >= MinimumSpeechSamples ? _active.ToArray() : null;
            ResetActive();
            return completed;
        }
    }

    private static float[] ConvertTo16KhzMono(AudioChunk chunk)
    {
        var format = chunk.Format;
        if (format.SampleRate != 48_000 || format.Channels != 2 || format.BitsPerSample != 32)
        {
            return [];
        }

        var floats = MemoryMarshal.Cast<byte, float>(chunk.Data.Span);
        var frames = floats.Length / 2;
        var outputFrames = frames / 3;
        var output = new float[outputFrames];
        for (var outputIndex = 0; outputIndex < outputFrames; outputIndex++)
        {
            var sourceFrame = outputIndex * 3;
            double sum = 0;
            for (var offset = 0; offset < 3; offset++)
            {
                var index = (sourceFrame + offset) * 2;
                var left = float.IsFinite(floats[index]) ? floats[index] : 0;
                var right = float.IsFinite(floats[index + 1]) ? floats[index + 1] : 0;
                sum += (left + right) * 0.5;
            }

            output[outputIndex] = (float)(sum / 3);
        }

        return output;
    }

    private void AppendPreRoll(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _preRoll.Enqueue(sample);
            if (_preRoll.Count > PreRollSamples)
            {
                _preRoll.Dequeue();
            }
        }
    }

    private void ResetActive()
    {
        _active.Clear();
        _inSpeech = false;
        _consecutiveSpeechChunks = 0;
        _speechSamples = 0;
        _silenceSamples = 0;
    }
}

public readonly record struct SpeechSegmenterResult(bool IsSpeechActive, float[]? CompletedSegment, int TrailingSilenceSamples = 0);
