using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using VRChatTranslator.App.Services;
using VRChatTranslator.App.ViewModels;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;

namespace VRChatTranslator.App;

public partial class MainWindow
{
    private DesktopShell? _shell;
    private bool _paused, _explicitQuit, _reconfiguring;
    private readonly SemaphoreSlim _preferencesGate = new(1, 1);
    private CaptionEvent? _lastDesktopCaption;
    private long _historyGeneration;
    private readonly Dictionary<string, string> _speakerNames = [];

    private void InitializeDesktopFeatures()
    {
        InitializeTranslationControls();
        InitializeHeadsetControls();
        CorrectionScopeSelector.ItemsSource = Enum.GetValues<CorrectionScope>();
        CorrectionScopeSelector.SelectedItem = CorrectionScope.Both;
        _viewModel.PropertyChanged += OnDesktopPreferenceChanged;
        StateChanged += (_, _) => { if (!_skipLifecycleCleanup && WindowState == WindowState.Minimized && _viewModel.MinimizeToTray) Hide(); };
    }

    private void WriteDiagnostic(string category, string message) => _ = _log.WriteAsync(category, message);

    private void StartDesktopShell()
    {
        _overlay.Diagnostics = WriteDiagnostic;
        _shell = new DesktopShell(this, RestoreMainWindow, () => { RestoreMainWindow(); MainTabs.SelectedItem = SettingsTab; },
            () => OnToggleCaptureClick(this, new RoutedEventArgs()), () => OnPauseClick(this, new RoutedEventArgs()),
            () => OnToggleOverlayClick(this, new RoutedEventArgs()), () => { _explicitQuit = true; Close(); }, WriteDiagnostic);
        _viewModel.OperationStatus = _shell.Configure(_settings);
        if (!_overlay.LockShortcutRegistered) _viewModel.OperationStatus += " · Ctrl+Alt+O is in use. Use the Settings lock button.";
    }

    private void RestoreMainWindow() { Show(); WindowState = WindowState.Normal; Activate(); }

    /// <summary>Called when the user starts the app a second time: show this copy instead of running two.</summary>
    internal void BringToFront()
    {
        if (_cleanupStarted) return;
        RestoreMainWindow();
        WriteDiagnostic("STARTUP", "A second launch was ignored; this copy was brought to the front.");
    }

    private void OnDesktopPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.TargetLanguage)) RefreshLanguageTools();
        if (e.PropertyName == nameof(MainWindowViewModel.SpeakerLabelsEnabled))
        {
            if (!_viewModel.SpeakerLabelsEnabled) _speakerNames.Clear();
            _speech.SpeakerLabelsEnabled = _viewModel.SpeakerLabelsEnabled;
            _viewModel.OperationStatus = !_viewModel.SpeakerLabelsEnabled ? "Speaker analysis off." :
                VRChatTranslator.Speech.SessionSpeakerLabeler.IsInstalled ? "Experimental speaker analysis on. Names are estimates for this session only." : "Download the optional speaker model in Settings first. Until then attribution is Unknown.";
        }
        if (e.PropertyName is nameof(MainWindowViewModel.CaptionFontSize) or nameof(MainWindowViewModel.CaptionOpacity) or
            nameof(MainWindowViewModel.CaptionSeconds) or nameof(MainWindowViewModel.ShowOriginalText))
        {
            _overlay.ApplyPreferences(_viewModel.CaptureDesktopSettings(_settings).Overlay, _viewModel.ShowOriginalText);
            _headset.SetPreferences(_viewModel.CaptureDesktopSettings(_settings).Overlay, _viewModel.ShowOriginalText);
            if (_lastDesktopCaption is not null) _viewModel.ApplyCaption(_lastDesktopCaption);
        }
    }

    private async void OnApplyPreferencesClick(object sender, RoutedEventArgs e)
    {
        if (!await _preferencesGate.WaitAsync(0)) return;
        _reconfiguring = true;
        try
        {
            _settings = _viewModel.CaptureDesktopSettings(_settings);
            _settings = _settings.WithRuntimeSelection(_selectedMode, _viewModel.GetCustomSettings());
            _overlay.ApplyPreferences(_settings.Overlay, _settings.ShowOriginalText);
            if (_settings.Overlay.Enabled) _overlay.Show(); else _overlay.Hide();
            _headset.SetVisible(_settings.Overlay.Enabled);
            await _settingsStore.SaveAsync(_settings);
            var shortcutStatus = _shell?.Configure(_settings) ?? "";
            await _speech.StopAsync();
            var preloadStatus = await PreloadOutputIfRequestedAsync(_settings);
            _reconfiguring = false;
            await ReconcileSpeechAsync(_audioCapture.Current);
            _viewModel.OperationStatus = $"Saved · {(_settings.SourceLanguage == "auto" ? "auto-detect" : "source locked to " + _viewModel.SourceLanguage.Name)} · {shortcutStatus} {preloadStatus}";
            // Only mode and language codes: never the vocabulary or corrections, which can hold names.
            _ = _log.WriteAsync("SETTINGS", $"Applied and saved: mode={_selectedMode}; source={_settings.SourceLanguage}; target={_settings.TargetLanguage}; {shortcutStatus.Trim()}.");
        }
        catch (Exception ex) { _viewModel.OperationStatus = $"Could not apply settings: {ex.Message}"; }
        finally { _reconfiguring = false; _preferencesGate.Release(); }
    }

    private async void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_cleanupStarted) return;
        if (!_listeningRequested && !_audioCapture.Current.IsActive) { _viewModel.OperationStatus = "Start listening first; pause keeps your current listening session ready to resume."; return; }
        _paused = !_paused;
        if (_paused) await _audioCapture.StopAsync();
        else await ReconcileCaptureAsync(_processDetector.Current);
        _viewModel.OperationStatus = _paused ? "Listening paused · resume to continue" : "Listening resumed";
        _shell?.SetStatus(_paused ? "paused" : "ready");
    }

    private void OnToggleOverlayClick(object sender, RoutedEventArgs e)
    {
        _viewModel.OverlayEnabled = !_viewModel.OverlayEnabled;
        if (_viewModel.OverlayEnabled) _overlay.Show(); else _overlay.Hide();
        _headset.SetVisible(_viewModel.OverlayEnabled);
        _viewModel.OperationStatus = _viewModel.OverlayEnabled ? "Captions visible" : "Captions hidden · listening state unchanged";
    }

    private void OnPreviewCaptionClick(object sender, RoutedEventArgs e)
    {
        _overlay.ApplyPreferences(_viewModel.CaptureDesktopSettings(_settings).Overlay, _viewModel.ShowOriginalText);
        _overlay.Show();
        _overlay.ShowCaption(new(DateTimeOffset.UtcNow, "ja", "Japanese", "こんにちは！一緒にお話ししましょう。", "Hello! Let's enjoy the conversation together.", 1, TimeSpan.Zero));
        _headset.ShowCaption(new(DateTimeOffset.UtcNow, "ja", "Japanese", "こんにちは！", "Hello! Headset caption preview.", 1, TimeSpan.Zero));
        _viewModel.OperationStatus = "Preview only · not added to history or sent to VRChat";
    }

    private void OnOverlayLockClick(object sender, RoutedEventArgs e) { _overlay.Show(); _overlay.SetClickThrough(!_overlay.IsClickThrough); }
    private void OnResetOverlayClick(object sender, RoutedEventArgs e) { _overlay.ResetPlacement(); OnPreviewCaptionClick(sender, e); }

    private void OnCopyHistoryClick(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry) { _viewModel.OperationStatus = "Select a history entry to copy."; return; }
        try { Clipboard.SetText(entry.ToString()); _viewModel.OperationStatus = "Selected caption copied."; }
        catch { _viewModel.OperationStatus = "Clipboard busy. Try again."; }
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _historyGeneration);
        _speech.DiscardPendingCaptions();
        _headset.Clear();
        _captionHub.Clear(); _viewModel.Conversation.Clear(); _lastDesktopCaption = null; _overlay.ClearCaption();
        _viewModel.ApplyCaption(new(DateTimeOffset.UtcNow, "und", "No caption", "", "Waiting for speech…", null, TimeSpan.Zero) { TargetLanguage = "und" });
        _viewModel.OperationStatus = "Session history cleared. No transcript was saved.";
    }

    private void OnCorrectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CorrectionList.SelectedItem is not WordCorrection rule) return;
        CorrectionFind.Text = rule.Find; CorrectionReplace.Text = rule.Replace;
        CorrectionEnabled.IsChecked = rule.Enabled; CorrectionScopeSelector.SelectedItem = rule.Scope;
    }

    private void OnSaveCorrectionClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CorrectionFind.Text)) { _viewModel.OperationStatus = "Enter the word or phrase to correct."; return; }
        var rule = new WordCorrection { Find = CorrectionFind.Text.Trim(), Replace = CorrectionReplace.Text, Enabled = CorrectionEnabled.IsChecked == true,
            Scope = CorrectionScopeSelector.SelectedItem is CorrectionScope scope ? scope : CorrectionScope.Both };
        var index = CorrectionList.SelectedIndex;
        if (index >= 0) _viewModel.Corrections[index] = rule;
        else if (_viewModel.Corrections.Count < 200) _viewModel.Corrections.Add(rule);
        CorrectionList.SelectedIndex = -1; CorrectionFind.Clear(); CorrectionReplace.Clear();
        _viewModel.OperationStatus = "Correction updated. Apply & save settings to activate it.";
    }

    private void OnRemoveCorrectionClick(object sender, RoutedEventArgs e)
    {
        if (CorrectionList.SelectedItem is WordCorrection rule) _viewModel.Corrections.Remove(rule);
        _viewModel.OperationStatus = "Correction removed from editor. Apply & save settings to activate changes.";
    }

    private void OnPreviewCorrectionClick(object sender, RoutedEventArgs e) => CorrectionPreview.Text =
        "Original: " + WordCorrectionService.Apply(CorrectionSample.Text, _viewModel.Corrections, CorrectionScope.Original) +
        "\nTranslation: " + WordCorrectionService.Apply(CorrectionSample.Text, _viewModel.Corrections, CorrectionScope.Translated);

    private void PresentDesktopCaption(CaptionEvent caption)
    {
        _lastDesktopCaption = caption;
        _viewModel.ApplyCaption(caption);
        var speaker = !_viewModel.SpeakerLabelsEnabled || caption.SpeakerId is null ? "" : _speakerNames.GetValueOrDefault(caption.SpeakerId, caption.SpeakerId);
        var entry = new HistoryEntry(caption, speaker);
        _viewModel.Conversation.Add(entry);
        while (_viewModel.Conversation.Count > 100) _viewModel.Conversation.RemoveAt(0);
        if (_viewModel.AutoScrollHistory) HistoryList.ScrollIntoView(entry);
        if (_viewModel.OverlayEnabled) _overlay.ShowCaption(caption with { SpeakerId = string.IsNullOrEmpty(speaker) ? null : speaker });
        _headset.ShowCaption(caption with { SpeakerId = string.IsNullOrEmpty(speaker) ? null : speaker });
        if (caption.Notice is not null) _viewModel.OperationStatus = caption.Notice;
    }

    private async void OnDownloadSpeakersClick(object sender, RoutedEventArgs e)
    {
        if (_languageToolsBusy) return;
        await RunLanguageActionAsync(async () =>
        {
            _viewModel.OperationStatus = "Downloading optional local speaker model…";
            await VRChatTranslator.Speech.SessionSpeakerLabeler.DownloadModelAsync(LanguageActionToken);
            _viewModel.OperationStatus = "Speaker model ready. Enable optional speaker labels when you want them.";
        });
    }

    private void OnRenameSpeakerClick(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry selected || selected.Caption.SpeakerId is not { } id || id == "Unknown speaker")
        { _viewModel.OperationStatus = "Select a caption with a numbered speaker first. Uncertain voices cannot be named."; return; }
        var name = SpeakerName.Text.Trim();
        if (name.Length == 0) _speakerNames.Remove(id); else _speakerNames[id] = name;
        for (var i = 0; i < _viewModel.Conversation.Count; i++)
        {
            var entry = _viewModel.Conversation[i];
            if (entry.Caption.SpeakerId == id) _viewModel.Conversation[i] = new HistoryEntry(entry.Caption, name.Length == 0 ? id : name);
        }
        _viewModel.OperationStatus = "Session name updated. No identity profile was saved.";
    }

    internal void RunDesktopChecks()
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Require(MainTabs.Items.Count == 6, "All six desktop tabs must be present.");
        Require(!_viewModel.PreloadOutputLanguage, "Preloading must be opt-in.");
        Require(!PackDownloadButton.IsEnabled, "English must not offer an unnecessary extra pack.");
        Require(!_headset.IsConnected && !_headset.IsReconnecting, "The headset must start idle and never reconnect on its own before the user connects.");
        _headset.Disconnect();
        Require(!_headset.IsConnected && !_headset.IsReconnecting, "Disconnecting an idle headset must be safe and leave it idle.");
        Require(AutoConnectHeadset.IsChecked == false && !_settings.AutoConnectHeadset, "Connecting the headset at start must be opt-in and off by default.");
        Require(OscStatusText.Text.Contains("VRChat is not running", StringComparison.Ordinal) && OscStatusText.Text.Contains("Nothing sent yet", StringComparison.Ordinal),
            "The OSC status must say VRChat is not running and nothing was sent before any detection or send.");
        Require(OscStatusText.Text.Contains("Sending is off", StringComparison.Ordinal), "The OSC status must explain that sending needs consent.");
        ReplyConsent.IsChecked = true;
        Require(!OscStatusText.Text.Contains("Sending is off", StringComparison.Ordinal), "Ticking consent must refresh the OSC status immediately.");
        ReplyConsent.IsChecked = false;
        Require(OscStatusText.Text.Contains("Sending is off", StringComparison.Ordinal), "Clearing consent must refresh the OSC status immediately.");
        _viewModel.TargetLanguage = _viewModel.TargetLanguages.Single(l => l.Code == "es");
        Require(PackDownloadButton.IsEnabled && LivePackStatus.Text.Contains("Spanish", StringComparison.Ordinal), "Live language selection must update pack actions and status.");
        Require(ReferenceEquals(PackLanguageSelector.SelectedItem, _viewModel.TargetLanguage), "Pack manager and Live must share one output-language choice.");
        _viewModel.TargetLanguage = _viewModel.TargetLanguages.Single(l => l.Code == "en");
        Require(!_viewModel.SpeakerLabelsEnabled && _viewModel.SourceLanguage.Code == "auto", "Privacy and language defaults must remain safe.");
        _viewModel.ShowOriginalText = true;
        for (var i = 0; i < 105; i++) PresentDesktopCaption(new(DateTimeOffset.UtcNow, "ja", "Japanese", "こんにちは、世界。", "Hello, world " + i, .9, TimeSpan.Zero));
        Require(_viewModel.Conversation.Count == 100, "Visible history must remain bounded.");
        Require(_viewModel.CurrentCaptionLanguage == "Japanese (auto) → English", "The live caption must mark an auto-detected source language.");
        Require(_viewModel.Conversation[^1].Heading.Contains("Japanese (auto)", StringComparison.Ordinal), "History must mark an auto-detected source language.");
        Require(_viewModel.OriginalCaption == "こんにちは、世界。", "Bilingual display must show actual source text.");
        _viewModel.ShowOriginalText = false;
        Require(_viewModel.OriginalCaption.Length == 0, "Translated-only mode must hide the original.");
        PresentDesktopCaption(new(DateTimeOffset.UtcNow, "ja", "Japanese", "こんにちは", "Hello", .9, TimeSpan.Zero) { LanguageLocked = true });
        Require(_viewModel.CurrentCaptionLanguage == "Japanese (locked) → English", "The live caption must mark a user-locked source language.");
        Require(_viewModel.Conversation[^1].Heading.Contains("Japanese (locked)", StringComparison.Ordinal), "History must mark a user-locked source language.");
        OnClearHistoryClick(this, new RoutedEventArgs());
        Require(_viewModel.Conversation.Count == 0 && _captionHub.History.Count == 0, "Clear must discard all local replay history.");
        _overlay.ApplyPreferences(new OverlaySettings { FontSize = 64 }, true);
        _overlay.Show();
        _overlay.ShowCaption(new(DateTimeOffset.UtcNow, "ja", "Japanese", string.Concat(Enumerable.Repeat("こんにちは、世界。", 12)), string.Join(" ", Enumerable.Repeat("Long caption wrapping test", 12)), 1, TimeSpan.Zero));
        Require(_overlay.Height > 180, "Long bilingual captions must grow instead of clipping at the default height.");
        Require(!_overlay.IsSkipNoticeVisible, "The skipped-speech note must start hidden.");
        _overlay.ShowSkipNotice(SkippedSpeechNotice.OverlayText);
        Require(_overlay.IsSkipNoticeVisible, "When speech is skipped the overlay must say so next to the caption.");
        List<string> shellLog = [];
        var listeningToggled = 0;
        using var shell = new DesktopShell(this, () => { }, () => { }, () => listeningToggled++, () => { }, () => { }, () => { },
            (category, message) => shellLog.Add($"{category}: {message}"));
        var conflict = shell.Configure(new AppSettings { ListeningShortcut = "Ctrl+Alt+F10", PauseShortcut = "Ctrl+Alt+F10", VisibilityShortcut = "" });
        Require(conflict.Contains("conflict", StringComparison.OrdinalIgnoreCase), "Duplicate shortcuts must report a conflict.");
        Require(shellLog.Any(line => line.StartsWith("SETTINGS: Shortcut conflict", StringComparison.Ordinal)), "A shortcut conflict must be written to the diagnostic log.");
        shell.Configure(new AppSettings { ListeningShortcut = "Ctrl+Alt+F10", PauseShortcut = "Ctrl+Alt+F11", VisibilityShortcut = "" });
        var hotkeyId = new IntPtr(0x5201);
        _ = SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, 0x0312, hotkeyId, IntPtr.Zero);
        Require(listeningToggled == 1, "A registered shortcut must run its action.");
        Require(shellLog.Contains("SHORTCUT: Start / stop listening (Ctrl+Alt+F10)"), "A shortcut press must be written to the diagnostic log with the action and key, so a bug report can tell L from P.");

        // First run: with no speech model installed, no tab may say "ready" or "listening" as if captions will appear.
        _viewModel.SetModelAvailability(false, "ggml-base.bin", 147_951_465);
        _viewModel.ApplyCapture(new AudioCaptureSnapshot { State = AudioCaptureState.Capturing, Mode = CaptureMode.VrchatProcessOnly });
        Require(_viewModel.ListeningSummary == SpeechModelNotice.ListeningHeadline && _viewModel.ListeningDetail.Contains("DOWNLOAD SELECTED MODEL", StringComparison.Ordinal),
            "While capturing with no speech model, the header must say captions need the model and where to download it.");
        _viewModel.SetModelAvailability(true, "ggml-base.bin", 147_951_465);
        Require(_viewModel.ListeningSummary == "Listening to VRChat only", "Once the model is installed the header must return to the normal listening text without a restart.");
        _viewModel.ApplyCapture(new AudioCaptureSnapshot());
        _viewModel.ApplyDetection(new(true, 4242, null, DateTimeOffset.UtcNow, null));
        _viewModel.SetModelAvailability(false, "ggml-base.bin", 147_951_465);
        Require(_viewModel.ListeningSummary == SpeechModelNotice.ReadyHeadline, "Before listening, a missing model must replace the 'ready to listen' text.");
        _viewModel.SetModelAvailability(true, "ggml-base.bin", 147_951_465);
        Require(_viewModel.ListeningSummary == "VRChat detected · ready to listen", "An installed model restores the normal 'ready to listen' text.");
        _viewModel.ApplyDetection(new(false, null, null, DateTimeOffset.UtcNow, null));
        SetSelectedModelAvailability();

        // Fast English captions: off by default, honest about what is needed, and never claims to be on without the model.
        Require(!_settings.FastEnglishEngine && !_viewModel.FastEnglishEnabled, "The fast English engine must be off by default.");
        _viewModel.SetFastEnglishAvailability(false, 182_000_000);
        Require(_viewModel.FastEnglishStatus.StartsWith("Off", StringComparison.Ordinal) && _viewModel.CanDownloadFastEnglish && _viewModel.FastEnglishButtonText.Contains("182 MB", StringComparison.Ordinal),
            "With the switch off and no model, Settings must say it is off and offer the download with its size.");
        _viewModel.FastEnglishEnabled = true;
        Require(_viewModel.FastEnglishStatus.Contains("download", StringComparison.OrdinalIgnoreCase) && _viewModel.FastEnglishStatus.Contains("Whisper", StringComparison.Ordinal),
            "Switched on without the model, Settings must say the download is needed and that Whisper handles everything until then.");
        _viewModel.SetFastEnglishAvailability(true, 0);
        Require(_viewModel.FastEnglishStatus.StartsWith("On.", StringComparison.Ordinal) && !_viewModel.CanDownloadFastEnglish, "With the model installed and the switch on, Settings must say it is on.");
        var previousSource = _viewModel.SourceLanguage;
        _viewModel.SourceLanguage = _viewModel.SourceLanguages.First(l => l.Code == "ja");
        Require(_viewModel.FastEnglishStatus.Contains("locked", StringComparison.Ordinal), "A spoken language locked to Japanese must be explained as using Whisper.");
        _viewModel.SourceLanguage = previousSource;
        _viewModel.FastEnglishEnabled = false;
        RefreshFastEnglishAvailability();
        _overlay.Close();
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
