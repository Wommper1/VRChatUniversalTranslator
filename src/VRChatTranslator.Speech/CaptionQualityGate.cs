using System.Text.RegularExpressions;

namespace VRChatTranslator.Speech;

public static class CaptionQualityGate
{
    private static readonly HashSet<string> NonSpeech = new(StringComparer.OrdinalIgnoreCase)
    {
        "blank_audio", "silence", "music", "applause", "laughter", "inaudible", "unintelligible",
        "speaking foreign language", "foreign language", "no speech", "background noise"
    };

    // Whisper was trained on video subtitles, so on sound that is not speech it often "hears" the stock endings of online videos.
    // These are matched only when the WHOLE caption is made of them (plus filler words), so a real sentence that merely
    // contains one of these phrases is never touched.
    private static readonly string[] StockPhrases =
    [
        "thank you for watching this video", "thank you for watching", "thanks for watching this video", "thanks for watching",
        "please like and subscribe", "please subscribe and like", "like and subscribe", "please subscribe", "subscribe to my channel",
        "subscribe to the channel", "and hit that like button", "hit that like button", "welcome back to my channel",
        "welcome to my channel", "see you in the next video", "see you next time", "subtitles by the amara org community",
        "amara org"
    ];

    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "and", "like", "please", "so", "much", "the", "to", "my", "a", "button", "that", "hit", "this", "video", "channel",
        "everyone", "guys", "you", "for", "thanks", "thank", "bye"
    };

    public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    public static bool ShouldSuppress(string text, double? probability)
    {
        text = Normalize(text);
        if (text.Length == 0 || (probability.HasValue && probability.Value < 0.12)) return true;
        var marker = text.Trim(' ', '[', ']', '(', ')', '.', '♪', '♫', '*', '_');
        if (NonSpeech.Contains(marker) || !text.Any(char.IsLetterOrDigit)) return true;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 8 && words.GroupBy(w => w, StringComparer.OrdinalIgnoreCase).Max(g => g.Count()) >= words.Length * 0.8) return true;
        return IsSoundAnnotation(text) || IsStockVideoPhrase(text) || HasLoopedSentence(text);
    }

    /// <summary>
    /// True when the whole caption is one short bracketed sound note such as "(upbeat music)", "[MUSIC PLAYING]" or "*sighs*".
    /// The recogniser writes these for sound that is not speech; a sentence that merely contains brackets is never matched.
    /// </summary>
    public static bool IsSoundAnnotation(string text)
    {
        text = Normalize(text).TrimEnd('.', '!', '?', ' ');
        if (text.Length < 3 || text.Length > 60) return false;
        var close = text[0] switch { '[' => ']', '(' => ')', '*' => '*', _ => '\0' };
        if (close == '\0' || text[^1] != close) return false;
        var inner = text[1..^1];
        if (inner.IndexOfAny(['[', ']', '(', ')', '*']) >= 0) return false;
        var wordCount = Words(inner).Count();
        return wordCount is >= 1 and <= 5;
    }

    /// <summary>True when the caption is nothing but a stock video ending such as "Thanks for watching, please subscribe".</summary>
    public static bool IsStockVideoPhrase(string text)
    {
        var remaining = " " + string.Join(' ', Words(text)) + " ";
        var removedAny = false;
        foreach (var phrase in StockPhrases)
        {
            var padded = " " + phrase + " ";
            while (remaining.Contains(padded, StringComparison.Ordinal))
            {
                remaining = remaining.Replace(padded, " ", StringComparison.Ordinal);
                removedAny = true;
            }
        }

        return removedAny && remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(FillerWords.Contains);
    }

    /// <summary>True when the same sentence of three or more words appears three or more times: a recogniser stuck in a loop.</summary>
    public static bool HasLoopedSentence(string text)
    {
        var sentences = Regex.Split(text, @"[.!?…。！？]+")
            .Select(sentence => string.Join(' ', Words(sentence)))
            .Where(sentence => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3);
        return sentences.GroupBy(sentence => sentence, StringComparer.Ordinal).Any(group => group.Count() >= 3);
    }

    private static IEnumerable<string> Words(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
