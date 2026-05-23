// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

﻿using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Gizmos;
using Lumora.Core.Components.Meshes;
using Lumora.Core;

namespace Lumora.Source.Godot.Bootstrap;

/// <summary>
/// Centralized registration for all Godot-specific hooks.
/// </summary>
public static class GodotHookRegistry
{
    public static void RegisterAll()
    {
        // Core slot hook (MUST be registered first!)
        World.HookTypes.Register<Slot, Lumora.Godot.Hooks.SlotHook>();

        // Mesh hooks
        World.HookTypes.Register<ProceduralMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<BoxMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<QuadMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<CylinderMesh, Lumora.Godot.Hooks.MeshHook>();
        World.HookTypes.Register<SphereMesh, Lumora.Godot.Hooks.MeshHook>();

        // Renderer hooks
        World.HookTypes.Register<MeshRenderer, Lumora.Godot.Hooks.MeshRendererHook>();
        World.HookTypes.Register<ModelData, Lumora.Godot.Hooks.ModelDataHook>();

        // Physics collider hooks
        World.HookTypes.Register<BoxCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<CapsuleCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<SphereCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<CylinderCollider, Lumora.Godot.Hooks.PhysicsColliderHook>();
        World.HookTypes.Register<RigidBody, Lumora.Godot.Hooks.RigidBodyHook>();
        World.HookTypes.Register<RespawnPlane, Lumora.Godot.Hooks.RespawnPlaneHook>();

        // Specialized hooks
        World.HookTypes.Register<Lumora.Core.Components.Light, Lumora.Godot.Hooks.LightHook>();
        World.HookTypes.Register<GradientSkybox, Lumora.Godot.Hooks.GradientSkyboxHook>();
        World.HookTypes.Register<Lumora.Core.Components.ParticleSystem, Lumora.Godot.Hooks.ParticleSystemHook>();
        World.HookTypes.Register<Lumora.Core.Components.Avatar.LocalViewOverride, Lumora.Godot.Hooks.LocalViewOverrideHook>();
        World.HookTypes.Register<SkeletonBuilder, Lumora.Godot.Hooks.SkeletonHook>();
        World.HookTypes.Register<SkinnedMeshRenderer, Lumora.Godot.Hooks.SkinnedMeshHook>();
        World.HookTypes.Register<Lumora.Core.Components.HeadOutput, Lumora.Godot.Hooks.HeadOutputHook>();
        World.HookTypes.Register<CharacterController, Lumora.Godot.Hooks.CharacterControllerHook>();
        World.HookTypes.Register<Lumora.Core.Components.Avatar.GodotIKAvatar, Lumora.Godot.Hooks.GodotIKAvatarHook>();

        // engine-side UI lives in HelioUI now. dialog/menu/inspector hooks gone with the GodotUI folder. - xlinka
        World.HookTypes.Register<Nameplate, Lumora.Godot.Hooks.NameplateHook>();

        // Gizmo hooks
        World.HookTypes.Register<SlotGizmo, Lumora.Godot.Hooks.Gizmos.SlotGizmoHook>();

        // Asset hooks
        AssetHookRegistry.Register<TextureAsset, Lumora.Godot.Hooks.TextureAssetHook>();
        AssetHookRegistry.Register<FontAsset, Lumora.Godot.Hooks.FontAssetHook>();
        AssetHookRegistry.Register<MaterialAsset, Lumora.Godot.Hooks.MaterialAssetHook>();
        AssetHookRegistry.Register<MaterialPropertyBlockAsset, Lumora.Godot.Hooks.MaterialPropertyBlockAssetHook>();
    }
}
