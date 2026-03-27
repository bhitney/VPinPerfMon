namespace VPinPerfMon.Setup.Pages;

internal sealed class DatabasePage : WizardPage
{
    private readonly SetupContext _context;
    private readonly CheckBox _createDbCheck;
    private readonly TextBox _dbPathBox;
    private readonly Button _browseBtn;

    public override string Title => "Database Setup (Optional)";
    public override string Description => "Create the SQLite performance tracking table.";

    public DatabasePage(SetupContext context)
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var infoLabel = new Label
        {
            Text = "This step is optional. VPinPerfMon works fine with console-only " +
                   "output and does not require a database." +
                   "\r\n\r\n" +
                   "If enabled, this will create the CustomPerfStats table in a SQLite " +
                   "database for historical tracking. This is typically the PinUP Popper " +
                   "PUPDatabase.db, but any SQLite database will work.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 10)
        };

        _createDbCheck = new CheckBox
        {
            Text = "Create the CustomPerfStats table in a SQLite database",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 4)
        };
        _createDbCheck.CheckedChanged += (_, _) => UpdatePathEnabled();

        var pathPanel = new Panel { Height = 30, Dock = DockStyle.Top };
        _dbPathBox = new TextBox
        {
            Text = _context.DatabasePath,
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

        pathPanel.Controls.Add(_dbPathBox);
        pathPanel.Controls.Add(_browseBtn);

        var noteLabel = new Label
        {
            Text = "If the database file doesn't exist, it will be created automatically.",
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f),
            Padding = new Padding(0, 4, 0, 0)
        };

        layout.Controls.Add(infoLabel, 0, 0);
        layout.Controls.Add(_createDbCheck, 0, 1);
        layout.Controls.Add(pathPanel, 0, 2);
        layout.Controls.Add(noteLabel, 0, 3);

        Controls.Add(layout);
    }

    private void UpdatePathEnabled()
    {
        _dbPathBox.Enabled = _createDbCheck.Checked;
        _browseBtn.Enabled = _createDbCheck.Checked;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Select or create SQLite database file",
            Filter = "SQLite Database|*.db|All files|*.*",
            FileName = Path.GetFileName(_dbPathBox.Text),
            InitialDirectory = Path.GetDirectoryName(_dbPathBox.Text) ?? "",
            OverwritePrompt = false
        };

        if (dlg.ShowDialog() == DialogResult.OK)
            _dbPathBox.Text = dlg.FileName;
    }

    public override bool OnLeave()
    {
        _context.CreateDatabase = _createDbCheck.Checked;

        if (_createDbCheck.Checked)
        {
            if (string.IsNullOrWhiteSpace(_dbPathBox.Text))
            {
                MessageBox.Show("Please specify a database file path.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _context.DatabasePath = _dbPathBox.Text.Trim();
        }

        return true;
    }
}
