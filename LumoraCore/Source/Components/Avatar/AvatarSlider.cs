// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Avatar;

// A value in an avatar's menu that turns rather than switches: a length, a size, how strong a colour is.
// Path files it the way a switch's does ("Hair/Length": a Hair folder holding Length), Value runs from 0
// to 1, and whatever it changes points a driver at Value (a ValueCurveDriver for fields, a
// BlendShapeCurveDriver for shapes). Default is the value the avatar comes with, which the dial marks.
//
// The menu shows it to the wearer as a dial. The value is saved with the avatar and replicated from the
// wearer like any other field, and every driver reads it on every machine. With Saved off the avatar always
// comes back at Default. -xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarSlider : Component
{
    public readonly Sync<string> Path;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Value;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Default;

    public readonly Sync<bool> Saved;

    public readonly AssetRef<TextureAsset> Icon;

    public AvatarSlider()
    {
        Path = new Sync<string>(this, "");
        Value = new Sync<float>(this, 0f);
        Default = new Sync<float>(this, 0f);
        Saved = new Sync<bool>(this, true);
        Icon = new AssetRef<TextureAsset>(this);
    }

    public override void Load(Persistence.DataTreeNode node, Persistence.LoadControl control)
    {
        base.Load(node, control);
        if (!Saved.Value)
            Value.Value = Default.Value;
    }

    public string Label => AvatarToggle.LabelOf(Path.Value, Slot);

    public string[] Folders => AvatarToggle.FoldersOf(Path.Value);

    public void Set(float value) => Value.Value = float.IsNaN(value) ? 0f : System.Math.Clamp(value, 0f, 1f);

    public void Reset() => Set(Default.Value);
}
