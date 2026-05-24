// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Templates;

internal sealed class LocalHomeWorldTemplate : WorldTemplateDefinition
{
    public LocalHomeWorldTemplate() : base("LocalHome") { }

    protected override void Build(World world)
    {
        var spawnSlot = world.RootSlot.AddSlot("SpawnArea");
        spawnSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
        spawnSlot.Tag.Value = "spawn";
        spawnSlot.AttachComponent<SimpleUserSpawn>();

        var lightSlot = world.RootSlot.AddSlot("DirectionalLight");
        lightSlot.LocalPosition.Value = new float3(0f, 10f, 0f);
        lightSlot.LocalRotation.Value = floatQ.Euler(0.785f, -0.785f, 0f);
        var dirLight = lightSlot.AttachComponent<Light>();
        dirLight.Type.Value = LightType.Directional;
        dirLight.LightColor.Value = new color(1f, 0.96f, 0.84f, 1f);
        dirLight.Intensity.Value = 1.2f;
        dirLight.Shadows.Value = ShadowType.Soft;

        var skySlot = world.RootSlot.AddSlot("GradientSkybox");
        var skybox = skySlot.AttachComponent<GradientSkybox>();
        skybox.TopColor.Value = new color(0.070f, 0.060f, 0.150f, 1f);
        skybox.HorizonColor.Value = new color(0.46f, 0.38f, 0.58f, 1f);
        skybox.BottomColor.Value = new color(0.040f, 0.032f, 0.065f, 1f);
        skybox.SunColor.Value = new color(1.0f, 0.72f, 0.54f, 1f);
        skybox.SunDirection.Value = new float3(-0.38f, 0.58f, -0.72f);
        skybox.SunSize.Value = 0.034f;
        skybox.SunIntensity.Value = 2.4f;
        skybox.AmbientEnergy.Value = 0.48f;

        const float groundRadius = 25f;
        var groundSlot = world.RootSlot.AddSlot("Ground");
        groundSlot.LocalPosition.Value = new float3(0f, 0f, 0f);
        groundSlot.Tag.Value = "floor";

        var groundMesh = groundSlot.AttachComponent<CylinderMesh>();
        groundMesh.Radius.Value = groundRadius;
        float groundHeight = 0.04f;
        groundMesh.Height.Value = groundHeight;
        groundMesh.Segments.Value = 64;
        groundMesh.UVScale.Value = new float2(25f, 25f);

        // Sink the cylinder so its top face lands exactly at y=0 (otherwise spawned
        // users sink half-height into the floor). - xlinka
        groundSlot.LocalPosition.Value = new float3(0f, -groundHeight * 0.5f, 0f);

        var groundMaterial = groundSlot.AttachComponent<MetaballMaterial>();
        groundMaterial.BlendMode.Value = BlendMode.Transparent;
        groundMaterial.Culling.Value = Culling.Back;
        groundMaterial.TintA.Value = new colorHDR(0.90f, 1.00f, 0.94f, 1f);
        groundMaterial.TintB.Value = new colorHDR(1.00f, 0.58f, 0.88f, 1f);
        groundMaterial.WaterDeepColor.Value = new colorHDR(0.040f, 0.030f, 0.060f, 1f);
        groundMaterial.WaterSurfaceColor.Value = new colorHDR(0.10f, 0.085f, 0.13f, 1f);
        groundMaterial.RippleColor.Value = new colorHDR(0.72f, 1.00f, 0.92f, 1f);
        groundMaterial.BlobRadius.Value = 0.12f;
        groundMaterial.BlobSmoothness.Value = 0.075f;
        groundMaterial.BlobCount.Value = 72;
        groundMaterial.RiseSpeed.Value = 0.34f;
        // Match risingDiskRadius below so the ripple shader places its centers at
        // the same xz as the rising orbs. Off-by-half-a-meter is enough to make
        // ripples drift visibly from the bubbles. - xlinka
        groundMaterial.VolumeExtents.Value = new float2(24.0f, 24.0f);
        groundMaterial.VolumeHeight.Value = 3.4f;
        groundMaterial.VolumeOffset.Value = new float3(0f, groundHeight * 0.5f, 0f);
        groundMaterial.RimStrength.Value = 1.55f;
        groundMaterial.RimFalloff.Value = 2.8f;
        groundMaterial.FresnelPower.Value = 3.0f;
        groundMaterial.EmissionStrength.Value = 0.82f;
        groundMaterial.WaveStrength.Value = 0.28f;
        groundMaterial.RippleStrength.Value = 0.62f;
        groundMaterial.RippleRadius.Value = 0.58f;
        groundMaterial.RippleWidth.Value = 0.032f;
        groundMaterial.LineStrength.Value = 0.34f;
        groundMaterial.RenderQueue.Value = 10;

        var groundRenderer = groundSlot.AttachComponent<MeshRenderer>();
        groundRenderer.Mesh.Target = groundMesh;
        groundRenderer.Material.Target = groundMaterial;
        groundRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        // Decoupled from groundMaterial.VolumeHeight: that one positions the floor's
        // ripple-emitter blobs (invisible), this one is how high the visible orbs
        // actually float. Bumping VolumeHeight here without touching the ground
        // shader keeps the floor ripple cadence the same. - xlinka
        const float risingVolumeHeight = 7.0f;
        const float risingVolumeLift = 0.035f;
        // Stay inside groundRadius so polar-distributed blob centers never punch
        // past the cylinder rim. - xlinka
        const float risingDiskRadius = 24.0f;

        var risingBallsSlot = groundSlot.AddSlot("RisingBalls");
        risingBallsSlot.LocalPosition.Value = new float3(0f, groundHeight * 0.5f + risingVolumeHeight * 0.5f + risingVolumeLift, 0f);

        var risingBallsMesh = risingBallsSlot.AttachComponent<BoxMesh>();
        risingBallsMesh.Size.Value = new float3(risingDiskRadius * 2f, risingVolumeHeight, risingDiskRadius * 2f);

        var risingBallsMaterial = risingBallsSlot.AttachComponent<LocalHomeRisingMaterial>();
        risingBallsMaterial.TintA.Value = groundMaterial.TintA.Value;
        risingBallsMaterial.TintB.Value = groundMaterial.TintB.Value;
        risingBallsMaterial.BlobRadius.Value = groundMaterial.BlobRadius.Value;
        risingBallsMaterial.BlobSmoothness.Value = groundMaterial.BlobSmoothness.Value;
        risingBallsMaterial.BlobCount.Value = groundMaterial.BlobCount.Value;
        risingBallsMaterial.RiseSpeed.Value = groundMaterial.RiseSpeed.Value;
        risingBallsMaterial.VolumeExtents.Value = new float2(risingDiskRadius, risingDiskRadius);
        risingBallsMaterial.VolumeHeight.Value = risingVolumeHeight;
        risingBallsMaterial.VolumeOffset.Value = new float3(0f, -risingVolumeHeight * 0.5f - risingVolumeLift, 0f);
        risingBallsMaterial.RimStrength.Value = groundMaterial.RimStrength.Value;
        risingBallsMaterial.RimFalloff.Value = groundMaterial.RimFalloff.Value;
        risingBallsMaterial.FresnelPower.Value = groundMaterial.FresnelPower.Value;
        risingBallsMaterial.AlphaScale.Value = 0.92f;
        risingBallsMaterial.EmissionStrength.Value = 1.05f;
        risingBallsMaterial.TimeScale.Value = groundMaterial.TimeScale.Value;
        risingBallsMaterial.RenderQueue.Value = 35;

        var risingBallsRenderer = risingBallsSlot.AttachComponent<MeshRenderer>();
        risingBallsRenderer.Mesh.Target = risingBallsMesh;
        risingBallsRenderer.Material.Target = risingBallsMaterial;
        risingBallsRenderer.ShadowCastMode.Value = ShadowCastMode.Off;
        risingBallsRenderer.SortingOrder.Value = 8;

        var dropletParticlesSlot = groundSlot.AddSlot("DropletParticles");
        dropletParticlesSlot.LocalPosition.Value = new float3(0f, groundHeight * 0.5f + 0.01f, 0f);
        var dropletParticles = dropletParticlesSlot.AttachComponent<ParticleSystem>();
        dropletParticles.MaxParticles.Value = 420;
        dropletParticles.EmissionRate.Value = 20f;
        dropletParticles.BurstCount.Value = 7;
        dropletParticles.BurstInterval.Value = 0.38f;
        dropletParticles.EmitterExtents.Value = new float3(24.2f, 0f, 24.2f);
        dropletParticles.SpawnHeight.Value = 0.035f;
        dropletParticles.Lifetime.Value = 0.74f;
        dropletParticles.LifetimeVariance.Value = 0.20f;
        dropletParticles.StartSize.Value = 0.070f;
        dropletParticles.EndSize.Value = 0.014f;
        dropletParticles.InitialSpeed.Value = 1.35f;
        dropletParticles.SpeedVariance.Value = 0.48f;
        dropletParticles.Spread.Value = 0.30f;
        dropletParticles.Gravity.Value = -1.18f;
        dropletParticles.StartColor.Value = new colorHDR(0.90f, 1.00f, 0.94f, 0.90f);
        dropletParticles.EndColor.Value = new colorHDR(1.00f, 0.58f, 0.88f, 0.0f);
        dropletParticles.EmissionStrength.Value = 1.65f;
        dropletParticles.RenderQueue.Value = 60;
        dropletParticles.Seed.Value = 1771;

        var groundCollider = groundSlot.AttachComponent<CylinderCollider>();
        groundCollider.Type.Value = ColliderType.Static;
        groundCollider.Radius.Value = groundMesh.Radius.Value;
        groundCollider.Height.Value = groundMesh.Height.Value;
        groundCollider.Offset.Value = float3.Zero;

        var ambientLightSlot = world.RootSlot.AddSlot("AmbientLight");
        ambientLightSlot.LocalPosition.Value = new float3(0f, 5f, 0f);
        var ambientLight = ambientLightSlot.AttachComponent<Light>();
        ambientLight.Type.Value = LightType.Point;
        ambientLight.LightColor.Value = new color(0.4f, 0.45f, 0.55f, 1f);
        ambientLight.Intensity.Value = 0.4f;
        ambientLight.Range.Value = 100f;
        ambientLight.Shadows.Value = ShadowType.None;

        var clipboardSlot = world.RootSlot.AddSlot("ClipboardImporter");
        clipboardSlot.AttachComponent<ClipboardImporter>();

        CreateHelioTestPanel(world);

        // Catch-all under the world. Bigger than ground so a user yeeted off-edge
        // still triggers respawn instead of falling forever. - xlinka
        var respawnSlot = world.RootSlot.AddSlot("RespawnPlane");
        var respawnPlane = respawnSlot.AttachComponent<RespawnPlane>();
        respawnPlane.Size.Value = new float2(100f, 100f);
        respawnPlane.Height.Value = -20f;
        respawnPlane.UseBounds.Value = false;
        respawnPlane.ShowVisual.Value = false;
        respawnPlane.ShowDebug.Value = false;
        respawnPlane.UserRespawnPosition.Value = new float3(0f, 1f, 0f);
    }

    private static void CreateHelioTestPanel(World world)
    {
        var panelSlot = world.RootSlot.AddSlot("HelioTestPanel");
        panelSlot.LocalPosition.Value = new float3(0f, 1.52f, -1.68f);
        panelSlot.LocalScale.Value = new float3(0.00135f, 0.00135f, 0.00135f);

        var fontSlot = panelSlot.AddSlot("UIFont");
        var font = fontSlot.AttachComponent<FontProvider>();
        font.URL.Value = new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");
        font.FallbackURLs.Add(new Uri("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf"));

        var checkerSlot = panelSlot.AddSlot("UIValidationChecker");
        var checker = checkerSlot.AttachComponent<CheckerTextureProvider>();
        checker.Width.Value = 64;
        checker.Height.Value = 64;
        checker.CellSize.Value = 8;
        checker.ColorA.Value = new color(0.12f, 0.62f, 0.90f, 1f);
        checker.ColorB.Value = new color(0.95f, 0.30f, 0.56f, 1f);

        var panel = panelSlot.AttachComponent<PanelShell>();
        panel.Title.Value = "Helio Validation";
        panel.Size.Value = new float2(700f, 520f);
        panel.HeaderHeight.Value = 42f;
        panel.Padding.Value = 12f;
        panel.Font.Target = font;
        panel.BackgroundColor.Value = new color(0.025f, 0.030f, 0.040f, 0.92f);
        panel.HeaderColor.Value = new color(0.080f, 0.095f, 0.120f, 0.98f);

        panel.RebuildContent(ui =>
        {
            ui.Font(font);
            var layout = ui.VerticalLayout(8f, 10f);
            Fill(layout.RectTransform!);

            var status = ui.Text("Validation ready: hover/press, scroll clip, raw/tiled images, font fallback.", 16f, new color(0.78f, 0.92f, 1f, 1f));
            status.WordWrap.Value = true;
            Fill(status.RectTransform!);

            int shellClickCount = 0;
            var shellButton = ui.Button("Live shell update", (_, _) =>
            {
                shellClickCount++;
                bool alternate = shellClickCount % 2 == 1;
                panel.Title.Value = alternate ? $"Helio Validation {shellClickCount}" : "Helio Validation";
                panel.Padding.Value = alternate ? 18f : 12f;
                panel.Size.Value = alternate ? new float2(720f, 540f) : new float2(700f, 520f);
                panel.ShowCloseButton.Value = !alternate;
                panel.HeaderColor.Value = alternate
                    ? new color(0.13f, 0.08f, 0.18f, 0.98f)
                    : new color(0.080f, 0.095f, 0.120f, 0.98f);
                status.Content.Value = "PanelShell fields updated live";
            }, new color(0.13f, 0.18f, 0.25f, 0.95f));
            Fill(shellButton.RectTransform!);

            var button = ui.Button("Laser click test", (_, _) =>
            {
                status.Content.Value = $"Clicked {DateTime.Now:HH:mm:ss}";
            }, new color(0.12f, 0.20f, 0.30f, 0.95f));
            Fill(button.RectTransform!);

            ui.HorizontalElementWithLabel("Checkbox", 0.58f, () =>
            {
                var checkbox = ui.Checkbox(false, (_, value) =>
                {
                    status.Content.Value = value ? "Checkbox on" : "Checkbox off";
                }, new color(0.12f, 0.14f, 0.17f, 0.95f));
                Fill(checkbox.RectTransform!);
                return checkbox;
            }, 0.04f);

            ui.HorizontalElementWithLabel("Slider", 0.42f, () =>
            {
                var slider = ui.Slider(0.35f, 0f, 1f, (_, value) =>
                {
                    status.Content.Value = $"Slider {value:0.00}";
                }, new color(0.10f, 0.14f, 0.18f, 0.95f));
                Fill(slider.RectTransform!);
                return slider;
            }, 0.04f);

            var scroll = ui.ScrollRect(out var scrollContent, new float2(1f, 1f), new color(0.035f, 0.045f, 0.060f, 0.94f));
            Fill(scroll.RectTransform!);
            const float scrollContentHeight = 260f;
            ConfigureScrollContent(scrollContent, scrollContentHeight, 38f);
            scroll.Scroll.Value = new float2(0f, 38f);
            scroll.ScrollChanged += (_, value) =>
            {
                float y = Clamp(value.y, 0f, 150f);
                ConfigureScrollContent(scrollContent, scrollContentHeight, y);
                status.Content.Value = $"Scroll clip y={y:0}";
            };

            var scrollUi = new UIBuilder(scrollContent.Slot);
            scrollUi.Font(font);
            var scrollLayout = scrollUi.VerticalLayout(4f, 6f);
            Fill(scrollLayout.RectTransform!);
            for (int i = 1; i <= 9; i++)
            {
                var row = scrollUi.Text($"clipped scroll row {i:00} - content should stay inside the mask", 14f, new color(0.82f, 0.86f, 0.92f, 1f));
                Fill(row.RectTransform!);
            }
            scrollUi.NestOut();

            var imagePanel = ui.Panel(new color(0.035f, 0.040f, 0.050f, 0.96f));
            Fill(imagePanel.RectTransform!);
            ui.Nest();
            var imageLayout = ui.HorizontalLayout(8f, 8f);
            Fill(imageLayout.RectTransform!);
            var raw = ui.RawImage(checker, color.White, new Rect(0.05f, 0.05f, 0.90f, 0.90f), true);
            Fill(raw.RectTransform!);
            var tiled = ui.TiledRawImage(checker, color.White, new float2(24f, 24f), new float2(8f, 6f));
            Fill(tiled.RectTransform!);
            ui.NestOut();
            ui.NestOut();

            var fallback = ui.Text("FontSet fallback path: ASCII + symbols <> [] {} + missing glyph fallback", 14f, new color(0.88f, 0.82f, 0.96f, 1f));
            fallback.WordWrap.Value = true;
            Fill(fallback.RectTransform!);

            ui.NestOut();
        });
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static void ConfigureScrollContent(RectTransform rect, float contentHeight, float scrollY)
    {
        rect.AnchorMin.Value = new float2(0f, 1f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(0f, -contentHeight + scrollY);
        rect.OffsetMax.Value = new float2(0f, scrollY);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
