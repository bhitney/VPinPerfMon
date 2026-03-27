using System.Security.Principal;

namespace VPinPerfMon.Setup.Pages;

internal sealed class PermissionsPage : WizardPage
{
    private readonly SetupContext _context;
    private readonly CheckBox _addGroupCheck;

    public override string Title => "Permissions";
    public override string Description => "Configure Windows permissions for PresentMon.";

    public PermissionsPage(SetupContext context)
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var userName = WindowsIdentity.GetCurrent().Name;

        var infoLabel = new Label
        {
            Text = "PresentMon requires access to Event Tracing for Windows (ETW) " +
                   "performance counters. Your user account must be a member of the " +
                   "\"Performance Log Users\" local group for this to work." +
                   "\r\n\r\n" +
                   "Without this, PresentMon will return empty frame data.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 0, 0, 12)
        };

        _addGroupCheck = new CheckBox
        {
            Text = $"Add \"{userName}\" to the Performance Log Users group",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Checked = _context.AddToPerformanceLogUsers,
            Font = new Font("Segoe UI", 9.5f)
        };

        var noteLabel = new Label
        {
            Text = "\u26A0 You will need to log off and back on for this change to take effect.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = Color.DarkOrange,
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(20, 6, 0, 0)
        };

        layout.Controls.Add(infoLabel, 0, 0);
        layout.Controls.Add(_addGroupCheck, 0, 1);
        layout.Controls.Add(noteLabel, 0, 2);

        Controls.Add(layout);
    }

    public override bool OnLeave()
    {
        _context.AddToPerformanceLogUsers = _addGroupCheck.Checked;
        return true;
    }
}
