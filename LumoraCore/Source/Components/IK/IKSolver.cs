namespace Lumora.Core.Components.IK;

/// <summary>
/// Solves inverse kinematics constraints for bone hierarchies.
/// Attach to the skeleton root slot alongside BipedRig.
/// </summary>
[ComponentCategory("IK")]
public class IKSolver : Component
{
    public Sync<bool> Enabled { get; private set; }
    public Sync<float> TimeStepDuration { get; private set; }
    public Sync<int> ControlIterations { get; private set; }
    public Sync<int> FixerIterations { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Enabled = new Sync<bool>(this, true);
        TimeStepDuration = new Sync<float>(this, 0.02f);
        ControlIterations = new Sync<int>(this, 4);
        FixerIterations = new Sync<int>(this, 4);
    }
}
