<#
.SYNOPSIS
	Creates a repeatable CPU load on a schedule, and logs exactly when each burst started and stopped.

.DESCRIPTION
	Meant to be started from the same batch file that launches VPinPerfMon, so its clock starts with the capture.
	It waits -DelaySeconds, then runs a CPU burst of -BurstSeconds every -IntervalSeconds, and exits when
	-RuntimeSeconds have passed since the script started. A burst that would not finish before the runtime
	ends is skipped.

	Every burst is written to a CSV with UTC time and QPC milliseconds. QPC is the clock that PresentMon
	(--timebase qpc), the VPinPerfMon anchor and the SysStats files use, so the bursts can be laid directly
	over the frame and sampler data.

	The load runs inside this one PowerShell process (threads, not child processes), so a burst starts and
	ends on time. The process shows up as "powershell" in SysProcs. Works in Windows PowerShell 5.1.

.PARAMETER DelaySeconds
	Seconds to wait before the first burst. Counted from the start of the script, or from the moment the
	process named by -WaitForProcess appears.

.PARAMETER BurstSeconds
	Length of each burst.

.PARAMETER IntervalSeconds
	Time from the start of one burst to the start of the next. Must be larger than BurstSeconds.

.PARAMETER RuntimeSeconds
	Total time before the script exits, counted from the same moment as DelaySeconds.

.PARAMETER Cores
	Number of busy threads. Default: half of the logical cores.

.PARAMETER JitterSeconds
	Each burst starts up to this many seconds late (random), so bursts do not always land on the same
	phase of a 1 Hz sampler. 0 disables it.

.PARAMETER WaitForProcess
	Optional process name without .exe (for example VPinballX64). The delay and runtime are counted from
	the moment that process appears, not from the start of the script. Gives up after -WaitTimeoutSeconds.

.PARAMETER WaitTimeoutSeconds
	How long to wait for -WaitForProcess before giving up and starting the clock anyway.

.PARAMETER LogPath
	CSV file for the burst log. Default: LoadBursts_<yyyyMMddHHmmss>.csv next to the script.

.PARAMETER DryRun
	Print the schedule and exit without creating any load.

.EXAMPLE
	powershell -NoProfile -File LoadBursts.ps1 -DelaySeconds 120 -BurstSeconds 10 -IntervalSeconds 30 -RuntimeSeconds 300 -Cores 4

.EXAMPLE
	START /min "" powershell -NoProfile -File "C:\vPinball\mytools\VPinPerfMon\LoadBursts.ps1" -DelaySeconds 120 -BurstSeconds 10 -IntervalSeconds 30 -RuntimeSeconds 300 -Cores 4 -LogPath "C:\vPinball\mytools\VPinPerfMon\logs\LoadBursts_run1.csv"
#>
[CmdletBinding()]
param(
	[int]$DelaySeconds = 120,
	[int]$BurstSeconds = 10,
	[int]$IntervalSeconds = 30,
	[int]$RuntimeSeconds = 300,
	[int]$Cores = 0,
	[double]$JitterSeconds = 0,
	[string]$WaitForProcess = '',
	[int]$WaitTimeoutSeconds = 600,
	[string]$LogPath = '',
	[switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if ($BurstSeconds -lt 1) { throw 'BurstSeconds must be at least 1.' }
if ($IntervalSeconds -le $BurstSeconds) { throw 'IntervalSeconds must be larger than BurstSeconds.' }
if ($DelaySeconds -lt 0 -or $RuntimeSeconds -lt 1) { throw 'DelaySeconds must be 0 or more and RuntimeSeconds at least 1.' }
if ($JitterSeconds -lt 0) { throw 'JitterSeconds cannot be negative.' }
if ($Cores -le 0) { $Cores = [Math]::Max(1, [int]([Environment]::ProcessorCount / 2)) }
if ($Cores -gt [Environment]::ProcessorCount) { $Cores = [Environment]::ProcessorCount }

if (-not $LogPath) {
	$folder = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
	$LogPath = Join-Path $folder ('LoadBursts_{0}.csv' -f (Get-Date).ToString('yyyyMMddHHmmss'))
}

# Burst start offsets from the zero point (the start of the script, or when -WaitForProcess appears).
$starts = @()
for ($t = $DelaySeconds; ($t + $BurstSeconds + $JitterSeconds) -le $RuntimeSeconds; $t += $IntervalSeconds) { $starts += $t }

Write-Host ("LoadBursts: {0} burst(s) of {1} s every {2} s on {3} of {4} logical cores, first at {5} s, exit at {6} s" -f `
		$starts.Count, $BurstSeconds, $IntervalSeconds, $Cores, [Environment]::ProcessorCount, $DelaySeconds, $RuntimeSeconds)
if ($starts.Count -eq 0) { Write-Host 'No burst fits in the runtime. Nothing to do.'; return }
Write-Host ('Burst start offsets (s): ' + ($starts -join ', '))
Write-Host ('Log: ' + $LogPath)
if ($DryRun) { return }

Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Threading;

public static class LoadBurst
{
	// Spin on the requested number of threads until the deadline (Stopwatch ticks). Returns when all threads are done.
	public static void Run(int threads, long deadlineTimestamp)
	{
		Thread[] workers = new Thread[threads];
		for (int i = 0; i < threads; i++)
		{
			workers[i] = new Thread(() =>
			{
				double x = 1.0001;
				while (Stopwatch.GetTimestamp() < deadlineTimestamp)
				{
					for (int k = 0; k < 20000; k++) { x = Math.Sqrt(x * 1.0000001 + 1.0); }
				}
				GC.KeepAlive(x);
			});
			workers[i].IsBackground = true;
			workers[i].Start();
		}
		foreach (Thread w in workers) { w.Join(); }
	}
}
'@

function Get-QpcMs { [Diagnostics.Stopwatch]::GetTimestamp() * 1000.0 / [Diagnostics.Stopwatch]::Frequency }
function Get-Utc { [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture) }

$log = New-Object System.IO.StreamWriter($LogPath, $false, (New-Object System.Text.UTF8Encoding($false)))
$log.WriteLine('Event,Burst,Cores,PlannedStartOffsetSec,StartDelaySec,StartUtc,StartQpcMs,StopUtc,StopQpcMs,DurationSec,PowerShellPid')
$log.Flush()

$scriptStart = [Diagnostics.Stopwatch]::GetTimestamp()
$scriptStartQpc = Get-QpcMs
$scriptStartUtc = Get-Utc
$log.WriteLine(('ScriptStart,0,{0},,,{1},{2:F4},,,,{3}' -f $Cores, $scriptStartUtc, $scriptStartQpc, $PID))
$log.Flush()

# Zero point: the script start, or the moment the named process appears.
$zero = $scriptStart
if ($WaitForProcess) {
	$waitUntil = (Get-Date).AddSeconds($WaitTimeoutSeconds)
	Write-Host ("Waiting for process '{0}' (up to {1} s)..." -f $WaitForProcess, $WaitTimeoutSeconds)
	while (-not (Get-Process -Name $WaitForProcess -ErrorAction SilentlyContinue) -and (Get-Date) -lt $waitUntil) {
		Start-Sleep -Milliseconds 250
	}
	$zero = [Diagnostics.Stopwatch]::GetTimestamp()
	$found = [bool](Get-Process -Name $WaitForProcess -ErrorAction SilentlyContinue)
	$log.WriteLine(('ProcessWait,0,{0},,,{1},{2:F4},,,,{3}' -f $Cores, (Get-Utc), (Get-QpcMs), $(if ($found) { 'found' } else { 'timeout' })))
	$log.Flush()
	Write-Host ("Process {0}; starting the clock." -f $(if ($found) { 'found' } else { 'not found before the timeout' }))
}

$freq = [double][Diagnostics.Stopwatch]::Frequency
$rng = New-Object System.Random
$runtimeEnd = $zero + [long]($RuntimeSeconds * $freq)
$n = 0
foreach ($offset in $starts) {
	$n++
	$jitter = if ($JitterSeconds -gt 0) { $rng.NextDouble() * $JitterSeconds } else { 0.0 }
	$plannedStart = $zero + [long](($offset + $jitter) * $freq)

	# Sleep in short steps so the start is accurate, then spin the last few ms.
	while (($plannedStart - [Diagnostics.Stopwatch]::GetTimestamp()) -gt ($freq * 0.02)) { Start-Sleep -Milliseconds 10 }
	while ([Diagnostics.Stopwatch]::GetTimestamp() -lt $plannedStart) { }

	$startTs = [Diagnostics.Stopwatch]::GetTimestamp()
	$startUtc = Get-Utc
	$startQpc = Get-QpcMs
	$lateSec = ($startTs - ($zero + [long]($offset * $freq))) / $freq
	$deadline = $startTs + [long]($BurstSeconds * $freq)
	Write-Host ("[{0:F1}s] burst {1}/{2} start" -f (($startTs - $zero) / $freq), $n, $starts.Count)

	[LoadBurst]::Run($Cores, $deadline)

	$stopTs = [Diagnostics.Stopwatch]::GetTimestamp()
	$log.WriteLine(('Burst,{0},{1},{2},{3:F3},{4},{5:F4},{6},{7:F4},{8:F3},{9}' -f `
			$n, $Cores, $offset, $lateSec, $startUtc, $startQpc, (Get-Utc), (Get-QpcMs), (($stopTs - $startTs) / $freq), $PID))
	$log.Flush()
	Write-Host ("[{0:F1}s] burst {1}/{2} stop ({3:F2} s)" -f (($stopTs - $zero) / $freq), $n, $starts.Count, (($stopTs - $startTs) / $freq))
}

# Wait out the rest of the runtime so the script lives as long as asked.
while ([Diagnostics.Stopwatch]::GetTimestamp() -lt $runtimeEnd) { Start-Sleep -Milliseconds 200 }
$log.WriteLine(('ScriptEnd,0,{0},,,{1},{2:F4},,,,{3}' -f $Cores, (Get-Utc), (Get-QpcMs), $PID))
$log.Close()
Write-Host 'LoadBursts: done.'
