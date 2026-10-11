using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Valve.VR;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;
using VRChatTranslator.App.ViewModels;

namespace VRChatTranslator.App.Services;

/// <summary>Optional OpenVR overlay application; never hooks or modifies VRChat.</summary>
public sealed class SteamVrCaptionService : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(75) };
    private ulong _captionHandle, _dashboardHandle, _thumbnailHandle;
    private bool _initialized, _visible = true;
    private CaptionEvent? _caption;
    private DateTimeOffset _expires;
    private OverlaySettings _comfort = new();
    private bool _showOriginal;
    private string _listeningStatus = "Stopped";
    private readonly DispatcherTimer _retryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HeadsetLink _link;
    public bool IsConnected => _initialized;
    /// <summary>True while SteamVR was lost and the overlay is waiting to reconnect on its own.</summary>
    public bool IsReconnecting => _link.IsWaiting;
    public HeadsetSettings Placement { get; private set; } = new();
    public event Action<string>? StatusChanged;
    public event Action<string>? ControlRequested;

    public SteamVrCaptionService()
    {
        _timer.Tick += (_, _) => Tick();
        _link = new HeadsetLink(
            tryConnect: () => TryConnect(Placement),
            tearDown: TearDown,
            setRetryTimer: on => { if (on) _retryTimer.Start(); else _retryTimer.Stop(); },
            status: message => StatusChanged?.Invoke(message),
            clock: () => DateTimeOffset.UtcNow,
            onReconnected: () => { if (_visible && _caption is not null && DateTimeOffset.UtcNow < _expires) Guard(RenderCaption); });
        _retryTimer.Tick += (_, _) => _link.RetryTick();
    }

    /// <summary>User-requested connect. If the link was lost it keeps waiting for SteamVR when this attempt also fails.</summary>
    public void Connect(HeadsetSettings settings)
    {
        Placement = ClampPlacement(settings);
        _link.UserConnect();
    }

    /// <summary>Opt-in connect at app start. If SteamVR is not running yet, keeps waiting for it instead of giving up.</summary>
    public void AutoConnect(HeadsetSettings settings)
    {
        Placement = ClampPlacement(settings);
        _link.AutoConnect();
    }

    /// <returns>null on success, otherwise why SteamVR could not be used.</returns>
    private string? TryConnect(HeadsetSettings settings)
    {
        TearDown();
        try
        {
            if (!OpenVR.IsRuntimeInstalled()) throw new InvalidOperationException("SteamVR is not installed. Desktop captions still work.");
            if (!OpenVR.IsHmdPresent()) throw new InvalidOperationException("No SteamVR headset detected. Start SteamVR, connect the headset, then reconnect here.");
            var error = EVRInitError.None;
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
            if (error != EVRInitError.None) throw new InvalidOperationException("SteamVR could not connect: " + error);
            _initialized = true;
            var key = "theleveleverythingcompany.vrchattranslator." + Environment.ProcessId;
            Check(OpenVR.Overlay.CreateOverlay(key + ".captions", "Translator captions", ref _captionHandle));
            Check(OpenVR.Overlay.CreateDashboardOverlay(key + ".controls", "Translator", ref _dashboardHandle, ref _thumbnailHandle));
            Check(OpenVR.Overlay.SetOverlayWidthInMeters(_dashboardHandle, 1.5f));
            Check(OpenVR.Overlay.SetOverlayInputMethod(_dashboardHandle, VROverlayInputMethod.Mouse));
            var scale = new HmdVector2_t { v0 = 1000, v1 = 700 };
            Check(OpenVR.Overlay.SetOverlayMouseScale(_dashboardHandle, ref scale));
            SetPlacement(settings);
            DrawDashboard();
            Upload(_thumbnailHandle, DrawPanel(256, 256, dc => DrawText(dc, "Translate", 30, 100, 38, Brushes.White, 230)));
            _timer.Start();
            return null;
        }
        catch (Exception ex)
        {
            TearDown();
            return ex.Message;
        }
    }

    private static HeadsetSettings ClampPlacement(HeadsetSettings settings) =>
        settings with { Distance = Math.Clamp(settings.Distance, 0.6, 3), VerticalOffset = Math.Clamp(settings.VerticalOffset, -1.2, 0.5), WidthMeters = Math.Clamp(settings.WidthMeters, 0.5, 2) };

    public void SetPlacement(HeadsetSettings settings)
    {
        Placement = ClampPlacement(settings);
        if (!_initialized) return;
        var matrix = new HmdMatrix34_t { m0 = 1, m5 = 1, m10 = 1, m7 = (float)Placement.VerticalOffset, m11 = -(float)Placement.Distance };
        Check(OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(_captionHandle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref matrix));
        Check(OpenVR.Overlay.SetOverlayWidthInMeters(_captionHandle, (float)Placement.WidthMeters));
    }

    public void SetPreferences(OverlaySettings comfort, bool showOriginal)
    {
        _comfort = comfort; _showOriginal = showOriginal;
        if (_caption is not null && DateTimeOffset.UtcNow < _expires) Guard(RenderCaption);
    }
    public void SetStatus(string status) { if (_listeningStatus == status) return; _listeningStatus = status; if (_initialized) Guard(DrawDashboard); }
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (!_initialized) return;
        if (!visible) OpenVR.Overlay.HideOverlay(_captionHandle);
        else if (_caption is not null && DateTimeOffset.UtcNow < _expires) Guard(RenderCaption);
        if (_initialized) Guard(DrawDashboard);
    }
    public void ShowCaption(CaptionEvent caption)
    {
        _caption = caption;
        _expires = DateTimeOffset.UtcNow.AddSeconds(_comfort.CaptionDurationSeconds);
        if (_initialized && _visible) Guard(RenderCaption);
    }
    public void Clear()
    {
        _caption = null;
        if (_initialized) OpenVR.Overlay.HideOverlay(_captionHandle);
    }
    private void RenderCaption()
    {
        if (!_initialized || _caption is null) return;
        var original = _showOriginal && _caption.OriginalText != _caption.TranslatedText ? _caption.OriginalText : "";
        var size = Math.Clamp(_comfort.FontSize * 1.5, 30, 80);
        var main = Text(_caption.TranslatedText, size, Brushes.White, 940);
        var source = Text(original, size * .68, Brushes.LightGray, 940);
        var height = (int)Math.Clamp(100 + main.Height + (original.Length == 0 ? 0 : source.Height + 18), 180, 1200);
        var bitmap = DrawPanel(1000, height, dc =>
        {
            DrawText(dc, CaptionPresentation.Route(_caption) + (_caption.SpeakerId is null ? "" : " · " + _caption.SpeakerId), 30, 22, 24, Brushes.Aquamarine, 940);
            dc.DrawText(main, new Point(30, 65));
            if (original.Length != 0) dc.DrawText(source, new Point(30, 80 + main.Height));
        }, (byte)(_comfort.Opacity * 255));
        Upload(_captionHandle, bitmap);
        if (_visible) Check(OpenVR.Overlay.ShowOverlay(_captionHandle));
    }

    private static readonly string[] Buttons = ["Start / stop", "Pause / resume", "Show / hide", "Nearer", "Farther", "Reset position", "Move up", "Move down", "Wider captions"];
    private void DrawDashboard()
    {
        Upload(_dashboardHandle, DrawPanel(1000, 700, dc =>
        {
            DrawText(dc, "VRChat Translator", 30, 28, 42, Brushes.White, 940);
            DrawText(dc, _listeningStatus + (_visible ? " · Captions visible" : " · Captions hidden"), 30, 98, 26, Brushes.Aquamarine, 940);
            DrawText(dc, "Local captions · controls never send chat messages", 30, 146, 23, Brushes.LightGray, 940);
            for (var i = 0; i < Buttons.Length; i++)
            {
                var x = 30 + i % 3 * 320; var y = 230 + i / 3 * 135;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(42, 62, 77)), null, new Rect(x, y, 300, 108), 12, 12);
                DrawText(dc, Buttons[i], x + 16, y + 32, 27, Brushes.White, 270);
            }
        }));
    }

    private void Tick()
    {
        if (!_initialized) return;
        try
        {
            var ev = new VREvent_t();
            var size = (uint)Marshal.SizeOf<VREvent_t>();
            for (var n = 0; n < 32 && OpenVR.System.PollNextEvent(ref ev, size); n++)
                if ((EVREventType)ev.eventType == EVREventType.VREvent_Quit) { _link.ConnectionLost("SteamVR closed. Desktop captions remain available."); return; }
            for (var n = 0; n < 32 && OpenVR.Overlay.PollNextOverlayEvent(_dashboardHandle, ref ev, size); n++)
            {
                if ((EVREventType)ev.eventType != EVREventType.VREvent_MouseButtonUp) continue;
                var x = ev.data.mouse.x; var y = 700 - ev.data.mouse.y;
                for (var i = 0; i < Buttons.Length; i++)
                    if (new Rect(30 + i % 3 * 320, 230 + i / 3 * 135, 300, 108).Contains(x, y)) Activate(i);
            }
            if (_caption is not null && DateTimeOffset.UtcNow >= _expires) { _caption = null; OpenVR.Overlay.HideOverlay(_captionHandle); }
        }
        catch (Exception ex) { _link.ConnectionLost("SteamVR disconnected: " + ex.Message); }
    }

    private void Activate(int index)
    {
        switch (index)
        {
            case 0: ControlRequested?.Invoke("listen"); break;
            case 1: ControlRequested?.Invoke("pause"); break;
            case 2: ControlRequested?.Invoke("visible"); break;
            case 3: SetPlacement(Placement with { Distance = Placement.Distance - .15 }); break;
            case 4: SetPlacement(Placement with { Distance = Placement.Distance + .15 }); break;
            case 5: SetPlacement(new HeadsetSettings { Enabled = true }); break;
            case 6: SetPlacement(Placement with { VerticalOffset = Placement.VerticalOffset + .08 }); break;
            case 7: SetPlacement(Placement with { VerticalOffset = Placement.VerticalOffset - .08 }); break;
            case 8: SetPlacement(Placement with { WidthMeters = Placement.WidthMeters + .1 }); break;
        }
        ControlRequested?.Invoke("placement");
    }

    private static FormattedText Text(string text, double size, Brush brush, double width) => new(text, CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1) { MaxTextWidth = width, MaxTextHeight = 1100, Trimming = TextTrimming.WordEllipsis };
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, double width) => dc.DrawText(Text(text, size, brush, width), new Point(x, y));
    private static RenderTargetBitmap DrawPanel(int width, int height, Action<DrawingContext> draw, byte alpha = 245)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) { dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, 14, 21, 30)), null, new Rect(0, 0, width, height), 18, 18); draw(dc); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }
    private static void Upload(ulong handle, RenderTargetBitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3]; var blue = pixels[i];
            pixels[i] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
            pixels[i + 1] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
            pixels[i + 2] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, blue * 255 / alpha);
        }
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { Check(OpenVR.Overlay.SetOverlayRaw(handle, pinned.AddrOfPinnedObject(), (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 4)); }
        finally { pinned.Free(); }
    }
    private static void Check(EVROverlayError error) { if (error != EVROverlayError.None) throw new InvalidOperationException("SteamVR overlay: " + error); }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { _link.ConnectionLost("SteamVR unavailable: " + ex.Message); }
    }
    /// <summary>User-requested disconnect, also used on exit. Ends automatic reconnecting.</summary>
    public void Disconnect() => _link.UserDisconnect();

    private void TearDown()
    {
        _timer.Stop();
        if (!_initialized) return;
        try
        {
            if (_captionHandle != 0) OpenVR.Overlay.DestroyOverlay(_captionHandle);
            if (_dashboardHandle != 0) OpenVR.Overlay.DestroyOverlay(_dashboardHandle);
            if (_thumbnailHandle != 0) OpenVR.Overlay.DestroyOverlay(_thumbnailHandle);
        }
        catch { /* The compositor may already have exited; desktop cleanup must still complete. */ }
        finally { try { OpenVR.Shutdown(); } catch { } _initialized = false; _captionHandle = _dashboardHandle = _thumbnailHandle = 0; }
    }
    public void Dispose() => Disconnect();
}
