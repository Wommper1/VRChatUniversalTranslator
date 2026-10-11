using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VRChatTranslator.Core.Services;

/// <summary>
/// The browser-side rules for the phone display page. The page is one HTML document with one inline script and one inline
/// style, so the Content-Security-Policy allows exactly those two blocks (by hash) and nothing else: no other script, no
/// other origin, no framing, no forms. Captions are other people's speech, so the page must never be a way to run their text.
/// </summary>
public static partial class PhonePageSecurity
{
    [GeneratedRegex(@"<script>(?<body>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex(@"<style>(?<body>.*?)</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    public static string ContentSecurityPolicy(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var script = Hashes(ScriptBlock(), html);
        var style = Hashes(StyleBlock(), html);
        return "default-src 'none'; " +
               $"script-src {(script.Length == 0 ? "'none'" : script)}; " +
               $"style-src {(style.Length == 0 ? "'none'" : style)}; " +
               "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    }

    /// <summary>Headers added to every response, including refusals.</summary>
    public static IReadOnlyDictionary<string, string> ResponseHeaders(string html) => new Dictionary<string, string>
    {
        ["Content-Security-Policy"] = ContentSecurityPolicy(html),
        ["X-Content-Type-Options"] = "nosniff",
        // The address carries the display PIN, so it must never be sent on to another site as a Referer.
        ["Referrer-Policy"] = "no-referrer",
        ["X-Frame-Options"] = "DENY",
        ["Cache-Control"] = "no-store",
        ["Cross-Origin-Resource-Policy"] = "same-origin"
    };

    private static string Hashes(Regex pattern, string html) => string.Join(' ', pattern.Matches(html)
        .Select(match => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(match.Groups["body"].Value)))) + "'"));

    // A browser hashes the element text after turning every line ending into a plain line feed.
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
