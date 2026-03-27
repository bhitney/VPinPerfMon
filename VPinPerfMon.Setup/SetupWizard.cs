using System.Diagnostics;
using System.Security.Principal;

namespace VPinPerfMon.Setup;

internal sealed class SetupWizard : Form
{
    private readonly SetupContext _context = new();
    private readonly List<WizardPage> _pages = [];
    private int _currentIndex;

    private readonly Panel _pagePanel;
    private readonly Label _titleLabel;
    private readonly Label _descriptionLabel;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _cancelButton;
    private readonly Label _stepLabel;

    public SetupWizard()
    {
        Text = "VPinPerfMon Setup";
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(640, 610);
        MinimumSize = new Size(640, 610);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        // Header area — use TableLayoutPanel so title + description auto-stack at any DPI
        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 78),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(14, 10, 14, 8)
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _titleLabel = new Label
        {
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        };

        _descriptionLabel = new Label
        {
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 0)
        };

        headerPanel.Controls.Add(_titleLabel, 0, 0);
        headerPanel.Controls.Add(_descriptionLabel, 0, 1);

        // Separator below header
        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.LightGray
        };

        // Page host
        _pagePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 8)
        };

        // Bottom bar: FlowLayoutPanel for reliable DPI-aware button layout
        var bottomOuter = new Panel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 58),
            Padding = new Padding(0)
        };

        var bottomSep = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.LightGray
        };

        _stepLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f),
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };

        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 10, 10, 10)
        };

        _backButton = new Button { Text = "\u2190 Back", AutoSize = true, MinimumSize = new Size(80, 30), Margin = new Padding(3) };
        _nextButton = new Button { Text = "Next \u2192", AutoSize = true, MinimumSize = new Size(80, 30), Margin = new Padding(3) };
        _cancelButton = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 30), Margin = new Padding(3) };

        buttonFlow.Controls.Add(_backButton);
        buttonFlow.Controls.Add(_nextButton);
        buttonFlow.Controls.Add(_cancelButton);

        bottomOuter.Controls.Add(buttonFlow);
        bottomOuter.Controls.Add(_stepLabel);
        bottomOuter.Controls.Add(bottomSep);

        _backButton.Click += (_, _) => Navigate(-1);
        _nextButton.Click += (_, _) => OnNextClicked();
        _cancelButton.Click += (_, _) => Close();

        // Build layout (add order matters for Dock: Fill must be added first)
        Controls.Add(_pagePanel);
        Controls.Add(separator);
        Controls.Add(headerPanel);
        Controls.Add(bottomOuter);

        // Create pages
        _pages.Add(new Pages.WelcomePage());
        _pages.Add(new Pages.PermissionsPage(_context));
        _pages.Add(new Pages.DatabasePage(_context));
        _pages.Add(new Pages.TestPage(_context));
        _pages.Add(new Pages.GettingStartedPage(_context));

        ShowPage(0);
    }

    private int InstallPageIndex => _pages.Count - 3; // DatabasePage (before Test, GettingStarted)

    private void ShowPage(int index)
    {
        _currentIndex = index;
        var page = _pages[index];

        _pagePanel.Controls.Clear();
        page.Dock = DockStyle.Fill;
        _pagePanel.Controls.Add(page);

        _titleLabel.Text = page.Title;
        _descriptionLabel.Text = page.Description;
        _stepLabel.Text = $"Step {index + 1} of {_pages.Count}";

        _backButton.Enabled = index > 0;

        bool isLast = index == _pages.Count - 1;
        _nextButton.Text = index == InstallPageIndex ? "Install" : isLast ? "Finish" : "Next \u2192";

        page.OnEnter();
    }

    private void Navigate(int direction)
    {
        int next = _currentIndex + direction;
        if (next < 0 || next >= _pages.Count) return;

        if (direction > 0 && !_pages[_currentIndex].OnLeave())
            return;

        ShowPage(next);
    }

    private async void OnNextClicked()
    {
        // Last page → close
        if (_currentIndex == _pages.Count - 1)
        {
            Close();
            return;
        }

        // Install page → run installation, then advance
        if (_currentIndex == InstallPageIndex)
        {
            if (!_pages[_currentIndex].OnLeave())
                return;

            _nextButton.Enabled = false;
            _backButton.Enabled = false;
            _cancelButton.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                await Task.Run(RunInstallation);
            }
            finally
            {
                Cursor = Cursors.Default;
                _cancelButton.Enabled = true;
                _nextButton.Enabled = true;
            }

            ShowPage(_currentIndex + 1);
            return;
        }

        Navigate(1);
    }

    private void RunInstallation()
    {
        // 1. Add user to Performance Log Users
        if (_context.AddToPerformanceLogUsers)
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var userName = identity.Name;

                var psi = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = $"localgroup \"Performance Log Users\" \"{userName}\" /add",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc!.WaitForExit(10_000);
                var stderr = proc.StandardError.ReadToEnd();

                if (proc.ExitCode == 0)
                    _context.CompletedActions.Add($"Added {userName} to Performance Log Users group");
                else if (stderr.Contains("1378")) // Already a member
                    _context.CompletedActions.Add($"{userName} is already in Performance Log Users group");
                else
                    _context.Errors.Add($"Failed to add to group (exit {proc.ExitCode}): {stderr.Trim()}");
            }
            catch (Exception ex)
            {
                _context.Errors.Add($"Failed to modify group membership: {ex.Message}");
            }
        }

        // 2. Create database table
        if (_context.CreateDatabase)
        {
            try
            {
                var sqlFile = Path.Combine(_context.InstallPath, "CreateSqlTable.sql");
                if (!File.Exists(sqlFile))
                    sqlFile = Path.Combine(AppContext.BaseDirectory, "CreateSqlTable.sql");

                if (File.Exists(sqlFile))
                {
                    var sql = File.ReadAllText(sqlFile);
                    var dbDir = Path.GetDirectoryName(_context.DatabasePath);
                    if (!string.IsNullOrEmpty(dbDir))
                        Directory.CreateDirectory(dbDir);

                    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_context.DatabasePath}");
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                    _context.CompletedActions.Add($"Created CustomPerfStats table in {_context.DatabasePath}");
                }
                else
                {
                    _context.Errors.Add("CreateSqlTable.sql not found — could not create database table");
                }
            }
            catch (Exception ex)
            {
                _context.Errors.Add($"Failed to create database table: {ex.Message}");
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}
