namespace VPinPerfMon.Setup.Pages;

internal sealed class SummaryPage : WizardPage
{
    private readonly SetupContext _context;
    private readonly TextBox _resultsBox;

    public override string Title => "Setup Complete";
    public override string Description => "Review the installation results.";

    public SummaryPage(SetupContext context)
    {
        _context = context;

        _resultsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.White
        };

        Controls.Add(_resultsBox);
    }

    public override void OnEnter()
    {
        var sb = new System.Text.StringBuilder();

        if (_context.CompletedActions.Count > 0)
        {
            sb.AppendLine("✓ Completed Actions:");
            sb.AppendLine(new string('─', 50));
            foreach (var action in _context.CompletedActions)
            {
                sb.AppendLine($"  • {action}");
            }
            sb.AppendLine();
        }

        if (_context.Errors.Count > 0)
        {
            sb.AppendLine("✗ Errors:");
            sb.AppendLine(new string('─', 50));
            foreach (var error in _context.Errors)
            {
                sb.AppendLine($"  • {error}");
            }
            sb.AppendLine();
        }

        if (_context.CompletedActions.Count == 0 && _context.Errors.Count == 0)
        {
            sb.AppendLine("No actions were performed.");
        }

        sb.AppendLine();
        sb.AppendLine(new string('─', 50));
        sb.AppendLine($"Install location: {_context.InstallPath}");

        if (_context.AddToPerformanceLogUsers)
        {
            sb.AppendLine();
            sb.AppendLine("⚠ Remember to log off and back on for the");
            sb.AppendLine("  Performance Log Users group change to take effect.");
        }

        sb.AppendLine();
        sb.AppendLine("Click Finish to close the wizard.");

        _resultsBox.Text = sb.ToString();
    }
}
