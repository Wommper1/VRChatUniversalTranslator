using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Settings;

public sealed record AppSettings
{
    public PerformanceMode SelectedMode { get; init; } = PerformanceMode.Balanced;
    public bool ProtectVrchatPerformance { get; init; } = true;
    public bool TranslationEnabled { get; init; } = true;
    public bool ShowOriginalText { get; init; }
    public bool SaveTranscripts { get; init; }
    public double VadSensitivity { get; init; } = 0.008;
    public string TargetLanguage { get; init; } = "English";
    public string SourceLanguage { get; init; } = "auto";
    public bool PreloadOutputLanguage { get; init; }
    public bool SpeakerLabelsEnabled { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool CloseToTray { get; init; }
    public string ListeningShortcut { get; init; } = "Ctrl+Alt+L";
    public string VisibilityShortcut { get; init; } = "Ctrl+Alt+H";
    public string PauseShortcut { get; init; } = "Ctrl+Alt+P";
    public HeadsetSettings Headset { get; init; } = new();
    /// <summary>Opt-in: connect the SteamVR overlay when the app starts, waiting for SteamVR if it is not running yet.</summary>
    public bool AutoConnectHeadset { get; init; }
    public IReadOnlyList<WordCorrection> WordCorrections { get; init; } = [];
    public OverlaySettings Overlay { get; init; } = new();
    public PhoneDisplaySettings PhoneDisplay { get; init; } = new();
    public CustomModeSettings CustomMode { get; init; } = new();
    public IReadOnlyList<string> CustomVocabulary { get; init; } = DefaultVocabulary;

    public static IReadOnlyList<string> DefaultVocabulary { get; } =
    ["VRChat"];

    /// <summary>
    /// The window keeps the chosen performance preset and the custom-mode fields outside this record. Apply must save them too,
    /// not only closing the app, or a crash or a killed process loses the choice.
    /// </summary>
    public AppSettings WithRuntimeSelection(PerformanceMode mode, CustomModeSettings custom) =>
        this with { SelectedMode = mode, CustomMode = custom };
}

public sealed record HeadsetSettings
{
    public bool Enabled { get; init; }
    public double Distance { get; init; } = 1.5;
    public double VerticalOffset { get; init; } = -0.35;
    public double WidthMeters { get; init; } = 1.2;
}

public enum CorrectionScope { Both, Original, Translated }

public sealed record WordCorrection
{
    public bool Enabled { get; init; } = true;
    public string Find { get; init; } = "";
    public string Replace { get; init; } = "";
    public CorrectionScope Scope { get; init; } = CorrectionScope.Both;
    public override string ToString() => $"{(Enabled ? "" : "[Off] ")}{Find} → {Replace} ({Scope})";
}

public sealed record OverlaySettings
{
    public bool Enabled { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public bool ClickThrough { get; init; } = true;
    public double FontSize { get; init; } = 32;
    public double Opacity { get; init; } = 0.88;
    public double CaptionDurationSeconds { get; init; } = 6;
    public int MaximumVisibleLines { get; init; } = 3;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Width { get; init; } = 920;
    public double Height { get; init; } = 180;
}

public sealed record PhoneDisplaySettings
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 8765;
    public bool AllowLan { get; init; }
    public bool RequirePin { get; init; }
}

public sealed record CustomModeSettings
{
    public string Model { get; init; } = "Base multilingual";
    public string Backend { get; init; } = "CPU";
    public string ComputeType { get; init; } = "Float32";
    public int CpuThreads { get; init; } = 4;
    public int ChunkLengthSeconds { get; init; } = 6;
    public double VadThreshold { get; init; } = 0.008;
    public double CaptionDelaySeconds { get; init; }
    public int MaximumQueueLength { get; init; } = 3;
    public bool TranslationEnabled { get; init; } = true;
}
