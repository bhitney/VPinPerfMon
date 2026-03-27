using System.Diagnostics;

namespace VPinPerfMon.GUI;

internal sealed class MainForm : Form
{
    private readonly NumericUpDown _delayNumeric;
    private readonly NumericUpDown _timeoutNumeric;
    private readonly CheckBox _deleteCsvCheckBox;
    private readonly TextBox _logPathTextBox;
    private readonly TextBox _presentMonPathTextBox;
    private readonly TextBox _sourceFileTextBox;
    private readonly ComboBox _processNameComboBox;
    private readonly ListBox _processNameListBox;
    private readonly TextBox _commandLineTextBox;
    private readonly TextBox _outputTextBox;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly Button _copyButton;

    private Process? _runningProcess;
    private CancellationTokenSource? _cts;

    private static readonly string ProcessNamesFile =
        Path.Combine(AppContext.BaseDirectory, "ProcessNames.txt");

    public MainForm()
    {
        Text = "VPinPerfMon GUI";
        Size = new Size(700, 820);
        MinimumSize = new Size(620, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10),
            AutoScroll = true
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // === Options group ===
        var optionsGroup = new GroupBox { Text = "Options", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var optionsScroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var optionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(4)
        };
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;

        // Warmup
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("Warmup (seconds):"), 0, row);
        _delayNumeric = new NumericUpDown { Minimum = 0, Maximum = 300, Value = 5, Width = 80, Anchor = AnchorStyles.Left };
        _delayNumeric.ValueChanged += (_, _) => UpdateCommandLine();
        optionsLayout.Controls.Add(_delayNumeric, 1, row);
        row++;

        // Timeout
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("Timeout (seconds):"), 0, row);
        var timeoutPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        _timeoutNumeric = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = 15, Width = 80 };
        _timeoutNumeric.ValueChanged += (_, _) => UpdateCommandLine();
        var timeoutHint = new Label { Text = "(0 = unlimited)", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left, Padding = new Padding(4, 4, 0, 0) };
        timeoutPanel.Controls.Add(_timeoutNumeric);
        timeoutPanel.Controls.Add(timeoutHint);
        optionsLayout.Controls.Add(timeoutPanel, 1, row);
        row++;

        // DeleteCSV
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _deleteCsvCheckBox = new CheckBox { Text = "Keep CSV && Log file", Checked = true, AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
        _deleteCsvCheckBox.CheckedChanged += (_, _) =>
        {
            _logPathTextBox.Enabled = _deleteCsvCheckBox.Checked;
            UpdateCommandLine();
        };
        optionsLayout.Controls.Add(_deleteCsvCheckBox, 1, row);
        row++;

        // Log path
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("Log Path:"), 0, row);
        var logPathPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        logPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logPathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _logPathTextBox = new TextBox { Dock = DockStyle.Fill, Text = Path.Combine(AppContext.BaseDirectory, "logs") };
        _logPathTextBox.TextChanged += (_, _) => UpdateCommandLine();
        var browseBtn = new Button { Text = "...", Width = 30, Height = _logPathTextBox.Height };
        browseBtn.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _logPathTextBox.Text };
            if (dialog.ShowDialog() == DialogResult.OK)
                _logPathTextBox.Text = dialog.SelectedPath;
        };
        logPathPanel.Controls.Add(_logPathTextBox, 0, 0);
        logPathPanel.Controls.Add(browseBtn, 1, 0);
        optionsLayout.Controls.Add(logPathPanel, 1, row);
        optionsLayout.SetColumnSpan(logPathPanel, 2);
        row++;

        // PresentMon path
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("PresentMon Path:"), 0, row);
        var presentMonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        presentMonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        presentMonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _presentMonPathTextBox = new TextBox { Dock = DockStyle.Fill, Text = FindLatestPresentMon() };
        _presentMonPathTextBox.TextChanged += (_, _) => UpdateCommandLine();
        var browsePresentMonBtn = new Button { Text = "...", Width = 30 };
        browsePresentMonBtn.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select PresentMon executable",
                Filter = "PresentMon|PresentMon*.exe|All executables|*.exe",
                InitialDirectory = AppContext.BaseDirectory
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                _presentMonPathTextBox.Text = dialog.FileName;
        };
        presentMonPanel.Controls.Add(_presentMonPathTextBox, 0, 0);
        presentMonPanel.Controls.Add(browsePresentMonBtn, 1, 0);
        optionsLayout.Controls.Add(presentMonPanel, 1, row);
        optionsLayout.SetColumnSpan(presentMonPanel, 2);
        row++;

        // Source table name
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("Source Table Name:"), 0, row);
        _sourceFileTextBox = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "e.g. TestTable.vpx" };
        _sourceFileTextBox.TextChanged += (_, _) => UpdateCommandLine();
        optionsLayout.Controls.Add(_sourceFileTextBox, 1, row);
        row++;

        // Process names
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("Process Name:"), 0, row);
        var processPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        processPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        processPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _processNameComboBox = new ComboBox { Dock = DockStyle.Fill };
        LoadProcessNames();
        var addProcessBtn = new Button { Text = "Add", AutoSize = true };
        addProcessBtn.Click += (_, _) => AddProcessName();
        processPanel.Controls.Add(_processNameComboBox, 0, 0);
        processPanel.Controls.Add(addProcessBtn, 1, 0);
        optionsLayout.Controls.Add(processPanel, 1, row);
        optionsLayout.SetColumnSpan(processPanel, 2);
        row++;

        // Process name list with Remove button
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreateLabel("Current Processes:"), 0, row);
        var processListPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        processListPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        processListPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _processNameListBox = new ListBox { Height = 60, Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        var removeProcessBtn = new Button { Text = "Remove", AutoSize = true };
        removeProcessBtn.Click += (_, _) => RemoveProcessName();
        processListPanel.Controls.Add(_processNameListBox, 0, 0);
        processListPanel.Controls.Add(removeProcessBtn, 1, 0);
        optionsLayout.Controls.Add(processListPanel, 1, row);
        optionsLayout.SetColumnSpan(processListPanel, 2);
        row++;

        optionsScroller.Controls.Add(optionsLayout);
        optionsGroup.Controls.Add(optionsScroller);

        // === Command line group ===
        var cmdGroup = new GroupBox { Text = "Command Line", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var cmdLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        cmdLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cmdLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _commandLineTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Info,
            Font = new Font("Consolas", 9f),
            MinimumSize = new Size(0, 48)
        };
        var cmdBtnPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight };
        _copyButton = new Button { Text = "Copy to Clipboard", AutoSize = true };
        _copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_commandLineTextBox.Text))
                Clipboard.SetText(_commandLineTextBox.Text);
        };
        _runButton = new Button { Text = "Run", AutoSize = true };
        _runButton.Click += async (_, _) => await RunCommandAsync();
        _cancelButton = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        _cancelButton.Click += (_, _) => CancelRunningProcess();
        cmdBtnPanel.Controls.Add(_copyButton);
        cmdBtnPanel.Controls.Add(_runButton);
        cmdBtnPanel.Controls.Add(_cancelButton);
        cmdLayout.Controls.Add(_commandLineTextBox, 0, 0);
        cmdLayout.Controls.Add(cmdBtnPanel, 0, 1);
        cmdGroup.Controls.Add(cmdLayout);

        // === Output group ===
        var outputGroup = new GroupBox { Text = "Output", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _outputTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.White
        };
        outputGroup.Controls.Add(_outputTextBox);

        // Add groups to main layout
        mainLayout.RowCount = 3;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        mainLayout.Controls.Add(optionsGroup, 0, 0);
        mainLayout.Controls.Add(cmdGroup, 0, 1);
        mainLayout.Controls.Add(outputGroup, 0, 2);

        Controls.Add(mainLayout);

        LoadSettings();
        UpdateCommandLine();
    }

    private static Label CreateLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) };

    private void LoadSettings()
    {
        var s = Settings.Load();

        _delayNumeric.Value = Math.Clamp(s.Warmup, (int)_delayNumeric.Minimum, (int)_delayNumeric.Maximum);
        _timeoutNumeric.Value = Math.Clamp(s.Timeout, (int)_timeoutNumeric.Minimum, (int)_timeoutNumeric.Maximum);
        _deleteCsvCheckBox.Checked = s.KeepCsvAndLog;
        _logPathTextBox.Enabled = s.KeepCsvAndLog;

        if (!string.IsNullOrWhiteSpace(s.LogPath))
            _logPathTextBox.Text = s.LogPath;

        if (!string.IsNullOrWhiteSpace(s.PresentMonPath))
            _presentMonPathTextBox.Text = s.PresentMonPath;

        if (!string.IsNullOrWhiteSpace(s.SourceTableName))
            _sourceFileTextBox.Text = s.SourceTableName;

        _processNameListBox.Items.Clear();
        foreach (var name in s.ProcessNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _processNameListBox.Items.Add(name);
        }

        if (s.WindowWidth >= MinimumSize.Width && s.WindowHeight >= MinimumSize.Height)
            Size = new Size(s.WindowWidth, s.WindowHeight);
    }

    private void SaveSettings()
    {
        var s = new Settings
        {
            Warmup = (int)_delayNumeric.Value,
            Timeout = (int)_timeoutNumeric.Value,
            KeepCsvAndLog = _deleteCsvCheckBox.Checked,
            LogPath = _logPathTextBox.Text.Trim(),
            PresentMonPath = _presentMonPathTextBox.Text.Trim(),
            SourceTableName = _sourceFileTextBox.Text.Trim(),
            ProcessNames = _processNameListBox.Items.Cast<string>().ToList(),
            WindowWidth = WindowState == FormWindowState.Normal ? Width : RestoreBounds.Width,
            WindowHeight = WindowState == FormWindowState.Normal ? Height : RestoreBounds.Height
        };
        s.Save();
    }

    private static string FindLatestPresentMon()
    {
        try
        {
            var match = Directory.GetFiles(AppContext.BaseDirectory, "PresentMon*.exe")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            return match?.FullName ?? "PresentMon.exe";
        }
        catch
        {
            return "PresentMon.exe";
        }
    }

    private void LoadProcessNames()
    {
        _processNameComboBox.Items.Clear();

        if (File.Exists(ProcessNamesFile))
        {
            var lines = File.ReadAllLines(ProcessNamesFile)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l));
            foreach (var line in lines)
                _processNameComboBox.Items.Add(line);
        }

        if (_processNameComboBox.Items.Count == 0)
        {
            _processNameComboBox.Items.Add("VPinballX64.exe");
            _processNameComboBox.Items.Add("VPinballX.exe");
            _processNameComboBox.Items.Add("Future Pinball.exe");
        }
    }

    private void AddProcessName()
    {
        var name = _processNameComboBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
            return;

        if (!_processNameListBox.Items.Cast<string>().Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _processNameListBox.Items.Add(name);
            UpdateCommandLine();
        }

        _processNameComboBox.Text = string.Empty;
    }

    private void RemoveProcessName()
    {
        if (_processNameListBox.SelectedIndex >= 0)
        {
            _processNameListBox.Items.RemoveAt(_processNameListBox.SelectedIndex);
            UpdateCommandLine();
        }
    }

    private void UpdateCommandLine()
    {
        var parts = new List<string> { "VPinPerfMon.exe" };

        parts.Add("--delaystart");
        parts.Add(((int)_delayNumeric.Value).ToString());

        parts.Add("--timeout");
        parts.Add(((int)_timeoutNumeric.Value).ToString());

        if (_deleteCsvCheckBox.Checked)
        {
            parts.Add("--deletecsv");
            parts.Add("false");
            parts.Add("--logconsole");
            parts.Add("true");
        }

        if (!string.IsNullOrWhiteSpace(_logPathTextBox.Text))
        {
            parts.Add("--logpath");
            parts.Add($"\"{_logPathTextBox.Text.Trim()}\"");
        }

        if (!string.IsNullOrWhiteSpace(_presentMonPathTextBox.Text))
        {
            parts.Add("--presentmonpath");
            parts.Add($"\"{_presentMonPathTextBox.Text.Trim()}\"");
        }

        if (!string.IsNullOrWhiteSpace(_sourceFileTextBox.Text))
        {
            parts.Add("--sourcefile");
            parts.Add($"\"{_sourceFileTextBox.Text.Trim()}\"");
        }

        foreach (string name in _processNameListBox.Items)
        {
            parts.Add("--process_name");
            parts.Add($"\"{name}\"");
        }

        _commandLineTextBox.Text = string.Join(" ", parts);
    }

    private async Task RunCommandAsync()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "VPinPerfMon.exe");
        if (!File.Exists(exePath))
        {
            MessageBox.Show(
                "VPinPerfMon.exe was not found in the application directory.\n\n" +
                "Place VPinPerfMon.GUI alongside VPinPerfMon.exe, or copy the command line and run it manually.",
                "Executable Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _runButton.Enabled = false;
        _cancelButton.Enabled = true;
        _outputTextBox.Clear();

        // Build arguments (everything after the exe name)
        var cmdLine = _commandLineTextBox.Text;
        var argsStart = cmdLine.IndexOf(' ');
        var arguments = argsStart > 0 ? cmdLine[(argsStart + 1)..] : string.Empty;

        _cts = new CancellationTokenSource();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _runningProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _runningProcess.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    BeginInvoke(() => AppendOutput(e.Data));
            };

            _runningProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    BeginInvoke(() => AppendOutput($"[ERR] {e.Data}"));
            };

            _runningProcess.Start();
            _runningProcess.BeginOutputReadLine();
            _runningProcess.BeginErrorReadLine();

            await _runningProcess.WaitForExitAsync(_cts.Token);

            AppendOutput($"\n--- Process exited with code {_runningProcess.ExitCode} ---");
        }
        catch (OperationCanceledException)
        {
            AppendOutput("\n--- Process was cancelled ---");
        }
        catch (Exception ex)
        {
            AppendOutput($"\n--- Error: {ex.Message} ---");
        }
        finally
        {
            CleanupProcess();
            _runButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }

    private async void CancelRunningProcess()
    {
        try
        {
            if (_runningProcess is { HasExited: false })
            {
                // Try graceful shutdown via the named stop event
                try
                {
                    using var stopEvent = EventWaitHandle.OpenExisting(@"Local\VPinPerfMon_Stop");
                    stopEvent.Set();
                }
                catch
                {
                    // Event may not exist yet
                }

                // Wait up to 2 seconds for graceful exit
                for (int i = 0; i < 20 && !_runningProcess.HasExited; i++)
                {
                    await Task.Delay(500);
                }

                // Force kill if still running
                if (!_runningProcess.HasExited)
                {
                    _runningProcess.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Best effort
        }

        _cts?.Cancel();
    }

    private void CleanupProcess()
    {
        _runningProcess?.Dispose();
        _runningProcess = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void AppendOutput(string line)
    {
        _outputTextBox.AppendText(line + Environment.NewLine);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        CancelRunningProcess();
        base.OnFormClosing(e);
    }
}
