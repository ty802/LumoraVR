// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;

namespace Lumora.Core.Components.Import;

// Settings bag passed from ModelImportDialog to a registered IModelImportHandler.
// Only carries fields the dialog actually drives today; the handler can fill in
// implementation-specific defaults for everything else. Grow this struct as the
// model pipeline gains features (PBS materials, animations, point clouds, etc.)
// - don't add fields here speculatively. - xlinka
public sealed class ModelImportRequest
{
    public float Scale = 1f;
    public bool AutoScale;
    public ModelMaterialType Material = ModelMaterialType.Lit;
    public bool Colliders = true;
    public bool Grabbable = true;
    public bool Scalable = true;
    public bool ImportAtOrigin;
    public bool CalculateNormals = true;
    public bool CalculateTangents = true;
    public bool ImportVertexColors;
    public bool ImportAlbedoColor = true;
    public bool ImportEmissive = true;
    public bool MakeDualSided;
    public bool MakeFlatShaded;
    public int MaxTextureSize = -1;
    public bool ForceNoMipMaps;
}

public interface IImageImportHandler
{
    Task ImportAsync(Slot slot, string path);
}

public interface IModelImportHandler
{
    Task ImportAsync(Slot slot, string path, ModelImportRequest request);
}

public interface IVideoImportHandler
{
    Task ImportAsync(Slot slot, string path);
}

public interface IRawFileImportHandler
{
    Task ImportAsync(Slot slot, string path);
}

// Reads the real OS clipboard and routes its contents (files / images / urls /
// text) through the import pipeline. The core has no clipboard access, so the
// platform layer (LumoraGodot) implements this and registers it; without it,
// a paste is a no-op (no fake placeholder). - xlinka
public interface IClipboardPasteHandler
{
    // Pull whatever's on the OS clipboard and import it into the focused world.
    void Paste();
}

// Registry for pluggable import pipelines. LumoraCore defines the dialog flow;
// platform/asset code (LumoraGodot) registers concrete handlers at engine startup.
// Dialogs call the registered handler in RunImport; if none is registered, the
// dialog spawns a labeled placeholder slot instead. - xlinka
public static class ImportHandlers
{
    public static IImageImportHandler? Image { get; set; }
    public static IModelImportHandler? Model { get; set; }
    public static IVideoImportHandler? Video { get; set; }
    public static IRawFileImportHandler? Raw { get; set; }

    // Platform clipboard bridge. Null until the platform layer registers it. - xlinka
    public static IClipboardPasteHandler? Clipboard { get; set; }
}
