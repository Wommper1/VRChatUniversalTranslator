using SherpaOnnx;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Speech;

/// <summary>Session-only voice clustering. Not a VRChat user identity service.</summary>
public sealed class SessionSpeakerLabeler : IDisposable
{
    private SpeakerEmbeddingExtractor? _extractor;
    private readonly SpeakerClusters _clusters = new();
    // Size and SHA-256 of the 3D-Speaker ERes2Net model as published in the sherpa-onnx "speaker-recongition-models" release
    // (recorded from that release over HTTPS; GitHub does not publish a digest for it).
    private const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";
    private const long ModelBytes = 39_593_761;
    private const string ModelSha256 = "1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b";

    public static string ModelPath => AppPaths.SpeakerModelPath;
    public static bool IsInstalled => File.Exists(ModelPath) && new FileInfo(ModelPath).Length == ModelBytes;
    public string Label(float[] samples)
    {
        if (!IsInstalled || samples.Length < 16000 * 1.2) return "Unknown speaker";
        _extractor ??= new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig { Model = ModelPath, NumThreads = 2, Provider = "cpu", Debug = 0 });
        float[] Compute(float[] input)
        {
            using var stream = _extractor.CreateStream();
            stream.AcceptWaveform(16000, input); stream.InputFinished();
            return _extractor.IsReady(stream) ? _extractor.Compute(stream) : [];
        }
        // Inconsistent halves suggest a speaker change or mixture. Do not force attribution.
        if (samples.Length >= 40000)
        {
            var first = Compute(samples[..(samples.Length / 2)]);
            var second = Compute(samples[(samples.Length / 2)..]);
            if (SpeakerClusters.Similarity(first, second) < .55) return "Unknown speaker";
            return _clusters.Resolve(first.Zip(second, (a, b) => (a + b) / 2).ToArray());
        }
        return _clusters.Resolve(Compute(samples));
    }
    public void Reset() => _clusters.Clear();
    public void Dispose() { _extractor?.Dispose(); _extractor = null; _clusters.Clear(); }

    public static async Task DownloadModelAsync(CancellationToken token = default)
    {
        if (IsInstalled) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var temporary = ModelPath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } announced && announced > ModelBytes) throw new InvalidDataException("The speaker model download is larger than expected.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var file = File.Create(temporary))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    received += read;
                    if (received > ModelBytes) throw new InvalidDataException("The speaker model download is larger than expected and was stopped.");
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                }
            }
            await DownloadIntegrity.VerifyAsync(temporary, ModelBytes, ModelSha256, "speaker model", token);
            File.Move(temporary, ModelPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class SpeakerClusters
{
    private readonly List<float[]> _prototypes = [];
    public void Clear() => _prototypes.Clear();
    public string Resolve(float[] embedding)
    {
        if (embedding.Length == 0 || embedding.Any(x => !float.IsFinite(x)) || embedding.Sum(x => (double)x * x) < 1e-8) return "Unknown speaker";
        var ranked = _prototypes.Select((p, i) => (Score: Similarity(p, embedding), Index: i)).OrderByDescending(x => x.Score).ToArray();
        if (ranked.Length != 0 && ranked[0].Score >= .72 && (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= .08)) return "Speaker " + (ranked[0].Index + 1);
        if ((ranked.Length == 0 || ranked[0].Score < .45) && _prototypes.Count < 8)
        {
            _prototypes.Add(embedding.ToArray());
            return "Speaker " + _prototypes.Count;
        }
        return "Unknown speaker";
    }
    public static double Similarity(float[] first, float[] second)
    {
        if (first.Length == 0 || first.Length != second.Length) return 0;
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < first.Length; i++) { dot += (double)first[i] * second[i]; a += (double)first[i] * first[i]; b += (double)second[i] * second[i]; }
        return a * b > 1e-12 ? dot / Math.Sqrt(a * b) : 0;
    }
}
