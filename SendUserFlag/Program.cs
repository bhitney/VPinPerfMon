using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

// Signals a running VPinPerfMon to log a "UserFlag" event. Fails silently (exit code 0) if it is not running.
try
{
    using EventWaitHandle flag = EventWaitHandle.OpenExisting("Local\\VPinPerfMon_Flag");
    flag.Set();
}
catch (Exception)
{
}
