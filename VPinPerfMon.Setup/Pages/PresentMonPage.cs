namespace VPinPerfMon.Setup.Pages;

internal sealed class PresentMonPage : WizardPage
{
    private readonly SetupContext _context;
    private readonly RadioButton _useBundledRadio;
    private readonly RadioButton _copyLocalRadio;
    private readonly RadioButton _skipRadio;
    private readonly TextBox _pathBox;
    private readonly Button _browseBtn;

    public override string Title => "PresentMon Setup";
    public override string Description => "Configure PresentMon for frame timing analysis.";

    public PresentMonPage(SetupContext context)
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // info
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // radio 1
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // radio 2
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // path panel
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // radio 3
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // link
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // spacer

        var infoLabel = new Label
        {
            Text = "PresentMon 2.x is required for frame timing analysis " +
                   "(FPS, frame time, stutter scoring, GPU/CPU busy/wait metrics).",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 10)
        };

        _useBundledRadio = new RadioButton
        {
            Text = "Use PresentMon bundled with this installer",
            AutoSize = true,
            Checked = true,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 2, 0, 2)
        };
        _useBundledRadio.CheckedChanged += (_, _) => UpdatePathEnabled();

        _copyLocalRadio = new RadioButton
        {
            Text = "Copy a local PresentMon executable into the install folder",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 2, 0, 2)
        };
        _copyLocalRadio.CheckedChanged += (_, _) => UpdatePathEnabled();

        var pathPanel = new Panel { Height = 30, Dock = DockStyle.Top, Padding = new Padding(20, 0, 14, 0) };
        _pathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
            Enabled = false
        };

        _browseBtn = new Button
        {
            Text = "Browse...",
            Width = 80,
            Dock = DockStyle.Right,
            Enabled = false
        };
        _browseBtn.Click += OnBrowse;

        pathPanel.Controls.Add(_pathBox);
        pathPanel.Controls.Add(_browseBtn);

        _skipRadio = new RadioButton
        {
            Text = "Skip \u2014 I don't need PresentMon or will provide the path via command line",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, 4, 0, 4)
        };
        _skipRadio.CheckedChanged += (_, _) => UpdatePathEnabled();

        var downloadLink = new LinkLabel
        {
            Text = "Download PresentMon from GitHub",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(0, 8, 0, 0)
        };
        downloadLink.LinkClicked += (_, _) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/GameTechDev/PresentMon/releases",
                UseShellExecute = true
            });
        };

        layout.Controls.Add(infoLabel, 0, 0);
        layout.Controls.Add(_useBundledRadio, 0, 1);
        layout.Controls.Add(_copyLocalRadio, 0, 2);
        layout.Controls.Add(pathPanel, 0, 3);
        layout.Controls.Add(_skipRadio, 0, 4);
        layout.Controls.Add(downloadLink, 0, 5);

        Controls.Add(layout);
    }

    private void UpdatePathEnabled()
    {
        bool localSelected = _copyLocalRadio.Checked;
        _pathBox.Enabled = localSelected;
        _browseBtn.Enabled = localSelected;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select PresentMon executable",
            Filter = "PresentMon|PresentMon*.exe|All executables|*.exe",
            RestoreDirectory = true
        };

        if (dlg.ShowDialog() == DialogResult.OK)
            _pathBox.Text = dlg.FileName;
    }

    public override bool OnLeave()
    {
        if (_useBundledRadio.Checked)
        {
            _context.PresentMonChoice = PresentMonOption.UseBundled;
        }
        else if (_copyLocalRadio.Checked)
        {
            if (string.IsNullOrWhiteSpace(_pathBox.Text) || !File.Exists(_pathBox.Text))
            {
                MessageBox.Show("Please select a valid PresentMon executable.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _context.PresentMonChoice = PresentMonOption.CopyLocal;
            _context.PresentMonSourcePath = _pathBox.Text.Trim();
        }
        else
        {
            _context.PresentMonChoice = PresentMonOption.Skip;
        }

        return true;
    }
}
