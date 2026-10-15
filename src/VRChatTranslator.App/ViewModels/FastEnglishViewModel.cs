using VRChatTranslator.Core.Services;

namespace VRChatTranslator.App.ViewModels;

/// <summary>The Settings controls for the optional fast English recogniser.</summary>
public sealed partial class MainWindowViewModel
{
    private bool _fastEnglishEnabled, _fastEnglishInstalled, _fastEnglishBusy;
    private long _fastEnglishBytes;
    private string _fastEnglishProgress = "";

    public bool FastEnglishEnabled
    {
        get => _fastEnglishEnabled;
        set
        {
            SetField(ref _fastEnglishEnabled, value);
            RefreshFastEnglish();
        }
    }

    public string FastEnglishStatus => _fastEnglishBusy && _fastEnglishProgress.Length > 0
        ? _fastEnglishProgress
        : FastEnglishNotice.Status(_fastEnglishEnabled, _fastEnglishInstalled, SourceLanguage.Code, _fastEnglishBytes);

    public string FastEnglishButtonText => FastEnglishNotice.ButtonText(_fastEnglishInstalled, _fastEnglishBusy, _fastEnglishBytes);

    public bool CanDownloadFastEnglish => !_fastEnglishInstalled && !_fastEnglishBusy;

    /// <summary>Tells the Settings text whether the model files are installed and how big the one-time download is.</summary>
    public void SetFastEnglishAvailability(bool installed, long downloadBytes)
    {
        _fastEnglishInstalled = installed;
        _fastEnglishBytes = downloadBytes;
        _fastEnglishBusy = false;
        _fastEnglishProgress = "";
        RefreshFastEnglish();
    }

    public void SetFastEnglishProgress(string status)
    {
        _fastEnglishBusy = true;
        _fastEnglishProgress = status;
        RefreshFastEnglish();
    }

    public void RefreshFastEnglish()
    {
        OnPropertyChanged(nameof(FastEnglishStatus));
        OnPropertyChanged(nameof(FastEnglishButtonText));
        OnPropertyChanged(nameof(CanDownloadFastEnglish));
    }
}
