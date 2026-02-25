using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Tag component placed on the root slot of an avatar hierarchy.
/// Used to distinguish avatar roots from regular scene objects — grab detection,
/// avatar-specific logic, and tools can search for this component to identify avatars.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public class AvatarRoot : Component
{
    /// <summary>The user who owns this avatar.</summary>
    public SyncRef<UserRoot> Owner { get; private set; }

    /// <summary>Whether this avatar is currently active/visible.</summary>
    public Sync<bool> IsActive { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Owner = new SyncRef<UserRoot>(this, null);
        IsActive = new Sync<bool>(this, true);
    }
}
