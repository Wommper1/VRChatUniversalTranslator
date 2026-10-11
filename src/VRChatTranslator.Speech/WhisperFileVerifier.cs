using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace VRChatTranslator.Speech;

public static class WhisperFileVerifier
{
    public static async Task<string> TranslateToEnglishAsync(
        string modelPath,
        string wavePath,
        CancellationToken cancellationToken = default)
    {
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
        using var factory = WhisperFactory.FromPath(modelPath);
        await using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .WithTranslate()
            .WithThreads(4)
            .WithNoContext()
            .Build();
        await using var stream = File.OpenRead(wavePath);
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
        {
            text.Append(segment.Text);
        }

        return text.ToString().Trim();
    }
}

