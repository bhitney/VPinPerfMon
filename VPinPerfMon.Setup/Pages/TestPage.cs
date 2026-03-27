using System.Diagnostics;

namespace VPinPerfMon.Setup.Pages;

internal sealed class TestPage : WizardPage
{
    private readonly SetupContext _context;
    private readonly TextBox _outputBox;
    private readonly Button _basicTestBtn;
    private readonly Button _frameTestBtn;
    private readonly Label _statusLabel;

    public override string Title => "Test Installation";
    public override string Description => "Verify VPinPerfMon is working correctly.";

    public TestPage(SetupContext context)
    {
        _context = context;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(4, 8, 4, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // info
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // output
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // note

        var infoLabel = new Label
        {
            Text = "Run a quick test to verify the installation. " +
                   "The basic test checks CPU and GPU stats. " +
                   "The frame test uses PresentMon to capture frame data " +
                   "using this setup process as the target.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 8)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 4)
        };

        _basicTestBtn = new Button
        {
            Text = "Run Basic Test (CPU/GPU)",
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2)
        };
        _basicTestBtn.Click += async (_, _) => await RunTestAsync(basicOnly: true);

        _frameTestBtn = new Button
        {
            Text = "Run Frame Test (PresentMon)",
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2)
        };
        _frameTestBtn.Click += async (_, _) => await RunTestAsync(basicOnly: false);

        buttonPanel.Controls.Add(_basicTestBtn);
        buttonPanel.Controls.Add(_frameTestBtn);

        _statusLabel = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(0, 2, 0, 4)
        };

        _outputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.White
        };

        var noteLabel = new Label
        {
            Text = "These tests are optional. You can skip this step and click Next.",
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f),
            Padding = new Padding(0, 4, 0, 0)
        };

        layout.Controls.Add(infoLabel, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        layout.Controls.Add(_statusLabel, 0, 2);
        layout.Controls.Add(_outputBox, 0, 3);
        layout.Controls.Add(noteLabel, 0, 4);

        Controls.Add(layout);
    }

    private async Task RunTestAsync(bool basicOnly)
    {
        var exePath = Path.Combine(_context.InstallPath, "VPinPerfMon.exe");
        if (!File.Exists(exePath))
        {
            _statusLabel.Text = "\u274C VPinPerfMon.exe not found in install directory. Run the install step first.";
            _statusLabel.ForeColor = Color.Red;
            return;
        }

        _basicTestBtn.Enabled = false;
        _frameTestBtn.Enabled = false;
        _outputBox.Clear();
        _statusLabel.ForeColor = Color.DarkBlue;

        string args;
        if (basicOnly)
        {
            _statusLabel.Text = "Running basic test (CPU/GPU for 5 seconds, 1s warm-up)...";
            args = "--delaystart 1 --timeout 5";
        }
        else
        {
            // Find PresentMon in install dir
            var presentMon = Directory.GetFiles(_context.InstallPath, "PresentMon*.exe").FirstOrDefault();
            if (presentMon == null)
            {
                _statusLabel.Text = "\u274C PresentMon not found in install directory. Run basic test or install PresentMon first.";
                _statusLabel.ForeColor = Color.Red;
                _basicTestBtn.Enabled = true;
                _frameTestBtn.Enabled = true;
                return;
            }

            // Use this setup process as the target so PresentMon has something to capture
            var selfName = Path.GetFileName(Environment.ProcessPath) ?? "VPinPerfMon.Setup.exe";
            _statusLabel.Text = $"Running frame test (PresentMon targeting \"{selfName}\" for 10s, 1s warm-up)...";
            args = $"--delaystart 1 --timeout 10 --presentmonpath \"{presentMon}\" --process_name \"dwm.exe\" --process_name \"{selfName}\"";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = _context.InstallPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    BeginInvoke(() => AppendOutput(e.Data));
            };

            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    BeginInvoke(() => AppendOutput($"[ERR] {e.Data}"));
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                _statusLabel.Text = "\u2705 Test completed successfully.";
                _statusLabel.ForeColor = Color.DarkGreen;
            }
            else
            {
                _statusLabel.Text = $"\u26A0 Test exited with code {proc.ExitCode}. Check output below.";
                _statusLabel.ForeColor = Color.DarkOrange;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"\u274C Error running test: {ex.Message}";
            _statusLabel.ForeColor = Color.Red;
        }
        finally
        {
            _basicTestBtn.Enabled = true;
            _frameTestBtn.Enabled = true;
        }
    }

    private void AppendOutput(string line)
    {
        _outputBox.AppendText(line + Environment.NewLine);
    }
}
