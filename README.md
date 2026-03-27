# VPinPerfMon

Automated performance capture for pinball emulators (and any Windows application). VPinPerfMon collects CPU, GPU, and per-frame timing data, then produces a detailed report including a **Stutter Score** that grades perceived smoothness on a 0–100 scale.

## Components

| Project | Description |
|---------|-------------|
| **VPinPerfMon** | Command-line tool that orchestrates data collection. Reads CPU/GPU counters via NVML and launches [PresentMon 2.x](https://github.com/GameTechDev/PresentMon) for frame-level analysis. Outputs statistics to the console and optionally to a SQLite database. |
| **VPinPerfMon.GUI** | WinForms front-end for building command-line options, running VPinPerfMon, and viewing output without touching a terminal. Settings are saved between sessions. |
| **VPinPerfMon.Setup** | First-run setup wizard that verifies prerequisites (permissions, PresentMon, database) and copies all files to a chosen install location. |

## Quick Start

1. **Download** the latest build from [Releases](https://github.com/bhitney/VPinPerfMon/releases) (or the Actions artifact).
2. Run `VPinPerfMon.Setup.exe` to verify prerequisites, or skip straight to the CLI / GUI.
3. Minimal test — CPU & GPU only, 60-second capture with a 15-second warm-up:

   ```
   VPinPerfMon.exe --delaystart 15 --timeout 60
   ```

4. Full capture with frame timing (requires PresentMon):

   ```
   VPinPerfMon.exe --delaystart 15 --timeout 45 --presentmonpath "PresentMon-2.5.0-x64.exe" --process_name VPinballX64.exe
   ```

See [`VPinPerfMon/readme.md`](VPinPerfMon/readme.md) for the complete command reference, output examples, stutter-score methodology, and troubleshooting.

## Requirements

- Windows 10/11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download)
- NVIDIA GPU (for GPU utilization/memory stats; CPU and frame metrics work on any system)
- [PresentMon 2.x](https://github.com/GameTechDev/PresentMon/releases) (for frame timing — included in the release)

## Database Integration (Optional)

VPinPerfMon can write results to a SQLite database for historical tracking. This is aimed at advanced users comfortable with SQL queries and is especially useful when paired with [PinUP Popper](https://www.nailbuster.com/)'s database for per-game performance history.

```
VPinPerfMon.exe --gameid 1523 --outputsql --sqlitedbpath "C:\vPinball\PinUPSystem\PUPDatabase.db" --timeout 60 --process_name VPinballX64.exe
```

Run `--createsql` once to create the table and indexes. See the full readme for schema details.

## Building

```
dotnet build VPinPerfMon.slnx -c Release
```

The `VPinPerfMon.Setup` project's post-build step copies all outputs (CLI, GUI, Setup, runtimes, PresentMon executables) into a `distrib/` folder ready for distribution.

## License

MIT
