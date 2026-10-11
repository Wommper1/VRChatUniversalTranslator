using System.Windows;
using VRChatTranslator.App.Services;
using VRChatTranslator.Core.Settings;

namespace VRChatTranslator.App;

public partial class MainWindow
{
    private readonly SteamVrCaptionService _headset = new();
    private void InitializeHeadsetControls()
    {
        _headset.StatusChanged += status => HeadsetStatus.Text = status;
        _headset.ControlRequested += action =>
        {
            if (action == "listen") OnToggleCaptureClick(this, new RoutedEventArgs());
            if (action == "pause") OnPauseClick(this, new RoutedEventArgs());
            if (action == "visible") OnToggleOverlayClick(this, new RoutedEventArgs());
            if (action == "placement") LoadHeadsetPlacement(_headset.Placement);
        };
    }
    private void LoadHeadsetPlacement(HeadsetSettings settings)
    {
        HeadsetDistance.Value = settings.Distance; HeadsetHeight.Value = settings.VerticalOffset; HeadsetWidth.Value = settings.WidthMeters;
    }
    private HeadsetSettings ReadHeadsetPlacement() => new() { Enabled = _headset.IsConnected, Distance = HeadsetDistance.Value, VerticalOffset = HeadsetHeight.Value, WidthMeters = HeadsetWidth.Value };
    private void OnConnectHeadsetClick(object sender, RoutedEventArgs e)
    {
        _headset.SetPreferences(_viewModel.CaptureDesktopSettings(_settings).Overlay, _viewModel.ShowOriginalText);
        _headset.SetVisible(_viewModel.OverlayEnabled);
        _headset.Connect(ReadHeadsetPlacement());
    }
    private bool _loadingHeadsetPreference;

    /// <summary>Shows the saved opt-in and, only if it is on, starts connecting (or waiting for SteamVR).</summary>
    private void ApplyHeadsetAutoConnectPreference()
    {
        _loadingHeadsetPreference = true;
        AutoConnectHeadset.IsChecked = _settings.AutoConnectHeadset;
        _loadingHeadsetPreference = false;
        if (!_settings.AutoConnectHeadset) return;
        _headset.SetPreferences(_viewModel.CaptureDesktopSettings(_settings).Overlay, _viewModel.ShowOriginalText);
        _headset.SetVisible(_viewModel.OverlayEnabled);
        _headset.AutoConnect(ReadHeadsetPlacement());
    }

    private async void OnAutoConnectHeadsetChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingHeadsetPreference || _cleanupStarted) return;
        var enabled = AutoConnectHeadset.IsChecked == true;
        _settings = _settings with { AutoConnectHeadset = enabled };
        _viewModel.OperationStatus = enabled
            ? "Saved. The headset will connect when the app starts, waiting for SteamVR if needed. Use Connect / reconnect to connect now."
            : "Saved. The headset will no longer connect on its own at start.";
        try { await _settingsStore.SaveAsync(_settings); }
        catch (Exception ex) { _viewModel.OperationStatus = "Could not save the headset preference: " + ex.Message; }
    }

    private void OnDisconnectHeadsetClick(object sender, RoutedEventArgs e)
    {
        var wasReconnecting = _headset.IsReconnecting;
        _headset.Disconnect();
        HeadsetStatus.Text = wasReconnecting ? "Stopped waiting for SteamVR. Desktop captions still work." : "Headset disconnected. Desktop captions still work.";
    }
    private void OnApplyHeadsetClick(object sender, RoutedEventArgs e)
    {
        try { _headset.SetPlacement(ReadHeadsetPlacement()); _settings = _settings with { Headset = ReadHeadsetPlacement() }; OnApplyPreferencesClick(sender, e); }
        catch (Exception ex) { HeadsetStatus.Text = ex.Message; }
    }
    private void OnResetHeadsetClick(object sender, RoutedEventArgs e) { LoadHeadsetPlacement(new()); OnApplyHeadsetClick(sender, e); }
}
