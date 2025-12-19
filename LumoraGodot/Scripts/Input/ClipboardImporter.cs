using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Godot.UI;

namespace Lumora.Godot.Input;

/// <summary>
/// Handles clipboard paste operations for importing assets.
/// Detects file paths, URLs, and file data from clipboard.
/// Similar to BarkVR's journaling import system.
/// </summary>
public partial class ClipboardImporter : Node
{
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

    private LocalDB _localDB;
    private Slot _targetSlot;
    private ImportDialog _importDialog;
    private Camera3D _camera;
    private Lumora.Core.Engine _engine;

    /// <summary>
    /// Event fired when an asset is imported from clipboard.
    /// </summary>
    public event Action<string, Slot> OnAssetImported;

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
    /// </summary>
    private async void OnFilesDropped(string[] files)
    {
        GD.Print($"ClipboardImporter: {files.Length} file(s) dropped");

        foreach (var filePath in files)
        {
            GD.Print($"ClipboardImporter: Processing dropped file: {filePath}");
            await HandleFilePath(filePath);
        }
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
    }

    /// <summary>
    /// Get the target slot, falling back to focused world root if not set.
    /// </summary>
    private Slot GetTargetSlot()
    {
        if (_targetSlot != null)
            return _targetSlot;

        // Try to get from focused world
        return _engine?.WorldManager?.FocusedWorld?.RootSlot;
    }

    /// <summary>
    /// Set the import dialog reference.
    /// </summary>
    public void SetImportDialog(ImportDialog dialog)
    {
        _importDialog = dialog;
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
                uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);

                for (uint i = 0; i < fileCount; i++)
                {
                    // Get required buffer size
                    uint size = DragQueryFile(hDrop, i, null, 0) + 1;
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
                HandleText(clipboardText);
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
        content = content.Trim();

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
        var extensions = new[] { ".glb", ".gltf", ".vrm", ".png", ".jpg", ".jpeg", ".webp", ".obj", ".fbx" };
        foreach (var ext in extensions)
        {
            if (content.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(content);
            }
        }

        return false;
    }

    private async Task HandleFilePath(string filePath)
    {
        GD.Print($"ClipboardImporter: Handling file path: {filePath}");

        if (!File.Exists(filePath))
        {
            GD.PrintErr($"ClipboardImporter: File not found: {filePath}");
            return;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Show import dialog if available
        var targetSlot = GetTargetSlot();
        if (_importDialog != null && targetSlot != null)
        {
            _importDialog.ShowForFile(filePath, targetSlot, _localDB);
            return;
        }

        // Otherwise, auto-import based on extension
        await AutoImport(filePath, extension);
    }

    private async Task AutoImport(string filePath, string extension)
    {
        var targetSlot = GetTargetSlot();
        if (targetSlot == null)
        {
            GD.PrintErr("ClipboardImporter: No target slot available");
            return;
        }

        switch (extension)
        {
            case ".glb":
            case ".gltf":
                await ImportModel(filePath, isAvatar: false, targetSlot);
                break;

            case ".vrm":
                await ImportModel(filePath, isAvatar: true, targetSlot);
                break;

            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".webp":
            case ".bmp":
            case ".tga":
                await ImportImage(filePath, targetSlot);
                break;

            default:
                GD.Print($"ClipboardImporter: Unsupported file type: {extension}");
                break;
        }
    }

    /// <summary>
    /// Handle image data from clipboard (e.g., screenshots, copied images).
    /// </summary>
    private async Task HandleClipboardImage()
    {
        var image = DisplayServer.ClipboardGetImage();
        if (image == null || image.IsEmpty())
        {
            GD.PrintErr("ClipboardImporter: Failed to get image from clipboard");
            return;
        }

        var targetSlot = GetTargetSlot();
        if (targetSlot == null)
        {
            GD.PrintErr("ClipboardImporter: No target slot available");
            return;
        }

        // Save image to temp file
        var tempDir = OS.GetUserDataDir() + "/tmp";
        Directory.CreateDirectory(tempDir);
        var tempPath = tempDir + "/clipboard_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

        var error = image.SavePng(tempPath);
        if (error != Error.Ok)
        {
            GD.PrintErr($"ClipboardImporter: Failed to save clipboard image: {error}");
            return;
        }

        GD.Print($"ClipboardImporter: Saved clipboard image to {tempPath}");
        await ImportImage(tempPath, targetSlot);
    }

    private async Task ImportModel(string filePath, bool isAvatar, Slot targetSlot)
    {
        GD.Print($"ClipboardImporter: Importing model as {(isAvatar ? "avatar" : "3D model")}: {filePath}");

        ModelImportResult result;

        if (isAvatar)
        {
            result = await ModelImporter.ImportAvatarAsync(filePath, targetSlot, _localDB);
        }
        else
        {
            result = await ModelImporter.ImportModelAsync(filePath, targetSlot, null, _localDB);
        }

        if (result.Success)
        {
            GD.Print($"ClipboardImporter: Model imported successfully");

            // Position the model in front of the camera
            if (_camera != null && result.RootSlot != null)
            {
                var spawnPosition = _camera.GlobalPosition + (-_camera.GlobalTransform.Basis.Z * 2.0f);
                result.RootSlot.LocalPosition.Value = new Lumora.Core.Math.float3(
                    spawnPosition.X, spawnPosition.Y, spawnPosition.Z
                );
            }

            OnAssetImported?.Invoke(filePath, result.RootSlot);
        }
        else
        {
            GD.PrintErr($"ClipboardImporter: Model import failed: {result.ErrorMessage}");
        }
    }

    private async Task ImportImage(string filePath, Slot targetSlot)
    {
        GD.Print($"ClipboardImporter: Importing image: {filePath}");

        // Import to LocalDB
        string localUri = null;
        if (_localDB != null)
        {
            localUri = await _localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy);
        }

        // Create image slot
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var imageSlot = targetSlot.AddSlot(fileName);

        // Position in front of camera
        if (_camera != null)
        {
            var spawnPosition = _camera.GlobalPosition + (-_camera.GlobalTransform.Basis.Z * 2.0f);
            imageSlot.LocalPosition.Value = new Lumora.Core.Math.float3(
                spawnPosition.X, spawnPosition.Y, spawnPosition.Z
            );
        }

        // TODO: Create ImageProvider component to display the image
        GD.Print($"ClipboardImporter: Image imported to {localUri ?? filePath}");

        OnAssetImported?.Invoke(filePath, imageSlot);
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
            await AutoImport(tempPath, extension);
        }
    }

    private string GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var extension = Path.GetExtension(path).ToLowerInvariant();

            var knownExtensions = new[] { ".glb", ".gltf", ".vrm", ".png", ".jpg", ".jpeg", ".webp", ".obj" };
            foreach (var ext in knownExtensions)
            {
                if (extension == ext) return ext;
            }
        }
        catch { }

        return null;
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
                    tcs.SetResult(null);
                }
            };

            var error = httpRequest.Request(url);
            if (error != Error.Ok)
            {
                httpRequest.QueueFree();
                GD.PrintErr($"ClipboardImporter: Failed to start request: {error}");
                return null;
            }

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ClipboardImporter: Exception downloading file: {ex.Message}");
            return null;
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
                await AutoImport(tempPath, extension);
            }
        }
    }

    private string DetectFileTypeFromHeader(string filePath)
    {
        try
        {
            using var file = File.OpenRead(filePath);
            var header = new byte[12];
            file.Read(header, 0, 12);

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

        return null;
    }

    private void HandleText(string text)
    {
        GD.Print($"ClipboardImporter: Handling text content (length: {text.Length})");
        // Could create a text label or note in the world
    }
}
