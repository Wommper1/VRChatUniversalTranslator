using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VRChatTranslator.Core.Settings;
using Forms = System.Windows.Forms;

namespace VRChatTranslator.App.Services;

public sealed partial class DesktopShell : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly HwndSource _source;
    private readonly Dictionary<int, (string Name, string Gesture, Action Run)> _actions = [];
    private readonly Action _toggleListening, _pause, _visibility;
    private readonly Action<string, string>? _log;
    private readonly IntPtr _handle;

    /// <param name="log">Optional (category, message) sink for the diagnostic log. Messages name the action and key, never captions.</param>
    public DesktopShell(Window window, Action show, Action settings, Action toggleListening, Action pause, Action visibility, Action quit, Action<string, string>? log = null)
    {
        _toggleListening = toggleListening; _pause = pause; _visibility = visibility; _log = log;
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException("Window handle not ready.");
        _source.AddHook(WindowProcedure);
        var menu = new Forms.ContextMenuStrip();
        void Add(string label, Action action) => menu.Items.Add(label, null, (_, _) => window.Dispatcher.InvokeAsync(() => { _log?.Invoke("TRAY", label); action(); }));
        Add("Open translator", show); Add("Settings", settings); Add("Start / stop listening", toggleListening);
        Add("Pause / resume", pause); Add("Show / hide captions", visibility); Add("Quit", quit);
        _tray = new Forms.NotifyIcon { Icon = LoadAppIcon(), Text = "VRChat Translator · stopped", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => window.Dispatcher.InvokeAsync(show);
    }

    /// <summary>The app's own icon, taken from the executable; the generic Windows icon only if that is unavailable.</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path && System.Drawing.Icon.ExtractAssociatedIcon(path) is { } icon) return icon;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { }
        return System.Drawing.SystemIcons.Application;
    }

    public void SetStatus(string status) => _tray.Text = ("VRChat Translator · " + status)[..Math.Min(63, ("VRChat Translator · " + status).Length)];

    public string Configure(AppSettings settings)
    {
        foreach (var oldId in _actions.Keys) UnregisterHotKey(_handle, oldId);
        _actions.Clear();
        List<string> failures = [];
        var choices = new[]
        {
            ("Start / stop listening", settings.ListeningShortcut, _toggleListening),
            ("Pause / resume", settings.PauseShortcut, _pause),
            ("Show / hide captions", settings.VisibilityShortcut, _visibility)
        };
        var id = 0x5200;
        foreach (var (name, text, action) in choices)
        {
            id++;
            try
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var gesture = (KeyGesture?)new KeyGestureConverter().ConvertFromInvariantString(text);
                if (gesture is null || gesture.Modifiers == ModifierKeys.None || gesture.Key == Key.None) throw new FormatException();
                uint modifiers = 0x4000;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
                if (!RegisterHotKey(_handle, id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key))) failures.Add(text);
                else _actions.Add(id, (name, text, action));
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { failures.Add(text); }
        }
        if (failures.Count == 0) return "Shortcuts ready";
        _log?.Invoke("SETTINGS", $"Shortcut conflict or invalid key: {string.Join(", ", failures)}.");
        return $"Shortcut conflict or invalid key: {string.Join(", ", failures)}. Choose another Ctrl/Alt combination; tray controls still work.";
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && _actions.TryGetValue(wParam.ToInt32(), out var entry))
        {
            _log?.Invoke("SHORTCUT", $"{entry.Name} ({entry.Gesture})");
            entry.Run();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_handle, id);
        _source.RemoveHook(WindowProcedure);
        _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr handle, int id);
}
