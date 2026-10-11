using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRChatTranslator.Core.Models;
using VRChatTranslator.App.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.App.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private bool _isVrchatDetected;
    private bool _hasDetectionError;
    private string _vrchatStatusText = "Scanning for VRChat…";
    private string _vrchatProcessIdText = "—";
    private string _listeningSummary = "Waiting for VRChat…";
    private string _listeningDetail = "Launch VRChat to enable verified process-only audio capture.";
    private string _detectionDiagnostic = "Starting";
    private AudioCaptureSnapshot _captureSnapshot = new();
    private string _audioCaptureStateText = "Stopped";
    private string _captureModeText = "Unavailable";
    private string _audioFormatText = "—";
    private string _audioLevelText = "0%";
    private double _audioLevelPercent;
    private string _droppedChunksText = "0";
    private string _lastAudioErrorText = "None";
    private bool _canToggleCapture;
    private string _startButtonText = "START LISTENING";
    private bool _canSaveAudioTest;
    private string _audioTestStatus = "Developer tool · saves only when explicitly requested";
    private string _speechModelStatus = "Checking local model…";
    private string _speechBackend = "CPU";
    private string _detectedLanguage = "—";
    private string _speechLatency = "—";
    private string _speechQueue = "0";
    private string _currentCaption = "Waiting for speech…";
    private string _currentCaptionLanguage = "No caption yet";
    private bool _canDownloadModel;
    private string _modelButtonText = "DOWNLOAD BALANCED MODEL";
    private bool _isPhoneDisplayRunning;
    private string _phoneDisplayStatus = "Off";
    private string _phoneDisplayUrl = "Local network access stays disabled until you explicitly start it.";
    private string _phoneButtonText = "START PHONE DISPLAY";
    private PerformancePreset _selectedPreset = PerformancePreset.BuiltIn.Single(preset => preset.Mode == PerformanceMode.Balanced);
    private string _presetRuntimeDetail = "base multilingual · 4 CPU threads · bounded queue";
    private string _vrchatCpu = "—";
    private string _translatorCpu = "—";
    private string _systemCpu = "—";
    private string _translatorRam = "—";
    private string _systemRam = "—";
    private string _gpuUsage = "—";
    private string _gpuVram = "—";
    private string _performanceProtectionStatus = "Protect VRChat Performance is ON";
    private string _customModel = "Base multilingual";
    private string _customBackend = "CPU";
    private int _customThreads = 4;
    private int _customChunkSeconds = 6;
    private double _customVad = 0.008;
    private double _customCaptionDelay;
    private int _customQueue = 3;
    private bool _customTranslation = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<PerformancePreset> PerformancePresets { get; } = PerformancePreset.BuiltIn;
    public IReadOnlyList<string> CustomModels { get; } = ["Tiny multilingual", "Base multilingual", "Small multilingual"];
    public IReadOnlyList<string> CustomBackends { get; } = ["CPU"];

    public PerformancePreset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (ReferenceEquals(_selectedPreset, value))
            {
                return;
            }

            SetField(ref _selectedPreset, value);
            var profile = PresetRuntimeProfile.For(value.Mode);
            PresetRuntimeDetail = $"{profile.ModelFileName} · {profile.CpuThreads} threads · {profile.BackendPolicy}";
            OnPropertyChanged(nameof(IsCustomMode));
        }
    }

    public bool IsCustomMode => SelectedPreset.Mode == PerformanceMode.Custom;

    public string PresetRuntimeDetail
    {
        get => _presetRuntimeDetail;
        private set => SetField(ref _presetRuntimeDetail, value);
    }

    public string VrchatCpu { get => _vrchatCpu; private set => SetField(ref _vrchatCpu, value); }
    public string TranslatorCpu { get => _translatorCpu; private set => SetField(ref _translatorCpu, value); }
    public string SystemCpu { get => _systemCpu; private set => SetField(ref _systemCpu, value); }
    public string TranslatorRam { get => _translatorRam; private set => SetField(ref _translatorRam, value); }
    public string SystemRam { get => _systemRam; private set => SetField(ref _systemRam, value); }
    public string GpuUsage { get => _gpuUsage; private set => SetField(ref _gpuUsage, value); }
    public string GpuVram { get => _gpuVram; private set => SetField(ref _gpuVram, value); }
    public string PerformanceProtectionStatus
    {
        get => _performanceProtectionStatus;
        private set => SetField(ref _performanceProtectionStatus, value);
    }

    public string CustomModel { get => _customModel; set => SetField(ref _customModel, value); }
    public string CustomBackend { get => _customBackend; set => SetField(ref _customBackend, value); }
    public int CustomThreads { get => _customThreads; set => SetField(ref _customThreads, Math.Clamp(value, 1, 8)); }
    public int CustomChunkSeconds { get => _customChunkSeconds; set => SetField(ref _customChunkSeconds, Math.Clamp(value, 4, 20)); }
    public double CustomVad { get => _customVad; set => SetField(ref _customVad, Math.Clamp(value, 0.001, 0.08)); }
    public double CustomCaptionDelay { get => _customCaptionDelay; set => SetField(ref _customCaptionDelay, Math.Clamp(value, 0, 3)); }
    public int CustomQueue { get => _customQueue; set => SetField(ref _customQueue, Math.Clamp(value, 1, 5)); }
    public bool CustomTranslation { get => _customTranslation; set => SetField(ref _customTranslation, value); }

    public bool IsVrchatDetected
    {
        get => _isVrchatDetected;
        private set => SetField(ref _isVrchatDetected, value);
    }

    public bool HasDetectionError
    {
        get => _hasDetectionError;
        private set => SetField(ref _hasDetectionError, value);
    }

    public string VrchatStatusText
    {
        get => _vrchatStatusText;
        private set => SetField(ref _vrchatStatusText, value);
    }

    public string VrchatProcessIdText
    {
        get => _vrchatProcessIdText;
        private set => SetField(ref _vrchatProcessIdText, value);
    }

    public string ListeningSummary
    {
        get => _listeningSummary;
        private set => SetField(ref _listeningSummary, value);
    }

    public string ListeningDetail
    {
        get => _listeningDetail;
        private set => SetField(ref _listeningDetail, value);
    }

    public string DetectionDiagnostic
    {
        get => _detectionDiagnostic;
        private set => SetField(ref _detectionDiagnostic, value);
    }

    public string AudioCaptureStateText
    {
        get => _audioCaptureStateText;
        private set => SetField(ref _audioCaptureStateText, value);
    }

    public string CaptureModeText
    {
        get => _captureModeText;
        private set => SetField(ref _captureModeText, value);
    }

    public string AudioFormatText
    {
        get => _audioFormatText;
        private set => SetField(ref _audioFormatText, value);
    }

    public string AudioLevelText
    {
        get => _audioLevelText;
        private set => SetField(ref _audioLevelText, value);
    }

    public double AudioLevelPercent
    {
        get => _audioLevelPercent;
        private set => SetField(ref _audioLevelPercent, value);
    }

    public string DroppedChunksText
    {
        get => _droppedChunksText;
        private set => SetField(ref _droppedChunksText, value);
    }

    public string LastAudioErrorText
    {
        get => _lastAudioErrorText;
        private set => SetField(ref _lastAudioErrorText, value);
    }

    public bool CanToggleCapture
    {
        get => _canToggleCapture;
        private set => SetField(ref _canToggleCapture, value);
    }

    public string StartButtonText
    {
        get => _startButtonText;
        private set => SetField(ref _startButtonText, value);
    }

    public bool CanSaveAudioTest
    {
        get => _canSaveAudioTest;
        private set => SetField(ref _canSaveAudioTest, value);
    }

    public string AudioTestStatus
    {
        get => _audioTestStatus;
        private set => SetField(ref _audioTestStatus, value);
    }

    public string SpeechModelStatus
    {
        get => _speechModelStatus;
        private set => SetField(ref _speechModelStatus, value);
    }

    public string SpeechBackend
    {
        get => _speechBackend;
        private set => SetField(ref _speechBackend, value);
    }

    public string DetectedLanguage
    {
        get => _detectedLanguage;
        private set => SetField(ref _detectedLanguage, value);
    }

    public string SpeechLatency
    {
        get => _speechLatency;
        private set => SetField(ref _speechLatency, value);
    }

    public string SpeechQueue
    {
        get => _speechQueue;
        private set => SetField(ref _speechQueue, value);
    }

    public string CurrentCaption
    {
        get => _currentCaption;
        private set => SetField(ref _currentCaption, value);
    }

    public string CurrentCaptionLanguage
    {
        get => _currentCaptionLanguage;
        private set => SetField(ref _currentCaptionLanguage, value);
    }

    public bool CanDownloadModel
    {
        get => _canDownloadModel;
        private set => SetField(ref _canDownloadModel, value);
    }

    public string ModelButtonText
    {
        get => _modelButtonText;
        private set => SetField(ref _modelButtonText, value);
    }

    public bool IsPhoneDisplayRunning
    {
        get => _isPhoneDisplayRunning;
        private set => SetField(ref _isPhoneDisplayRunning, value);
    }

    public string PhoneDisplayStatus
    {
        get => _phoneDisplayStatus;
        private set => SetField(ref _phoneDisplayStatus, value);
    }

    public string PhoneDisplayUrl
    {
        get => _phoneDisplayUrl;
        private set => SetField(ref _phoneDisplayUrl, value);
    }

    public string PhoneButtonText
    {
        get => _phoneButtonText;
        private set => SetField(ref _phoneButtonText, value);
    }

    public bool IsCaptureActive => _captureSnapshot.IsActive;

    public void ApplyDetection(VrchatDetectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IsVrchatDetected = snapshot.IsDetected;
        HasDetectionError = snapshot.LastError is not null;

        if (snapshot.LastError is not null)
        {
            VrchatStatusText = "Detection error";
            VrchatProcessIdText = "—";
            ListeningSummary = "VRChat detection needs attention";
            ListeningDetail = snapshot.LastError;
            DetectionDiagnostic = snapshot.LastError;
            RefreshCaptureControls();
            return;
        }

        if (snapshot.IsDetected)
        {
            VrchatStatusText = "Detected";
            VrchatProcessIdText = snapshot.ProcessId?.ToString() ?? "—";
            ListeningSummary = "VRChat detected · ready to listen";
            ListeningDetail = "Start Listening opens a process-only loopback stream for this VRChat PID and its child processes.";
            DetectionDiagnostic = $"Running · PID {VrchatProcessIdText}";
            RefreshCaptureControls();
            return;
        }

        VrchatStatusText = "Waiting for VRChat…";
        VrchatProcessIdText = "—";
        ListeningSummary = "Waiting for VRChat…";
        ListeningDetail = "Launch VRChat at any time. Detection checks once per second and follows restarts automatically.";
        DetectionDiagnostic = "Running · no process found";
        RefreshCaptureControls();
    }

    public void ApplyCapture(AudioCaptureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _captureSnapshot = snapshot;
        AudioCaptureStateText = snapshot.State switch
        {
            AudioCaptureState.Starting => "Starting VRChat capture…",
            AudioCaptureState.Capturing => "Capturing VRChat",
            AudioCaptureState.Stopping => "Stopping…",
            AudioCaptureState.Faulted => "Capture faulted",
            _ => "Stopped"
        };
        CaptureModeText = snapshot.Mode switch
        {
            CaptureMode.VrchatProcessOnly => "VRCHAT ONLY",
            CaptureMode.SystemAudioFallback => "SYSTEM AUDIO FALLBACK",
            _ => "Unavailable"
        };
        AudioFormatText = snapshot.Format?.ToString() ?? "—";
        AudioLevelPercent = Math.Clamp(snapshot.PeakLevel * 100, 0, 100);
        AudioLevelText = $"{AudioLevelPercent:0}%";
        DroppedChunksText = snapshot.DroppedChunks.ToString("N0");
        LastAudioErrorText = snapshot.LastError ?? "None";
        switch (snapshot.State)
        {
            case AudioCaptureState.Starting:
                ListeningSummary = "Opening VRChat-only audio…";
                ListeningDetail = "Windows is activating a process-specific WASAPI loopback stream.";
                break;
            case AudioCaptureState.Capturing:
                ListeningSummary = "Listening to VRChat only";
                ListeningDetail = "The meter and PCM queue contain audio from the selected VRChat process tree—not the microphone or system mix.";
                break;
            case AudioCaptureState.Faulted:
                ListeningSummary = "Audio capture needs attention";
                ListeningDetail = snapshot.LastError ?? "The VRChat process audio stream stopped unexpectedly.";
                break;
            case AudioCaptureState.Stopped when IsVrchatDetected:
                ListeningSummary = "VRChat detected · ready to listen";
                ListeningDetail = "Start Listening opens a process-only loopback stream for this VRChat PID and its child processes.";
                break;
        }
        OnPropertyChanged(nameof(IsCaptureActive));
        CanSaveAudioTest = snapshot.State == AudioCaptureState.Capturing;
        RefreshCaptureControls();
    }

    public void SetAudioTestState(bool isRunning, string status)
    {
        AudioTestStatus = status;
        CanSaveAudioTest = !isRunning && _captureSnapshot.State == AudioCaptureState.Capturing;
    }

    public void SetModelAvailability(bool installed, string? modelFileName = null)
    {
        var profile = PresetRuntimeProfile.For(SelectedPreset.Mode);
        var fileName = modelFileName ?? profile.ModelFileName;
        SpeechModelStatus = installed ? $"{fileName} · installed" : $"{fileName} required";
        ModelButtonText = installed ? "MODEL INSTALLED" : "DOWNLOAD SELECTED MODEL";
        CanDownloadModel = !installed;
    }

    public void SetModelDownloadProgress(double? percentage, string status)
    {
        SpeechModelStatus = percentage is null ? status : $"{status} · {percentage:0}%";
        ModelButtonText = "DOWNLOADING…";
        CanDownloadModel = false;
    }

    public void ApplySpeech(SpeechPipelineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SpeechModelStatus = snapshot.State switch
        {
            SpeechPipelineState.ModelMissing => "Balanced model required",
            SpeechPipelineState.LoadingModel => $"Loading {snapshot.ModelName}…",
            SpeechPipelineState.Listening => $"{snapshot.ModelName} · ready",
            SpeechPipelineState.Transcribing => $"{snapshot.ModelName} · transcribing",
            SpeechPipelineState.Faulted => snapshot.LastError ?? "Speech pipeline faulted",
            _ => snapshot.ModelName
        };
        SpeechBackend = snapshot.Backend;
        DetectedLanguage = snapshot.DetectedLanguage;
        SpeechLatency = snapshot.TranscriptionLatency is null ? "—" : $"{snapshot.TranscriptionLatency.Value.TotalSeconds:0.00} sec";
        SpeechQueue = snapshot.QueueLength.ToString();
    }

    public void ApplyCaption(CaptionEvent caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        CurrentCaption = caption.TranslatedText;
        CurrentCaptionLanguage = CaptionPresentation.Route(caption);
        OriginalCaption = ShowOriginalText && caption.OriginalText != caption.TranslatedText ? caption.OriginalText : "";
    }

    public void SetPhoneDisplay(bool running, string detail)
    {
        IsPhoneDisplayRunning = running;
        PhoneDisplayStatus = running ? "On · local network" : "Off";
        PhoneDisplayUrl = detail;
        PhoneButtonText = running ? "STOP PHONE DISPLAY" : "START PHONE DISPLAY";
    }

    public void ApplyPerformance(PerformanceSnapshot snapshot, bool throttling)
    {
        VrchatCpu = snapshot.VrchatCpuPercent is null ? "—" : $"{snapshot.VrchatCpuPercent:0.0}%";
        TranslatorCpu = $"{snapshot.TranslatorCpuPercent:0.0}%";
        SystemCpu = $"{snapshot.SystemCpuPercent:0.0}%";
        TranslatorRam = $"{snapshot.TranslatorMemoryBytes / 1024d / 1024d:0} MB";
        SystemRam = snapshot.SystemTotalMemoryBytes == 0
            ? "—"
            : $"{snapshot.SystemUsedMemoryBytes / 1024d / 1024d / 1024d:0.0} / {snapshot.SystemTotalMemoryBytes / 1024d / 1024d / 1024d:0.0} GB";
        GpuUsage = snapshot.GpuPercent is null ? "Unavailable" : $"{snapshot.GpuPercent:0}%";
        GpuVram = snapshot.GpuUsedMemoryMegabytes is null
            ? "Unavailable"
            : $"{snapshot.GpuUsedMemoryMegabytes:N0} / {snapshot.GpuTotalMemoryMegabytes:N0} MB";
        PerformanceProtectionStatus = throttling
            ? "High sustained load · stale captions are dropped to stay live"
            : "Protect VRChat Performance is ON · bounded live queue";
    }

    public void ApplyCustomSettings(CustomModeSettings settings)
    {
        CustomModel = CustomModels.Contains(settings.Model) ? settings.Model : "Base multilingual";
        CustomBackend = CustomBackends.Contains(settings.Backend) ? settings.Backend : "CPU";
        CustomThreads = settings.CpuThreads;
        CustomChunkSeconds = settings.ChunkLengthSeconds;
        CustomVad = settings.VadThreshold;
        CustomCaptionDelay = settings.CaptionDelaySeconds;
        CustomQueue = settings.MaximumQueueLength;
        CustomTranslation = settings.TranslationEnabled;
    }

    public CustomModeSettings GetCustomSettings() => new()
    {
        Model = CustomModel,
        Backend = CustomBackend,
        ComputeType = "Float32",
        CpuThreads = CustomThreads,
        ChunkLengthSeconds = CustomChunkSeconds,
        VadThreshold = CustomVad,
        CaptionDelaySeconds = CustomCaptionDelay,
        MaximumQueueLength = CustomQueue,
        TranslationEnabled = CustomTranslation
    };

    private void RefreshCaptureControls()
    {
        StartButtonText = _captureSnapshot.State switch
        {
            AudioCaptureState.Starting => "STARTING…",
            AudioCaptureState.Capturing => "STOP LISTENING",
            AudioCaptureState.Stopping => "STOPPING…",
            _ => "START LISTENING"
        };
        CanToggleCapture = _captureSnapshot.State switch
        {
            AudioCaptureState.Starting or AudioCaptureState.Stopping => false,
            AudioCaptureState.Capturing => true,
            _ => IsVrchatDetected
        };
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
