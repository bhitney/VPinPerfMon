using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VPinPerfMon;

/// <summary>
/// 1 Hz sampler of system and per-process load, written next to the PresentMon CSV so a stall can be matched to what else was busy.
/// Uses Windows performance counters only (no elevation). Every row carries QpcMs, the QPC clock in milliseconds
/// (Stopwatch.GetTimestamp * 1000 / Frequency) at the END of the sampling interval; rate values (CPU %, GPU %, bytes/sec)
/// are averages over the second before QpcMs. This is the same clock as PresentMon's CPUStartQPCTime (--timebase qpc).
/// Files: SysStats_*.csv (one row per second), SysProcs_*.csv (busiest processes), SysEvents_*.csv (foreground changes, process start/exit).
/// A counter that is missing or fails is written as an empty cell and never stops the capture.
/// </summary>
internal sealed class SystemSampler
{
    private const string GpuEngineCounter = "Utilization Percentage";
    public const string FlagEventName = "Local\\VPinPerfMon_Flag";
    private const int FlagDebounceMs = 500;
    private const double CpuReportThresholdPct = 1.0;
    private const double GpuReportThresholdPct = 2.0;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static readonly Regex GpuInstanceRegex = new(
        @"^pid_(\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_\d+_eng_\d+_engtype_(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _statsPath;
    private readonly string _procsPath;
    private readonly string _eventsPath;
    private readonly int _topN;
    private readonly int _selfPid = Environment.ProcessId;
    private readonly int _coreCount = Environment.ProcessorCount;
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase) { "_Total", "Idle", "VPinPerfMon" };

    private StreamWriter? _stats;
    private StreamWriter? _procs;
    private StreamWriter? _events;
    private Thread? _thread;
    private Thread? _flagThread;
    private EventWaitHandle? _flagEvent;
    private readonly ThreadPriority _priority;

    private CategoryReader? _processorReader;
    private CategoryReader? _processReader;
    private CategoryReader? _gpuReader;
    private Snapshot? _prevProcessor;
    private Snapshot? _prevProcess;
    private Snapshot? _prevGpu;
    private PerformanceCounter? _availableMb;
    private PerformanceCounter? _pagesInput;
    private PerformanceCounter? _contextSwitches;
    private PerformanceCounter? _diskBytes;
    private PerformanceCounter? _diskQueue;

    private Dictionary<int, string>? _pidNames;
    private string _lastForeground = string.Empty;
    private int _samples;
    private double _sumMs;
    private double _maxMs;
    private double _sumCpuMs;
    private double _maxCpuMs;
    private double _sumReadMs;
    private double _sumComputeMs;
    private bool _tickErrorReported;

    public SystemSampler(string baseFilename, int topN, IEnumerable<string> excludedProcessNames, ThreadPriority priority = ThreadPriority.Normal)
    {
        _priority = priority;
        const string prefix = "PresentMon_";
        string directory = Path.GetDirectoryName(baseFilename) ?? string.Empty;
        string stem = Path.GetFileName(baseFilename);
        string tail = stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? stem[prefix.Length..] : stem;

        _statsPath = Path.Combine(directory, $"SysStats_{tail}.csv");
        _procsPath = Path.Combine(directory, $"SysProcs_{tail}.csv");
        _eventsPath = Path.Combine(directory, $"SysEvents_{tail}.csv");
        _topN = Math.Max(1, topN);

        foreach (string name in excludedProcessNames)
        {
            _excluded.Add(StripExe(name.Trim()));
        }
    }

    /// <summary>QPC clock in milliseconds, computed the same way as the capture anchor.</summary>
    public static double QpcMsNow() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public bool Start()
    {
        try
        {
            _stats = OpenWriter(_statsPath, "QpcMs,UtcTime,CpuTotalPct,CpuMaxCorePct,DpcPct,InterruptPct,CtxSwitchesPerSec,DiskBytesPerSec,DiskQueue,MemAvailMB,PagesInputPerSec,GpuTotalPct,ForegroundProcess,SampleMs,SampleCpuMs,ReadMs,ComputeMs");
            _procs = OpenWriter(_procsPath, "QpcMs,Pid,ProcessName,CpuPct,GpuPct,WorkingSetMB,IoBytesPerSec");
            _events = OpenWriter(_eventsPath, "QpcMs,Event,ProcessName,Pid");

            List<string> unavailable = [];
            _processorReader = CategoryReader.TryCreate("Processor");
            _processReader = CategoryReader.TryCreate("Process");
            _gpuReader = CategoryReader.TryCreate("GPU Engine");
            if (_processorReader == null) unavailable.Add("Processor");
            if (_processReader == null) unavailable.Add("Process");
            if (_gpuReader == null) unavailable.Add("GPU Engine");

            _availableMb = TryCounter("Memory", "Available MBytes", null, unavailable);
            _pagesInput = TryCounter("Memory", "Pages Input/sec", null, unavailable);
            _contextSwitches = TryCounter("System", "Context Switches/sec", null, unavailable);
            _diskBytes = TryCounter("PhysicalDisk", "Disk Bytes/sec", "_Total", unavailable);
            _diskQueue = TryCounter("PhysicalDisk", "Avg. Disk Queue Length", "_Total", unavailable);

            _prevProcessor = _processorReader?.Read();
            _prevProcess = _processReader?.Read();
            _prevGpu = _gpuReader?.Read();
            _pidNames = _prevProcess != null ? BuildPidNames(_prevProcess) : null;

            Console.WriteLine("System sampler started (1 Hz).");
            Console.WriteLine($"System sampler thread priority: {_priority}");
            Console.WriteLine($"System stats: {Path.GetFullPath(_statsPath)}");
            Console.WriteLine($"System processes: {Path.GetFullPath(_procsPath)}");
            Console.WriteLine($"System events: {Path.GetFullPath(_eventsPath)}");
            if (unavailable.Count > 0)
            {
                Console.WriteLine($"System sampler: counters unavailable (cells left empty): {string.Join(", ", unavailable)}");
            }

            _thread = new Thread(() => RunLoop(_cts.Token)) { IsBackground = true, Name = "VPinPerfMon sampler", Priority = _priority };
            _thread.Start();

            _flagEvent = new EventWaitHandle(false, EventResetMode.AutoReset, FlagEventName);
            _flagThread = new Thread(() => RunFlagLoop(_flagEvent, _cts.Token)) { IsBackground = true, Name = "VPinPerfMon flag listener" };
            _flagThread.Start();
            Console.WriteLine("User flag listener started.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"System sampler failed to start: {ex.Message}");
            CloseAll();
            return false;
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_thread != null)
        {
            // A tick that overruns is abandoned after 3 s; the writers are closed under the same lock below.
            await Task.Run(() => _thread.Join(TimeSpan.FromSeconds(3)));
        }

        _flagThread?.Join(TimeSpan.FromSeconds(1));
        _flagEvent?.Dispose();
        _flagEvent = null;

        CloseAll();
        _cts.Dispose();

        if (_samples > 0)
        {
            Console.WriteLine($"System sampler stopped: {_samples} samples, {_sumMs / _samples:F1} ms average and {_maxMs:F1} ms maximum elapsed per sample; " +
                $"{_sumCpuMs / _samples:F1} ms average and {_maxCpuMs:F1} ms maximum of that was the sampler's own CPU time; " +
                $"{_sumReadMs / _samples:F1} ms average reading counters, {_sumComputeMs / _samples:F1} ms average computing rates and processes.");
        }
        else
        {
            Console.WriteLine("System sampler stopped: no samples were written.");
        }
    }

    private void RunFlagLoop(EventWaitHandle flagEvent, CancellationToken cancellationToken)
    {
        WaitHandle[] handles = [flagEvent, cancellationToken.WaitHandle];
        long lastFlag = 0;
        while (WaitHandle.WaitAny(handles) == 0)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastFlag != 0 && now - lastFlag < Stopwatch.Frequency * FlagDebounceMs / 1000) continue;
            lastFlag = now;
            double qpcMs = QpcMsNow();
            lock (_gate)
            {
                if (_events == null) return;
                WriteEvent(qpcMs, "UserFlag", string.Empty, 0);
                _events.Flush();
            }
        }
    }

    private void RunLoop(CancellationToken cancellationToken)
    {
        // A fixed 1 s schedule that does not pile up ticks when one runs late (a late tick is skipped, not queued).
        long period = Stopwatch.Frequency;
        long next = Stopwatch.GetTimestamp() + period;
        while (!cancellationToken.IsCancellationRequested)
        {
            long wait = next - Stopwatch.GetTimestamp();
            if (wait > 0 && cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency)))
            {
                break;
            }

            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                if (!_tickErrorReported)
                {
                    _tickErrorReported = true;
                    Console.WriteLine($"System sampler sample error (further errors are not shown): {ex.Message}");
                }
            }

            next += period;
            long now = Stopwatch.GetTimestamp();
            if (next < now)
            {
                // Missed one or more ticks: continue on the next whole second instead of firing them back to back.
                next += ((now - next) / period + 1) * period;
            }
        }
    }

    private static double ThreadCpuMs()
    {
        // Kernel plus user time of the calling thread, in 100 ns units.
        return GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user) ? (kernel + user) / 10000.0 : 0.0;
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_stats == null || _procs == null || _events == null) return;

            double cpuBefore = ThreadCpuMs();

            long startTimestamp = Stopwatch.GetTimestamp();
            double qpcMs = startTimestamp * 1000.0 / Stopwatch.Frequency;
            string utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            Snapshot? processor = _processorReader?.Read();
            Snapshot? process = _processReader?.Read();
            Snapshot? gpu = _gpuReader?.Read();
            double readMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

            // System-wide load
            double? cpuTotal = Rate(_prevProcessor, processor, "% Processor Time", "_Total");
            double? dpc = Rate(_prevProcessor, processor, "% DPC Time", "_Total");
            double? interrupt = Rate(_prevProcessor, processor, "% Interrupt Time", "_Total");
            double? maxCore = null;
            if (processor != null)
            {
                foreach (string instance in processor.Instances("% Processor Time"))
                {
                    if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;
                    double? core = Rate(_prevProcessor, processor, "% Processor Time", instance);
                    if (core.HasValue && (!maxCore.HasValue || core.Value > maxCore.Value)) maxCore = core;
                }
            }

            double? contextSwitches = NextValue(_contextSwitches);
            double? diskBytes = NextValue(_diskBytes);
            double? diskQueue = NextValue(_diskQueue);
            double? memAvailable = NextValue(_availableMb);
            double? pagesInput = NextValue(_pagesInput);

            // GPU: per process = busiest engine type; total = busiest engine type of one adapter, summed over processes
            Dictionary<int, double> gpuByPid = [];
            double? gpuTotal = null;
            if (gpu != null && _prevGpu != null)
            {
                Dictionary<(int, string), double> perPidEngine = [];
                Dictionary<(string, string), double> perAdapterEngine = [];
                foreach (string instance in gpu.Instances(GpuEngineCounter))
                {
                    Match match = GpuInstanceRegex.Match(instance);
                    if (!match.Success) continue;
                    double? value = Rate(_prevGpu, gpu, GpuEngineCounter, instance);
                    if (!value.HasValue) continue;

                    int pid = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    string luid = match.Groups[2].Value;
                    string engine = match.Groups[3].Value;
                    perPidEngine[(pid, engine)] = perPidEngine.GetValueOrDefault((pid, engine)) + value.Value;
                    perAdapterEngine[(luid, engine)] = perAdapterEngine.GetValueOrDefault((luid, engine)) + value.Value;
                }

                foreach (KeyValuePair<(int, string), double> entry in perPidEngine)
                {
                    gpuByPid[entry.Key.Item1] = Math.Max(gpuByPid.GetValueOrDefault(entry.Key.Item1), entry.Value);
                }

                gpuTotal = perAdapterEngine.Count > 0 ? perAdapterEngine.Values.Max() : 0.0;
            }

            // Processes: events, foreground name and the busiest ones
            Dictionary<int, string>? pidNames = process != null ? BuildPidNames(process) : null;
            List<ProcessRow> candidates = [];
            if (process != null && _prevProcess != null)
            {
                foreach (string instance in process.Instances("ID Process"))
                {
                    string name = NormalizeName(instance);
                    if (_excluded.Contains(name)) continue;
                    if (!process.TryGet("ID Process", instance, out CounterSample idSample)) continue;

                    int pid = (int)idSample.RawValue;
                    if (pid == 0 || pid == _selfPid) continue;

                    // Instance names are renumbered when processes exit, so only compare an instance with itself.
                    if (!_prevProcess.TryGet("ID Process", instance, out CounterSample previousId) || previousId.RawValue != idSample.RawValue) continue;

                    double cpu = (Rate(_prevProcess, process, "% Processor Time", instance) ?? 0.0) / _coreCount;
                    double workingSetMb = process.TryGet("Working Set", instance, out CounterSample workingSet) ? workingSet.RawValue / 1048576.0 : 0.0;
                    double? io = Rate(_prevProcess, process, "IO Data Bytes/sec", instance);
                    candidates.Add(new ProcessRow(pid, name, cpu, gpuByPid.GetValueOrDefault(pid), workingSetMb, io));
                }
            }

            string foreground = string.Empty;
            uint foregroundPid = 0;
            IntPtr window = GetForegroundWindow();
            if (window != IntPtr.Zero && GetWindowThreadProcessId(window, out foregroundPid) != 0 && pidNames != null)
            {
                foreground = pidNames.GetValueOrDefault((int)foregroundPid, string.Empty);
            }

            double sampleMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            double cpuMs = ThreadCpuMs() - cpuBefore;
            double computeMs = sampleMs - readMs;

            // Write
            if (pidNames != null && _pidNames != null)
            {
                foreach (KeyValuePair<int, string> started in pidNames)
                {
                    if (!_pidNames.ContainsKey(started.Key)) WriteEvent(qpcMs, "ProcessStart", started.Value, started.Key);
                }

                foreach (KeyValuePair<int, string> exited in _pidNames)
                {
                    if (!pidNames.ContainsKey(exited.Key)) WriteEvent(qpcMs, "ProcessExit", exited.Value, exited.Key);
                }
            }

            if (foreground.Length > 0 && !foreground.Equals(_lastForeground, StringComparison.Ordinal))
            {
                WriteEvent(qpcMs, "ForegroundChange", foreground, (int)foregroundPid);
                _lastForeground = foreground;
            }

            IEnumerable<ProcessRow> selected = candidates.Where(p => p.CpuPct >= CpuReportThresholdPct).OrderByDescending(p => p.CpuPct).Take(_topN)
                .Concat(candidates.Where(p => p.GpuPct >= GpuReportThresholdPct).OrderByDescending(p => p.GpuPct).Take(_topN))
                .DistinctBy(p => p.Pid);
            foreach (ProcessRow row in selected)
            {
                _procs.WriteLine(string.Join(',',
                    Format(qpcMs, "F4"), row.Pid.ToString(CultureInfo.InvariantCulture), Csv(row.Name),
                    Format(row.CpuPct), Format(row.GpuPct), Format(row.WorkingSetMb, "F1"), Format(row.IoBytesPerSec, "F0")));
            }

            _stats.WriteLine(string.Join(',',
                Format(qpcMs, "F4"), utc, Format(cpuTotal), Format(maxCore), Format(dpc), Format(interrupt),
                Format(contextSwitches, "F0"), Format(diskBytes, "F0"), Format(diskQueue), Format(memAvailable, "F0"),
                Format(pagesInput, "F1"), Format(gpuTotal), Csv(foreground), Format(sampleMs, "F1"), Format(cpuMs, "F1"), Format(readMs, "F1"), Format(computeMs, "F1")));

            _stats.Flush();
            _procs.Flush();
            _events.Flush();

            _prevProcessor = processor ?? _prevProcessor;
            _prevProcess = process ?? _prevProcess;
            _prevGpu = gpu ?? _prevGpu;
            if (pidNames != null) _pidNames = pidNames;

            _samples++;
            _sumMs += sampleMs;
            _maxMs = Math.Max(_maxMs, sampleMs);
            _sumCpuMs += cpuMs;
            _maxCpuMs = Math.Max(_maxCpuMs, cpuMs);
            _sumReadMs += readMs;
            _sumComputeMs += computeMs;
        }
    }

    private void WriteEvent(double qpcMs, string eventName, string processName, int pid)
    {
        _events?.WriteLine(string.Join(',', Format(qpcMs, "F4"), eventName, Csv(processName), pid.ToString(CultureInfo.InvariantCulture)));
    }

    private void CloseAll()
    {
        lock (_gate)
        {
            _stats?.Dispose();
            _procs?.Dispose();
            _events?.Dispose();
            _stats = _procs = _events = null;

            _availableMb?.Dispose();
            _pagesInput?.Dispose();
            _contextSwitches?.Dispose();
            _diskBytes?.Dispose();
            _diskQueue?.Dispose();
            _availableMb = _pagesInput = _contextSwitches = _diskBytes = _diskQueue = null;
        }
    }

    private static StreamWriter OpenWriter(string path, string header)
    {
        StreamWriter writer = new(path, false, new UTF8Encoding(false));
        writer.WriteLine(header);
        writer.Flush();
        return writer;
    }

    private static PerformanceCounter? TryCounter(string category, string counter, string? instance, List<string> unavailable)
    {
        try
        {
            PerformanceCounter performanceCounter = instance == null
                ? new PerformanceCounter(category, counter)
                : new PerformanceCounter(category, counter, instance);
            performanceCounter.NextValue(); // the first read of a rate counter is always 0
            return performanceCounter;
        }
        catch (Exception)
        {
            unavailable.Add($"{category}\\{counter}");
            return null;
        }
    }

    private static double? NextValue(PerformanceCounter? performanceCounter)
    {
        if (performanceCounter == null) return null;
        try
        {
            return Finite(performanceCounter.NextValue());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double? Rate(Snapshot? previous, Snapshot? current, string counter, string instance)
    {
        if (previous == null || current == null) return null;
        if (!previous.TryGet(counter, instance, out CounterSample before) || !current.TryGet(counter, instance, out CounterSample after)) return null;
        try
        {
            return Finite(CounterSample.Calculate(before, after));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double? Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? null : value;

    private static Dictionary<int, string> BuildPidNames(Snapshot process)
    {
        Dictionary<int, string> names = [];
        foreach (string instance in process.Instances("ID Process"))
        {
            if (!process.TryGet("ID Process", instance, out CounterSample idSample)) continue;
            int pid = (int)idSample.RawValue;
            string name = NormalizeName(instance);
            if (pid == 0 || name.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;
            names[pid] = name;
        }

        return names;
    }

    private static string StripExe(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>Removes the "#2" suffix that the Process counters add to duplicate names.</summary>
    private static string NormalizeName(string instance)
    {
        int hash = instance.LastIndexOf('#');
        return hash > 0 && int.TryParse(instance.AsSpan(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _) ? instance[..hash] : instance;
    }

    private static string Format(double? value, string format = "F2") => value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private sealed record ProcessRow(int Pid, string Name, double CpuPct, double GpuPct, double WorkingSetMb, double? IoBytesPerSec);

    private sealed class CategoryReader
    {
        private readonly PerformanceCounterCategory _category;

        private CategoryReader(PerformanceCounterCategory category) => _category = category;

        public static CategoryReader? TryCreate(string name)
        {
            try
            {
                return PerformanceCounterCategory.Exists(name) ? new CategoryReader(new PerformanceCounterCategory(name)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Snapshot? Read()
        {
            try
            {
                return new Snapshot(_category.ReadCategory());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>One ReadCategory result, indexed by counter name and then instance name (case-insensitive).</summary>
    private sealed class Snapshot
    {
        private readonly Dictionary<string, Dictionary<string, CounterSample>> _data = new(StringComparer.OrdinalIgnoreCase);

        public Snapshot(InstanceDataCollectionCollection all)
        {
            foreach (string counter in all.Keys)
            {
                Dictionary<string, CounterSample> byInstance = new(StringComparer.OrdinalIgnoreCase);
                foreach (InstanceData data in all[counter].Values)
                {
                    byInstance[data.InstanceName] = data.Sample;
                }

                _data[counter] = byInstance;
            }
        }

        public IEnumerable<string> Instances(string counter)
        {
            if (_data.TryGetValue(counter, out Dictionary<string, CounterSample>? byInstance)) return byInstance.Keys;
            return [];
        }

        public bool TryGet(string counter, string instance, out CounterSample sample)
        {
            sample = default;
            return _data.TryGetValue(counter, out Dictionary<string, CounterSample>? byInstance) && byInstance.TryGetValue(instance, out sample);
        }
    }
}
