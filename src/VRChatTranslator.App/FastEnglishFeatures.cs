using System.Windows;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Speech;

namespace VRChatTranslator.App;

/// <summary>The optional fast English recogniser: download, switch, and attaching it to the speech pipeline.</summary>
public partial class MainWindow
{
    private ISpeechRecognizer? _fastEnglish;
    private bool _fastEnglishBusy;

    private void InitializeFastEnglish()
    {
        FastEnglishExplanation.Text = FastEnglishNotice.Explanation + " " + FastEnglishNotice.Credit;
        RefreshFastEnglishAvailability();
    }

    /// <summary>The one-time download is the speech model plus, if missing, the small Whisper model that does the language check.</summary>
    private void RefreshFastEnglishAvailability()
    {
        var checkModel = PerformanceMode.UltraLight;
        var bytes = FastEnglishModel.IsInstalled() ? 0 : FastEnglishModel.DownloadBytes;
        if (!_modelManager.IsModelInstalled(checkModel)) bytes += _modelManager.GetExpectedBytes(checkModel);
        _viewModel.SetFastEnglishAvailability(FastEnglishModel.IsInstalled(), bytes);
    }

    private async void OnFastEnglishToggled(object sender, RoutedEventArgs e)
    {
        if (_cleanupStarted) return;
        var wanted = _viewModel.FastEnglishEnabled && FastEnglishModel.IsInstalled();
        var running = _fastEnglish is not null;
        if (wanted == running) return;
        if (_audioCapture.Current.State == AudioCaptureState.Capturing)
        {
            _ = _log.WriteAsync("SETTINGS", $"Fast English captions switched {(_viewModel.FastEnglishEnabled ? "on" : "off")}; installed={FastEnglishModel.IsInstalled()}; restarting recognition.");
            await _speech.StopAsync();
            await ReconcileSpeechAsync(_audioCapture.Current);
        }
    }

    private async void OnDownloadFastEnglishClick(object sender, RoutedEventArgs e)
    {
        if (_fastEnglishBusy) return;
        _fastEnglishBusy = true;
        try
        {
            if (!FastEnglishModel.IsInstalled())
            {
                var progress = new Progress<ModelDownloadProgress>(value =>
                    _viewModel.SetFastEnglishProgress($"Downloading the fast English model · {value.Percentage:0}%"));
                _viewModel.SetFastEnglishProgress("Downloading the fast English model…");
                await FastEnglishModel.DownloadAsync(progress, cancellationToken: _desktopCancellation.Token);
            }

            if (!_modelManager.IsModelInstalled(PerformanceMode.UltraLight))
            {
                var progress = new Progress<ModelDownloadProgress>(value =>
                    _viewModel.SetFastEnglishProgress($"Downloading the small language-check model · {value.Percentage:0}%"));
                _viewModel.SetFastEnglishProgress("Downloading the small language-check model…");
                await _modelManager.DownloadModelAsync(PerformanceMode.UltraLight, progress, _desktopCancellation.Token);
            }

            _ = _log.WriteAsync("SPEECH", "Fast English model downloaded and verified.");
            _viewModel.FastEnglishEnabled = true;
            _viewModel.OperationStatus = "Fast English captions are installed and on. Other languages and translation still use Whisper.";
        }
        catch (Exception exception)
        {
            _ = _log.WriteAsync("SPEECH", $"Fast English model download failed: {exception.Message}");
            _viewModel.OperationStatus = $"Fast English model download failed: {exception.Message}";
        }
        finally
        {
            _fastEnglishBusy = false;
            RefreshFastEnglishAvailability();
        }

        OnFastEnglishToggled(this, new RoutedEventArgs());
    }

    /// <summary>Attaches or removes the fast engine before the speech pipeline starts. A failure here never stops captions: Whisper handles everything.</summary>
    private void ConfigureFastEnglish()
    {
        var wanted = _viewModel.FastEnglishEnabled && FastEnglishModel.IsInstalled();
        if (!wanted)
        {
            ReleaseFastEnglish();
            return;
        }

        if (_fastEnglish is null)
        {
            try
            {
                _fastEnglish = FastEnglishModel.TryCreateRecognizer(threads: 4);
            }
            catch (Exception exception)
            {
                _fastEnglish = null;
                _ = _log.WriteAsync("SPEECH", $"The fast English engine could not start ({exception.Message}); Whisper handles all speech.");
                _viewModel.OperationStatus = "The fast English engine could not start, so Whisper handles all speech.";
            }
        }

        _speech.FastEnglishRecognizer = _fastEnglish;
        var checkModel = _modelManager.IsModelInstalled(PerformanceMode.UltraLight) ? _modelManager.GetModelPath(PerformanceMode.UltraLight) : null;
        _speech.EnglishGateModelPath = checkModel;
        if (_fastEnglish is not null)
        {
            _ = _log.WriteAsync("SPEECH", $"Fast English engine on; spoken language={_settings.SourceLanguage}; language check={(checkModel is null ? "main speech model" : "small model")}.");
        }
    }

    private void ReleaseFastEnglish()
    {
        _speech.FastEnglishRecognizer = null;
        _speech.EnglishGateModelPath = null;
        _fastEnglish?.Dispose();
        _fastEnglish = null;
    }
}
