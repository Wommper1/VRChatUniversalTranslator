using SherpaOnnx;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Speech;

/// <summary>
/// English speech recogniser using a NeMo CTC model (for example Parakeet 110M int8) through sherpa-onnx, which the app already
/// ships for speaker labels. It runs on the CPU and does not translate. Experimental: used by the evaluation tool only.
/// </summary>
public sealed class SherpaNemoCtcRecognizer : ISpeechRecognizer
{
    private readonly OfflineRecognizer _recognizer;
    private readonly object _gate = new();

    public SherpaNemoCtcRecognizer(string modelPath, string tokensPath, int threads, string language = "en")
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("The recogniser model file is missing.", modelPath);
        if (!File.Exists(tokensPath)) throw new FileNotFoundException("The recogniser token list is missing.", tokensPath);
        Language = language;
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.NeMoCtc.Model = modelPath;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.NumThreads = Math.Clamp(threads, 1, 8);
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.Debug = 0;
        config.DecodingMethod = "greedy_search";
        _recognizer = new OfflineRecognizer(config);
        try
        {
            // sherpa-onnx 2.x no longer throws when a model cannot be loaded: it returns an empty recogniser that fails on first
            // use. Decoding a moment of silence here makes a broken model fail now, where callers expect it, not on the first caption.
            using var probe = _recognizer.CreateStream();
            probe.AcceptWaveform(16_000, new float[1_600]);
            _recognizer.Decode(probe);
            _ = probe.Result.Text;
        }
        catch (Exception exception)
        {
            _recognizer.Dispose();
            throw new InvalidDataException("The recogniser model could not be loaded.", exception);
        }

        Name = "Parakeet (NeMo CTC, sherpa-onnx)";
    }

    public string Name { get; }

    public string Language { get; }

    public Task<RecognitionResult> RecognizeAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                using var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(16_000, samples.ToArray());
                _recognizer.Decode(stream);
                var text = stream.Result.Text?.Trim() ?? "";
                return new RecognitionResult(text, Language, text.Length == 0 ? 0 : 1);
            }
        }, cancellationToken);

    public void Dispose() => _recognizer.Dispose();
}
