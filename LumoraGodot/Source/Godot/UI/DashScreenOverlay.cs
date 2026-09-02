// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core.Components.UI;
using Lumora.Godot.Hooks;
using LumoraEngine = Lumora.Core.Engine;

namespace Lumora.Source.Godot.UI;

// The dash renders its UI into an offscreen viewport. In VR that capture goes onto a curved
// world-space panel, which is correct - you want to be able to walk around it. On desktop the same
// panel was a quad parked one metre in front of the camera and re-posed every late update to fill
// the frustum, and no matter how fresh the head pose is that read is still a pose from THIS frame's
// engine tick going onto geometry the renderer draws with its own camera transform. Anything that
// moves the view without going through that path - locomotion smoothing, physics interpolation, a
// fall - shows up as the dash swimming against the window.
//
// So on desktop the capture is blitted straight onto a CanvasLayer over the 3D output instead. 2D
// canvas items composite after the 3D frame with no camera in the path at all, so the dash is
// nailed to the window by construction: there is no transform left that could lag. The world panel
// is still built, still active and still posed - the laser hit plane and the u/v mapping are that
// slot's pose and that mesh's size - it just stops drawing (UserspaceDashboard.SetExternalDisplay).
// -xlinka
public partial class DashScreenOverlay : CanvasLayer
{
    // Over the 3D view and the world-loading indicator (99), under the desktop cursor layer (101).
    private const int OverlayLayer = 100;

    private TextureRect _view = null!;
    private Texture2D? _boundTexture;
    private UserspaceDashboard? _appliedDash;
    private bool _appliedExternal;

    public override void _Ready()
    {
        Layer = OverlayLayer;
        Visible = false;

        _view = new TextureRect
        {
            Name = "DashView",
            // Stretch the capture across the whole window. The capture is framed 1:1 on the dash
            // canvas and the canvas aspect is kept at the window aspect (SetAspect), so this is a
            // straight rescale, not a crop or a letterbox.
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_view);
    }

    // Called once per frame from the runner, right after the head/camera pose is pushed.
    public void Tick(LumoraEngine? engine)
    {
        var dash = ResolveDash(engine);
        var texture = ResolveTexture(engine, dash);
        bool show = texture != null;

        // Re-read the texture off the hook every tick rather than caching it at open time. The
        // SubViewport behind it is rebuilt when the capture is reconfigured (a window resize walks
        // the capture width through SetAspect), and a ViewportTexture survives its target being
        // RESIZED but not its viewport being recreated - the old one then points at nothing. Cheap
        // reference compare, reassign only when the object actually changed. -xlinka
        if (!ReferenceEquals(_boundTexture, texture))
        {
            _boundTexture = texture;
            _view.Texture = texture;
        }

        if (Visible != show)
            Visible = show;

        ApplyExternalDisplay(dash, show);
    }

    private static UserspaceDashboard? ResolveDash(LumoraEngine? engine)
    {
        if (engine == null)
            return null;
        var dash = UserspaceDashboard.LocalInstance;
        return dash != null && !dash.IsDestroyed ? dash : null;
    }

    // Null means "do not composite": no engine, VR has the headset (the mesh is the display there),
    // the dash is closed, or the capture has no live texture to show yet.
    private static Texture2D? ResolveTexture(LumoraEngine? engine, UserspaceDashboard? dash)
    {
        if (engine == null || dash == null)
            return null;

        var input = engine.InputInterface;
        if (input == null || input.IsVRActive)
            return null;

        if (!dash.IsOpen.Value)
            return null;

        var capture = dash.RenderTextureSource?.Asset;
        if (capture?.Hook is not IGodotTexture hook || !hook.IsValid)
            return null;

        return hook.GodotTexture2D;
    }

    // The dash side only wants writes on an actual change, and a dash that got torn down and rebuilt
    // (world reload) comes back with its renderer on, so the applied state is tracked against the
    // instance it was applied to and reset when that changes. -xlinka
    private void ApplyExternalDisplay(UserspaceDashboard? dash, bool external)
    {
        if (!ReferenceEquals(dash, _appliedDash))
        {
            if (_appliedDash != null && !_appliedDash.IsDestroyed)
                _appliedDash.SetExternalDisplay(false);
            _appliedDash = dash;
            _appliedExternal = false;
        }

        if (dash == null || _appliedExternal == external)
            return;

        _appliedExternal = external;
        dash.SetExternalDisplay(external);
    }

    public override void _ExitTree()
    {
        // Hand the panel back before we go, or a dash that outlives this node keeps a hidden mesh.
        if (_appliedDash != null && !_appliedDash.IsDestroyed)
            _appliedDash.SetExternalDisplay(false);
        _appliedDash = null;
        _appliedExternal = false;
        base._ExitTree();
    }
}
