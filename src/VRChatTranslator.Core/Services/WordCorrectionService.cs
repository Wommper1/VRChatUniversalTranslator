using System.Text.RegularExpressions;
using VRChatTranslator.Core.Settings;

namespace VRChatTranslator.Core.Services;

public static class WordCorrectionService
{
    public static string Apply(string text, IEnumerable<WordCorrection> rules, CorrectionScope output)
    {
        foreach (var rule in rules.Take(200))
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Find) || rule.Find.Length > 200 ||
                (rule.Scope != CorrectionScope.Both && rule.Scope != output)) continue;
            var pattern = @"(?<![\p{L}\p{N}_])" + Regex.Escape(rule.Find.Trim()) + @"(?![\p{L}\p{N}_])";
            text = Regex.Replace(text, pattern, _ => rule.Replace ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        return text;
    }
}
