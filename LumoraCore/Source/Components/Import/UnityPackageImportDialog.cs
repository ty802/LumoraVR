// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Linq;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets.Interop.Unity;
using Lumora.Core.Components.UI;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

// A Unity package, read before anything is built: which avatars and models it holds, and what will not
// come across (shaders are converted to our materials, scripts and animator setups are left behind).
// Pick one and it comes in through the ordinary model importer with its Unity materials and switched-off
// parts applied. The unpacked files live until the dialog closes. -xlinka
[ComponentCategory("Assets/Import")]
public sealed class UnityPackageImportDialog : ImportDialog
{
    private UnityPackageReport? _report;
    private UnityImportCandidate? _selected;
    private UnityImportResult? _result;
    private string? _error;
    private bool _reading;
    private bool _importing;

    protected override string TitleText => "Unity Package";
    protected override float2 CanvasSize => new float2(460f, 600f);

    private static readonly color BodyColor = DashTheme.Text;
    private static readonly color GoodColor = DashTheme.Positive;
    private static readonly color LossColor = DashTheme.Warning;
    private static readonly color MutedColor = DashTheme.TextDim;

    protected override void OpenRoot(UIBuilder ui)
    {
        if (_report == null && _error == null)
        {
            BeginRead();
            Message(ui, "Reading Package", "Unpacking the package and finding its avatars and models...", BodyColor);
            return;
        }
        if (_error != null)
        {
            Message(ui, "Cannot Read Package", _error, LossColor);
            return;
        }
        if (_importing)
        {
            Message(ui, "Importing", $"Bringing in {_selected?.Name}...", BodyColor);
            return;
        }
        if (_result != null)
        {
            BuildOutcome(ui);
            return;
        }
        BuildContents(ui);
    }

    private void BeginRead()
    {
        if (_reading)
            return;
        _reading = true;
        var path = Paths.Count > 0 ? Paths[0] : null;
        if (string.IsNullOrEmpty(path))
        {
            _error = "No package file was given.";
            _reading = false;
            return;
        }
        var world = ResolveTargetWorld();
        world?.StartTask(async () =>
        {
            await WorldContext.ToBackground();
            UnityPackageReport? report = null;
            string? error = null;
            try
            {
                report = UnityPackageImporter.Read(path);
                if (report.Candidates.Count == 0)
                    error = "The package holds no model or avatar to bring in.";
            }
            catch (Exception ex)
            {
                error = ex is InvalidDataException or IOException ? ex.Message : "The file is not a readable Unity package: " + ex.Message;
            }

            await WorldContext.ToWorld();
            if (IsDestroyed)
            {
                report?.Dispose();
                return;
            }
            _report = report;
            _error = error;
            _reading = false;
            OpenPage(OpenRoot);
        });
    }

    private void BuildContents(UIBuilder ui)
    {
        var report = _report!;
        var section = SetupSection(ui, "Package Contents", backButton: false);
        var body = ScrollContent(section);
        Line(body, report.Archive.Name, 15f, BodyColor);
        Line(body, $"{report.Models} models, {report.Materials} materials, {report.Textures} textures", 12f, MutedColor);
        if (report.Animations > 0)
            Line(body, $"{report.AnimationClips} animation clips, {report.AnimatorControllers} animator controllers", 12f, MutedColor);
        Spacer(body, 6f);

        // The scenes the package is meant to be used from, each with its avatars by the names it gives them;
        // everything else one press away, so a package's history does not bury what it ships.
        var inline = report.Candidates.Any(c => c.Section == UnityImportCandidate.Listing.Scene)
            ? UnityImportCandidate.Listing.Scene
            : report.Candidates.Select(c => c.Section).DefaultIfEmpty(UnityImportCandidate.Listing.ModelFile).Min();
        Listing(body, report.Candidates.Where(c => c.Section == inline));
        foreach (var listing in new[] { UnityImportCandidate.Listing.Scene, UnityImportCandidate.Listing.OlderScene,
            UnityImportCandidate.Listing.Prefab, UnityImportCandidate.Listing.ModelFile })
        {
            if (listing == inline)
                continue;
            var entries = report.Candidates.Where(c => c.Section == listing).ToList();
            if (entries.Count == 0)
                continue;
            int files = entries.Select(c => c.SourcePath).Distinct().Count();
            var shown = listing;
            ActionButton(body, $"{ListingTitle(listing)} ({Count(listing, entries.Count, files)})",
                () => { if (CanInteract) OpenPage(ui2 => BuildListing(ui2, shown)); });
        }

        Spacer(body, 8f);
        Line(body, "NOT CARRIED OVER", 12f, BodyColor);
        if (report.Shaders > 0)
            Line(body, $"{report.Shaders} shaders (their materials are converted to ours)", 12f, LossColor);
        if (report.Scripts > 0)
            Line(body, $"{report.Scripts} scripts", 12f, LossColor);
        foreach (var note in report.Notes.Take(4))
            Line(body, note, 11f, MutedColor);

        Spacer(body, 8f);
        SetupGrid(section);
        GridButton(section, "As Raw File", AsRawFile, BackColor);
        GridButton(section, "Close", () => Slot.Destroy());
    }

    private void BuildListing(UIBuilder ui, UnityImportCandidate.Listing listing)
    {
        var section = SetupSection(ui, ListingTitle(listing));
        var body = ScrollContent(section);
        Listing(body, _report!.Candidates.Where(c => c.Section == listing));
    }

    // Candidates grouped under the scene or prefab they come from, in the report's order.
    private void Listing(UIBuilder body, System.Collections.Generic.IEnumerable<UnityImportCandidate> candidates)
    {
        string? group = null;
        foreach (var candidate in candidates)
        {
            bool modelFile = candidate.Section == UnityImportCandidate.Listing.ModelFile;
            if (!modelFile && candidate.SourcePath != group)
            {
                group = candidate.SourcePath;
                Spacer(body, 2f);
                Line(body, candidate.SourceName, 12f, MutedColor);
            }
            var picked = candidate;
            ActionButton(body, modelFile ? Path.GetFileName(candidate.SourcePath) : candidate.Name, () => Pick(picked), primary: candidate.IsAvatar);
        }
    }

    private static string ListingTitle(UnityImportCandidate.Listing listing) => listing switch
    {
        UnityImportCandidate.Listing.Scene => "Scenes",
        UnityImportCandidate.Listing.OlderScene => "Older and work-in-progress",
        UnityImportCandidate.Listing.Prefab => "Prefabs",
        _ => "Model files",
    };

    private static string Count(UnityImportCandidate.Listing listing, int entries, int files) => listing switch
    {
        UnityImportCandidate.Listing.ModelFile => $"{entries}",
        UnityImportCandidate.Listing.Prefab => $"{entries}",
        UnityImportCandidate.Listing.OlderScene => $"{entries} in {files} files",
        _ => $"{entries} in {files} scenes",
    };

    // A scene avatar's whole setup is read when it is picked, off the world thread.
    private void Pick(UnityImportCandidate candidate)
    {
        if (!CanInteract || _report == null)
            return;
        _selected = candidate;
        if (candidate.IsPrepared)
        {
            OpenPage(BuildCandidate);
            return;
        }
        var report = _report;
        OpenPage(ui => Message(ui, "Reading Setup", $"Reading how {candidate.SourceName} sets up {candidate.Name}...", BodyColor));
        ResolveTargetWorld()?.StartTask(async () =>
        {
            await WorldContext.ToBackground();
            try
            {
                candidate.Prepare(report.Archive);
            }
            catch (Exception ex)
            {
                candidate.Notes.Add("The setup could not be read: " + ex.Message);
            }
            await WorldContext.ToWorld();
            if (IsDestroyed || !ReferenceEquals(_selected, candidate))
                return;
            OpenPage(BuildCandidate);
        });
    }

    private void BuildCandidate(UIBuilder ui)
    {
        var c = _selected!;
        var section = SetupSection(ui, c.IsAvatar ? "Avatar" : "Model");
        var body = ScrollContent(section);
        Line(body, c.Name, 15f, BodyColor);
        Line(body, $"From {c.SourcePath}", 11f, MutedColor);
        Line(body, $"Model file {Path.GetFileName(c.Model.Path)}", 11f, MutedColor);
        if (c.Parts.Count > 0)
            Line(body, $"{c.Parts.Count} more models placed in it: " + string.Join(", ", c.Parts.Select(p => p.Root.Name).Distinct().Take(8)), 12f, GoodColor);
        foreach (var note in c.Notes.Take(4))
            Line(body, note, 11f, MutedColor);
        int materials = c.MaterialGuids().Distinct().Count();
        if (materials > 0)
            Line(body, $"{materials} materials set in the project", 12f, GoodColor);
        int off = c.ActiveById.Count(a => !a.Value) + c.EnabledById.Count(a => !a.Value) + c.ActiveByName.Count(a => !a.Value);
        if (off > 0)
            Line(body, $"{off} parts switched off", 12f, MutedColor);
        if (c.IsAvatar)
            Line(body, "Has an avatar descriptor.", 12f, GoodColor);
        if (c.Links.Count > 0)
            Line(body, $"{c.Links.Count} VRCFury armature links (linked as ours)", 12f, GoodColor);
        if (c.Toggles.Count > 0)
            Line(body, $"{c.Toggles.Count} VRCFury toggles (into the avatar's menu)", 12f, GoodColor);
        if (c.Gestures.Count > 0)
            Line(body, $"{c.Gestures.Count} VRCFury gesture sets (read from your fingers)", 12f, GoodColor);
        if (c.Constraints.Count > 0)
            Line(body, $"{c.Constraints.Count} constraints", 12f, GoodColor);
        if (c.LeftBehind.Count > 0)
            Line(body, "Not carried: VRCFury " + string.Join(", ", c.LeftBehind.Select(f => f.Value > 1 ? $"{f.Key} x{f.Value}" : f.Key)), 12f, LossColor);

        Spacer(body, 8f);
        SetupGrid(section);
        GridButton(section, "Import as Avatar", () => RunImport(asAvatar: true), primary: c.IsAvatar);
        GridButton(section, "Import as Object", () => RunImport(asAvatar: false), primary: !c.IsAvatar);
    }

    internal void RunImport(bool asAvatar)
    {
        if (!CanInteract || _importing || _report == null || _selected == null || RefuseSpawn())
            return;
        var world = ResolveTargetWorld();
        if (world == null)
            return;
        var report = _report;
        var candidate = _selected;
        _importing = true;
        OpenPage(OpenRoot);

        var target = world.AddSpawnSlot(world.RootSlot, candidate.Name);
        target.GlobalPosition = Slot.GlobalPosition;
        target.GlobalRotation = Slot.GlobalRotation;

        world.StartTask(async () =>
        {
            UnityImportResult result;
            try
            {
                result = await UnityPackageImporter.ImportAsync(report, candidate, target, asAvatar);
            }
            catch (Exception ex)
            {
                result = new UnityImportResult { FailureReason = ex.Message };
            }
            await WorldContext.ToWorld();
            if (!result.Succeeded && !target.IsDestroyed)
                target.Destroy();
            if (IsDestroyed)
                return;
            _result = result;
            _importing = false;
            OpenPage(OpenRoot);
        });
    }

    private void BuildOutcome(UIBuilder ui)
    {
        var r = _result!;
        var section = SetupSection(ui, r.Succeeded ? "Imported" : "Import Failed", backButton: false);
        var body = ScrollContent(section);
        if (!r.Succeeded)
        {
            Line(body, r.FailureReason, 13f, LossColor);
        }
        else
        {
            Line(body, _selected?.Name, 15f, BodyColor);
            Line(body, $"{r.SurfacesMatched} surfaces took their Unity materials", 12f, GoodColor);
            if (r.SurfacesUnmatched > 0)
                Line(body, $"{r.SurfacesUnmatched} kept the model file's own", 12f, LossColor);
            if (r.MaterialsByFamily.Count > 0)
                Line(body, string.Join(", ", r.MaterialsByFamily.Select(f => $"{f.Value} {Family(f.Key)}")), 12f, BodyColor);
            Line(body, $"{r.TexturesImported} textures", 12f, BodyColor);
            if (r.Models > 0)
                Line(body, $"{r.Models} more models placed in it", 12f, GoodColor);
            if (r.Chains > 0 || r.ChainsSkipped > 0)
                Line(body, $"{r.Chains} bone chains, {r.Colliders} colliders" + (r.ChainsSkipped > 0 ? $" ({r.ChainsSkipped} could not be placed)" : ""), 12f, r.ChainsSkipped > 0 ? LossColor : GoodColor);
            if (r.VisemesMapped)
                Line(body, "Lip sync shapes set from the descriptor", 12f, GoodColor);
            if (r.Links > 0 || r.LinksSkipped > 0)
                Line(body, $"{r.Links} armature links" + (r.LinksSkipped > 0 ? $" ({r.LinksSkipped} found no bone)" : ""), 12f, r.LinksSkipped > 0 ? LossColor : GoodColor);
            if (r.Toggles > 0)
                Line(body, $"{r.Toggles} switches in the avatar's menu", 12f, GoodColor);
            if (r.Sliders > 0)
                Line(body, $"{r.Sliders} dials in the avatar's menu", 12f, GoodColor);
            if (r.Gestures > 0)
                Line(body, $"{r.Gestures} hand gestures", 12f, GoodColor);
            if (r.Constraints > 0)
                Line(body, $"{r.Constraints} constraints", 12f, GoodColor);
            if (r.Contacts > 0)
                Line(body, $"{r.Contacts} touch reactions (headpats, boops)", 12f, GoodColor);
            if (r.Animations > 0)
                Line(body, $"{r.Animations} animation clips converted", 12f, GoodColor);
            if (r.AnimationControllers > 0)
                Line(body, $"{r.AnimationControllers} live animator controllers", 12f, GoodColor);
            foreach (var note in r.Notes.Take(6))
                Line(body, note, 11f, MutedColor);
        }
        Spacer(body, 8f);
        SetupGrid(section);
        GridButton(section, "Another", () => { _result = null; OpenPage(OpenRoot); });
        GridButton(section, "Close", () => Slot.Destroy());
    }

    private static string Family(UnityShaderFamily family) => family switch
    {
        UnityShaderFamily.Poiyomi => "Poiyomi as toon",
        UnityShaderFamily.LilToon => "lilToon as toon",
        UnityShaderFamily.UnityChanToon => "UTS as toon",
        UnityShaderFamily.MToon => "MToon as toon",
        UnityShaderFamily.Xiexe => "Xiexe as toon",
        UnityShaderFamily.Toon => "toon",
        UnityShaderFamily.Standard => "Standard as PBS",
        UnityShaderFamily.StandardSpecular => "Standard Specular as PBS",
        UnityShaderFamily.Unlit => "unlit",
        _ => "unknown shader as PBS",
    };

    public override void OnDestroy()
    {
        _report?.Dispose();
        _report = null;
        base.OnDestroy();
    }

    private void Message(UIBuilder ui, string title, string text, color tint)
    {
        var body = SetupSection(ui, title, backButton: false);
        body.PushStyle().FlexibleHeight(1f);
        var msg = body.Text(text, 14f, tint);
        msg.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        msg.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        msg.WordWrap.Value = true;
        body.PopStyle();
        if (_error != null)
        {
            SetupGrid(body);
            GridButton(body, "As Raw File", AsRawFile, BackColor);
            GridButton(body, "Close", () => Slot.Destroy());
        }
    }

    private static void Line(UIBuilder ui, string? text, float size, color tint)
    {
        if (string.IsNullOrEmpty(text))
            return;
        ui.PushStyle().MinHeight(size + 7f).FlexibleHeight(0f);
        var label = ui.Text(text, size, tint);
        var layout = label.Slot.GetComponent<LayoutElement>()!;
        layout.PriorityValue.Value = 0;
        layout.UseZeroMetrics.Value = true;
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        label.WordWrap.Value = true;
        ui.PopStyle();
    }

    private static void Spacer(UIBuilder ui, float height)
    {
        ui.PushStyle().MinHeight(height).PreferredHeight(height).FlexibleHeight(0f);
        ui.Next("Spacer");
        ui.PopStyle();
    }
}
