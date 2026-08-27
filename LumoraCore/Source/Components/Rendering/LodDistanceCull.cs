// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Stops drawing everything under this slot past a distance.
//
// The cheap end of what LodGroup does: one band instead of a list, no alternative
// versions of the model, nothing to author. Most props do not have a low-poly variant to switch to
// and the only honest thing to do with them far away is not draw them, and that is this.
//
// Same mechanism, so the same rule applies: nothing here runs per frame. The band is pushed to the
// renderers once and the renderer does the distance test it was already doing for culling. -xlinka
[ComponentCategory("Rendering")]
public class LodDistanceCull : ImplementableComponent
{
    // metres
    public readonly Sync<float> MaxDistance;

    // 0 cuts hard
    [Range(0f, 20f, "0.00")]
    public readonly Sync<float> FadeMargin;

    public readonly Sync<bool> IgnoreScale;

    // the hook is the only listener
    public event System.Action? BandInvalidated;

    private bool _watching;

    public LodDistanceCull()
    {
        MaxDistance = new Sync<float>(this, 50f);
        FadeMargin = new Sync<float>(this, 0f);
        IgnoreScale = new Sync<bool>(this, false);
    }

    public override void OnStart()
    {
        base.OnStart();
        EngineSettings.Changed += OnSettingChanged;

        // A renderer added under this slot later needs the band too, and structure changes do not come
        // through the ordinary change pass.
        if (Slot != null)
        {
            Slot.SubtreeStructureChanged += OnStructureChanged;
            Slot.WorldTransformChanged += OnTransformChanged;
            _watching = true;
        }
    }

    public override void OnDestroy()
    {
        EngineSettings.Changed -= OnSettingChanged;
        if (_watching && Slot != null)
        {
            Slot.SubtreeStructureChanged -= OnStructureChanged;
            Slot.WorldTransformChanged -= OnTransformChanged;
        }
        _watching = false;
        base.OnDestroy();
    }

    // after scale and the LOD bias setting
    public float ScaledDistance => System.Math.Max(0f, MaxDistance.Value) * ScaleFactor;

    // after scale and the LOD bias setting
    public float ScaledFade => System.Math.Max(0f, FadeMargin.Value) * ScaleFactor;

    private float ScaleFactor
    {
        get
        {
            if (IgnoreScale.Value || Slot == null)
                return EngineSettings.LodBias;

            var scale = Slot.GlobalScale;
            float largest = System.Math.Max(System.Math.Abs(scale.x),
                System.Math.Max(System.Math.Abs(scale.y), System.Math.Abs(scale.z)));
            if (largest < 1e-4f)
                largest = 1f;
            return largest * EngineSettings.LodBias;
        }
    }

    private void OnStructureChanged(Slot _) => BandInvalidated?.Invoke();

    private void OnTransformChanged(Slot _)
    {
        if (!IgnoreScale.Value)
            BandInvalidated?.Invoke();
    }

    private void OnSettingChanged()
    {
        var world = World;
        if (world == null || IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (!IsDestroyed)
                BandInvalidated?.Invoke();
        });
    }
}
