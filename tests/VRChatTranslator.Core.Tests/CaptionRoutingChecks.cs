using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static CaptionEvent CreateSpokenCaption(string language, string name, string original, string translated, string target = "en", bool locked = false) =>
        new(DateTimeOffset.UtcNow, language, name, original, translated, 0.9, TimeSpan.Zero) { TargetLanguage = target, LanguageLocked = locked };

    private static Task CaptionRouteMarksLockedAndAutoLanguages()
    {
        AssertEqual("Japanese (auto) → English", CaptionPresentation.Route(CreateSpokenCaption("ja", "Japanese", "こんにちは", "Hello")),
            "A detected source language must be marked auto.");
        AssertEqual("Japanese (locked) → English", CaptionPresentation.Route(CreateSpokenCaption("ja", "Japanese", "こんにちは", "Hello", locked: true)),
            "A user-selected source language must be marked locked.");
        AssertEqual("English (auto)", CaptionPresentation.Route(CreateSpokenCaption("en", "English", "Hi", "Hi")),
            "Same-language captions show the source once, still marked.");
        AssertEqual("Spanish (locked) → French", CaptionPresentation.Route(CreateSpokenCaption("es", "Spanish", "Hola", "Bonjour", "fr", locked: true)),
            "The marker applies to the source language, not the output language.");
        AssertEqual("No caption", CaptionPresentation.Route(CreateSpokenCaption("und", "No caption", "", "Waiting for speech…", "und")),
            "Placeholder captions have no language to mark.");
        AssertFalse(new CaptionEvent(DateTimeOffset.UtcNow, "ja", "Japanese", "x", "y", 1, TimeSpan.Zero).LanguageLocked,
            "Captions default to auto-detected so older producers do not claim a lock.");
        return Task.CompletedTask;
    }

    private static async Task OutputTranslationKeepsCaptionWhenItFails()
    {
        var caption = CreateSpokenCaption("ja", "Japanese", "こんにちは", "Hello", locked: true);

        var translatedCalls = 0;
        var translated = await CaptionOutputTranslation.ApplyAsync(caption, "es", (text, from, to, _) =>
        {
            translatedCalls++;
            AssertEqual("Hello", text, "Translation must start from the English caption text.");
            AssertEqual("en", from, "Source route is the caption's current target.");
            AssertEqual("es", to, "Destination route is the requested output language.");
            return Task.FromResult("Hola");
        }, CancellationToken.None);
        AssertEqual("Hola", translated.TranslatedText, "A working translator replaces the caption text.");
        AssertEqual("es", translated.TargetLanguage, "A translated caption is labelled with the new output language.");
        AssertEqual("こんにちは", translated.OriginalText, "The recognized source text must be preserved.");
        AssertTrue(translated.LanguageLocked, "Translation must not drop the lock marker.");
        AssertEqual<string?>(null, translated.Notice, "A successful translation has no notice.");

        var failed = await CaptionOutputTranslation.ApplyAsync(caption, "es",
            (_, _, _, _) => throw new InvalidOperationException("pack missing"), CancellationToken.None);
        AssertEqual("Hello", failed.TranslatedText, "A failed translation must keep the caption text.");
        AssertEqual("en", failed.TargetLanguage, "A failed translation must not claim the requested language.");
        AssertEqual("こんにちは", failed.OriginalText, "A failed translation must preserve the original text.");
        AssertEqual("Output language unavailable: pack missing", failed.Notice, "A failed translation must say why.");
        AssertEqual(caption.Id, failed.Id, "The same caption event continues; nothing is dropped or duplicated.");

        var calls = 0;
        var already = await CaptionOutputTranslation.ApplyAsync(caption, "en", (_, _, _, _) => { calls++; return Task.FromResult("never"); }, CancellationToken.None);
        AssertEqual(0, calls, "Matching routes must not invoke the translator.");
        AssertEqual("Hello", already.TranslatedText, "Matching routes pass the caption through.");

        var spoken = CreateSpokenCaption("es", "Spanish", "Hola", "Hello");
        var viaOriginal = await CaptionOutputTranslation.ApplyAsync(spoken, "es", (_, _, _, _) => { calls++; return Task.FromResult("never"); }, CancellationToken.None);
        AssertEqual(0, calls, "Speech already in the output language must not be translated.");
        AssertEqual("Hola", viaOriginal.TranslatedText, "Speech already in the output language shows the original text.");
        AssertEqual("es", viaOriginal.TargetLanguage, "Same-language output is labelled with that language.");

        var cancelled = false;
        using var cancellation = new CancellationTokenSource();
        try
        {
            await CaptionOutputTranslation.ApplyAsync(caption, "es", (_, _, _, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult("x"); }, cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        AssertTrue(cancelled, "Cancellation must stop the caption instead of being reported as an unavailable language.");
        AssertEqual(1, translatedCalls, "Only the successful call above reached the translator.");
    }

    private static Task OverlayPlacementKeepsWindowOnScreen()
    {
        // Single 1920x1080 monitor at the origin; overlay is 800 wide.
        AssertNear(100, OverlayPlacement.ClampAxis(100, 0, 1920, 800), 0, "A visible position must not move.");
        AssertNear(1120, OverlayPlacement.ClampAxis(3000, 0, 1920, 800), 0, "A monitor that was unplugged to the right pulls the overlay to the right edge.");
        AssertNear(0, OverlayPlacement.ClampAxis(-5000, 0, 1920, 800), 0, "A position far off the left edge returns to the left edge.");
        // Two monitors with a second one to the left: virtual screen starts at -1920.
        AssertNear(-1500, OverlayPlacement.ClampAxis(-1500, -1920, 3840, 800), 0, "A position on a left-hand monitor stays when that monitor exists.");
        AssertNear(-1920, OverlayPlacement.ClampAxis(-1920 - 400, -1920, 3840, 800), 0, "A position left of the virtual screen is clamped to its start.");
        AssertNear(0, OverlayPlacement.ClampAxis(double.NaN, 0, 1920, 800), 0, "A corrupt saved position falls back to the screen start.");
        AssertNear(0, OverlayPlacement.ClampAxis(double.PositiveInfinity, 0, 1920, 800), 0, "Infinity is treated as corrupt and falls back to the screen start.");
        // The overlay grows with long captions; one wider than the screen must anchor instead of throwing.
        AssertNear(0, OverlayPlacement.ClampAxis(300, 0, 1920, 2400), 0, "A window wider than the screen anchors at the screen start.");
        return Task.CompletedTask;
    }
}
