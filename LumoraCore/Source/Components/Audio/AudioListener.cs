// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Audio;

// An ear in this world. Every user's head carries one and it replicates with the head slot, but a
// listener only renders where its user is the one sitting at the machine: ActiveUser is the test, and
// the local user's is the only one the device ever hears. A listener with no user at all is a
// recording ear (a camera, a capture) - it mixes so its buffer can be read back, and it is never
// bound to the device.
//
// The transform is polled by the world's audio manager every frame rather than pushed on change: a
// head moves every frame anyway, so a change subscription would fire every frame and cost more. -xlinka
[ComponentCategory("Audio")]
public class AudioListener : Component
{
    public readonly SyncRef<User> ActiveUser;

    internal AudioListenerVoice? Voice;
    internal float3 LastPosition;
    internal bool HasLastPosition;
    internal bool BoundToDevice;
    internal bool InManager;

    public AudioListener()
    {
        ActiveUser = new SyncRef<User>(this);
    }

    public bool IsLocalUserOutput
    {
        get
        {
            var user = ActiveUser.Target;
            return user != null && ReferenceEquals(user, World?.LocalUser);
        }
    }

    public bool ShouldBeActive
    {
        get
        {
            if (IsDestroyed || !Enabled.Value || Slot == null || !Slot.IsActive)
                return false;
            // No user: an ear that belongs to nobody in particular, active everywhere it exists.
            return ActiveUser.Target == null || IsLocalUserOutput;
        }
    }

    public bool IsRegistered => Voice != null;

    public override void OnStart()
    {
        base.OnStart();
        World?.Audio?.RegisterListener(this);
    }

    public override void OnDestroy()
    {
        World?.Audio?.UnregisterListener(this);
        base.OnDestroy();
    }
}
