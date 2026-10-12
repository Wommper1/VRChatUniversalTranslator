using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;
using VRChatTranslator.App.ViewModels;
using System.Windows.Media;

namespace VRChatTranslator.App;

public partial class OverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int TransparentStyle = 0x20;
    private const int NoActivateStyle = 0x08000000;
    private const int HotkeyId = 0x4156;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint KeyO = 0x4F;
    private const int HotkeyMessage = 0x0312;
    private readonly DispatcherTimer _fadeTimer;
    private IntPtr _handle;
    private bool _clickThrough = true;
    private OverlaySettings _preferences = new();
    private bool _showOriginal;
    private CaptionEvent? _lastCaption;

    public OverlayWindow()
    {
        InitializeComponent();
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _fadeTimer.Tick += OnFadeTimerTick;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - Height - 70);
    }

    public bool IsClickThrough => _clickThrough;
    public bool LockShortcutRegistered { get; private set; }

    /// <summary>Optional (category, message) sink for the diagnostic log; messages never contain caption text.</summary>
    public Action<string, string>? Diagnostics { get; set; }

    public void ShowCaption(CaptionEvent caption, double durationSeconds = 6)
    {
        ArgumentNullException.ThrowIfNull(caption);
        _lastCaption = caption;
        LanguageText.Text = CaptionPresentation.Route(caption) + (caption.SpeakerId is null ? "" : $" · {caption.SpeakerId}");
        CaptionText.Text = caption.TranslatedText;
        OriginalText.Text = _showOriginal && caption.OriginalText != caption.TranslatedText ? caption.OriginalText : "";
        FitCaption();
        CaptionPanel.BeginAnimation(OpacityProperty, null);
        CaptionPanel.Opacity = 1;
        _fadeTimer.Stop();
        _fadeTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_preferences.CaptionDurationSeconds, 2, 30));
        _fadeTimer.Start();
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        InteractionHint.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = enabled ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        if (!enabled) { _fadeTimer.Stop(); CaptionPanel.BeginAnimation(OpacityProperty, null); CaptionPanel.Opacity = 1; }
        ApplyWindowStyles();
    }

    public void ApplyPreferences(OverlaySettings settings, bool showOriginal)
    {
        _preferences = settings;
        _showOriginal = showOriginal;
        CaptionText.FontSize = Math.Clamp(settings.FontSize, 18, 64);
        OriginalText.FontSize = Math.Clamp(settings.FontSize * 0.72, 14, 46);
        CaptionPanel.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(settings.Opacity, 0.1, 1) * 255), 17, 23, 34));
        if (_lastCaption is not null) OriginalText.Text = showOriginal && _lastCaption.OriginalText != _lastCaption.TranslatedText ? _lastCaption.OriginalText : "";
        if (_lastCaption is not null) FitCaption();
    }

    private void FitCaption()
    {
        // Grow upward for long bilingual captions; preserve user width and allow scrolling when unlocked.
        var content = (FrameworkElement)CaptionScroll.Content;
        content.InvalidateMeasure();
        CaptionText.InvalidateMeasure();
        OriginalText.InvalidateMeasure();
        content.Measure(new Size(Math.Max(250, Width - 90), double.PositiveInfinity));
        var needed = Math.Min(SystemParameters.WorkArea.Height * .8, Math.Max(180, content.DesiredSize.Height + 65));
        if (needed > Height) { var delta = needed - Height; Height = needed; Top = Math.Max(SystemParameters.VirtualScreenTop, Top - delta); }
        CaptionScroll.ScrollToTop();
    }

    public void ResetPlacement()
    {
        Width = 920;
        Height = 240;
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - Height - 70);
    }

    public void EnsureVisiblePlacement()
    {
        Left = OverlayPlacement.ClampAxis(Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth, Width);
        Top = OverlayPlacement.ClampAxis(Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight, Height);
    }

    public void ClearCaption()
    {
        _lastCaption = null;
        _fadeTimer.Stop();
        LanguageText.Text = CaptionText.Text = OriginalText.Text = "";
        CaptionPanel.BeginAnimation(OpacityProperty, null);
        CaptionPanel.Opacity = _clickThrough ? 0 : 1;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_handle)?.AddHook(WindowProcedure);
        LockShortcutRegistered = RegisterHotKey(_handle, HotkeyId, ModifierControl | ModifierAlt, KeyO);
        ApplyWindowStyles();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _fadeTimer.Stop();
        if (_handle != IntPtr.Zero)
        {
            _ = UnregisterHotKey(_handle, HotkeyId);
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == HotkeyId)
        {
            SetClickThrough(!_clickThrough);
            Diagnostics?.Invoke("SHORTCUT", $"Lock / unlock overlay (Ctrl+Alt+O): now {(_clickThrough ? "locked" : "unlocked")}");
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ApplyWindowStyles()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var styles = GetWindowLong(_handle, ExtendedStyleIndex);
        styles = _clickThrough
            ? styles | TransparentStyle | NoActivateStyle
            : styles & ~TransparentStyle & ~NoActivateStyle;
        _ = SetWindowLong(_handle, ExtendedStyleIndex, styles);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_clickThrough && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnFadeTimerTick(object? sender, EventArgs e)
    {
        _fadeTimer.Stop();
        CaptionPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(550)));
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr windowHandle, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowLong(IntPtr windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetWindowLong(IntPtr windowHandle, int index, int value);
}
