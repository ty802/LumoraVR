// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// Who wears the avatar a face driver sits on, read fresh every frame from the equip state every peer shares.
// Driving follows wearing: an avatar nobody wears has a still face, and a new wearer drops whatever the last
// one fed it. Sitting under someone's hierarchy is not wearing it. The drivers used to take whoever's that
// was, so a hand picking an avatar up lent it that hand's eyes, mouth and voice, and kept lending them after
// letting go. -xlinka
internal sealed class AvatarFaceWearer
{
    private AvatarForm? _form;

    public UserRoot? Current { get; private set; }

    // True when the wearer changed since the last call, including to or from nobody.
    public bool Update(Slot? slot)
    {
        if (_form == null || _form.IsDestroyed)
            _form = slot?.GetComponentInParents<AvatarForm>();
        var wearer = _form is { IsDestroyed: false } form ? form.Wearer : null;
        if (ReferenceEquals(wearer, Current))
            return false;
        Current = wearer;
        return true;
    }
}
