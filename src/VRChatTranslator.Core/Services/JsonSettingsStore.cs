using System.Text.Json;
using System.Text.Json.Serialization;
using VRChatTranslator.Core.Settings;

namespace VRChatTranslator.Core.Services;

public sealed class JsonSettingsStore(string? settingsPath = null) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsPath { get; } = settingsPath ?? GetDefaultPath();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(SettingsPath);
        return Normalize(await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken) ?? new AppSettings());
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, Normalize(settings), SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        static double Safe(double x, double fallback, double min, double max) => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;
        var overlay = settings.Overlay ?? new();
        var custom = settings.CustomMode ?? new();
        var headset = settings.Headset ?? new();
        return settings with
        {
            SaveTranscripts = false,
            SourceLanguage = settings.SourceLanguage ?? "auto", TargetLanguage = settings.TargetLanguage ?? "en",
            SelectedMode = Enum.IsDefined(settings.SelectedMode) ? settings.SelectedMode : Models.PerformanceMode.Balanced,
            Overlay = overlay with { FontSize = Safe(overlay.FontSize, 32, 18, 64), Opacity = Safe(overlay.Opacity, .88, .1, 1),
                CaptionDurationSeconds = Safe(overlay.CaptionDurationSeconds, 6, 2, 30), Width = Safe(overlay.Width, 920, 420, 1800), Height = Safe(overlay.Height, 240, 100, 800),
                Left = overlay.Left is { } left && double.IsFinite(left) ? left : null, Top = overlay.Top is { } top && double.IsFinite(top) ? top : null },
            Headset = headset with { Distance = Safe(headset.Distance, 1.5, .6, 3), VerticalOffset = Safe(headset.VerticalOffset, -.35, -1.2, .5), WidthMeters = Safe(headset.WidthMeters, 1.2, .5, 2) },
            CustomMode = custom with { Model = custom.Model ?? "Base multilingual", Backend = custom.Backend ?? "CPU", CpuThreads = Math.Clamp(custom.CpuThreads, 1, 8),
                VadThreshold = Safe(custom.VadThreshold, .008, .001, .08), ChunkLengthSeconds = Math.Clamp(custom.ChunkLengthSeconds, 4, 20),
                MaximumQueueLength = Math.Clamp(custom.MaximumQueueLength, 1, 5), CaptionDelaySeconds = Safe(custom.CaptionDelaySeconds, 0, 0, 5) },
            PhoneDisplay = settings.PhoneDisplay ?? new(),
            CustomVocabulary = (settings.CustomVocabulary ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Take(100).ToArray(),
            WordCorrections = (settings.WordCorrections ?? []).Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Find)).Take(200)
                .Select(x => x with { Find = x.Find[..Math.Min(x.Find.Length, 200)], Replace = (x.Replace ?? "")[..Math.Min(x.Replace?.Length ?? 0, 200)] }).ToArray(),
            ListeningShortcut = settings.ListeningShortcut ?? "Ctrl+Alt+L", PauseShortcut = settings.PauseShortcut ?? "Ctrl+Alt+P", VisibilityShortcut = settings.VisibilityShortcut ?? "Ctrl+Alt+H"
        };
    }

    private static string GetDefaultPath() => AppPaths.ConfigPath;
}
