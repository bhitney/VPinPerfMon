using System.Diagnostics;

namespace VPinPerfMon.Setup.Pages;

internal sealed class GettingStartedPage : WizardPage
{
    private readonly SetupContext _context;

    public override string Title => "Getting Started";
    public override string Description => "Example commands to get you up and running.";

    public GettingStartedPage(SetupContext context)
    {
        _context = context;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(4, 8, 4, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // basic label
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // basic textbox
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // full label
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // full textbox
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // note
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // gui link
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // spacer

        var basicLabel = new Label
        {
            Text = "Quick test \u2014 CPU and GPU monitoring only (no PresentMon needed):",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 4)
        };

        var basicBox = new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.White,
            Text = "VPinPerfMon.exe --delaystart 5 --timeout 10",
            Height = 24
        };

        var fullLabel = new Label
        {
            Text = "Full example \u2014 with PresentMon frame analysis, logging, and source file tracking:",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 12, 0, 4)
        };

        var fullBox = new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            Font = new Font("Consolas", 9f),
            BackColor = Color.White,
            ScrollBars = ScrollBars.Vertical,
            Height = 72
        };

        var noteLabel = new Label
        {
            Text = "Tip: Select the text above and press Ctrl+C to copy. " +
                   "Replace process names and paths as needed for your setup. " +
                   "See readme.md in the install folder for the full command reference.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f),
            Padding = new Padding(0, 10, 0, 0)
        };

        var guiButton = new Button
        {
            Text = "\U0001f5b5  Launch VPinPerfMon GUI",
            AutoSize = true,
            MinimumSize = new Size(220, 36),
            Font = new Font("Segoe UI", 10f),
            Margin = new Padding(0, 14, 0, 0),
            Cursor = Cursors.Hand
        };
        guiButton.Click += (_, _) =>
        {
            var guiPath = Path.Combine(AppContext.BaseDirectory, "VPinPerfMon.GUI.exe");
            if (File.Exists(guiPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = guiPath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show(
                    "VPinPerfMon.GUI.exe was not found in the application folder.",
                    "Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        layout.Controls.Add(basicLabel, 0, 0);
        layout.Controls.Add(basicBox, 0, 1);
        layout.Controls.Add(fullLabel, 0, 2);
        layout.Controls.Add(fullBox, 0, 3);
        layout.Controls.Add(noteLabel, 0, 4);
        layout.Controls.Add(guiButton, 0, 5);

        Controls.Add(layout);

        // Store references so OnEnter can update paths
        _fullBox = fullBox;
    }

    private readonly TextBox _fullBox;

    public override void OnEnter()
    {
        var installDir = AppContext.BaseDirectory.TrimEnd('\\');

        // Find PresentMon exe name in app directory (or fall back to default)
        string presentMonName = "PresentMon-2.4.1-x64.exe";
        var found = Directory.GetFiles(installDir, "PresentMon*.exe").FirstOrDefault();
        if (found != null)
            presentMonName = Path.GetFileName(found);

        var presentMonPath = Path.Combine(installDir, presentMonName);

        _fullBox.Text =
            $"VPinPerfMon.exe --delaystart 5 --timeout 10 " +
            $"--presentmonpath \"{presentMonPath}\" " +
            $"--process_name \"VPinballX64.exe\" " +
            $"--deletecsv false --logconsole true " +
            $"--logpath \"{Path.Combine(installDir, "logs")}\" " +
            $"--sourcefile \"PerfTest.vpx\"";
    }
}
