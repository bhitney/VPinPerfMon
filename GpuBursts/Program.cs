using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

[assembly: SupportedOSPlatform("windows")]

// Creates a repeatable GPU load (Direct3D 11 compute shader, no window) on a schedule and logs when each
// burst started and stopped. Same parameters and CSV layout as tools\LoadBursts.ps1.
const string Usage = """
GpuBursts - scheduled GPU compute load

  --delay <s>        Seconds before the first burst (default 120)
  --burst <s>        Length of each burst (default 10)
  --interval <s>     Start-to-start time of bursts, larger than --burst (default 30)
  --runtime <s>      Total time before exit (default 300)
  --jitter <s>       Random extra delay per burst (default 0)
  --adapter <n>      GPU adapter index (default 0, see --list)
  --target-ms <ms>   Length of one GPU dispatch (default 25, keep well under the 2 s driver timeout)
  --log <path>       CSV log (default GpuBursts_<yyyyMMddHHmmss>.csv next to the exe)
  --list             List GPU adapters and exit
  --dry-run          Print the schedule and exit
""";

const string Hlsl = """
cbuffer Params : register(b0) { uint Iterations; uint3 Pad; };
RWStructuredBuffer<float> Output : register(u0);

[numthreads(256, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float x = 1.0001f + id.x * 1e-7f;
    for (uint i = 0; i < Iterations; i++)
    {
        x = sqrt(x * 1.0000001f + 1.0f);
        x = sin(x) + cos(x) + 1.5f;
    }
    Output[id.x] = x;
}
""";

const int Groups = 4096;
const int Threads = Groups * 256;

static double QpcMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
static string Utc() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (!a.StartsWith("--")) { Console.Error.WriteLine(Usage); return 2; }
    if (a is "--list" or "--dry-run" or "--help") { flags.Add(a); continue; }
    if (i + 1 >= args.Length) { Console.Error.WriteLine(Usage); return 2; }
    opts[a] = args[++i];
}
if (flags.Contains("--help")) { Console.WriteLine(Usage); return 0; }

double Num(string key, double def) =>
    opts.TryGetValue(key, out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;

if (flags.Contains("--list"))
{
    DXGI.CreateDXGIFactory1(out IDXGIFactory1? lf).CheckError();
    using (lf)
    {
        for (uint i = 0; lf!.EnumAdapters1(i, out IDXGIAdapter1? ad).Success; i++)
        {
            using (ad) { Console.WriteLine($"{i}: {ad!.Description1.Description}"); }
        }
    }
    return 0;
}

double delay = Num("--delay", 120), burst = Num("--burst", 10), interval = Num("--interval", 30);
double runtime = Num("--runtime", 300), jitter = Num("--jitter", 0), targetMs = Num("--target-ms", 25);
uint adapterIndex = (uint)Num("--adapter", 0);
string logPath = opts.TryGetValue("--log", out string? lp) ? lp
    : Path.Combine(AppContext.BaseDirectory, $"GpuBursts_{DateTime.Now:yyyyMMddHHmmss}.csv");

if (burst < 1) { Console.Error.WriteLine("--burst must be at least 1."); return 2; }
if (interval <= burst) { Console.Error.WriteLine("--interval must be larger than --burst."); return 2; }
if (delay < 0 || runtime < 1) { Console.Error.WriteLine("--delay must be 0 or more and --runtime at least 1."); return 2; }
if (jitter < 0) { Console.Error.WriteLine("--jitter cannot be negative."); return 2; }

// Bursts that finish before the runtime ends.
var starts = new List<double>();
for (double t = delay; t + burst <= runtime; t += interval) { starts.Add(t); }

if (flags.Contains("--dry-run"))
{
    Console.WriteLine($"Adapter {adapterIndex}, {starts.Count} burst(s), log {logPath}");
    foreach (double s in starts) { Console.WriteLine($"  start {s:F1}s, stop {s + burst:F1}s"); }
    return 0;
}

DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
using IDXGIFactory1 factoryOwner = factory!;
if (!factory!.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success)
{
    Console.Error.WriteLine($"Adapter {adapterIndex} not found. Use --list.");
    return 2;
}
using IDXGIAdapter1 adapterOwner = adapter!;
string adapterName = adapter!.Description1.Description;

D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
    new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
using ID3D11Device dev = device!;
using ID3D11DeviceContext ctx = context!;

Compiler.Compile(Hlsl, "main", "GpuBursts.hlsl", "cs_5_0", out Blob? shaderBlob, out Blob? errorBlob);
if (shaderBlob is null)
{
    Console.Error.WriteLine("Shader compile failed: " + (errorBlob is null ? "unknown error" : Encoding.ASCII.GetString(errorBlob.AsSpan())));
    return 1;
}
using ID3D11ComputeShader shader = dev.CreateComputeShader(shaderBlob.AsSpan());
shaderBlob.Dispose();
errorBlob?.Dispose();

uint bufferBytes = (uint)Threads * 4;
using ID3D11Buffer output = dev.CreateBuffer(new BufferDescription(bufferBytes, BindFlags.UnorderedAccess,
    ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
using ID3D11UnorderedAccessView uav = dev.CreateUnorderedAccessView(output);
using ID3D11Buffer staging = dev.CreateBuffer(new BufferDescription(bufferBytes, BindFlags.None,
    ResourceUsage.Staging, CpuAccessFlags.Read));
using ID3D11Buffer constants = dev.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));

ctx.CSSetShader(shader);
ctx.CSSetUnorderedAccessView(0, uav);
ctx.CSSetConstantBuffer(0, constants);

// One dispatch, then wait for it to finish by reading the result back. Returns elapsed milliseconds.
double Dispatch(uint iterations)
{
    ctx.UpdateSubresource(new[] { iterations, 0u, 0u, 0u }, constants);
    long t0 = Stopwatch.GetTimestamp();
    ctx.Dispatch(Groups, 1, 1);
    ctx.CopyResource(staging, output);
    ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
    ctx.Unmap(staging, 0);
    return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
}

// Calibrate so one dispatch takes about targetMs.
uint iters = 256;
Dispatch(iters);
for (int i = 0; i < 8; i++)
{
    double ms = Dispatch(iters);
    if (ms <= 0) { break; }
    double next = iters * targetMs / ms;
    iters = (uint)Math.Clamp(next, 16, 1_000_000);
    if (Math.Abs(ms - targetMs) < targetMs * 0.1) { break; }
}

using var log = new StreamWriter(logPath, false, new UTF8Encoding(false));
log.WriteLine("Event,Burst,Adapter,PlannedStartOffsetSec,StartDelaySec,StartUtc,StartQpcMs,StopUtc,StopQpcMs,DurationSec,Pid");
log.WriteLine($"ScriptStart,0,\"{adapterName}\",,,{Utc()},{F(QpcMs(), "F4")},,,,{Environment.ProcessId}");
log.Flush();
Console.WriteLine($"GpuBursts: {adapterName}, {iters} iterations per {targetMs:F0} ms dispatch, {starts.Count} burst(s).");

long freq = Stopwatch.Frequency;
long zero = Stopwatch.GetTimestamp();
long runtimeEnd = zero + (long)(runtime * freq);
var rng = new Random();
int n = 0;
foreach (double offset in starts)
{
    n++;
    double j = jitter > 0 ? rng.NextDouble() * jitter : 0.0;
    long planned = zero + (long)((offset + j) * freq);
    while (planned - Stopwatch.GetTimestamp() > freq * 0.02) { Thread.Sleep(10); }
    while (Stopwatch.GetTimestamp() < planned) { }

    long startTs = Stopwatch.GetTimestamp();
    string startUtc = Utc();
    double startQpc = QpcMs();
    double late = (startTs - (zero + (long)(offset * freq))) / (double)freq;
    long deadline = startTs + (long)(burst * freq);
    Console.WriteLine($"[{(startTs - zero) / (double)freq:F1}s] burst {n}/{starts.Count} start");

    while (Stopwatch.GetTimestamp() < deadline) { Dispatch(iters); }

    long stopTs = Stopwatch.GetTimestamp();
    log.WriteLine($"Burst,{n},\"{adapterName}\",{F(offset, "F1")},{F(late, "F3")},{startUtc},{F(startQpc, "F4")},{Utc()},{F(QpcMs(), "F4")},{F((stopTs - startTs) / (double)freq, "F3")},{Environment.ProcessId}");
    log.Flush();
    Console.WriteLine($"[{(stopTs - zero) / (double)freq:F1}s] burst {n}/{starts.Count} stop ({(stopTs - startTs) / (double)freq:F2} s)");
}

while (Stopwatch.GetTimestamp() < runtimeEnd) { Thread.Sleep(200); }
log.WriteLine($"ScriptEnd,0,\"{adapterName}\",,,{Utc()},{F(QpcMs(), "F4")},,,,{Environment.ProcessId}");
Console.WriteLine("GpuBursts: done.");
return 0;
