namespace VPinPerfMon.Setup.Pages;

internal sealed class WelcomePage : WizardPage
{
    public override string Title => "Welcome";
    public override string Description => "VPinPerfMon Setup Assistant";

    public WelcomePage()
    {
        var label = new Label
        {
            Text = "Welcome to the VPinPerfMon Setup Assistant." +
                   "\r\n\r\n" +
                   "This will guide you through a few quick steps:" +
                   "\r\n\r\n" +
                   "  \u2022  Adding your user to the Performance Log Users group\r\n" +
                   "  \u2022  Optionally creating the SQLite database table\r\n" +
                   "  \u2022  Running a quick test to verify everything works\r\n" +
                   "  \u2022  Getting started with example commands and the GUI" +
                   "\r\n\r\n" +
                   "Click Next to begin.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f),
            Padding = new Padding(8, 12, 8, 8),
            MaximumSize = new Size(560, 0),
            AutoSize = true
        };

        Controls.Add(label);
    }
}
