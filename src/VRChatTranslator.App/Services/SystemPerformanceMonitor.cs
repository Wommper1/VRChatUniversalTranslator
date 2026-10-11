using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace VRChatTranslator.App.Services;

public sealed partial class SystemPerformanceMonitor : IAsyncDisposable
{
    private readonly Func<int?> _vrchatProcessIdProvider;
    private readonly Process _translator = Process.GetCurrentProcess();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private TimeSpan _lastTranslatorCpu;
    private readonly Dictionary<int, TimeSpan> _lastVrchatCpu = [];
    private ulong _lastSystemIdle;
    private ulong _lastSystemKernel;
    private ulong _lastSystemUser;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.UtcNow;

    public SystemPerformanceMonitor(Func<int?> vrchatProcessIdProvider)
    {
        _vrchatProcessIdProvider = vrchatProcessIdProvider;
        _translator.Refresh();
        _lastTranslatorCpu = _translator.TotalProcessorTime;
        ReadSystemTimes(out _lastSystemIdle, out _lastSystemKernel, out _lastSystemUser);
    }

    public event EventHandler<PerformanceSnapshot>? SnapshotAvailable;

    public void Start()
    {
        if (_worker is { IsCompleted: false })
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _worker = RunAsync(_cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation?.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation?.Dispose();
        _translator.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            SnapshotAvailable?.Invoke(this, await CollectAsync(cancellationToken));
        }
    }

    private async Task<PerformanceSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        string? lastError = null;
        var now = DateTimeOffset.UtcNow;
        var elapsed = Math.Max(0.1, (now - _lastSampleAt).TotalSeconds);
        _lastSampleAt = now;

        _translator.Refresh();
        var translatorCpu = CalculateProcessCpu(_translator.TotalProcessorTime, ref _lastTranslatorCpu, elapsed);
        var vrchatCpu = GetVrchatCpu(elapsed);
        var systemCpu = GetSystemCpu();
        var memory = GetMemory();
        var gpu = await TryGetGpuAsync(cancellationToken);
        if (gpu.Error is not null)
        {
            lastError = gpu.Error;
        }

        return new PerformanceSnapshot(
            systemCpu,
            translatorCpu,
            vrchatCpu,
            _translator.WorkingSet64,
            (long)(memory.TotalPhysical - memory.AvailablePhysical),
            (long)memory.TotalPhysical,
            gpu.Usage,
            gpu.UsedMemory,
            gpu.TotalMemory,
            lastError);
    }

    private double? GetVrchatCpu(double elapsed)
    {
        var processId = _vrchatProcessIdProvider();
        if (processId is null)
        {
            _lastVrchatCpu.Clear();
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            var current = process.TotalProcessorTime;
            if (!_lastVrchatCpu.TryGetValue(processId.Value, out var previous))
            {
                _lastVrchatCpu.Clear();
                _lastVrchatCpu[processId.Value] = current;
                return 0;
            }

            _lastVrchatCpu[processId.Value] = current;
            return Math.Clamp((current - previous).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private static double CalculateProcessCpu(TimeSpan current, ref TimeSpan previous, double elapsed)
    {
        var value = Math.Clamp((current - previous).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
        previous = current;
        return value;
    }

    private double GetSystemCpu()
    {
        ReadSystemTimes(out var idle, out var kernel, out var user);
        var idleDelta = idle - _lastSystemIdle;
        var kernelDelta = kernel - _lastSystemKernel;
        var userDelta = user - _lastSystemUser;
        _lastSystemIdle = idle;
        _lastSystemKernel = kernel;
        _lastSystemUser = user;
        var total = kernelDelta + userDelta;
        return total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
    }

    private static void ReadSystemTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            idle = kernel = user = 0;
            return;
        }

        idle = idleTime.Value;
        kernel = kernelTime.Value;
        user = userTime.Value;
    }

    private static MemoryStatusEx GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status : default;
    }

    private static async Task<GpuSample> TryGetGpuAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Only the driver's own install locations are used, never a copy found through the app or working folder.
            var nvidiaSmi = new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
            }.FirstOrDefault(File.Exists);
            if (nvidiaSmi is null)
            {
                return new(null, null, null, "GPU metrics unavailable");
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = nvidiaSmi,
                Arguments = "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return new(null, null, null, "GPU metrics unavailable");
            }

            var output = await process.StandardOutput.ReadLineAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            var values = output?.Split(',', StringSplitOptions.TrimEntries);
            if (values is not { Length: >= 3 } ||
                !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var usage) ||
                !long.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used) ||
                !long.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
            {
                return new(null, null, null, "NVIDIA GPU metrics unavailable");
            }

            return new(usage, used, total, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(null, null, null, $"GPU metrics unavailable: {exception.Message}");
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed record GpuSample(double? Usage, long? UsedMemory, long? TotalMemory, string? Error);
}
