using System.Text.RegularExpressions;

namespace VRChatTranslator.Speech;

public static class CaptionQualityGate
{
    private static readonly HashSet<string> NonSpeech = new(StringComparer.OrdinalIgnoreCase)
    {
        "blank_audio", "silence", "music", "applause", "laughter", "inaudible", "unintelligible",
        "speaking foreign language", "foreign language", "no speech", "background noise"
    };

    public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    public static bool ShouldSuppress(string text, double? probability)
    {
        text = Normalize(text);
        if (text.Length == 0 || (probability.HasValue && probability.Value < 0.12)) return true;
        var marker = text.Trim(' ', '[', ']', '(', ')', '.', '♪', '♫', '*', '_');
        if (NonSpeech.Contains(marker) || !text.Any(char.IsLetterOrDigit)) return true;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 8 && words.GroupBy(w => w, StringComparer.OrdinalIgnoreCase).Max(g => g.Count()) >= words.Length * 0.8;
    }
}
