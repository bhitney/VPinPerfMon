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

## Reporting

The [`Reporting`](Reporting) folder contains a two-step Python pipeline for turning PinUP Popper performance history into an interactive HTML report. `extract_perf_stats.py` exports `CustomPerfStats` with game names, ratings, and emulator names from the database; `generate_scatter_report.py` builds scatter charts, emulator comparisons, best/worst table rankings, and anomaly detection with recommended re-runs. You can filter by rating, capture date, and table, search for tables, and zoom or expand charts.

Requires Python 3 and `pandas` (separate from the .NET application). From the repository root:

```powershell
cd .\Reporting
python -m pip install pandas
python extract_perf_stats.py "C:\vPinball\PinUPSystem\PUPDatabase.db"
python generate_scatter_report.py
```

Open the generated `PerfStats_ScatterReport_<timestamp>.html` in your browser. Outputs are written to the current directory; without an explicit CSV path, the generator selects the last `CustomPerfStats_*.csv` filename in sorted order (the latest timestamp for standard filenames). Charts require internet access to load JavaScript libraries from a CDN.

See the [reporting guide](Reporting/PERF_STATS_REPORT_PROCESS.md) for usage and methodology. Download or open the included [demo report](Reporting/PerfStats_ScatterReport_Demo.html) locally to explore a sample without a database. Local databases, CSV exports, and generated reports are excluded from version control; only the demo HTML is included.

## Building

```
dotnet build VPinPerfMon.slnx -c Release
```

The `VPinPerfMon.Setup` project's post-build step copies all outputs (CLI, GUI, Setup, runtimes, PresentMon executables) into a `distrib/` folder ready for distribution.

## License

MIT
