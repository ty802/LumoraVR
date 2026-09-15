// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Import;

// Only includes materials Lumora actually has today. As more material providers
// land extend this enum and add the corresponding presets in ModelImportDialog. - xlinka
// Lit is first so it's the default: imported models/avatars are shaded by scene lighting
// (PBS metallic). Unlit is opt-in for flat/self-lit content.
public enum ModelMaterialType
{
    Lit,
    Unlit,
}

// How the two eyes are packed into one frame. A stereo video is one texture: half of it is the left
// eye and half is the right, and which half is which is not written down anywhere in the file, so the
// importer has to be told. Custom is for a layout the player sets up by hand afterwards. -xlinka
public enum StereoLayout
{
    None,
    Horizontal_LR,
    Vertical_LR,
    Horizontal_RL,
    Vertical_RL,
    Custom,
}

// The surface the frame is mapped onto. Flat is a screen; the two spheres are equirectangular, full
// or front half.
public enum VideoProjection
{
    Flat,
    Sphere360,
    Sphere180,
}

// What the video dialog decided, handed to whichever handler the platform registered. Stream is set
// when the source is a URL: nothing is downloaded first, the decoder pulls as it plays.
public sealed record VideoImportOptions(bool Stream, StereoLayout Layout, VideoProjection Projection);
