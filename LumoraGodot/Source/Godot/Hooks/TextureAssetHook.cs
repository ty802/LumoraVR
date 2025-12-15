using Godot;
using Lumora.Core.Assets;

namespace Aquamarine.Godot.Hooks;

/// <summary>
/// Godot implementation of texture asset hook.
/// Creates and manages Godot ImageTexture resources.
/// </summary>
public class TextureAssetHook : AssetHook, ITextureAssetHook
{
    private ImageTexture _godotTexture;
    private TextureWrapMode _wrapU = TextureWrapMode.Repeat;
    private TextureWrapMode _wrapV = TextureWrapMode.Repeat;

    /// <summary>
    /// Get the Godot ImageTexture.
    /// </summary>
    public ImageTexture GodotTexture => _godotTexture;

    /// <summary>
    /// Whether the texture is valid and usable.
    /// </summary>
    public bool IsValid => _godotTexture != null;

    /// <summary>
    /// Upload pixel data to the Godot texture.
    /// </summary>
    public void UploadData(byte[] pixels, int width, int height, bool hasMipmaps)
    {
        // Check if this is raw RGBA data or encoded image data
        int expectedRgbaSize = width * height * 4;

        Image image;

        if (width > 0 && height > 0 && pixels.Length == expectedRgbaSize)
        {
            // Raw RGBA8 pixel data
            image = Image.CreateFromData(width, height, hasMipmaps, Image.Format.Rgba8, pixels);
        }
        else
        {
            // Encoded image data (PNG, JPEG, etc.) - let Godot decode it
            image = new Image();
            var error = image.LoadPngFromBuffer(pixels);

            if (error != Error.Ok)
            {
                error = image.LoadJpgFromBuffer(pixels);
            }

            if (error != Error.Ok)
            {
                error = image.LoadWebpFromBuffer(pixels);
            }

            if (error != Error.Ok)
            {
                error = image.LoadBmpFromBuffer(pixels);
            }

            if (error != Error.Ok)
            {
                GD.PrintErr($"TextureAssetHook: Failed to decode image data");
                return;
            }

            // Generate mipmaps if requested
            if (hasMipmaps)
            {
                image.GenerateMipmaps();
            }
        }

        // Create or update the ImageTexture
        if (_godotTexture == null)
        {
            _godotTexture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            _godotTexture.SetImage(image);
        }
    }

    /// <summary>
    /// Set texture wrap modes.
    /// Note: In Godot 4, wrap mode is set on the material/sampler, not the texture itself.
    /// We store the values for reference.
    /// </summary>
    public void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
        _wrapU = wrapU;
        _wrapV = wrapV;
    }

    /// <summary>
    /// Get the stored horizontal wrap mode.
    /// </summary>
    public TextureWrapMode WrapModeU => _wrapU;

    /// <summary>
    /// Get the stored vertical wrap mode.
    /// </summary>
    public TextureWrapMode WrapModeV => _wrapV;

    /// <summary>
    /// Unload and dispose the Godot texture.
    /// </summary>
    public override void Unload()
    {
        if (_godotTexture != null)
        {
            _godotTexture.Dispose();
            _godotTexture = null;
        }
    }
}
