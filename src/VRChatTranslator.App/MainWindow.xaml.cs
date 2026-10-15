using System.IO;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using VRChatTranslator.App.ViewModels;
using VRChatTranslator.App.Services;
using VRChatTranslator.Audio;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.App;

public partial class MainWindow : Window
{
    private readonly VrchatProcessDetector _processDetector = new();
    private readonly IProcessAudioCaptureService _audioCapture;
    private readonly DiagnosticAudioSnapshotService _diagnosticAudio = new();
    private readonly LocalWhisperModelManager _modelManager = new();
    private readonly ILocalSpeechService _speech = new LocalWhisperSpeechService();
    private readonly ICaptionHub _captionHub = new CaptionHub();
    private readonly MainWindowViewModel _viewModel = new();
    private readonly OverlayWindow _overlay = new();
    private readonly PhoneDisplayServer _phoneDisplay;
    private readonly SystemPerformanceMonitor _performanceMonitor;
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore();
    private readonly DiagnosticLog _log = new();
    private AppSettings _settings = new();
    private bool _listeningRequested;
    private PerformanceMode _selectedMode = PerformanceMode.Balanced;
    private int _heavyLoadSamples;
    private PerformanceSnapshot? _lastPerformanceSnapshot;
    private bool _cleanupStarted;
    private bool _cleanupComplete;
    private readonly bool _skipLifecycleCleanup;
    private int? _lastLoggedVrchatPid;
    private AudioCaptureState? _lastLoggedCaptureState;
    private SpeechPipelineState? _lastLoggedSpeechState;
    private int _performanceLogSamples;
    private bool _modelNoticeShown;
    private DateTimeOffset _lastSkipOverlayAt = DateTimeOffset.MinValue;

    public MainWindow(bool skipLifecycleCleanup = false)
    {
        _skipLifecycleCleanup = skipLifecycleCleanup;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            throw new PlatformNotSupportedException("VRChat-only audio capture requires Windows 10 build 20348 or later.");
        }

        _audioCapture = new WasapiProcessAudioCaptureService();
        _phoneDisplay = new PhoneDisplayServer(_captionHub, GetPhoneStatus);
        _performanceMonitor = new SystemPerformanceMonitor(() => _processDetector.Current.ProcessId);
        InitializeComponent();
        DataContext = _viewModel;
        InitializeDesktopFeatures();
        InitializeFastEnglish();
        _speech.TransformCaptionAsync = TranslateIncomingCaptionAsync;
        _processDetector.DetectionChanged += OnDetectionChanged;
        _audioCapture.CaptureChanged += OnCaptureChanged;
        _speech.StateChanged += OnSpeechStateChanged;
        _speech.CaptionProduced += OnCaptionProduced;
        _speech.SpeechSkipped += OnSpeechSkipped;
        _captionHub.CaptionPublished += OnCaptionPublished;
        _performanceMonitor.SnapshotAvailable += OnPerformanceSnapshot;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_skipLifecycleCleanup)
        {
            return;
        }

        try
        {
            try
            {
                _settings = await _settingsStore.LoadAsync();
            }
            catch (Exception exception)
            {
                await _log.WriteAsync("SETTINGS", $"Malformed or unreadable config; safe defaults loaded. {exception.Message}");
                _settings = new AppSettings();
            }

            if (_cleanupStarted) return;
            _selectedMode = _settings.SelectedMode;
            _viewModel.LoadDesktopSettings(_settings);
            LoadHeadsetPlacement(_settings.Headset);
            _settings = _viewModel.CaptureDesktopSettings(_settings);
            _viewModel.ApplyCustomSettings(_settings.CustomMode);
            _viewModel.SelectedPreset = PerformancePreset.BuiltIn.FirstOrDefault(preset => preset.Mode == _selectedMode)
                ?? PerformancePreset.BuiltIn.Single(preset => preset.Mode == PerformanceMode.Balanced);
            await _processDetector.StartAsync();
            if (_cleanupStarted) return;
            _viewModel.ApplyDetection(_processDetector.Current);
            RefreshOscStatus();
            _oscTimer.Start();
            SetSelectedModelAvailability();
            _overlay.Owner = this;
            ApplyOverlaySettings(_settings.Overlay);
            _overlay.Show();
            _overlay.ApplyPreferences(_settings.Overlay, _settings.ShowOriginalText);
            _overlay.EnsureVisiblePlacement();
            if (!_settings.Overlay.Enabled) _overlay.Hide();
            StartDesktopShell();
            _performanceMonitor.Start();
            ApplyHeadsetAutoConnectPreference();
            var version =typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            await _log.WriteAsync("STARTUP", $"Version {version} started; mode={_selectedMode}; privacy=local-only.");
        }
        catch (Exception exception)
        {
            _viewModel.ApplyDetection(new(
                false,
                null,
                null,
                DateTimeOffset.UtcNow,
                $"Process detection failed to start: {exception.Message}"));
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_skipLifecycleCleanup)
        {
            return;
        }

        if (_cleanupComplete)
        {
            return;
        }

        if (!_explicitQuit && !_cleanupStarted && _viewModel.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        e.Cancel = true;
        if (_cleanupStarted)
        {
            return;
        }

        _cleanupStarted = true;
        _oscTimer.Stop();
        _desktopCancellation.Cancel();
        _shell?.Dispose();
        _settings = _settings with { Headset = ReadHeadsetPlacement() };
        _headset.Dispose();
        _viewModel.PropertyChanged -= OnDesktopPreferenceChanged;
        _listeningRequested = false;
        _processDetector.DetectionChanged -= OnDetectionChanged;
        _audioCapture.CaptureChanged -= OnCaptureChanged;
        _speech.StateChanged -= OnSpeechStateChanged;
        _speech.CaptionProduced -= OnCaptionProduced;
        _speech.SpeechSkipped -= OnSpeechSkipped;
        _captionHub.CaptionPublished -= OnCaptionPublished;
        _performanceMonitor.SnapshotAvailable -= OnPerformanceSnapshot;
        await _preferencesGate.WaitAsync();
        _settings = _viewModel.CaptureDesktopSettings(_settings);
        _settings = _settings with
        {
            SelectedMode = _selectedMode,
            CustomMode = _viewModel.GetCustomSettings(),
            Overlay = _settings.Overlay with
            {
                Left = _overlay.Left,
                Top = _overlay.Top,
                Width = _overlay.Width,
                Height = _overlay.Height,
                ClickThrough = _overlay.IsClickThrough
            }
        };
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            await _log.WriteAsync("SETTINGS", $"Could not save settings: {exception.Message}");
        }
        finally { _preferencesGate.Release(); }

        await TryCleanupAsync("performance monitor", async () => await _performanceMonitor.DisposeAsync());
        await TryCleanupAsync("phone display", async () => await _phoneDisplay.DisposeAsync());
        try
        {
            _overlay.Close();
        }
        catch (Exception exception)
        {
            await _log.WriteAsync("SHUTDOWN", $"Could not close overlay: {exception.Message}");
        }

        await TryCleanupAsync("speech pipeline", async () => await _speech.DisposeAsync());
        await TryCleanupAsync("fast English engine", () => { ReleaseFastEnglish(); return Task.CompletedTask; });
        await TryCleanupAsync("local text translation", async () => await _textTranslator.DisposeAsync());
        _replySender.Dispose();
        await TryCleanupAsync("audio capture", async () => await _audioCapture.DisposeAsync());
        await TryCleanupAsync("process detector", async () => await _processDetector.DisposeAsync());
        await _log.WriteAsync("SHUTDOWN", "Services stopped cleanly; temporary caption history discarded.");
        _cleanupComplete = true;
        Close();
    }

    private void OnDetectionChanged(object? sender, VrchatDetectionChangedEventArgs e)
    {
        if (_lastLoggedVrchatPid != e.Snapshot.ProcessId)
        {
            _lastLoggedVrchatPid = e.Snapshot.ProcessId;
            _ = _log.WriteAsync("VRCHAT", e.Snapshot.IsDetected
                ? $"Detected VRChat PID {e.Snapshot.ProcessId}."
                : "VRChat not running.");
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            _viewModel.ApplyDetection(e.Snapshot);
            RefreshOscStatus();
            _ = ReconcileCaptureAsync(e.Snapshot);
        });
    }

    private void OnCaptureChanged(object? sender, AudioCaptureChangedEventArgs e)
    {
        if (_lastLoggedCaptureState != e.Snapshot.State)
        {
            _lastLoggedCaptureState = e.Snapshot.State;
            _ = _log.WriteAsync("AUDIO", $"State={e.Snapshot.State}; mode={e.Snapshot.Mode}; error={e.Snapshot.LastError ?? "none"}.");
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            _viewModel.ApplyCapture(e.Snapshot);
            _shell?.SetStatus(e.Snapshot.State == AudioCaptureState.Capturing ? "listening" : (_paused ? "paused" : "stopped"));
            _headset.SetStatus(e.Snapshot.State == AudioCaptureState.Capturing ? "Listening" : (_paused ? "Paused" : "Stopped"));
            _ = ReconcileSpeechAsync(e.Snapshot);
        });
    }

    private async void OnToggleCaptureClick(object sender, RoutedEventArgs e)
    {
        if (_cleanupStarted) return;
        _paused = false;
        if (_audioCapture.Current.IsActive)
        {
            _listeningRequested = false;
            await _audioCapture.StopAsync();
            return;
        }

        var detection = _processDetector.Current;
        if (!detection.IsDetected || detection.ProcessId is null)
        {
            return;
        }

        _listeningRequested = true;
        await TryStartCaptureAsync(detection.ProcessId.Value);
    }

    private async Task ReconcileCaptureAsync(VrchatDetectionSnapshot detection)
    {
        var capture = _audioCapture.Current;
        if (capture.IsActive && capture.TargetProcessId != detection.ProcessId)
        {
            await _audioCapture.StopAsync();
        }

        if (!_cleanupStarted && !_paused && _listeningRequested && detection.IsDetected && detection.ProcessId is not null)
        {
            await TryStartCaptureAsync(detection.ProcessId.Value);
        }
    }

    private async Task TryStartCaptureAsync(int processId)
    {
        try
        {
            await _audioCapture.StartAsync(processId);
        }
        catch
        {
            // The capture service publishes the full actionable error to diagnostics.
        }
    }

    private async Task ReconcileSpeechAsync(AudioCaptureSnapshot capture)
    {
        if (_cleanupStarted || _reconfiguring) return;
        capture = _audioCapture.Current;
        if (capture.State == AudioCaptureState.Capturing &&
            _modelManager.IsModelInstalled(GetEffectiveModelMode()) &&
            _speech.Current.State is SpeechPipelineState.Stopped or SpeechPipelineState.ModelMissing or SpeechPipelineState.Faulted)
        {
            try
            {
                ConfigureFastEnglish();
                _speech.AudioContextSize = GetRuntimeProfile().AudioContextSize;
                var profile = GetRuntimeProfile();
                await _speech.StartAsync(
                    _audioCapture,
                    _modelManager.GetModelPath(GetEffectiveModelMode()),
                    profile.CpuThreads,
                    showOriginalText: _settings.ShowOriginalText,
                    vadThreshold: profile.VadThreshold,
                    maximumSegmentSeconds: profile.MaximumSegmentSeconds,
                    maximumQueueLength: profile.MaximumQueueLength,
                    translateToEnglish: _settings.SourceLanguage != _settings.TargetLanguage && (_selectedMode != PerformanceMode.Custom || _viewModel.CustomTranslation),
                    sourceLanguage: _settings.SourceLanguage,
                    vocabulary: string.Join(", ", _settings.CustomVocabulary), cancellationToken: _desktopCancellation.Token);
            }
            catch
            {
                // Full local model errors are published through speech diagnostics.
            }
        }
        else if ((capture.State is AudioCaptureState.Stopped or AudioCaptureState.Stopping or AudioCaptureState.Faulted) &&
                 _speech.Current.State != SpeechPipelineState.Stopped)
        {
            await _speech.StopAsync();
        }

        ShowFirstRunModelNotice(capture);
    }

    /// <summary>
    /// With no speech model installed, listening produces no captions and nothing else says why. Once per listening session,
    /// log it and open Settings, where the download button is. The Live header already explains it on every tab.
    /// </summary>
    private void ShowFirstRunModelNotice(AudioCaptureSnapshot capture)
    {
        var mode = GetEffectiveModelMode();
        if (capture.State != AudioCaptureState.Capturing || _modelManager.IsModelInstalled(mode))
        {
            _modelNoticeShown = false;
            return;
        }

        if (_modelNoticeShown)
        {
            return;
        }

        _modelNoticeShown = true;
        var fileName = Path.GetFileName(_modelManager.GetModelPath(mode));
        _ = _log.WriteAsync("SPEECH", $"Speech model {fileName} is not installed, so there are no captions yet. Settings, DOWNLOAD SELECTED MODEL, fixes it.");
        _viewModel.OperationStatus = "No speech model installed yet. Click DOWNLOAD SELECTED MODEL below; captions start when it finishes.";
        MainTabs.SelectedItem = SettingsTab;
    }

    private void OnSpeechStateChanged(object? sender, SpeechPipelineChangedEventArgs e)
    {
        if (_lastLoggedSpeechState != e.Snapshot.State)
        {
            _lastLoggedSpeechState = e.Snapshot.State;
            _ = _log.WriteAsync("SPEECH", $"State={e.Snapshot.State}; model={e.Snapshot.ModelName}; backend={e.Snapshot.Backend}; voice={e.Snapshot.VoiceDetection}; error={e.Snapshot.LastError ?? "none"}.");
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            _viewModel.ApplySpeech(e.Snapshot);
            TimingDetails.Text = $"Queue: {e.Snapshot.QueueDelay?.TotalSeconds:0.00}s · End of speech to caption: {e.Snapshot.EndToCaptionDelay?.TotalSeconds:0.00}s · Suppressed: {e.Snapshot.SuppressedCaptions} · Skipped speech: {e.Snapshot.SkippedSpeechSeconds:0.0}s";
        });
    }

    private void OnSpeechSkipped(object? sender, SpeechSkippedEventArgs e)
    {
        var total = _speech.Current.SkippedSpeechSeconds;
        var message = SkippedSpeechNotice.Describe(e.Skipped, total);
        _ = _log.WriteAsync("SPEECH", message);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_cleanupStarted) return;
            _viewModel.OperationStatus = message;
            var now = DateTimeOffset.UtcNow;
            if (_viewModel.OverlayEnabled && SkippedSpeechNotice.ShouldShowOverlay(_lastSkipOverlayAt, now))
            {
                _lastSkipOverlayAt = now;
                _overlay.ShowSkipNotice(SkippedSpeechNotice.OverlayText);
            }
        });
    }

    private void OnCaptionProduced(object? sender, CaptionProducedEventArgs e)
    {
        if (_cleanupStarted || _audioCapture.Current.State != AudioCaptureState.Capturing) return;
        _captionHub.Publish(e.Caption with
        {
            OriginalText = WordCorrectionService.Apply(e.Caption.OriginalText, _settings.WordCorrections, CorrectionScope.Original),
            TranslatedText = WordCorrectionService.Apply(e.Caption.TranslatedText, _settings.WordCorrections, CorrectionScope.Translated)
        });
    }

    private void OnCaptionPublished(object? sender, CaptionProducedEventArgs e)
    {
        var generation = Interlocked.Read(ref _historyGeneration);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_cleanupStarted && generation == Interlocked.Read(ref _historyGeneration)) PresentDesktopCaption(e.Caption);
        });
    }

    private async void OnPhoneDisplayClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_phoneDisplay.IsRunning)
            {
                await _phoneDisplay.StopAsync();
                _viewModel.SetPhoneDisplay(false, "Local network access is disabled.");
                await _log.WriteAsync("PHONE", "Local phone display stopped.");
            }
            else
            {
                await _phoneDisplay.StartAsync(_settings.PhoneDisplay.Port);
                _viewModel.SetPhoneDisplay(true, string.Join(" · ", _phoneDisplay.DisplayUrls));
                await _log.WriteAsync("PHONE", $"Local phone display started on port {_phoneDisplay.Port}.");
            }
        }
        catch (Exception exception)
        {
            _viewModel.SetPhoneDisplay(false, $"Phone display failed: {exception.Message}");
            await _log.WriteAsync("PHONE", $"Phone display failed: {exception.Message}");
        }
    }

    private void OnFakeCaptionClick(object sender, RoutedEventArgs e)
    {
        _captionHub.Publish(new CaptionEvent(
            DateTimeOffset.UtcNow,
            "ja",
            "Japanese",
            "こんにちは、VRChat！",
            "Hello, VRChat! This test reaches the app, overlay, and phone.",
            1,
            TimeSpan.FromMilliseconds(125)));
    }

    private PhoneStatusSnapshot GetPhoneStatus()
    {
        var detection = _processDetector.Current;
        var capture = _audioCapture.Current;
        var speech = _speech.Current;
        return new PhoneStatusSnapshot(
            detection.IsDetected,
            capture.State == AudioCaptureState.Capturing,
            speech.State.ToString(),
            speech.DetectedLanguage,
            speech.TranscriptionLatency?.TotalMilliseconds,
            speech.QueueLength,
            capture.PeakLevel * 100,
            speech.ModelName,
            _viewModel.SelectedPreset.DisplayName,
            _lastPerformanceSnapshot?.TranslatorCpuPercent,
            _lastPerformanceSnapshot?.SystemCpuPercent,
            _lastPerformanceSnapshot?.GpuPercent,
            PhoneStatusRedactor.Redact(capture.LastError ?? speech.LastError));
    }

    private async void OnPerformanceModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = _viewModel.SelectedPreset;
        if (_selectedMode == selected.Mode)
        {
            return;
        }

        _selectedMode = selected.Mode;
        SetSelectedModelAvailability();
        if (_audioCapture.Current.State == AudioCaptureState.Capturing)
        {
            await _speech.StopAsync();
            await ReconcileSpeechAsync(_audioCapture.Current);
        }
    }

    private void OnPerformanceSnapshot(object? sender, PerformanceSnapshot snapshot)
    {
        _lastPerformanceSnapshot = snapshot;
        _performanceLogSamples++;
        if (_performanceLogSamples >= 30)
        {
            _performanceLogSamples = 0;
            _ = _log.WriteAsync("PERFORMANCE", $"systemCpu={snapshot.SystemCpuPercent:0.0}%; translatorCpu={snapshot.TranslatorCpuPercent:0.0}%; translatorRamMiB={snapshot.TranslatorMemoryBytes / 1024d / 1024d:0}; queue={_speech.Current.QueueLength}; latencyMs={_speech.Current.TranscriptionLatency?.TotalMilliseconds:0}; fastEnglishSegments={_speech.Current.FastEnglishSegments}; whisperSegments={_speech.Current.WhisperSegments}.");
        }
        var heavy = snapshot.SystemCpuPercent >= 90 || _speech.Current.QueueLength >= 2;
        _heavyLoadSamples = heavy ? _heavyLoadSamples + 1 : 0;
        _ = Dispatcher.InvokeAsync(() => _viewModel.ApplyPerformance(snapshot, _heavyLoadSamples >= 3));
    }

    private void ApplyOverlaySettings(OverlaySettings settings)
    {
        _overlay.Width = Math.Clamp(settings.Width, 420, 1800);
        _overlay.Height = Math.Clamp(settings.Height, 100, 600);
        if (settings.Left is >= -200 and <= 10_000)
        {
            _overlay.Left = settings.Left.Value;
        }

        if (settings.Top is >= -200 and <= 10_000)
        {
            _overlay.Top = settings.Top.Value;
        }

        _overlay.Topmost = settings.AlwaysOnTop;
        _overlay.SetClickThrough(settings.ClickThrough);
    }

    private async void OnDownloadModelClick(object sender, RoutedEventArgs e)
    {
        var effectiveMode = GetEffectiveModelMode();
        if (_modelManager.IsModelInstalled(effectiveMode))
        {
            return;
        }

        var progress = new Progress<ModelDownloadProgress>(value =>
            _viewModel.SetModelDownloadProgress(value.Percentage, "Downloading local multilingual model"));
        try
        {
            await _modelManager.DownloadModelAsync(effectiveMode, progress);
            SetSelectedModelAvailability();
            _viewModel.OperationStatus = "Speech model downloaded and verified. Captions start as soon as VRChat audio is being captured.";
            _ = _log.WriteAsync("SPEECH", $"Speech model {Path.GetFileName(_modelManager.GetModelPath(effectiveMode))} downloaded and verified.");
            await ReconcileSpeechAsync(_audioCapture.Current);
        }
        catch (Exception exception)
        {
            _viewModel.SetModelDownloadProgress(null, $"Model download failed: {exception.Message}");
            SetSelectedModelAvailability();
        }
    }

    private async void OnSaveAudioTestClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save 10 Second VRChat Audio Test",
            Filter = "Wave audio (*.wav)|*.wav",
            DefaultExt = ".wav",
            AddExtension = true,
            FileName = $"vrchat-audio-test-{DateTime.Now:yyyyMMdd-HHmmss}.wav"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _viewModel.SetAudioTestState(true, "Recording exactly 10 seconds of inference input…");
        try
        {
            await _diagnosticAudio.SaveAsync(_audioCapture, dialog.FileName);
            _viewModel.SetAudioTestState(false, $"Saved diagnostic WAV: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            _viewModel.SetAudioTestState(false, $"Audio test failed: {exception.Message}");
        }
    }

    private async void OnApplyCustomClick(object sender, RoutedEventArgs e)
    {
        if (_selectedMode != PerformanceMode.Custom)
        {
            return;
        }

        SetSelectedModelAvailability();
        if (_audioCapture.Current.State == AudioCaptureState.Capturing)
        {
            await _speech.StopAsync();
            await ReconcileSpeechAsync(_audioCapture.Current);
        }
    }

    private PerformanceMode GetEffectiveModelMode() => _selectedMode != PerformanceMode.Custom
        ? _selectedMode
        : _viewModel.CustomModel switch
        {
            "Tiny multilingual" => PerformanceMode.UltraLight,
            "Small multilingual" => PerformanceMode.Accurate,
            _ => PerformanceMode.Balanced
        };

    private PresetRuntimeProfile GetRuntimeProfile()
    {
        if (_selectedMode != PerformanceMode.Custom)
        {
            return PresetRuntimeProfile.For(_selectedMode);
        }

        return new PresetRuntimeProfile(
            PerformanceMode.Custom,
            Path.GetFileName(_modelManager.GetModelPath(GetEffectiveModelMode())),
            _viewModel.CustomThreads,
            _viewModel.CustomVad,
            _viewModel.CustomChunkSeconds,
            _viewModel.CustomQueue,
            _viewModel.CustomBackend);
    }

    private void SetSelectedModelAvailability()
    {
        var effectiveMode = GetEffectiveModelMode();
        var path = _modelManager.GetModelPath(effectiveMode);
        _viewModel.SetModelAvailability(_modelManager.IsModelInstalled(effectiveMode), Path.GetFileName(path), _modelManager.GetExpectedBytes(effectiveMode));
    }

    private async Task TryCleanupAsync(string service, Func<Task> cleanup)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            await _log.WriteAsync("SHUTDOWN", $"Could not stop {service}: {exception.Message}");
        }
    }
}
