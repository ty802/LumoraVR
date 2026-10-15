// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Utility;

namespace Lumora.Core.Assets.Interop.Unity;

// The rest of a VRCFury toggle's settings. A hold button is on only while it is held. An exclusive group's
// off state is the choice that is on when nothing else in the group is. A toggle that blocks blinking or
// visemes hands the lids or the mouth to whatever it plays for as long as it is on. A transition's time
// becomes the fade of the shapes it moves.
//
// What a toggle can ask for that has no place here (a separate state seen only by the wearer, the
// transition's own in-between animations, the security lock) is counted rather than guessed at; the switch
// still works without them. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private readonly List<(AvatarToggle Toggle, ValueSwitchDriver<bool> Driver, string Group)> _offStates = new();
        // Icons the toggles named, put on once the menu plan knows their files.
        private readonly List<(AssetRef<TextureAsset> Icon, string Guid)> _furyIcons = new();
        private HashSet<string>? _sliderGlobals;

        // Parameters a VRCFury slider sets: the merged layers' dial for one stays even when it moves nothing
        // itself, since the slider's own actions join it.
        private HashSet<string> KeptSliders => _sliderGlobals ??= _candidate.Toggles
            .Where(t => t.Data is { } d && (d["slider"]?.Int() ?? 0) != 0 && (d["useGlobalParam"]?.Int() ?? 0) != 0)
            .Select(t => t.Data!["globalParam"]?.Text?.Trim() ?? "")
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        private void ToggleExtras(UnityNode data)
        {
            if ((data["separateLocal"]?.Int() ?? 0) != 0)
                Skip("states only the wearer sees");
            if ((data["securityEnabled"]?.Int() ?? 0) != 0)
                Skip("security locks (the switch works without one)");
            if ((data["hasTransition"]?.Int() ?? 0) != 0
                && (data["transitionStateIn"]?["actions"] is UnitySeq { Items.Count: > 0 } || data["transitionStateOut"]?["actions"] is UnitySeq { Items.Count: > 0 }))
                Skip("transition animations");
            if ((data["enableDriveGlobalParam"]?.Int() ?? 0) != 0)
                Skip("parameters a switch drives");
            if (data["resetPhysbones"] is UnitySeq { Items.Count: > 0 })
                Skip("bone resets");
        }

        private static void HoldButton(AvatarToggle toggle) => toggle.Momentary.Value = true;

        // The toggles' own icons, and VRCFury's set-icon for any control at a path.
        private void Icons(UnityVrcPlan? plan, UnityBuildTools.Parts tools)
        {
            if (plan == null)
                return;
            foreach (var (guid, file) in tools.IconFiles)
                plan.IconFiles.TryAdd(guid, file);
            foreach (var (icon, guid) in _furyIcons)
                if (!icon.IsDestroyed)
                    Icon(plan, icon, guid);
            if (tools.Icons.Count == 0 || _menu == null)
                return;
            var toggles = _menu.GetComponentsInChildren<AvatarToggle>().ToList();
            var sliders = _menu.GetComponentsInChildren<AvatarSlider>().ToList();
            foreach (var part in tools.Icons)
            {
                var path = part.Data?["path"]?.Text?.Trim('/') ?? "";
                var guid = UnityVrcFury.Guid(part.Data?["icon"]);
                if (path.Length == 0 || guid == null)
                    continue;
                foreach (var toggle in toggles.Where(t => t.Path.Value == path))
                    Icon(plan, toggle.Icon, guid);
                foreach (var slider in sliders.Where(s => s.Path.Value == path))
                    Icon(plan, slider.Icon, guid);
            }
        }

        // The live graph's input for a parameter, when the FX controller plays live and reads it.
        private ImportedAnimatorParameter? AnimatorInput(string? parameter)
            => !string.IsNullOrEmpty(parameter) && _animatorInputs.TryGetValue(parameter, out var input) ? input : null;

        // A toggle's own parameter fed to the live graph: one while the toggle is on.
        private bool Feed(string? parameter, AvatarToggle toggle)
        {
            if (AnimatorInput(parameter) is not { } input)
                return false;
            input.Switches.Add(toggle.State);
            input.SwitchValues.Add(1f);
            return true;
        }

        // The field an off-state toggle's actions follow: its own switch, or the whole group being off.
        private IField<bool> OffState(AvatarToggle toggle, string group)
        {
            var holder = toggle.Slot.AttachComponent<ValueHolder<bool>>();
            var driver = toggle.Slot.AttachComponent<ValueSwitchDriver<bool>>();
            driver.OffValue.Value = true;
            driver.OnValue.Value = true;
            driver.AddSwitch(toggle.State, true);
            driver.AddDrive(holder.Value);
            _offStates.Add((toggle, driver, group));
            return holder.Value;
        }

        // Once every toggle is in: any other member of the group being on puts the off state away.
        private void FinishOffStates()
        {
            if (_offStates.Count == 0 || _menu == null)
                return;
            var all = _menu.GetComponentsInChildren<AvatarToggle>().ToList();
            foreach (var (toggle, driver, group) in _offStates)
                foreach (var other in all)
                    if (!ReferenceEquals(other, toggle) && other.ExclusiveGroup.Value == group)
                        driver.AddSwitch(other.State, false);
        }

        // The lids or the mouth handed over while the switch is on.
        private void FaceBlock(IField<bool> on, Slot host, bool blinking)
        {
            var hold = host.AttachComponent<FaceHold>();
            hold.Blinking.Value = blinking;
            hold.Mouth.Value = !blinking;
            hold.Enabled.Value = false;
            Switch(hold.Enabled, false, true, on, host);
        }
    }
}
