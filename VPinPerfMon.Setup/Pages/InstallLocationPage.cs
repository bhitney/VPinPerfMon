namespace VPinPerfMon.Setup.Pages;

internal sealed class InstallLocationPage : WizardPage
{
    private readonly SetupContext _context;
    private readonly TextBox _pathBox;

    public override string Title => "Install Location";
    public override string Description => "Choose where to install VPinPerfMon.";

    public InstallLocationPage(SetupContext context)
    {
        _context = context;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(4, 8, 4, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = "Select the destination folder for VPinPerfMon files:",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 6)
        };

        var pathPanel = new Panel { Height = 30, Dock = DockStyle.Top };
        _pathBox = new TextBox
        {
            Text = _context.InstallPath,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };

        var browseBtn = new Button
        {
            Text = "Browse...",
            Width = 80,
            Dock = DockStyle.Right
        };
        browseBtn.Click += OnBrowse;

        pathPanel.Controls.Add(_pathBox);
        pathPanel.Controls.Add(browseBtn);

        var noteLabel = new Label
        {
            Text = "The setup will copy VPinPerfMon.exe and all of its dependencies to this folder.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f)
        };

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(pathPanel, 0, 1);
        layout.Controls.Add(noteLabel, 0, 3);

        Controls.Add(layout);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select install folder",
            SelectedPath = _pathBox.Text,
            ShowNewFolderButton = true
        };

        if (dlg.ShowDialog() == DialogResult.OK)
            _pathBox.Text = dlg.SelectedPath;
    }

    public override bool OnLeave()
    {
        if (string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            MessageBox.Show("Please select an install location.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _context.InstallPath = _pathBox.Text.Trim();
        return true;
    }
}
