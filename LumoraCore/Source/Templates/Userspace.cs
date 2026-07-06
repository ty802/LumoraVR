// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.UI;
using Lumora.Core.Input;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Templates
{
    /// <summary>
    /// Manages userspace world creation and setup.
    /// </summary>
    public static class Userspace
    {
        /// <summary>
        /// Setup the userspace world for the engine.
        /// Userspace is a private overlay world for UI and settings.
        /// </summary>
        public static World SetupUserspace(Engine engine)
        {
            Logger.Log("Userspace: Setting up userspace world");

            // Create local userspace world
            var world = World.LocalWorld(engine, "Userspace", (w) =>
            {
                Logger.Log("Userspace: Initializing userspace world");

                // Create root structure
                var userspaceRoot = w.RootSlot.AddSlot("UserspaceRoot");

                // Userspace dashboard root. UserspaceDashboard owns the open
                // state and positions the Helio dash surface in front of the
                // focused user's view when shown.
                var dashboardSlot = userspaceRoot.AddSlot("UserspaceDashboard");
                var dashboard = dashboardSlot.AttachComponent<UserspaceDashboard>();
                dashboard.Close();

                // The radial context menu lives per-user in the game world
                // (built by AvatarAssembler, opened by HandTool). Items
                // come from ContextMenuItemSource components at open time.
                Logger.Log("Userspace: Userspace world initialized");
            });

            // The local user only exists AFTER LocalWorld returns - the init callback above runs
            // before the host user is created - so the pointer rig, which binds a UserRoot to that
            // user, is built here rather than in the callback. -xlinka
            BuildPointerRig(world);

            // Register with WorldManager as userspace world
            // (UserspaceWorld setter automatically calls PrivateOverlayWorld which adds to managed worlds)
            engine.WorldManager.UserspaceWorld = world;

            Logger.Log("Userspace: Userspace setup complete");
            return world;
        }

        // Userspace-owned pointer rig: a stripped proxy avatar that lives in the overlay world and
        // owns the dash pointer outright. It has a UserRoot, a head node, and two controller-tracked
        // hands, each carrying a pointer - and nothing else (no avatar mesh, no camera, no
        // locomotion). Because the rig belongs to userspace and never to a game world, the dash
        // keeps a working cursor even right after you delete the world you were standing in, which
        // used to take the borrowed avatar laser down with it. On desktop the right hand aims off
        // the platform free-cursor ray; in VR both hands aim off their tracked controllers. -xlinka
        private static void BuildPointerRig(World world)
        {
            var userspaceRoot = world.RootSlot.FindChild("UserspaceRoot");
            if (userspaceRoot == null)
            {
                Logger.Error("Userspace: UserspaceRoot missing, cannot build pointer rig");
                return;
            }

            if (world.LocalUser == null)
            {
                Logger.Error("Userspace: LocalUser missing, cannot build pointer rig");
                return;
            }

            var rigSlot = userspaceRoot.AddSlot("PointerUser");
            var userRoot = rigSlot.AttachComponent<UserRoot>();
            userRoot.Initialize(world.LocalUser);
            // Bind explicitly as well: a local world reports no network authority, so the
            // authority-gated bind inside Initialize may not fire. This makes it deterministic so
            // the controller positioners resolve their UserRoot and actually track. -xlinka
            world.LocalUser.Root = userRoot;

            var bodyNodes = rigSlot.AddSlot("Body Nodes");

            // Head node tracks the HMD so the cursor billboards toward the viewer in VR and the rig
            // has a head to sit relative to. No HeadOutput here, so it never spawns a camera. -xlinka
            var head = bodyNodes.AddSlot("Head");
            head.LocalPosition.Value = new float3(0f, 1.6f, 0f);
            var headPositioner = head.AttachComponent<TrackedDevicePositioner>();
            headPositioner.AutoBodyNode.Value = BodyNode.Head;

            BuildPointerHand(bodyNodes, Chirality.Left);
            BuildPointerHand(bodyNodes, Chirality.Right);

            // Keep the rig overlaying the focused user so the VR controller rays line up with the
            // dash. When nothing is focused (you just deleted the world you were in) it holds its
            // last pose - exactly what keeps the dash pointable through the gap. -xlinka
            rigSlot.AttachComponent<UserspaceViewMirror>();
        }

        private static void BuildPointerHand(Slot bodyNodes, Chirality side)
        {
            bool isLeft = side == Chirality.Left;

            var controller = bodyNodes.AddSlot(isLeft ? "LeftController" : "RightController");
            var positioner = controller.AttachComponent<TrackedDevicePositioner>();
            positioner.AutoBodyNode.Value = isLeft ? BodyNode.LeftController : BodyNode.RightController;

            var pointer = controller.AttachComponent<UserspacePointer>();
            pointer.Side.Value = side;
        }
    }
}
