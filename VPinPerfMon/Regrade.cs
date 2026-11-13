using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace VPinPerfMon;

internal partial class Program
{
    // Columns written by --regrade. Names match the parameters bound by PerformanceData.AddGradeParameters.
    private static readonly string[] GradeColumns =
    [
        "StutterGrade", "StutterGradeScore", "StutterGradeCapped", "StutterGradeNote", "StutterGradeVersion",
        "GradeFrames", "GradeMissingFrames", "GradeDurationSec", "GradeRefreshMs", "GradeRefreshSource",
        "DoubledFrames", "TripleFrames", "LongestMissRefreshes", "MissedFramePct", "MissesPerMinute", "WeightedMissPct",
        "ClusterPeakMisses", "ClusterRatio", "ClusterMultiplier", "EffectiveMissPct",
        "CfgDoubledRefreshes", "CfgTripleRefreshes", "CfgBandA", "CfgBandB", "CfgBandC", "CfgBandD", "CfgZeroScorePct",
        "CfgTripleWeight", "CfgTripleCapGrade", "CfgClusterWindowSec", "CfgClusterWeight", "CfgClusterMaxExcess",
        "CfgClusterMinEvents", "CfgMinFrames", "CfgTripleCapMinCount", "CfgTrimEndSeconds", "CfgClusterMinPeak", "CfgTrimStartSeconds",
        "LongGapCount", "LongGapPct", "LongGapThresholdMs", "LongGapMissCount", "LongGapNextDisplayedAvg",
        "SwapChainAddress", "SwapChainCount", "PresentApplication", "PresentMode",
        "BlameCpuPct", "BlameGpuPct", "BlameVersion", "GpuBusyPctOfRefresh", "StallCpuCount", "StallGpuCount",
        "StallModeSwitchCount", "StallAppWaitCount", "StallDisplayCount", "CfgBlameMinStalls", "CfgBlameGpuSaturation",
        "FileAnalysisPath", "FileRawDataPath", "FileAssetStem"
    ];

    private sealed record RegradeRow(long Id, int GameId, string? SourceFile, DateTime EntryUtc, int FrameCount, double? OldScoreDT, bool HasGrade, double? OldBlameCpu);

    private sealed class LogHeader
    {
        public int? GameId { get; set; }
        public string? SourceFile { get; set; }
        public float? Hz { get; set; }
        public int? DelayStart { get; set; }
        public List<string> Processes { get; } = [];
        public bool DatabaseInsert { get; set; }
    }

    private static LogHeader ParseLogHeader(string txtPath)
    {
        LogHeader header = new();
        foreach (string line in File.ReadLines(txtPath).Take(60))
        {
            if (line.StartsWith("Game ID:", StringComparison.Ordinal) &&
                int.TryParse(line["Game ID:".Length..].Trim(), out int gid))
            {
                header.GameId = gid;
            }
            else if (line.StartsWith("Source File:", StringComparison.Ordinal))
            {
                header.SourceFile = line["Source File:".Length..].Trim();
            }
            else if (line.StartsWith("Monitor Refresh Rate:", StringComparison.Ordinal))
            {
                var m = Regex.Match(line, @"Refresh Rate:\s*([\d.]+)\s*Hz\s*\(manual override\)");
                if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hz) && hz > 0)
                    header.Hz = hz;
            }
            else if (line.StartsWith("Target Process(es):", StringComparison.Ordinal))
            {
                header.Processes.AddRange(line["Target Process(es):".Length..]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
            else if (line.StartsWith("Data collection will begin after", StringComparison.Ordinal))
            {
                var m = Regex.Match(line, @"after\s+(\d+)\s+second");
                if (m.Success) header.DelayStart = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("Database insert: Enabled", StringComparison.Ordinal))
            {
                header.DatabaseInsert = true;
            }
        }
        return header;
    }

    private static int RunRegrade(string[] args, string databasePath)
    {
        OutputHeader();
        Console.WriteLine("Regrade mode...\n");

        string logPath = Directory.GetCurrentDirectory();
        bool apply = false, force = false, allowMismatch = false;
        int limit = 0;
        string? gpuName = null, gpuDriver = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--logpath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                logPath = args[++i].Trim('"');
            else if (args[i].Equals("--gpu_name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                gpuName = args[++i].Trim('"');
            else if (args[i].Equals("--gpu_driver", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                gpuDriver = args[++i].Trim('"');
            else if (args[i].Equals("--apply", StringComparison.OrdinalIgnoreCase))
                apply = true;
            else if (args[i].Equals("--force", StringComparison.OrdinalIgnoreCase))
                force = true;
            else if (args[i].Equals("--allow-mismatch", StringComparison.OrdinalIgnoreCase))
                allowMismatch = true;
            else if (args[i].Equals("--limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out limit);
            else if (args[i].Equals("--blame_gpu_saturation", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out float sat) && sat > 0 && sat <= 1.5f)
                    BlameConfig.GpuSaturationFraction = sat;
            }
            else if (args[i].Equals("--blame_min_stalls", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int minStalls) && minStalls >= 1)
                    BlameConfig.MinAttributableStalls = minStalls;
            }
        }

        if (!File.Exists(databasePath))
        {
            Console.WriteLine($"ERROR: Database file not found at: {databasePath}");
            return 1;
        }

        if (!Directory.Exists(logPath))
        {
            Console.WriteLine($"ERROR: Log directory not found: {logPath}");
            return 1;
        }

        Console.WriteLine($"Database:   {databasePath}");
        Console.WriteLine($"Log path:   {Path.GetFullPath(logPath)}");
        Console.WriteLine($"Mode:       {(apply ? "APPLY (database will be updated)" : "DRY RUN (no changes; add --apply to write)")}");
        Console.WriteLine($"Rows:       {(force ? "all matched rows (--force)" : "only rows without a new-style grade")}");
        if (limit > 0) Console.WriteLine($"Limit:      {limit} most recent runs with a CSV");
        Console.WriteLine();

        try
        {
            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            using (SqliteCommand check = connection.CreateCommand())
            {
                check.CommandText = "SELECT name FROM pragma_table_info('CustomPerfStats')";
                HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
                using (SqliteDataReader columns = check.ExecuteReader())
                {
                    while (columns.Read()) existing.Add(columns.GetString(0));
                }

                string[] missingColumns = GradeColumns.Concat(["GpuName", "GpuDriverVersion"]).Where(c => !existing.Contains(c)).ToArray();
                if (missingColumns.Length > 0)
                {
                    Console.WriteLine($"ERROR: The database is missing {missingColumns.Length} column(s) that --regrade writes: {string.Join(", ", missingColumns)}");
                    Console.WriteLine("Apply Migrations\\001_add_stutter_grade_columns.sql (or the individual ALTER TABLE statements for the missing columns) first.");
                    return 1;
                }
            }

            List<RegradeRow> rows = [];
            using (SqliteCommand select = connection.CreateCommand())
            {
                select.CommandText = "SELECT Id, GameId, SourceFile, EntryDate, PresentMonFrameCount, StutterScoreDT, StutterGradeVersion, BlameCpuPct FROM CustomPerfStats";
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read())
                {
                    if (!DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime entryUtc))
                        continue;

                    rows.Add(new RegradeRow(
                        reader.GetInt64(0),
                        reader.GetInt32(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        entryUtc,
                        reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        !reader.IsDBNull(6),
                                                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
                }
            }

            Console.WriteLine($"Database rows: {rows.Count}");

            bool writeGpu = gpuName != null || gpuDriver != null;
            Console.WriteLine(writeGpu
                ? $"GPU:        {gpuName} {gpuDriver} (from --gpu_name/--gpu_driver; written to every updated row)"
                : "GPU:        GpuName/GpuDriverVersion left unchanged (use --gpu_name and --gpu_driver to set them)");

            var txtFiles = Directory.EnumerateFiles(logPath, "PresentMon_*.txt")
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();
            Console.WriteLine($"Log .txt files: {txtFiles.Count}\n");

            Dictionary<string, int> tally = [];
            void Count(string key) => tally[key] = tally.GetValueOrDefault(key) + 1;

            List<(RegradeRow Row, PerformanceData Data)> updates = [];
            HashSet<long> claimed = [];
            int processed = 0;

            Console.WriteLine($"{"Row",6}  {"Run",-44} {"Old DT",7}  {"New",-4} {"Score",6} {"Dbl",4} {"3+",3} {"Longest",7}  Status");

            foreach (string txt in txtFiles)
            {
                string csv = Path.ChangeExtension(txt, ".csv");
                string runName = Regex.Replace(Path.GetFileNameWithoutExtension(txt), @"^PresentMon_\d{14}_?", string.Empty);
                if (runName.Length > 44) runName = runName[..44];

                if (!File.Exists(csv))
                {
                    Count("skipped: no CSV");
                    continue;
                }

                if (limit > 0 && processed >= limit) break;
                processed++;

                void Report(string status, RegradeRow? row = null, PerformanceData? data = null)
                {
                    Count(status);
                    string oldScore = row?.OldScoreDT.HasValue == true ? row.OldScoreDT.Value.ToString("F1", CultureInfo.InvariantCulture) : "-";
                    var g = data?.StutterGrade;
                    string grade = g is { Graded: true } ? g.Grade : "-";
                    string score = g is { Graded: true } ? g.Score.ToString("F1", CultureInfo.InvariantCulture) : "-";
                    string dbl = g is { Graded: true } ? g.Doubled.ToString(CultureInfo.InvariantCulture) : "-";
                    string tri = g is { Graded: true } ? g.Triple.ToString(CultureInfo.InvariantCulture) : "-";
                    string longest = g is { Graded: true } ? g.LongestRefreshes.ToString(CultureInfo.InvariantCulture) : "-";
                    Console.WriteLine($"{(row?.Id.ToString(CultureInfo.InvariantCulture) ?? "-"),6}  {runName,-44} {oldScore,7}  {grade,-4} {score,6} {dbl,4} {tri,3} {longest,7}  {status}");
                }

                LogHeader header;
                try
                {
                    header = ParseLogHeader(txt);
                }
                catch (Exception ex)
                {
                    Report($"skipped: cannot read .txt ({ex.Message})");
                    continue;
                }

                if (!header.GameId.HasValue || !header.DelayStart.HasValue)
                {
                    Report("skipped: .txt header incomplete (no Game ID or warm-up)");
                    continue;
                }

                if (!header.DatabaseInsert)
                {
                    Report("skipped: run did not insert into the database");
                    continue;
                }

                // Entry time is when the run inserted its row, just before the .txt was written (EntryDate is UTC).
                DateTime txtUtc = File.GetLastWriteTimeUtc(txt);
                var candidates = rows
                    .Where(r => r.GameId == header.GameId.Value
                                && (header.SourceFile == null || r.SourceFile == null ||
                                    string.Equals(r.SourceFile, header.SourceFile, StringComparison.OrdinalIgnoreCase))
                                && Math.Abs((r.EntryUtc - txtUtc).TotalSeconds) <= 120)
                    .ToList();

                if (candidates.Count == 0)
                {
                    Report("skipped: no matching database row");
                    continue;
                }

                if (candidates.Count > 1)
                {
                    Report($"skipped: {candidates.Count} rows match, ambiguous");
                    continue;
                }

                RegradeRow row = candidates[0];
                if (claimed.Contains(row.Id))
                {
                    Report("skipped: row already matched by another file", row);
                    continue;
                }

                if (row.HasGrade && !force)
                {
                    Report("skipped: already graded (use --force)", row);
                    continue;
                }

                PerformanceData data = new()
                {
                    Hz = header.Hz,
                    SourceFile = header.SourceFile,
                    ProcessFiltered = header.Processes.Count > 0,
                    DelayStartSeconds = header.DelayStart,
                    GpuName = gpuName,
                    GpuDriverVersion = gpuDriver
                };

                TextWriter originalOut = Console.Out;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    ParsePresentMonCsv(csv, data, header.Processes, header.DelayStart.Value, header.Hz);
                }
                catch (Exception ex)
                {
                    Console.SetOut(originalOut);
                    Report($"skipped: CSV parse failed ({ex.Message})", row);
                    continue;
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                if (data.StutterGrade == null)
                {
                    Report("skipped: no frames", row, data);
                    continue;
                }

                if (data.PresentMonFrames.Count != row.FrameCount && !allowMismatch)
                {
                    Report($"skipped: frame count {data.PresentMonFrames.Count} != stored {row.FrameCount} (--allow-mismatch to override)", row, data);
                    continue;
                }

                data.FileAnalysisPath = Path.GetFullPath(txt);
                data.FileRawDataPath = Path.GetFullPath(csv);
                string txtName = Path.GetFileNameWithoutExtension(txt);
                data.FileAssetStem = txtName.StartsWith("PresentMon_", StringComparison.Ordinal) ? txtName["PresentMon_".Length..] : null;
                claimed.Add(row.Id);
                updates.Add((row, data));
                Report(apply ? "will update" : "would update", row, data);

                var sb = ComputeStallBlame(data);
                if (sb != null)
                {
                    string F(float? v) => v.HasValue ? v.Value.ToString("F0", CultureInfo.InvariantCulture) : "-";
                    string oldDom = row.OldBlameCpu.HasValue ? (row.OldBlameCpu >= 50 ? "CPU" : "GPU") : "-";
                    string newDom = sb.CpuPct.HasValue ? (sb.CpuPct >= 50 ? "CPU" : "GPU") : "-";
                    Count($"blame old {oldDom} -> new {newDom}");
                    Console.WriteLine($"        blame old cpu {F((float?)row.OldBlameCpu),3}% | stalls {sb.Stalls,4}: cpu {sb.Cpu} gpu {sb.Gpu} mode {sb.ModeSwitch} appwait {sb.AppWait} display {sb.Display} | new cpu {F(sb.CpuPct),3}% | GPU busy {F(sb.GpuBusyPctOfRefresh)}% of refresh");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Summary:");
            foreach (var kv in tally.OrderByDescending(k => k.Value))
                Console.WriteLine($"  {kv.Value,5}  {kv.Key}");
            Console.WriteLine();

            if (updates.Count == 0)
            {
                Console.WriteLine("Nothing to update.");
                return 0;
            }

            if (!apply)
            {
                Console.WriteLine($"Dry run: {updates.Count} row(s) would be updated. Re-run with --apply to write.");
                return 0;
            }

            string backupPath = $"{databasePath}.bak_{DateTime.Now:yyyyMMddHHmmss}";
            using (SqliteCommand backup = connection.CreateCommand())
            {
                backup.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
                backup.ExecuteNonQuery();
            }
            Console.WriteLine($"Backup written: {backupPath}");

            string setClause = string.Join(", ", GradeColumns.Select(c => $"{c} = @{c}"));
            if (gpuName != null) setClause += ", GpuName = @GpuName";
            if (gpuDriver != null) setClause += ", GpuDriverVersion = @GpuDriverVersion";
            using (SqliteTransaction tx = connection.BeginTransaction())
            {
                int affected = 0;
                foreach (var (row, data) in updates)
                {
                    using SqliteCommand update = connection.CreateCommand();
                    update.Transaction = tx;
                    update.CommandText = $"UPDATE CustomPerfStats SET {setClause} WHERE Id = @Id";
                    data.AddGradeParameters(update);
                    update.Parameters.AddWithValue("@Id", row.Id);
                    affected += update.ExecuteNonQuery();
                }
                tx.Commit();
                Console.WriteLine($"SUCCESS: {affected} row(s) updated.");
            }

            return 0;
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"\nERROR: Database error: {ex.Message}");
            return 1;
        }
    }
}
