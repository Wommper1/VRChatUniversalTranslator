using System.Collections.ObjectModel;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private LanguageChoice _sourceLanguage = LanguageCatalog.SourceLanguages[0];
    private LanguageChoice _targetLanguage = LanguageCatalog.Languages.Single(l => l.Code == "en");
    private bool _showOriginalText, _speakerLabelsEnabled, _closeToTray, _preloadOutputLanguage;
    private bool _overlayEnabled = true, _minimizeToTray = true, _autoScrollHistory = true;
    private double _captionFontSize = 32, _captionOpacity = 0.88, _captionSeconds = 6;
    private string _originalCaption = "", _operationStatus = "Ready · conversations stay on this PC";
    private string _recognitionHints = "", _listeningShortcut = "Ctrl+Alt+L", _visibilityShortcut = "Ctrl+Alt+H", _pauseShortcut = "Ctrl+Alt+P";
    public IReadOnlyList<LanguageChoice> SourceLanguages => LanguageCatalog.SourceLanguages;
    public IReadOnlyList<LanguageChoice> TargetLanguages => LanguageCatalog.Languages;
    public ObservableCollection<HistoryEntry> Conversation { get; } = [];
    public ObservableCollection<WordCorrection> Corrections { get; } = [];
    public LanguageChoice SourceLanguage { get => _sourceLanguage; set { SetField(ref _sourceLanguage, value); RefreshFastEnglish(); } }
    public LanguageChoice TargetLanguage { get => _targetLanguage; set => SetField(ref _targetLanguage, value); }
    public bool ShowOriginalText { get => _showOriginalText; set => SetField(ref _showOriginalText, value); }
    public bool PreloadOutputLanguage { get => _preloadOutputLanguage; set => SetField(ref _preloadOutputLanguage, value); }
    public bool SpeakerLabelsEnabled { get => _speakerLabelsEnabled; set => SetField(ref _speakerLabelsEnabled, value); }
    public bool OverlayEnabled { get => _overlayEnabled; set => SetField(ref _overlayEnabled, value); }
    public bool CloseToTray { get => _closeToTray; set => SetField(ref _closeToTray, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => SetField(ref _minimizeToTray, value); }
    public bool AutoScrollHistory { get => _autoScrollHistory; set => SetField(ref _autoScrollHistory, value); }
    public double CaptionFontSize { get => _captionFontSize; set => SetField(ref _captionFontSize, Math.Clamp(value, 18, 64)); }
    public double CaptionOpacity { get => _captionOpacity; set => SetField(ref _captionOpacity, Math.Clamp(value, 0.1, 1)); }
    public double CaptionSeconds { get => _captionSeconds; set => SetField(ref _captionSeconds, Math.Clamp(value, 2, 30)); }
    public string OriginalCaption { get => _originalCaption; private set => SetField(ref _originalCaption, value); }
    public string OperationStatus { get => _operationStatus; set => SetField(ref _operationStatus, value); }
    public string RecognitionHints { get => _recognitionHints; set => SetField(ref _recognitionHints, value); }
    public string ListeningShortcut { get => _listeningShortcut; set => SetField(ref _listeningShortcut, value); }
    public string VisibilityShortcut { get => _visibilityShortcut; set => SetField(ref _visibilityShortcut, value); }
    public string PauseShortcut { get => _pauseShortcut; set => SetField(ref _pauseShortcut, value); }

    public void LoadDesktopSettings(AppSettings settings)
    {
        SourceLanguage = SourceLanguages.FirstOrDefault(l => l.Code == settings.SourceLanguage) ?? SourceLanguages[0];
        TargetLanguage = TargetLanguages.FirstOrDefault(l => l.Code == settings.TargetLanguage || l.Name == settings.TargetLanguage)
            ?? TargetLanguages.Single(l => l.Code == "en");
        ShowOriginalText = settings.ShowOriginalText;
        PreloadOutputLanguage = settings.PreloadOutputLanguage;
        SpeakerLabelsEnabled = settings.SpeakerLabelsEnabled;
        FastEnglishEnabled = settings.FastEnglishEngine;
        OverlayEnabled = settings.Overlay.Enabled;
        CaptionFontSize = settings.Overlay.FontSize;
        CaptionOpacity = settings.Overlay.Opacity;
        CaptionSeconds = settings.Overlay.CaptionDurationSeconds;
        MinimizeToTray = settings.MinimizeToTray;
        CloseToTray = settings.CloseToTray;
        ListeningShortcut = settings.ListeningShortcut;
        VisibilityShortcut = settings.VisibilityShortcut;
        PauseShortcut = settings.PauseShortcut;
        RecognitionHints = string.Join(", ", settings.CustomVocabulary ?? []);
        Corrections.Clear();
        foreach (var correction in (settings.WordCorrections ?? []).Take(200)) Corrections.Add(correction);
    }

    public AppSettings CaptureDesktopSettings(AppSettings previous) => previous with
    {
        SourceLanguage = SourceLanguage.Code, TargetLanguage = TargetLanguage.Code,
        ShowOriginalText = ShowOriginalText, SpeakerLabelsEnabled = SpeakerLabelsEnabled, FastEnglishEngine = FastEnglishEnabled,
        PreloadOutputLanguage = PreloadOutputLanguage,
        CloseToTray = CloseToTray, MinimizeToTray = MinimizeToTray,
        ListeningShortcut = ListeningShortcut, VisibilityShortcut = VisibilityShortcut, PauseShortcut = PauseShortcut,
        WordCorrections = Corrections.ToArray(),
        CustomVocabulary = RecognitionHints.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(100).ToArray(),
        Overlay = previous.Overlay with { Enabled = OverlayEnabled, FontSize = CaptionFontSize, Opacity = CaptionOpacity, CaptionDurationSeconds = CaptionSeconds }
    };
}

public sealed record HistoryEntry(CaptionEvent Caption, string Speaker)
{
    public string Heading => $"{Caption.Timestamp.LocalDateTime:HH:mm:ss} · {CaptionPresentation.Route(Caption)}{(string.IsNullOrEmpty(Speaker) ? "" : " · " + Speaker)}";
    public string Original => Caption.OriginalText == Caption.TranslatedText ? "" : Caption.OriginalText;
    public string Translation => Caption.TranslatedText;
    public string Notice => Caption.Notice ?? "";
    public override string ToString() => $"{Heading}\n{Original}\n{Translation}".Replace("\n\n", "\n");
}
