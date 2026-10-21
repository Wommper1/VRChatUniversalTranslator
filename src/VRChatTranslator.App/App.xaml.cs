using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.App.Services;

namespace VRChatTranslator.App;

public partial class App : Application
{
    private const string SingleInstanceName = "TheLevelEverythingCompany.VRChatUniversalTranslator";
    private readonly DiagnosticLog _log = new();
    private SingleInstanceGuard? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (e.Args.Contains("--phone-self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await PhoneServerSelfTest.RunAsync();
                Console.WriteLine("PASS  phone server startup/LAN/SSE/reconnect/escaping/conflict/shutdown");
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL  phone server self-test: {exception}");
                Shutdown(1);
            }

            return;
        }

        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            RunSmokeTest();
            return;
        }

        // One copy per Windows session: a second launch shows the first copy and exits. Two copies would each
        // capture and transcribe the same VRChat audio, doubling CPU use and delay.
        _singleInstance = new SingleInstanceGuard(SingleInstanceName);
        if (!_singleInstance.TryBecomePrimary())
        {
            _ = AllowSetForegroundWindow(-1);
            _singleInstance.NotifyPrimary();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        _singleInstance.WatchForSecondLaunch(() => Dispatcher.InvokeAsync(() => (MainWindow as MainWindow)?.BringToFront()));
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Lets the copy that was already running take focus when the user opens the app a second time.</summary>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    private async void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        await _log.WriteAsync("FATAL", e.Exception.ToString());
        MessageBox.Show(
            $"VRChat Universal Translator hit an unexpected error.\n\n{e.Exception.Message}\n\nA local diagnostic log was written without conversation text.",
            "Translator error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _ = _log.WriteAsync("BACKGROUND", e.Exception.ToString());
        e.SetObserved();
    }

    private void RunSmokeTest()
    {
        try
        {
            if (PerformancePreset.BuiltIn.Count != 5)
            {
                throw new InvalidOperationException("All five performance presets must be available.");
            }

            var window = new MainWindow(skipLifecycleCleanup: true)
            {
                ShowInTaskbar = false
            };
            window.Show();
            window.Width = 820;
            window.Height = 600;
            foreach (var tab in window.MainTabs.Items)
            {
                window.MainTabs.SelectedItem = tab;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            window.MainTabs.SelectedItem = window.SettingsTab;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var selector = window.PerformanceModeSelector;
            selector.ApplyTemplate();
            var selectorToggle = selector.Template.FindName("DropDownToggle", selector) as ToggleButton
                ?? throw new InvalidOperationException("The performance selector template did not render.");
            var expectedColor = (Color)Resources["PanelLightColor"];
            if (selectorToggle.Background is not SolidColorBrush selectorBrush || selectorBrush.Color != expectedColor)
            {
                throw new InvalidOperationException("The performance selector did not render with the dark readable theme.");
            }
            window.RunDesktopChecks();
            window.RunAccessibilityChecks();

            window.Close();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL  WPF render smoke test: {exception}");
            Shutdown(1);
        }
    }
}
