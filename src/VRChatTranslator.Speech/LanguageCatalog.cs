namespace VRChatTranslator.Speech;

public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageChoice> Languages { get; } = CreateLanguages();
    public static IReadOnlyList<LanguageChoice> SourceLanguages { get; } = [new("auto", "Auto-detect"), .. Languages];

    private static IReadOnlyList<LanguageChoice> CreateLanguages() => new[]
    {
        new LanguageChoice("en", "English"), new("es", "Spanish"), new("de", "German"), new("fr", "French"),
        new("ja", "Japanese"), new("ko", "Korean"), new("zh", "Chinese"), new("pt", "Portuguese"),
        new("ru", "Russian"), new("it", "Italian"), new("nl", "Dutch"), new("pl", "Polish"),
        new("ar", "Arabic"), new("tr", "Turkish"), new("uk", "Ukrainian"), new("sv", "Swedish"),
        new("id", "Indonesian"), new("hi", "Hindi"), new("vi", "Vietnamese"), new("th", "Thai")
    }.OrderBy(l => l.Name).ToArray();
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English", ["es"] = "Spanish", ["de"] = "German", ["fr"] = "French",
        ["ja"] = "Japanese", ["ko"] = "Korean", ["zh"] = "Chinese", ["pt"] = "Portuguese",
        ["ru"] = "Russian", ["it"] = "Italian", ["nl"] = "Dutch", ["pl"] = "Polish",
        ["ar"] = "Arabic", ["tr"] = "Turkish", ["uk"] = "Ukrainian", ["sv"] = "Swedish",
        ["id"] = "Indonesian", ["hi"] = "Hindi", ["vi"] = "Vietnamese", ["th"] = "Thai"
    };

    public static string GetName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Unknown";
        }

        return Names.TryGetValue(code, out var name) ? name : code.ToUpperInvariant();
    }
}

public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}
