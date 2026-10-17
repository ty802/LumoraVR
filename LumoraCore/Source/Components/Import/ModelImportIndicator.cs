// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;
using Lumora.Core;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Import;
using Lumora.Core.Localization;
using Lumora.Core.Math;
using Helio.UI;
using LumoraMeshes = Lumora.Core.Components.Meshes;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// The readout that floats over an import while it happens.
//
// This is the IN-WORLD half, and it starts after the import dialog is done: the dialog asks what you
// want, this says how it is going. It wears the same plate the nameplates do - rounded panel, rim,
// centred label, billboarded at the viewer - because a second floating-text style in the same world
// would just look like a bug.
//
// It stands in the session like the dialog, so the people there see an import under way and how it went.
// Only the importer's machine makes it and drives it, minted as the importer's own: what goes on the wire
// is this slot and a few fields (title, status, the fraction, failed), written at most a few times a
// second. Every machine draws its own plate from them on a local slot, as the nameplates are drawn,
// turned toward its own viewer and easing toward the latest fraction, so nothing per frame is sent.
// Nothing on it answers a pointer.
//
// Threading: the static calls are safe from any thread because none of them touch the data model -
// they write volatile fields on the import's ticket. Only Show marshals, to raise the plate. OnUpdate
// reads the ticket back on the world thread and writes the synced fields only when they changed. -xlinka
[ComponentCategory("Assets/Import")]
public class ModelImportIndicator : Component
{
    // The slot the model imports into. Floating over it means the readout is wherever the model is
    // about to appear, which is in front of the user by construction. -xlinka
    public readonly SyncRef<Slot> Anchor;
    // Who is importing: the readout hangs in front of them while it has nothing to float over, and goes
    // when they leave.
    public readonly SyncRef<User> Importer;
    public readonly Sync<string> Title;
    public readonly Sync<string> Status;
    public readonly Sync<float> Progress;
    public readonly Sync<bool> Failed;

    private const float PanelWidth = 0.52f;
    private const float PanelHeight = 0.20f;
    private const float PanelCornerRadius = 0.022f;
    private const float RimWidth = 0.006f;
    private const float PanelDepth = -0.002f;
    private const float BarWidth = 0.44f;
    private const float BarHeight = 0.022f;
    private const float BarHalfWidth = BarWidth * 0.5f;

    private const float AnchorHeight = 1.9f;
    private const float LingerOnDone = 0.6f;
    private const float LingerOnFail = 4f;
    // How often the importer's machine sends what the ticket says. Finishing and failing go at once.
    private const float SyncInterval = 0.25f;

    private static readonly color PanelColor = new color(0.07f, 0.08f, 0.11f, 0.88f);
    private static readonly color RimColor = new color(0.30f, 0.55f, 0.95f, 0.95f);
    private static readonly color FailRimColor = new color(0.90f, 0.32f, 0.30f, 0.95f);
    private static readonly color BarTrackColor = new color(0.16f, 0.17f, 0.21f, 0.95f);
    private static readonly color BarFillColor = new color(0.32f, 0.68f, 1f, 1f);
    private static readonly color FailFillColor = new color(0.90f, 0.32f, 0.30f, 1f);
    private static readonly color StatusColor = new color(0.78f, 0.84f, 0.94f, 1f);

    // The import this plate reports on, on the importer's machine only; null everywhere else. Every
    // cross-thread write lands on the ticket, never on the component, so the readout is allowed to be one
    // frame behind and there is no lock here on purpose.
    private ImportTicket? _ticket;
    private float _sinceSync = SyncInterval;

    private float _lingerTimer;
    private float _shownProgress;
    private float _appliedProgress = -1f;
    private string _appliedStatus = null!;
    private string _appliedTitle = null!;
    private bool _appliedFailState;
    private bool _placed;

    // This machine's own plate, on a local slot.
    private Slot? _plate;
    private TextRenderer? _titleText;
    private TextRenderer? _statusText;
    private Slot? _progressFill;
    private LumoraMeshes.RoundedQuadMesh? _panelMesh;
    private LumoraMeshes.RoundedQuadRingMesh? _rimMesh;
    private LumoraMeshes.RoundedQuadMesh? _fillMesh;

    // ONE PLATE PER WORLD, ONE TICKET PER IMPORT, AND THE TICKET IS THE TOKEN.
    //
    // A multi-file drop starts one import per file and the last Show takes the plate, so a call has to
    // say which import it speaks for or the first one to finish puts "Done" on somebody else's
    // still-running readout, which is the same lie as hiding early.
    //
    // The state lives on the ticket rather than on the plate, and that is what covers the window between
    // Show returning a token and the plate actually existing on the world thread. A Report landing in
    // there updates its own import instead of painting the plate still up from the last one, and a Hide
    // or a Fail landing in there retires the import before it has a plate, so the plate is born finished
    // or never born at all - rather than appearing afterwards with nothing left alive to hide it. -xlinka
    private sealed class ImportTicket
    {
        public volatile string Status = string.Empty;
        public volatile float Progress;
        public volatile bool Done;
        public volatile bool Failed;
        public volatile bool Dismissed;
    }

    private static readonly ConditionalWeakTable<World, ModelImportIndicator> Current = new();
    private static volatile ImportTicket? _active;

    public ModelImportIndicator()
    {
        Anchor = new SyncRef<Slot>(this);
        Importer = new SyncRef<User>(this);
        Title = new Sync<string>(this, string.Empty);
        Status = new Sync<string>(this, string.Empty);
        Progress = new Sync<float>(this, 0f);
        Failed = new Sync<bool>(this, false);
    }

    public override void OnStart()
    {
        base.OnStart();
        Compose();
        UpdatePosition();
    }

    // Every machine's own plate. Local, so the theme it borrows (a per-world local singleton: one font,
    // one vertex-coloured unlit material) is in reach, and it costs no font provider or second material.
    private void Compose()
    {
        if (Slot == null || World == null || _plate != null)
            return;

        var theme = NameplateTheme.For(World);

        var plate = Slot.AddLocalSlot("Plate");
        plate.Persistent.Value = false;
        plate.AttachComponent<FaceLocalUser>();
        _plate = plate;

        var rim = plate.AddSlot("Rim");
        rim.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        _rimMesh = rim.AttachComponent<LumoraMeshes.RoundedQuadRingMesh>();
        _rimMesh.Size.Value = new float2(PanelWidth, PanelHeight);
        _rimMesh.CornerRadius.Value = PanelCornerRadius;
        _rimMesh.RimWidth.Value = RimWidth;
        _rimMesh.Color = RimColor;
        AttachRenderer(rim, _rimMesh, theme);

        var panel = plate.AddSlot("Panel");
        panel.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        _panelMesh = panel.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        _panelMesh.Size.Value = new float2(PanelWidth, PanelHeight);
        _panelMesh.CornerRadius.Value = PanelCornerRadius;
        _panelMesh.Color = PanelColor;
        AttachRenderer(panel, _panelMesh, theme);

        var title = plate.AddSlot("Title");
        title.LocalPosition.Value = new float3(0f, 0.048f, 0f);
        _titleText = title.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            _titleText.Font.Target = theme.Font;
        _titleText.Size.Value = 0.042f;
        _titleText.Color.Value = color.White;
        _titleText.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _titleText.VerticalAlign.Value = TextVerticalAlignment.Middle;
        _titleText.OutlineThickness.Value = 1.2f;
        _titleText.Text.Value = string.Empty;

        var status = plate.AddSlot("Status");
        status.LocalPosition.Value = new float3(0f, -0.004f, 0f);
        _statusText = status.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            _statusText.Font.Target = theme.Font;
        _statusText.Size.Value = 0.028f;
        _statusText.Color.Value = StatusColor;
        _statusText.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _statusText.VerticalAlign.Value = TextVerticalAlignment.Middle;
        _statusText.Text.Value = string.Empty;

        var track = plate.AddSlot("BarTrack");
        track.LocalPosition.Value = new float3(0f, -0.055f, 0f);
        var trackMesh = track.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        trackMesh.Size.Value = new float2(BarWidth, BarHeight);
        trackMesh.CornerRadius.Value = BarHeight * 0.5f;
        trackMesh.Color = BarTrackColor;
        AttachRenderer(track, trackMesh, theme);

        // Left-anchored fill: a centred quad shifted left by half its missing width and x-scaled by the
        // fraction, so it grows from the left edge without re-meshing anything per update.
        var fill = track.AddSlot("BarFill");
        fill.LocalPosition.Value = new float3(-BarHalfWidth, 0f, 0.001f);
        fill.LocalScale.Value = new float3(0.0001f, 1f, 1f);
        _fillMesh = fill.AttachComponent<LumoraMeshes.RoundedQuadMesh>();
        _fillMesh.Size.Value = new float2(BarWidth, BarHeight * 0.72f);
        _fillMesh.CornerRadius.Value = BarHeight * 0.36f;
        _fillMesh.Color = BarFillColor;
        AttachRenderer(fill, _fillMesh, theme);
        _progressFill = fill;
    }

    private static void AttachRenderer(Slot slot, LumoraMeshes.ProceduralMesh mesh, NameplateTheme? theme)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        if (theme?.PlateMaterial != null)
            renderer.Material.Target = theme.PlateMaterial;
        // A floating readout has no business darkening the floor under it.
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (_ticket != null && !DriveFromTicket(_ticket, delta))
            return;

        UpdatePosition(delta);
        ApplyState(delta);
    }

    // The importer's machine: what the ticket says goes out a few times a second, and the end at once.
    // False once the readout has been taken down.
    private bool DriveFromTicket(ImportTicket ticket, float delta)
    {
        if (ticket.Dismissed)
        {
            Slot?.Destroy();
            return false;
        }

        bool ended = ticket.Done || ticket.Failed;
        _sinceSync += delta;
        if (ended || _sinceSync >= SyncInterval)
        {
            _sinceSync = 0f;
            if (!ticket.Failed)
                Progress.Value = System.Math.Clamp(ticket.Progress, 0f, 1f);
            Status.Value = ticket.Status ?? string.Empty;
            Failed.Value = ticket.Failed;
        }

        if (ended)
        {
            _lingerTimer += delta;
            if (_lingerTimer >= (ticket.Failed ? LingerOnFail : LingerOnDone))
            {
                Slot?.Destroy();
                return false;
            }
        }
        return true;
    }

    private void ApplyState(float delta)
    {
        bool failState = Failed.Value;
        if (failState != _appliedFailState)
        {
            _appliedFailState = failState;
            if (_rimMesh != null && !_rimMesh.IsDestroyed)
                _rimMesh.Color = failState ? FailRimColor : RimColor;
            if (_fillMesh != null && !_fillMesh.IsDestroyed)
                _fillMesh.Color = failState ? FailFillColor : BarFillColor;
        }

        string title = Title.Value ?? string.Empty;
        if (title != _appliedTitle && _titleText != null && !_titleText.IsDestroyed)
        {
            _appliedTitle = title;
            _titleText.Text.Value = title;
        }

        string status = Status.Value ?? string.Empty;
        if (status != _appliedStatus && _statusText != null && !_statusText.IsDestroyed)
        {
            _appliedStatus = status;
            _statusText.Text.Value = status;
        }

        // The fraction arrives a few times a second; the bar eases toward it here, on each machine.
        float target = System.Math.Clamp(Progress.Value, 0f, 1f);
        _shownProgress = delta > 0f && _appliedProgress >= 0f
            ? _shownProgress + (target - _shownProgress) * System.Math.Min(1f, delta * 8f)
            : target;
        if (System.Math.Abs(target - _shownProgress) < 0.002f)
            _shownProgress = target;

        if (System.Math.Abs(_shownProgress - _appliedProgress) > 0.001f)
        {
            _appliedProgress = _shownProgress;
            var fill = _progressFill;
            if (fill != null && !fill.IsDestroyed)
            {
                float scale = System.Math.Max(0.0001f, _shownProgress);
                fill.LocalScale.Value = new float3(scale, 1f, 1f);
                fill.LocalPosition.Value = new float3(BarHalfWidth * (_shownProgress - 1f), 0f, 0.001f);
            }
        }
    }

    // Every machine places its own plate, over the model as it arrives or in front of the importer.
    private void UpdatePosition(float delta = 0f)
    {
        var anchor = Anchor.Target;
        var head = Importer.Target?.Root?.HeadSlot;

        float3 target;
        if (anchor != null && !anchor.IsDestroyed)
        {
            target = anchor.GlobalPosition + float3.Up * AnchorHeight;
        }
        else if (head != null && !head.IsDestroyed)
        {
            target = head.GlobalPosition + (head.GlobalRotation * float3.Forward) * 1.1f + float3.Up * 0.18f;
        }
        else
        {
            // Nothing to hang off yet. Retry next frame rather than snapping to the world origin, which
            // is what "just place it somewhere" looks like from inside the world. -xlinka
            return;
        }

        var plate = _plate;
        if (plate == null || plate.IsDestroyed)
            return;

        // Facing is FaceLocalUser's job on the plate; this only carries position, so the readout does not
        // fight the billboard.
        if (delta > 0f && _placed)
            plate.GlobalPosition = float3.Lerp(plate.GlobalPosition, target, System.Math.Min(1f, delta * 6f));
        else
            plate.GlobalPosition = target;
        _placed = true;
    }

    // Out in the world where the importer may bring things in, minted as theirs; a world that will not take
    // what they bring gets a readout on their machine alone, said so in the log.
    private static Slot MakeSlot(World world, out bool local)
    {
        var refusal = ImportDialog.SpawnRefusal(world);
        local = refusal != null;
        if (local)
            LumoraLogger.Warn($"ModelImportIndicator: {refusal} The import readout is shown only on this machine.");
        var slot = local ? world.RootSlot.AddLocalSlot("Import Indicator") : world.AddSpawnSlot(world.RootSlot, "Import Indicator");
        slot.Persistent.Value = false;
        return slot;
    }

    // STATIC API - safe from any thread

    // The returned token identifies THIS import. Pass it back to Report/Hide/Fail/Dismiss so a call
    // that arrives after another import has taken the plate is dropped rather than applied to it.
    public static object Show(World world, Slot anchor, LocaleText title)
    {
        var ticket = new ImportTicket();
        if (world == null)
            return ticket;

        // Published before the hop, so a Report or a Fail from the importer's threads reaches THIS import
        // from the moment its caller holds the token. It reaches the ticket, not whatever plate happens to
        // be up, which is why publishing early is safe here.
        _active = ticket;
        world.RunSynchronously(() =>
        {
            try
            {
                // Superseded by a later Show, or already cancelled: no plate to raise. The plate that is
                // up stays up, because whoever superseded us clears it when they raise theirs.
                if (!ReferenceEquals(_active, ticket) || ticket.Dismissed)
                    return;

                if (Current.TryGetValue(world, out var previous) && !previous.IsDestroyed)
                    previous.Slot?.Destroy();
                Current.Remove(world);

                var slot = MakeSlot(world, out bool local);
                using (local ? null : world.EnterSpawnAllocation())
                {
                    var indicator = slot.AttachComponent<ModelImportIndicator>();
                    indicator._ticket = ticket;
                    indicator.Importer.Target = world.LocalUser;
                    indicator.Title.Value = title.IsEmpty ? string.Empty : title.Resolve();
                    // A replicated readout can only point at something every machine has.
                    if (anchor != null && !anchor.IsDestroyed && anchor.World == world && (local || !anchor.IsLocalElement))
                    {
                        indicator.Anchor.Target = anchor;
                        slot.GlobalPosition = anchor.GlobalPosition + float3.Up * AnchorHeight;
                    }
                    Current.AddOrUpdate(world, indicator);
                }
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"ModelImportIndicator.Show: {ex.Message}");
            }
        });
        return ticket;
    }

    // A token from another Show still names its own import, so it is answered rather than dropped: its
    // plate is gone but its ticket is not, and writing there cannot touch the live one. No token at all
    // means "whichever import is current", which is what the template callers pass.
    private static ImportTicket? Resolve(object? token)
        => token as ImportTicket ?? (token == null ? _active : null);

    // Retire the ticket so a later call with no token cannot land on a finished import.
    private static void Retire(ImportTicket ticket)
    {
        if (ReferenceEquals(_active, ticket))
            _active = null;
    }

    public static void Report(in ImportProgress progress, object? token = null)
    {
        var ticket = Resolve(token);
        if (ticket == null)
            return;

        if (progress.Fraction >= 0f)
            ticket.Progress = progress.Fraction;

        var text = progress.Describe();
        ticket.Status = text.IsEmpty ? string.Empty : text.Resolve();
    }

    // Finished cleanly: the bar completes and the plate goes away a beat later, so the eye gets to see
    // it land instead of the readout just blinking out.
    public static void Hide(object? token = null)
    {
        var ticket = Resolve(token);
        if (ticket == null)
            return;

        ticket.Progress = 1f;
        var done = new ImportProgress(ImportStage.Complete, 1f).Describe();
        ticket.Status = done.Resolve();
        ticket.Done = true;
        Retire(ticket);
    }

    // Failed: say WHY, in the world, for long enough to read, then go. A silent disappearance is
    // indistinguishable from the import having worked and the model being somewhere else. -xlinka
    public static void Fail(string reason, object? token = null)
    {
        var ticket = Resolve(token);
        if (ticket == null)
            return;

        var label = string.IsNullOrWhiteSpace(reason)
            ? "Import.Failed".AsLocale("Import failed")
            : "Import.FailedReason".AsLocale("Import failed: {0}", reason);
        ticket.Status = label.Resolve();
        ticket.Failed = true;
        Retire(ticket);
    }

    // Cancelled: whatever the readout was reporting on is gone (the target slot was deleted, or the
    // world was left), so there is nothing to call finished and nobody to read a failure. Take the
    // plate away rather than leaving it hanging in front of the user at "Meshes 3 of 9". -xlinka
    public static void Dismiss(object? token = null)
    {
        var ticket = Resolve(token);
        if (ticket == null)
            return;

        // The plate reads this on its own update and destroys itself; a plate that does not exist yet
        // reads it in Show and is never raised.
        ticket.Dismissed = true;
        Retire(ticket);
    }

    // The importer gone, the readout goes with them, the way their dialogs do.
    public override void OnUserLeft(User user) => ImportDialog.TakeDownForLeaver(this, Importer, user);

    public override void OnDestroy()
    {
        if (World != null && Current.TryGetValue(World, out var current) && ReferenceEquals(current, this))
            Current.Remove(World);
        if (_plate != null && !_plate.IsDestroyed)
            _plate.Destroy();
        base.OnDestroy();
    }
}
