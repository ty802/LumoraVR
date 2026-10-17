// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Photos;

namespace Lumora.Core.Components;

// A camera made with one hand. An L with the left hand, index up and thumb out to the side, the other fingers
// curled and nothing held, opens a viewfinder in the corner the L makes: what shows in it is what the photo
// takes, from the eye through the frame, wider the closer the hand. While it is open the right hand works it
// and nothing else: its trigger takes the photo, its other button starts a timed one, which leaves the frame
// where it is and counts down so its taker can walk into the picture. Only the user it belongs to sees it;
// the photo lands in the world in the frame's place. On a screen there are no hands: the photo key held
// shows the frame in front of the view and letting go takes what the screen shows, the timed key starts the
// count instead. On the user's root; does nothing on anyone else's machine. -xlinka
[ComponentCategory("Users")]
public class HandViewfinder : Component
{
    private const float ArmRate = 4f;
    private const float DisarmBelow = 0.4f;
    private const int PreviewWidth = 640;
    private const int PreviewHeight = 360;
    private const float Aspect = 16f / 9f;

    // A screen's frame films from the eye at the screen's own width of view; a hand's from the frame itself,
    // as wide as the frame looks from the eye.
    private readonly record struct Frame(float3 Eye, float3 Center, float3 Up, float3 Side, float Width, float Height, float ScreenFov = 0f)
    {
        public float3 Looking => (Center - Eye).Normalized;
        public float FieldOfView => ScreenFov > 0f ? ScreenFov
            : 2f * MathF.Atan(Height * 0.5f / MathF.Max((Center - Eye).Length, 0.01f)) * (180f / MathF.PI);
        public floatQ CameraRotation => PhotoShot.Looking(Looking, Up);
        public floatQ FacingEye => PhotoShot.Facing(-Looking, Up);
        public float3 CameraPosition => ScreenFov > 0f ? Eye : Center + Looking * 0.02f;
    }

    private float _charge;
    private bool _armed;
    private bool _screenTimed;
    private bool _claimed;
    private bool _busy;
    private double _timerEnds = -1;
    private Frame _held;
    private double _flashUntil;

    private Slot? _view;
    private Slot? _picture;
    private QuadMesh? _pictureQuad;
    private QuadMesh? _rimQuad;
    private UnlitMaterial? _rim;
    private TextRenderer? _hint;
    private Camera? _camera;
    private RenderTextureProvider? _feed;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        var root = Slot?.ActiveUserRoot;
        var input = Engine.Current?.InputInterface;
        if (world == null || root == null || input == null || !ReferenceEquals(root.ActiveUser, world.LocalUser))
        {
            Close(input);
            return;
        }
        double now = world.Time.TotalTime;

        if (_timerEnds >= 0)
        {
            Counting(now, root);
            return;
        }
        if (!input.IsVRActive)
        {
            Claim(input, false);
            if (!_busy)
                Screen(input, root, now);
            return;
        }
        if (!EngineSettings.HandFrame || _busy)
        {
            if (!_busy)
                Close(input);
            return;
        }

        bool shaped = ReadL(input, root, out var frame);
        _charge = shaped ? MathF.Min(1f, _charge + delta * ArmRate) : MathF.Max(0f, _charge - delta * ArmRate);
        if (!_armed && _charge >= 1f)
            _armed = true;
        else if (_armed && _charge < DisarmBelow)
            _armed = false;
        Claim(input, _armed);
        if (_charge <= 0f || !shaped && !_armed)
        {
            Hide();
            return;
        }
        if (shaped)
            _held = frame;
        Show(_held, _armed ? Ready(now) : Arming, _armed ? "Trigger: photo    B: timer" : string.Empty, _charge);

        if (!_armed)
            return;
        var right = input.Actions?.Interaction(Chirality.Right);
        if (right == null)
            return;
        if (right.Primary.Pressed)
            Take(_held, "Hand viewfinder");
        else if (right.Secondary.Pressed)
            StartTimer(now);
    }

    // The photo keys: held, the frame shows in front of the view; let go, the photo or the count.
    private void Screen(InputInterface input, UserRoot root, double now)
    {
        var keys = input.Actions?.Camera;
        if (keys == null)
            return;
        bool framing = keys.TakePhoto.Held || keys.TimedPhoto.Held;
        if (framing && ScreenFrame(input, root) is { } frame)
        {
            // The timed key holds the photo key's key too: once it is seen, the frame is a timed one.
            _screenTimed |= keys.TimedPhoto.Held;
            _held = frame;
            Show(frame, ReadyColor, _screenTimed ? "Let go to start the timer" : "Let go to take the photo", 1f);
            return;
        }
        bool timed = _screenTimed;
        _screenTimed = false;
        if ((keys.TakePhoto.Released || keys.TimedPhoto.Released) && _view is { ActiveSelf.Value: true })
        {
            if (timed)
            {
                StartTimer(now);
                return;
            }
            Take(_held, "Screen");
        }
        Hide();
    }

    // Twenty centimetres in front of what the screen shows, a third of its height.
    private static Frame? ScreenFrame(InputInterface input, UserRoot root)
    {
        float fov = input.DesktopCameraFovY > 1f ? input.DesktopCameraFovY : EngineSettings.FieldOfView;
        float3 eye;
        floatQ rotation;
        if (input.DesktopCameraHasOverride && input.DesktopCameraPoseValid)
        {
            eye = input.DesktopCameraPosition;
            rotation = input.DesktopCameraRotation;
        }
        else if (root.HeadSlot is { IsDestroyed: false } head)
        {
            eye = head.GlobalPosition;
            rotation = head.GlobalRotation;
        }
        else
            return null;
        const float away = 0.2f;
        float height = 2f * away * MathF.Tan(fov * 0.5f * MathF.PI / 180f) * 0.35f;
        var looking = rotation * float3.Backward;
        var up = rotation * float3.Up;
        return new Frame(eye, eye + looking * away, up, rotation * float3.Right, height * Aspect, height, fov);
    }

    private static readonly color Arming = new(0.95f, 0.25f, 0.2f, 1f);
    private static readonly color ReadyColor = new(0.2f, 0.85f, 1f, 1f);

    private color Ready(double now) => now < _flashUntil ? color.White : ReadyColor;

    // The L: index straight and up, thumb straight and out across it, the rest curled, the hand empty.
    private bool ReadL(InputInterface input, UserRoot root, out Frame frame)
    {
        frame = default;
        var hand = input.LeftHand;
        if (hand == null || !hand.IsTracking || HoldingLeft(root))
            return false;
        var knuckle = hand.Index.Proximal.Tracked.Position;
        var tip = hand.Index.Tip.Tracked.Position;
        var along = tip - knuckle;
        float reach = along.Length;
        if (reach < 0.02f || reach < Bones(hand.Index) * 0.85f)
            return false;
        var index = along / reach;

        var thumbBase = hand.Thumb.Proximal.Tracked.Position;
        var thumbAlong = hand.Thumb.Tip.Tracked.Position - thumbBase;
        float thumbReach = thumbAlong.Length;
        if (thumbReach < 0.015f || thumbReach < Bones(hand.Thumb) * 0.75f)
            return false;
        var thumb = thumbAlong / thumbReach;
        float across = float3.Dot(thumb, index);
        if (across > 0.6f || across < -0.6f)
            return false;

        if (!Curled(hand.Middle) || !Curled(hand.Ring) || !Curled(hand.Pinky))
            return false;

        var up = root.Slot.GlobalRotation * float3.Up;
        if (float3.Dot(index, up) < 0.75f)
            return false;

        var side = (thumb - index * float3.Dot(thumb, index)).Normalized;
        float height = reach * 1.6f;
        float width = height * Aspect;
        var center = knuckle + side * (width * 0.5f) + index * (height * 0.5f);
        frame = new Frame(root.HeadPosition, center, index, side, width, height);
        return true;
    }

    private static float Bones(HandFinger finger)
    {
        float sum = 0f;
        HandSegment? previous = null;
        foreach (var segment in finger)
        {
            if (segment.SegmentType == FingerSegmentType.Metacarpal)
                continue;
            if (previous != null)
                sum += (segment.Tracked.Position - previous.Tracked.Position).Length;
            previous = segment;
        }
        return sum;
    }

    // Its tip pulled back toward its knuckle.
    private static bool Curled(HandFinger finger)
        => (finger.Tip.Tracked.Position - finger.Proximal.Tracked.Position).Length < Bones(finger) * 0.6f;

    private static bool HoldingLeft(UserRoot root)
    {
        foreach (var tool in root.Slot.GetComponentsInChildren<HandTool>())
        {
            if (tool.Side.Value == Chirality.Left && tool.IsHoldingObjects)
                return true;
        }
        return false;
    }

    private void Claim(InputInterface? input, bool claim)
    {
        if (input == null || claim == _claimed)
            return;
        _claimed = claim;
        if (claim)
            input.ClaimHand(Chirality.Right, this);
        else
            input.ReleaseHand(Chirality.Right, this);
    }

    private void StartTimer(double now)
    {
        _timerEnds = now + EngineSettings.PhotoTimerSeconds;
        _armed = false;
        _charge = 0f;
        Claim(Engine.Current?.InputInterface, false);
    }

    // The frame stays put while it counts down, blinking faster at the end, then takes the photo.
    private void Counting(double now, UserRoot root)
    {
        double left = _timerEnds - now;
        if (left <= 0)
        {
            _timerEnds = -1;
            Take(_held, "Hand viewfinder, timed");
            return;
        }
        bool fast = left < 2.0;
        bool lit = (int)(left * (fast ? 6 : 2)) % 2 == 0;
        Show(_held, lit ? ReadyColor : new color(1f, 0.6f, 0.1f, 1f), $"{System.Math.Ceiling(left):0}", 1f);
    }

    private void Take(Frame frame, string camera)
    {
        var world = World;
        var user = world?.LocalUser;
        if (world == null || _busy)
            return;
        _busy = true;
        _flashUntil = world.Time.TotalTime + 0.15;
        var size = EngineSettings.PhotoSizes[System.Math.Clamp(EngineSettings.PhotoSize, 0, EngineSettings.PhotoSizes.Length - 1)];
        var encoding = EngineSettings.PhotoEncoding;
        var view = _view;
        StartTask(async () =>
        {
            try
            {
                var leaveOut = view != null ? new List<Slot> { view } : null;
                var frames = await PhotoShot.TakeRawAsync(world, new[] { (frame.CameraPosition, frame.CameraRotation) },
                    new PhotoLens(frame.FieldOfView, size.Width, size.Height) { LeaveOut = leaveOut });
                if (frames?[0] is { } picture)
                {
                    var (image, written) = PhotoEncoder.Encode(picture.Rgba, picture.Width, picture.Height, encoding, PhotoShot.Quality(encoding));
                    if (image != null)
                        await PhotoPrint.MakeAsync(world, image, written, frame.Center, frame.FacingEye, frame.Width,
                            new PhotoFacts(user, camera, frame.CameraPosition, frame.CameraRotation, frame.FieldOfView));
                }
                else
                    Logging.Logger.Warn("Photos: the camera gave back no picture");
            }
            finally
            {
                await WorldContext.ToWorld();
                _busy = false;
            }
        });
    }

    // The preview in the frame, a rim behind it in the state's colour, a line of help under it.
    private void Show(Frame frame, color rimColor, string hint, float grow)
    {
        Build();
        if (_view == null || _picture == null || _pictureQuad == null || _rimQuad == null || _rim == null || _camera == null)
            return;
        float scale = 0.6f + 0.4f * grow;
        _view.GlobalPosition = frame.Center;
        _view.GlobalRotation = frame.FacingEye;
        _view.LocalScale.Value = float3.One * scale;
        _pictureQuad.Size.Value = new float2(frame.Width, frame.Height);
        _rimQuad.Size.Value = new float2(frame.Width * 1.04f, frame.Height * 1.06f);
        _rim.TintColor.Value = new colorHDR(rimColor.r, rimColor.g, rimColor.b, 1f);
        if (_hint != null)
        {
            _hint.Text.Value = hint;
            _hint.Slot.LocalPosition.Value = new float3(0f, -frame.Height * 0.62f, 0f);
            _hint.Size.Value = frame.Height * 0.1f;
        }
        var cameraSlot = _camera.Slot;
        cameraSlot.GlobalPosition = frame.CameraPosition;
        cameraSlot.GlobalRotation = frame.CameraRotation;
        _camera.FieldOfView.Value = frame.FieldOfView;
        if (!_view.ActiveSelf.Value)
            _view.ActiveSelf.Value = true;
        if (!cameraSlot.ActiveSelf.Value)
            cameraSlot.ActiveSelf.Value = true;
        Filming(true);
    }

    // The feed draws only while the frame shows; a switched-off slot alone does not stop it.
    private void Filming(bool on)
    {
        if (_feed?.Asset is RenderTexture texture && texture.RenderEnabled != on)
            texture.SetRenderEnabled(on);
    }

    private void Build()
    {
        if (_view is { IsDestroyed: false } || World == null)
            return;
        _view = World.RootSlot.AddLocalSlot("Viewfinder");
        // The frame is this user's own interface, so cameras leave it out unless told to film it.
        _view.AttachComponent<RenderLayerOverride>().Layer.Value = RenderLayerOverride.PrivateLayer;

        var rimSlot = _view.AddLocalSlot("Rim");
        rimSlot.LocalPosition.Value = new float3(0f, 0f, -0.001f);
        _rimQuad = rimSlot.AttachComponent<QuadMesh>();
        var rimRenderer = rimSlot.AttachComponent<MeshRenderer>();
        rimRenderer.Mesh.Target = _rimQuad;
        _rim = rimSlot.AttachComponent<UnlitMaterial>();
        _rim.Culling.Value = Culling.None;
        rimRenderer.Material.Target = _rim;

        var cameraSlot = World.RootSlot.AddLocalSlot("Viewfinder Camera");
        var feed = cameraSlot.AttachComponent<RenderTextureProvider>();
        _feed = feed;
        feed.Width.Value = PreviewWidth;
        feed.Height.Value = PreviewHeight;
        _camera = cameraSlot.AttachComponent<Camera>();
        _camera.NearClip.Value = 0.02f;
        _camera.FarClip.Value = 2000f;
        _camera.TargetTexture.Target = feed;
        _camera.ExcludeRender.Add(_view);

        _picture = _view.AddLocalSlot("Picture");
        _pictureQuad = _picture.AttachComponent<QuadMesh>();
        var renderer = _picture.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _pictureQuad;
        var material = _picture.AttachComponent<UnlitMaterial>();
        material.Texture.Target = feed;
        material.Culling.Value = Culling.None;
        renderer.Material.Target = material;

        var hintSlot = _view.AddLocalSlot("Hint");
        _hint = hintSlot.AttachComponent<TextRenderer>();
        // Text with no font draws nothing.
        var font = _view.AddLocalSlot("Font").AttachComponent<FontProvider>();
        font.URL.Value = Lumora.Core.Components.UI.DashTheme.FontRegular;
        _hint.Font.Target = font;
        _hint.Color.Value = color.White;
        _hint.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _hint.VerticalAlign.Value = TextVerticalAlignment.Middle;
    }

    private void Hide()
    {
        Filming(false);
        if (_view is { IsDestroyed: false } && _view.ActiveSelf.Value)
            _view.ActiveSelf.Value = false;
        if (_camera is { IsDestroyed: false } && _camera.Slot.ActiveSelf.Value)
            _camera.Slot.ActiveSelf.Value = false;
    }

    private void Close(InputInterface? input)
    {
        _charge = 0f;
        _armed = false;
        _timerEnds = -1;
        Claim(input, false);
        Hide();
    }

    public override void OnDestroy()
    {
        Claim(Engine.Current?.InputInterface, false);
        _camera?.Slot.Destroy();
        _view?.Destroy();
        base.OnDestroy();
    }
}
