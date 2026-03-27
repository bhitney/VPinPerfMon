namespace VPinPerfMon.Setup;

/// <summary>
/// Base class for all wizard pages.
/// </summary>
internal class WizardPage : UserControl
{
    public virtual string Title => "Step";
    public virtual string Description => "";

    /// <summary>
    /// Called when the page becomes active. Override to refresh UI state.
    /// </summary>
    public virtual void OnEnter() { }

    /// <summary>
    /// Called when leaving the page. Return false to prevent navigation.
    /// </summary>
    public virtual bool OnLeave() => true;
}
