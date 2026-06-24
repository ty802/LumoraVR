// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;
using Lumora.Source.Godot.UI;
using LumoraMeshes = Lumora.Core.Components.Meshes;

namespace Lumora.Godot.Input;

/// <summary>
/// Handles clipboard paste operations for importing assets.
/// Detects file paths, URLs, and file data from clipboard.
/// </summary>
public partial class ClipboardImporter : Node
{
    private static readonly HashSet<string> ClipboardModelExtensions = new(ModelImporter.SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    // Windows clipboard P/Invoke for file drops
    private const uint CF_HDROP = 15;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

    private LocalDB _localDB = null!;
    private Slot _targetSlot = null!;
    private Camera3D _camera = null!;
    private Lumora.Core.Engine _engine = null!;

    /// <summary>
    /// Event fired when an asset is imported from clipboard.
    /// </summary>
    public event Action<string, Slot> OnAssetImported = null!;

    public override void _Ready()
    {
        GD.Print("ClipboardImporter: Ready");

        // Connect to files dropped signal for drag & drop
        GetTree().Root.FilesDropped += OnFilesDropped;
    }

    public override void _ExitTree()
    {
        GetTree().Root.FilesDropped -= OnFilesDropped;
        base._ExitTree();
    }

    /// <summary>
    /// Handle files dropped onto the window (drag & drop).
    /// Routes through UniversalImporter so the matching import dialog (Image/Model/
    /// Video/Folder) appears at the user's view, rather than auto-importing silently.
    /// - xlinka
    /// </summary>
    private async void OnFilesDropped(string[] files)
    {
        GD.Print($"ClipboardImporter: {files.Length} file(s) dropped");

        var world = _engine?.WorldManager?.FocusedWorld;
        if (world == null)
        {
            GD.PrintErr("ClipboardImporter: No focused world for drop");
            return;
        }

        var (pos, rot) = GetSpawnPose();
        foreach (var filePath in files)
        {
            var normalized = NormalizeFilePath(filePath);
            GD.Print($"ClipboardImporter: Processing dropped file: {normalized}");
            UniversalImporter.Import(normalized, world, pos, rot);
        }
        await Task.CompletedTask;
    }

    private (float3 position, floatQ rotation) GetSpawnPose()
    {
        if (_camera != null)
        {
            var camPos = _camera.GlobalPosition;
            var camFwd = -_camera.GlobalTransform.Basis.Z; // Godot camera looks down -Z
            // FLATTEN to horizontal so an up/down gaze can't tilt the model or shove it above/below eye-line (that's
            // the "spawns above me / lying back" bug), spawn ~1.5 m ahead + a touch low, and face the user with a
            // PURE YAW (+Z back toward the camera). floatQ.LookRotation is broken for facing (inverse-row builder ->
            // edge-on/sideways pose), so use AxisAngle like the working FaceLocalUser / ImportDialog.Executor. -xlinka
            var flat = new float3(camFwd.X, 0f, camFwd.Z);
            flat = flat.LengthSquared < 1e-6f ? new float3(0f, 0f, 1f) : flat.Normalized;
            var spawn = new float3(camPos.X + flat.x * 1.5f, camPos.Y - 0.5f, camPos.Z + flat.z * 1.5f);
            var rot = floatQ.AxisAngle(float3.Up, System.MathF.Atan2(-flat.x, -flat.z));
            return (spawn, rot);
        }
        return (float3.Zero, floatQ.Identity);
    }

    /// <summary>
    /// Initialize the clipboard importer.
    /// </summary>
    public void Initialize(LocalDB localDB, Slot targetSlot, Camera3D camera)
    {
        _localDB = localDB;
        _targetSlot = targetSlot;
        _camera = camera;
    }

    /// <summary>
    /// Set the engine reference for dynamic slot lookup.
    /// </summary>
    public void SetEngine(Lumora.Core.Engine engine)
    {
        _engine = engine;
        RegisterImportHandlers();
    }

    private void RegisterImportHandlers()
    {
        ImportHandlers.Image = new ImageHandler(this);
        ImportHandlers.Model = new ModelHandler(this);
        ImportHandlers.Raw = new RawFileHandler(this);
        GD.Print("ClipboardImporter: Registered Image/Model/Raw import handlers");
    }

    private sealed class ImageHandler : IImageImportHandler
    {
        private readonly ClipboardImporter _owner;
        public ImageHandler(ClipboardImporter owner) { _owner = owner; }
        public Task ImportAsync(Slot slot, string path) => _owner.PopulateImageSlotAsync(slot, path);
    }

    private sealed class ModelHandler : IModelImportHandler
    {
        private readonly ClipboardImporter _owner;
        public ModelHandler(ClipboardImporter owner) { _owner = owner; }
        public Task ImportAsync(Slot slot, string path, ModelImportRequest request)
        {
            // ModelImportRequest fields aren't propagated into ModelImporter yet;
            // path goes straight through. - xlinka
            var ext = Path.GetExtension(path).ToLowerInvariant();
            bool isAvatar = ext == ".vrm";
            return _owner.PopulateModelSlotAsync(slot, path, isAvatar);
        }
    }

    private sealed class RawFileHandler : IRawFileImportHandler
    {
        private readonly ClipboardImporter _owner;
        public RawFileHandler(ClipboardImporter owner) { _owner = owner; }
        public Task ImportAsync(Slot slot, string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tga"
                    => _owner.PopulateImageSlotAsync(slot, path),
                ".gdshader"
                    => _owner.PopulateShaderSlotAsync(slot, path),
                _ when ModelImporter.IsSupportedFormat(path)
                    => _owner.PopulateModelSlotAsync(slot, path, isAvatar: ext == ".vrm"),
                _ => Task.CompletedTask,
            };
        }
    }

    /// <summary>
    /// Get the target slot, falling back to focused world root if not set.
    /// </summary>
    private Slot GetTargetSlot()
    {
        if (_targetSlot != null)
            return _targetSlot;

        // Try to get from focused world
        return _engine?.WorldManager?.FocusedWorld?.RootSlot!;
    }

    /// <summary>
    /// Set the target slot for imports.
    /// </summary>
    public void SetTargetSlot(Slot slot)
    {
        _targetSlot = slot;
    }

    public override void _Input(InputEvent @event)
    {
        // Handle Ctrl+V paste
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.V && keyEvent.CtrlPressed)
            {
                HandlePaste();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// Get files from Windows clipboard (when files are copied with Ctrl+C in Explorer).
    /// </summary>
    private List<string> GetClipboardFiles()
    {
        var files = new List<string>();

        // Only works on Windows
        if (OS.GetName() != "Windows")
            return files;

        try
        {
            if (!IsClipboardFormatAvailable(CF_HDROP))
                return files;

            if (!OpenClipboard(IntPtr.Zero))
                return files;

            try
            {
                IntPtr hDrop = GetClipboardData(CF_HDROP);
                if (hDrop == IntPtr.Zero)
                    return files;

                // Get number of files
                uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null!, 0);

                for (uint i = 0; i < fileCount; i++)
                {
                    // Get required buffer size
                    uint size = DragQueryFile(hDrop, i, null!, 0) + 1;
                    var sb = new StringBuilder((int)size);
                    DragQueryFile(hDrop, i, sb, size);
                    files.Add(sb.ToString());
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ClipboardImporter: Error getting clipboard files: {ex.Message}");
        }

        return files;
    }

    /// <summary>
    /// Handle paste from clipboard.
    /// </summary>
    public async void HandlePaste()
    {
        GD.Print("ClipboardImporter: Handling paste...");

        // Check for Windows clipboard files first (copied from Explorer with Ctrl+C)
        var clipboardFiles = GetClipboardFiles();
        if (clipboardFiles.Count > 0)
        {
            GD.Print($"ClipboardImporter: Found {clipboardFiles.Count} file(s) in clipboard");
            foreach (var filePath in clipboardFiles)
            {
                GD.Print($"ClipboardImporter: Processing clipboard file: {filePath}");
                await HandleFilePath(filePath);
            }
            return;
        }

        // Check for clipboard image (e.g., screenshot, copied image)
        if (DisplayServer.ClipboardHasImage())
        {
            GD.Print("ClipboardImporter: Found image in clipboard");
            await HandleClipboardImage();
            return;
        }

        // Check for text (file path or URL)
        var clipboardText = DisplayServer.ClipboardGet();

        if (string.IsNullOrEmpty(clipboardText))
        {
            GD.Print("ClipboardImporter: Clipboard is empty");
            return;
        }

        GD.Print($"ClipboardImporter: Clipboard text: {clipboardText.Substring(0, System.Math.Min(100, clipboardText.Length))}...");

        // Detect what type of content is in the clipboard
        var contentType = DetectContentType(clipboardText);

        switch (contentType)
        {
            case ClipboardContentType.FilePath:
                await HandleFilePath(clipboardText.Trim());
                break;

            case ClipboardContentType.Url:
                await HandleUrl(clipboardText.Trim());
                break;

            case ClipboardContentType.Text:
                await HandleText(clipboardText);
                break;

            default:
                GD.Print("ClipboardImporter: Unknown content type");
                break;
        }
    }

    private enum ClipboardContentType
    {
        Unknown,
        FilePath,
        Url,
        Text
    }

    private ClipboardContentType DetectContentType(string content)
    {
        content = NormalizeFilePath(content);

        // Check for file path
        if (IsFilePath(content))
        {
            return ClipboardContentType.FilePath;
        }

        // Check for URL
        if (content.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            content.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return ClipboardContentType.Url;
        }

        return ClipboardContentType.Text;
    }

    private bool IsFilePath(string content)
    {
        content = NormalizeFilePath(content);

        // Windows paths
        if (content.Length >= 3 && char.IsLetter(content[0]) && content[1] == ':' && (content[2] == '\\' || content[2] == '/'))
        {
            return File.Exists(content) || Directory.Exists(content);
        }

        // Unix paths
        if (content.StartsWith("/") || content.StartsWith("~"))
        {
            return File.Exists(content) || Directory.Exists(content);
        }

        // Check common file extensions
        foreach (var ext in ClipboardModelExtensions)
        {
            if (content.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(content);
            }
        }

        var otherExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp", ".gdshader" };
        foreach (var ext in otherExtensions)
        {
            if (content.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(content);
            }
        }

        return false;
    }

    private static string NormalizeFilePath(string content)
    {
        return content?.Trim().Trim('"') ?? string.Empty;
    }

    private Task HandleFilePath(string filePath)
    {
        filePath = NormalizeFilePath(filePath);
        GD.Print($"ClipboardImporter: Handling file path: {filePath}");

        if (!File.Exists(filePath) && !Directory.Exists(filePath))
        {
            GD.PrintErr($"ClipboardImporter: Path not found: {filePath}");
            return Task.CompletedTask;
        }

        var world = _engine?.WorldManager?.FocusedWorld;
        if (world == null)
        {
            GD.PrintErr("ClipboardImporter: No focused world for paste");
            return Task.CompletedTask;
        }

        var (pos, rot) = GetSpawnPose();
        UniversalImporter.Import(filePath, world, pos, rot);
        return Task.CompletedTask;
    }

    // Single chokepoint for any clipboard/url/text path that ends up as a file.
    // Routes through UniversalImporter so the matching import dialog appears
    // instead of doing a silent in-place attach. - xlinka
    private Task AutoImport(string filePath)
    {
        var world = _engine?.WorldManager?.FocusedWorld;
        if (world == null)
        {
            GD.PrintErr("ClipboardImporter: No focused world for auto import");
            return Task.CompletedTask;
        }

        var (pos, rot) = GetSpawnPose();
        UniversalImporter.Import(filePath, world, pos, rot);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle image data from clipboard (e.g., screenshots, copied images).
    /// </summary>
    private Task HandleClipboardImage()
    {
        var image = DisplayServer.ClipboardGetImage();
        if (image == null || image.IsEmpty())
        {
            GD.PrintErr("ClipboardImporter: Failed to get image from clipboard");
            return Task.CompletedTask;
        }

        var world = _engine?.WorldManager?.FocusedWorld;
        if (world == null)
        {
            GD.PrintErr("ClipboardImporter: No focused world for clipboard image");
            return Task.CompletedTask;
        }

        var tempDir = OS.GetUserDataDir() + "/tmp";
        Directory.CreateDirectory(tempDir);
        var tempPath = tempDir + "/clipboard_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

        var error = image.SavePng(tempPath);
        if (error != Error.Ok)
        {
            GD.PrintErr($"ClipboardImporter: Failed to save clipboard image: {error}");
            return Task.CompletedTask;
        }

        GD.Print($"ClipboardImporter: Saved clipboard image to {tempPath}");
        var (pos, rot) = GetSpawnPose();
        UniversalImporter.Import(tempPath, world, pos, rot);
        return Task.CompletedTask;
    }

    // Attach quad+texture+grabbable to an existing slot. The dialog flow creates
    // the slot ahead of time (at the user-chosen spawn pose); this method only
    // populates it. - xlinka
    public async Task PopulateImageSlotAsync(Slot imageSlot, string filePath)
    {
        GD.Print($"ClipboardImporter: Populating image slot: {filePath}");

        string localUri = null!;
        if (_localDB != null)
        {
            localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
        }

        var quadMesh = imageSlot.AttachComponent<LumoraMeshes.QuadMesh>();
        quadMesh.Size.Value = new float2(1.0f, 1.0f);
        quadMesh.DualSided.Value = true;

        var meshRenderer = imageSlot.AttachComponent<MeshRenderer>();
        meshRenderer.Mesh.Target = quadMesh;

        var imageProvider = imageSlot.AttachComponent<ImageProvider>();
        var imageUri = new Uri(localUri ?? filePath);
        imageProvider.URL.Value = imageUri;

        var collider = imageSlot.AttachComponent<BoxCollider>();
        collider.Size.Value = new float3(1f, 1f, 0.02f);

        imageSlot.AttachComponent<Grabbable>();

        var sizeDriver = imageSlot.AttachComponent<TextureSizeDriver>();
        sizeDriver.Source.Target = imageProvider;
        sizeDriver.Target.Target = quadMesh;
        sizeDriver.ColliderTarget.Target = collider;

        var material = imageSlot.AttachComponent<UnlitMaterial>();
        material.Texture.Target = imageProvider;
        material.TextureScale.Value = new float2(-1f, 1f);
        material.TextureOffset.Value = new float2(1f, 0f);
        material.BlendMode.Value = BlendMode.Transparent;
        meshRenderer.Material.Target = material;

        GD.Print($"ClipboardImporter: Image populated with visual components from {localUri ?? filePath}");
        OnAssetImported?.Invoke(filePath, imageSlot);
    }

    public async Task PopulateModelSlotAsync(Slot slot, string filePath, bool isAvatar)
    {
        GD.Print($"ClipboardImporter: Populating model slot as {(isAvatar ? "avatar" : "3D model")}: {filePath}");

        // Show an IN-WORLD progress indicator (3D, in front of the user) like the reference - not a flat screen
        // overlay. It's non-modal: the game stays interactive while it loads (freeze fix) and the user sees a
        // floating title + percent + progress bar in the world. Driven straight off the importer's progress. -xlinka
        var importWorld = slot.World;
        ModelImportIndicator.Show(importWorld, slot, isAvatar ? "Importing Avatar" : "Importing Model");
        var progress = new Progress<(float progress, string status)>(
            u => ModelImportIndicator.Report(u.progress, u.status));
        try
        {
            ModelImportResult result;
            if (isAvatar)
            {
                result = await ModelImporter.ImportAvatarAsync(filePath, slot, _localDB, progress);
            }
            else
            {
                result = await ModelImporter.ImportModelAsync(filePath, slot, null!, _localDB, progress);
            }
            if (result.Success)
            {
                OnAssetImported?.Invoke(filePath, result.RootSlot);
            }
            else
            {
                GD.PrintErr($"ClipboardImporter: Model import failed: {result.ErrorMessage}");
            }
        }
        finally
        {
            ModelImportIndicator.Hide();
        }
    }

    private async Task HandleUrl(string url)
    {
        GD.Print($"ClipboardImporter: Handling URL: {url}");

        // Determine file type from URL
        var extension = GetExtensionFromUrl(url);

        if (string.IsNullOrEmpty(extension))
        {
            // Need to fetch to determine content type
            await FetchAndImport(url);
            return;
        }

        // Download the file first
        var tempPath = await DownloadFile(url);
        if (!string.IsNullOrEmpty(tempPath))
        {
            _ = extension;
            await AutoImport(tempPath);
        }
    }

    private string GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var extension = Path.GetExtension(path).ToLowerInvariant();

        var knownExtensions = new List<string>(ModelImporter.SupportedExtensions)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".gdshader"
        };
        foreach (var ext in knownExtensions)
        {
            if (extension == ext) return ext;
        }
        }
        catch { }

        return null!;
    }

    private async Task<string> DownloadFile(string url)
    {
        GD.Print($"ClipboardImporter: Downloading file from {url}");

        try
        {
            var httpRequest = new HttpRequest();
            AddChild(httpRequest);

            var tempDir = OS.GetUserDataDir() + "/tmp";
            Directory.CreateDirectory(tempDir);
            var tempPath = tempDir + "/" + Guid.NewGuid().ToString("N") + Path.GetExtension(url);

            httpRequest.DownloadFile = tempPath;

            var tcs = new TaskCompletionSource<string>();

            httpRequest.RequestCompleted += (result, responseCode, headers, body) =>
            {
                httpRequest.QueueFree();

                if (result == (long)HttpRequest.Result.Success && File.Exists(tempPath))
                {
                    tcs.SetResult(tempPath);
                }
                else
                {
                    GD.PrintErr($"ClipboardImporter: Download failed with code {responseCode}");
                    tcs.SetResult(null!);
                }
            };

            var error = httpRequest.Request(url);
            if (error != Error.Ok)
            {
                httpRequest.QueueFree();
                GD.PrintErr($"ClipboardImporter: Failed to start request: {error}");
                return null!;
            }

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ClipboardImporter: Exception downloading file: {ex.Message}");
            return null!;
        }
    }

    private async Task FetchAndImport(string url)
    {
        // Download and detect content type from response headers
        var tempPath = await DownloadFile(url);
        if (!string.IsNullOrEmpty(tempPath))
        {
            // Try to detect file type from magic bytes
            var extension = DetectFileTypeFromHeader(tempPath);
            if (!string.IsNullOrEmpty(extension))
            {
                await AutoImport(tempPath);
            }
        }
    }

    private string DetectFileTypeFromHeader(string filePath)
    {
        try
        {
            using var file = File.OpenRead(filePath);
            var header = new byte[12];
            var bytesToRead = (int)System.Math.Min(header.Length, file.Length);
            file.ReadExactly(header, 0, bytesToRead);

            // PNG
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                return ".png";

            // JPEG
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return ".jpg";

            // WebP
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return ".webp";

            // GLB (binary GLTF)
            if (header[0] == 0x67 && header[1] == 0x6C && header[2] == 0x54 && header[3] == 0x46)
                return ".glb";
        }
        catch { }

        return null!;
    }

    private async Task HandleText(string text)
    {
        GD.Print($"ClipboardImporter: Handling text content (length: {text.Length})");

        if (IsGdShaderText(text))
        {
            await ImportShaderText(text);
            return;
        }

        // Could create a text label or note in the world
    }

    private static bool IsGdShaderText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("shader_type", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ImportShaderText(string shaderText)
    {
        var targetSlot = GetTargetSlot();
        if (targetSlot == null)
        {
            GD.PrintErr("ClipboardImporter: No target slot available for shader import");
            return;
        }

        var tempPath = _localDB?.GetTempFilePath(".gdshader");
        if (string.IsNullOrEmpty(tempPath))
        {
            var tempDir = OS.GetUserDataDir() + "/tmp";
            Directory.CreateDirectory(tempDir);
            tempPath = Path.Combine(tempDir, $"clipboard_shader_{DateTime.Now:yyyyMMdd_HHmmss}.gdshader");
        }

        await File.WriteAllTextAsync(tempPath, shaderText);
        await ImportShader(tempPath, targetSlot);
    }

    private async Task ImportShader(string filePath, Slot targetSlot)
    {
        var shaderName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(shaderName)) shaderName = "CustomShader";
        var rootSlot = targetSlot.AddSlot($"{shaderName}_ShaderWorkbench");
        PositionInFrontOfCamera(rootSlot);
        await PopulateShaderSlotAsync(rootSlot, filePath);
    }

    public async Task PopulateShaderSlotAsync(Slot rootSlot, string filePath)
    {
        GD.Print($"ClipboardImporter: Populating shader workbench: {filePath}");

        string localUri = null!;
        if (_localDB != null)
        {
            localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
        }

        var sourceSlot = rootSlot.AddSlot("ShaderSource");
        var shaderProvider = sourceSlot.AttachComponent<ShaderSourceProvider>();
        shaderProvider.URL.Value = new Uri(localUri ?? filePath);

        var materialSlot = rootSlot.AddSlot("Material");
        var material = materialSlot.AttachComponent<CustomShaderMaterial>();
        material.Shader.Target = shaderProvider;

        var sphereSlot = rootSlot.AddSlot("PreviewSphere");
        sphereSlot.LocalPosition.Value = new float3(-0.35f, 0f, 0f);

        var sphereMesh = sphereSlot.AttachComponent<LumoraMeshes.SphereMesh>();
        sphereMesh.Radius.Value = 0.3f;
        sphereMesh.Segments.Value = 32;
        sphereMesh.Rings.Value = 16;

        var meshRenderer = sphereSlot.AttachComponent<MeshRenderer>();
        meshRenderer.Mesh.Target = sphereMesh;
        meshRenderer.Material.Target = material;

        sphereSlot.AttachComponent<Grabbable>();
        var sphereCollider = sphereSlot.AttachComponent<SphereCollider>();
        sphereCollider.Radius.Value = 0.3f;

        GD.Print($"ClipboardImporter: Shader material created with local URI {localUri ?? filePath}");
        OnAssetImported?.Invoke(filePath, rootSlot);
    }

    /// <summary>
    /// Position a slot in front of the camera and face it toward the camera.
    /// </summary>
    private void PositionInFrontOfCamera(Slot slot, float distance = 2.0f)
    {
        if (slot == null)
            return;

        if (_camera != null)
        {
            var camPos = _camera.GlobalPosition;
            var camFwd = -_camera.GlobalTransform.Basis.Z; // Godot camera looks down -Z
            var flat = new float3(camFwd.X, 0f, camFwd.Z);
            flat = flat.LengthSquared < 1e-6f ? new float3(0f, 0f, 1f) : flat.Normalized;
            slot.GlobalPosition = new float3(camPos.X + flat.x * distance, camPos.Y, camPos.Z + flat.z * distance);

            // Face the user via a pure yaw, pointing the content's +Z back at the camera. Our content front is +Z
            // (FaceLocalUser/Slot.Forward), and floatQ.LookRotation is broken for facing (inverse-row builder ->
            // edge-on), so use AxisAngle - not LookRotation, and not the old "-Z front" assumption. -xlinka
            var toUser = new float3(-flat.x, 0f, -flat.z);
            slot.GlobalRotation = floatQ.AxisAngle(float3.Up, System.MathF.Atan2(toUser.x, toUser.z));
            return;
        }

        slot.GlobalPosition = GetImportSpawnPosition(slot.Parent, distance);
    }

    private float3 GetImportSpawnPosition(Slot targetSlot, float distance)
    {
        if (_camera != null)
        {
            var spawnPosition = _camera.GlobalPosition + (-_camera.GlobalTransform.Basis.Z * distance);
            return new float3(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);
        }

        var origin = targetSlot?.GlobalPosition ?? float3.Zero;
        return origin + new float3(0f, 0f, -distance);
    }
}
