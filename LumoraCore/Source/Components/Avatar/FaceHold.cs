// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

// Takes the lids or the mouth away from the face drivers while something else moves them: an expression that
// shuts the eyes itself, an animation that works the mouth. While blinking is held the blink driver lets go of
// its lid shapes and bones; while the mouth is held the visemes and the jaw let go of theirs. Every holder
// counts on its own and the face comes back once the last one lets go, so two expressions overlapping never
// hand the lids back early. A destroyed holder has let go.
//
// The component holds while it is on, for a switch to drive. Code holds through Set with any object as the
// holder, once on a change or every frame; both work. Any slot inside the avatar names it. -xlinka
[ComponentCategory("Users/Avatar/Face")]
public sealed class FaceHold : Component
{
    public readonly Sync<bool> Blinking;
    public readonly Sync<bool> Mouth;

    private Slot? _heldOn;

    public FaceHold()
    {
        Blinking = new Sync<bool>(this, true);
        Mouth = new Sync<bool>(this, false);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
            Slot.ActiveChanged += OnSlotActiveChanged;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var root = AvatarOf(Slot);
        if (!ReferenceEquals(root, _heldOn))
            Let();
        _heldOn = root;
        if (root != null)
            Hold(root, this, Blinking.Value, Mouth.Value);
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        Let();
    }

    private void OnSlotActiveChanged(Slot slot)
    {
        if (Slot != null && !Slot.IsActive)
            Let();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        Let();
        base.OnDestroy();
    }

    private void Let()
    {
        if (_heldOn != null)
            Hold(_heldOn, this, false, false);
        _heldOn = null;
    }

    // ---- who holds what, per avatar ----

    private sealed class Holds
    {
        public readonly HashSet<object> Blink = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<object> Mouth = new(ReferenceEqualityComparer.Instance);
    }

    private static readonly ConditionalWeakTable<Slot, Holds> Registry = new();

    // The avatar a slot belongs to: the root its face drivers sit on.
    private static Slot? AvatarOf(Slot? within)
        => within == null ? null : within.GetComponentInParents<AvatarForm>()?.Slot ?? within;

    public static void Set(Slot within, object holder, bool blinking, bool mouth)
    {
        var root = AvatarOf(within);
        if (root != null && holder != null)
            Hold(root, holder, blinking, mouth);
    }

    public static void Release(Slot within, object holder) => Set(within, holder, false, false);

    public static bool BlinkingHeld(Slot? within) => Held(within, mouth: false);

    public static bool MouthHeld(Slot? within) => Held(within, mouth: true);

    private static void Hold(Slot root, object holder, bool blinking, bool mouth)
    {
        if (!Registry.TryGetValue(root, out var holds))
        {
            if (!blinking && !mouth)
                return;
            holds = Registry.GetOrCreateValue(root);
        }
        lock (holds)
        {
            if (blinking) holds.Blink.Add(holder); else holds.Blink.Remove(holder);
            if (mouth) holds.Mouth.Add(holder); else holds.Mouth.Remove(holder);
        }
    }

    private static bool Held(Slot? within, bool mouth)
    {
        var root = AvatarOf(within);
        if (root == null || !Registry.TryGetValue(root, out var holds))
            return false;
        lock (holds)
        {
            var set = mouth ? holds.Mouth : holds.Blink;
            if (set.Count == 0)
                return false;
            set.RemoveWhere(static h => h is IWorldElement { IsDestroyed: true });
            return set.Count > 0;
        }
    }
}
