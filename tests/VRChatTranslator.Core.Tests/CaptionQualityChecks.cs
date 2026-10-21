using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static Task TheFilterDropsBracketedSoundNotesAndKeepsShortRealSpeech()
    {
        // Sound notes the recogniser writes for music, laughter and noise: not speech, so not captions.
        string[] soundNotes =
        [
            "(upbeat music)", "[MUSIC PLAYING]", "(laughs)", "*sighs*", "(birds chirping)", "[Applause]", "[ Silence ]",
            "[laughter]", "(music)", "(Laughs).", "[BLANK_AUDIO]"
        ];
        foreach (var text in soundNotes)
        {
            AssertTrue(CaptionQualityGate.ShouldSuppress(text, 0.9), $"A bracketed sound note must be dropped: {text}");
        }

        // Short real speech, brackets inside a sentence, and numbers must survive.
        string[] realSpeech =
        [
            "Yes.", "Hmm.", "Okay, okay.", "No.", "Uh-huh.", "42", "Thank you.", "Thanks.", "Bye.", "You",
            "I know I know I know I know", "(laughs) That was funny.", "I said [inaudible] twice.", "She said (quietly) hello.",
            "Use the * key to go back", "(1) Walk to the bridge. (2) Wait there."
        ];
        foreach (var text in realSpeech)
        {
            AssertFalse(CaptionQualityGate.ShouldSuppress(text, 0.9), $"Real speech must survive: {text}");
        }

        // Long bracketed text is not a short sound note, so it is left alone.
        AssertFalse(CaptionQualityGate.IsSoundAnnotation("(this is a long parenthetical remark that is clearly many words long)"), "Long bracketed text is not a sound note.");
        AssertFalse(CaptionQualityGate.IsSoundAnnotation("[one] [two]"), "Two separate brackets are not one sound note.");
        AssertFalse(CaptionQualityGate.IsSoundAnnotation("()"), "Empty brackets are not a sound note.");

        // Repeats and the confidence boundary.
        AssertTrue(CaptionQualityGate.ShouldSuppress("no no no no no no no no", 0.9), "One word repeated for a whole caption is a loop.");
        AssertTrue(CaptionQualityGate.ShouldSuppress("Yes.", 0.10), "A very unsure guess is dropped, even a short one.");
        AssertFalse(CaptionQualityGate.ShouldSuppress("Yes.", 0.13), "Just above the confidence floor a short word is kept.");
        AssertFalse(CaptionQualityGate.ShouldSuppress("Yes.", null), "With no confidence value a short word is kept.");
        return Task.CompletedTask;
    }
}
