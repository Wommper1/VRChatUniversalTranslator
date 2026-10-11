namespace VRChatTranslator.Speech;

public sealed class LanguageStabilizer
{
    private readonly TimeSpan _memoryDuration;
    private string? _stableLanguage;
    private string? _pendingLanguage;
    private int _pendingCount;
    private DateTimeOffset _lastStableAt;

    public LanguageStabilizer(TimeSpan? memoryDuration = null)
    {
        _memoryDuration = memoryDuration ?? TimeSpan.FromSeconds(20);
    }

    public string Resolve(string? candidate, double confidence, DateTimeOffset now)
    {
        candidate = Normalize(candidate);
        confidence = Math.Clamp(confidence, 0, 1);

        if (candidate == "und" || confidence < 0.20)
        {
            return HasRecentStableLanguage(now) ? _stableLanguage! : "und";
        }

        if (_stableLanguage is null || !HasRecentStableLanguage(now))
        {
            SetStable(candidate, now);
            return candidate;
        }

        if (string.Equals(candidate, _stableLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _lastStableAt = now;
            _pendingLanguage = null;
            _pendingCount = 0;
            return _stableLanguage;
        }

        if (string.Equals(candidate, _pendingLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _pendingCount++;
        }
        else
        {
            _pendingLanguage = candidate;
            _pendingCount = 1;
        }

        // A very clear result can switch immediately. Ordinary results must agree twice,
        // preventing one noisy chunk from relabeling an ongoing foreign conversation as English.
        if (confidence >= 0.72 || _pendingCount >= 2)
        {
            SetStable(candidate, now);
        }

        return _stableLanguage;
    }

    public void Reset()
    {
        _stableLanguage = null;
        _pendingLanguage = null;
        _pendingCount = 0;
        _lastStableAt = default;
    }

    private bool HasRecentStableLanguage(DateTimeOffset now) =>
        _stableLanguage is not null && now - _lastStableAt <= _memoryDuration;

    private void SetStable(string language, DateTimeOffset now)
    {
        _stableLanguage = language;
        _lastStableAt = now;
        _pendingLanguage = null;
        _pendingCount = 0;
    }

    private static string Normalize(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "und" : language.Trim().ToLowerInvariant();
}
