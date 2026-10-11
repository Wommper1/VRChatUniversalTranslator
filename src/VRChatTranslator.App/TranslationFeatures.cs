using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.App;

public partial class MainWindow
{
    private readonly LocalTextTranslator _textTranslator = new();
    private readonly OscReplySender _replySender = new();
    private readonly CancellationTokenSource _desktopCancellation = new();
    private bool _languageToolsBusy;
    private readonly DispatcherTimer _oscTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private CancellationTokenSource? _languageActionCancellation;
    private CancellationToken LanguageActionToken => _languageActionCancellation?.Token ?? _desktopCancellation.Token;

    private async Task<CaptionEvent> TranslateIncomingCaptionAsync(CaptionEvent caption, CancellationToken token)
    {
        var settings = _settings;
        if (_selectedMode == PerformanceMode.Custom && settings.CustomMode.CaptionDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(settings.CustomMode.CaptionDelaySeconds), token);
        if (_selectedMode == PerformanceMode.Custom && !settings.CustomMode.TranslationEnabled) return caption;
        return await CaptionOutputTranslation.ApplyAsync(caption, settings.TargetLanguage,
            (text, from, to, cancellation) => _textTranslator.TranslateAsync(text, from, to, cancellation), token);
    }

    private void InitializeTranslationControls()
    {
        ReplySource.ItemsSource = LanguageCatalog.Languages;
        ReplyTarget.ItemsSource = LanguageCatalog.Languages;
        ReplySource.SelectedItem = LanguageCatalog.Languages.First(l => l.Code == "en");
        ReplyTarget.SelectedItem = LanguageCatalog.Languages.First(l => l.Code == "es");
        RefreshLanguageTools();
        _oscTimer.Tick += (_, _) => RefreshOscStatus();
        RefreshOscStatus();
    }

    private void OnReplyConsentChanged(object sender, RoutedEventArgs e) => RefreshOscStatus();

    /// <summary>Shows whether a reply could reach VRChat. The port is only probed while VRChat is running.</summary>
    private void RefreshOscStatus()
    {
        var running = _processDetector.Current.IsDetected;
        var status = OscStatusEvaluator.Evaluate(ReplyConsent.IsChecked == true, running,
            running ? OscStatusEvaluator.ProbeLocalListener() : null, _replySender.LastOutcome);
        OscStatusText.Text = status.Text;
        OscStatusText.Foreground = (Brush)FindResource(status.IsWarning ? "WarningBrush" : "MutedTextBrush");
    }

    private void RefreshLanguageTools()
    {
        LanguageToolsStatus.Text = (_textTranslator.IsRuntimeReady ? "Local language tools ready. " : "English captions work without extra tools. Other output languages and replies need a one-time setup. ") + _textTranslator.InstalledSummary;
        PackSelectionStatus.Text = _textTranslator.OutputPackStatus(_viewModel.TargetLanguage.Code);
        LivePackStatus.Text = PackSelectionStatus.Text;
        InstalledPackList.Text = _textTranslator.InstalledPairs is { Count: > 0 } pairs
            ? string.Join(Environment.NewLine, pairs) : "No additional packs downloaded yet.";
        PackDownloadButton.IsEnabled = _viewModel.TargetLanguage.Code != "en";
        PackPreloadButton.IsEnabled = _viewModel.TargetLanguage.Code != "en" && _textTranslator.IsRuntimeReady && _textTranslator.CanTranslate("en", _viewModel.TargetLanguage.Code);
    }

    private IProgress<string> LanguageProgress() => new Progress<string>(status =>
    {
        if (!_languageToolsBusy) return;
        _viewModel.OperationStatus = PackDownloadStatus.Text = status;
    });

    private void OnOpenLanguagePacksClick(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = SettingsTab;
        Dispatcher.BeginInvoke(() => { LanguagePacksPanel.BringIntoView(); PackLanguageSelector.Focus(); });
    }

    private void OnCancelLanguageActionClick(object sender, RoutedEventArgs e)
    {
        _languageActionCancellation?.Cancel();
        PackDownloadStatus.Text = "Canceling… Installed packs are kept.";
    }

    private async Task RunLanguageActionAsync(Func<Task> action)
    {
        if (_languageToolsBusy || _cleanupStarted) return;
        _languageToolsBusy = true;
        _languageActionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_desktopCancellation.Token);
        ReplyActions.IsEnabled = false;
        PackActions.IsEnabled = false;
        PackCancelButton.IsEnabled = true;
        try { await action(); }
        catch (OperationCanceledException) { if (!_cleanupStarted) _viewModel.OperationStatus = "Language action canceled."; }
        catch (Exception ex) { _viewModel.OperationStatus = ex.Message; }
        finally
        {
            _languageToolsBusy = false; ReplyActions.IsEnabled = true; PackActions.IsEnabled = true; PackCancelButton.IsEnabled = false;
            PackDownloadStatus.Text = _viewModel.OperationStatus;
            _languageActionCancellation.Dispose(); _languageActionCancellation = null;
            RefreshLanguageTools();
        }
    }

    private async void OnSetupLanguageToolsClick(object sender, RoutedEventArgs e) => await RunLanguageActionAsync(async () =>
    {
        await _textTranslator.SetupAsync(LanguageProgress(), LanguageActionToken);
        _viewModel.OperationStatus = "Language tools installed. Now install the language pair you need.";
    });

    private async void OnInstallOutputClick(object sender, RoutedEventArgs e) => await RunLanguageActionAsync(async () =>
    {
        var target = _viewModel.TargetLanguage.Code;
        if (target == "en") { _viewModel.OperationStatus = _textTranslator.OutputPackStatus(target); return; }
        if (!_textTranslator.IsRuntimeReady) await _textTranslator.SetupAsync(LanguageProgress(), LanguageActionToken);
        await _textTranslator.InstallPairAsync("en", target, LanguageProgress(), LanguageActionToken);
        _viewModel.OperationStatus = PackDownloadStatus.Text = "Loading the downloaded pack into memory…";
        await _textTranslator.PreloadAsync("en", target, LanguageActionToken);
        _viewModel.OperationStatus = $"{LanguageCatalog.GetName(target)} pack downloaded and preloaded. Choose Apply on Live to use your selected output language.";
    });

    private async void OnPreloadPackClick(object sender, RoutedEventArgs e) => await RunLanguageActionAsync(async () =>
    {
        var target = _viewModel.TargetLanguage.Code;
        _viewModel.OperationStatus = PackDownloadStatus.Text = "Loading selected language pack into memory…";
        await _textTranslator.PreloadAsync("en", target, LanguageActionToken);
        _viewModel.OperationStatus = target == "en" ? _textTranslator.OutputPackStatus(target) : $"{LanguageCatalog.GetName(target)} pack preloaded. First-caption model loading is now avoided while it remains cached.";
    });

    private async Task<string> PreloadOutputIfRequestedAsync(AppSettings settings)
    {
        if (!settings.PreloadOutputLanguage || settings.TargetLanguage == "en" || settings.SourceLanguage == settings.TargetLanguage ||
            (_selectedMode == PerformanceMode.Custom && !settings.CustomMode.TranslationEnabled)) return "";
        if (_languageToolsBusy) return "Pack preload skipped while a language action is running.";
        if (!_textTranslator.IsRuntimeReady || !_textTranslator.CanTranslate("en", settings.TargetLanguage))
            return "Download the selected pack in Settings → Offline language packs to enable preloading.";
        try
        {
            _viewModel.OperationStatus = "Preloading output language…";
            await _textTranslator.PreloadAsync("en", settings.TargetLanguage, _desktopCancellation.Token);
            return "Output pack preloaded.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return "Pack preload unavailable: " + ex.Message; }
    }

    private async void OnInstallReplyPairClick(object sender, RoutedEventArgs e) => await RunLanguageActionAsync(async () =>
    {
        if (ReplySource.SelectedItem is not LanguageChoice source || ReplyTarget.SelectedItem is not LanguageChoice target) return;
        await _textTranslator.InstallPairAsync(source.Code, target.Code, LanguageProgress(), LanguageActionToken);
        _viewModel.OperationStatus = "Reply language pair is ready offline.";
    });

    private async void OnTranslateReplyClick(object sender, RoutedEventArgs e) => await RunLanguageActionAsync(async () =>
    {
        if (ReplySource.SelectedItem is not LanguageChoice source || ReplyTarget.SelectedItem is not LanguageChoice target) return;
        var text = ReplyInput.Text;
        if (string.IsNullOrWhiteSpace(text)) { _viewModel.OperationStatus = "Type a reply first."; return; }
        ReplyPreview.Clear(); // Never leave an old translation looking like the current preview after failure.
        ReplyPreview.Text = await _textTranslator.TranslateAsync(text, source.Code, target.Code, LanguageActionToken);
        _viewModel.OperationStatus = $"Preview: {source.Name} → {target.Name}. Review or edit it before sending.";
    });

    private async void OnSendReplyClick(object sender, RoutedEventArgs e)
    {
        if (_languageToolsBusy || _cleanupStarted) return;
        if (ReplyConsent.IsChecked != true) { _viewModel.OperationStatus = "Enable explicit OSC sending below the preview first."; return; }
        try
        {
            await _replySender.SendAsync(ReplyPreview.Text, _desktopCancellation.Token);
            _viewModel.OperationStatus = "Sent to local VRChat OSC. Delivery is not confirmed; check your chatbox. Text has been kept.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _viewModel.OperationStatus = "Reply not sent: " + ex.Message; }
        RefreshOscStatus();
    }

    private void OnCopyReplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ReplyPreview.Text)) return;
        try { Clipboard.SetText(ReplyPreview.Text); _viewModel.OperationStatus = "Reply preview copied."; }
        catch { _viewModel.OperationStatus = "Clipboard busy. Try again."; }
    }
}
