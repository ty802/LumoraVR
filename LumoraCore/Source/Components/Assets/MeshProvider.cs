using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Phos;

namespace Lumora.Core.Assets;

/// <summary>
/// Component that provides mesh assets from URLs.
/// Loads mesh files and creates MeshDataAsset instances.
/// </summary>
public class MeshProvider : UrlAssetProvider<MeshDataAsset, MeshDataMetadata>
{
    /// <summary>
    /// Whether to keep mesh data readable after GPU upload.
    /// Useful for physics collisions or runtime mesh manipulation.
    /// </summary>
    public readonly Sync<bool> KeepReadable;

    /// <summary>
    /// Scale factor to apply when loading the mesh.
    /// </summary>
    public readonly Sync<float> ImportScale;

    public MeshProvider()
    {
        KeepReadable = new Sync<bool>(this, false);
        ImportScale = new Sync<float>(this, 1.0f);
    }

    protected override async Task<MeshDataMetadata> LoadMetadata(Uri url, CancellationToken token)
    {
        // Read mesh header to estimate size without full load
        var metadata = new MeshDataMetadata();

        if (url.IsFile)
        {
            var fileInfo = new FileInfo(url.LocalPath);
            string ext = fileInfo.Extension.ToLowerInvariant();

            // Rough estimates based on file format
            switch (ext)
            {
                case ".obj":
                    // OBJ files are text, ~100 bytes per vertex estimate
                    metadata.VertexCount = (int)(fileInfo.Length / 100);
                    metadata.TriangleCount = metadata.VertexCount / 3;
                    break;
                case ".glb":
                case ".gltf":
                    // GLB is binary, ~50 bytes per vertex estimate
                    metadata.VertexCount = (int)(fileInfo.Length / 50);
                    metadata.TriangleCount = metadata.VertexCount / 3;
                    break;
                case ".fbx":
                    // FBX varies, ~80 bytes per vertex estimate
                    metadata.VertexCount = (int)(fileInfo.Length / 80);
                    metadata.TriangleCount = metadata.VertexCount / 3;
                    break;
                default:
                    // Unknown format, rough estimate
                    metadata.VertexCount = (int)(fileInfo.Length / 64);
                    metadata.TriangleCount = metadata.VertexCount / 3;
                    break;
            }

            metadata.HasUV = true;
            metadata.HasNormals = true;
        }

        return metadata;
    }

    protected override async Task<MeshDataAsset> LoadAssetData(Uri url, MeshDataMetadata metadata, CancellationToken token)
    {
        byte[] fileData = await LoadFileBytes(url, token);
        if (token.IsCancellationRequested) return null;

        PhosMesh mesh = DecodeMesh(fileData, url);
        if (mesh == null) return null;

        // Apply import scale if not 1.0
        float scale = ImportScale.Value;
        if (System.Math.Abs(scale - 1.0f) > 0.0001f)
        {
            ScaleMesh(mesh, scale);
        }

        var asset = new MeshDataAsset();
        asset.KeepReadable = KeepReadable.Value;
        asset.SetMeshData(mesh);
        return asset;
    }

    private async Task<byte[]> LoadFileBytes(Uri url, CancellationToken token)
    {
        if (url.IsFile)
        {
            return await File.ReadAllBytesAsync(url.LocalPath, token);
        }

        // For HTTP URLs, would need HttpClient - placeholder
        throw new NotSupportedException($"URL scheme not supported: {url.Scheme}");
    }

    private PhosMesh DecodeMesh(byte[] fileData, Uri url)
    {
        // This is a placeholder - actual implementation would use a mesh decoder
        // like Assimp, glTF loader, or custom OBJ parser
        // For Godot integration, the hook will handle actual decoding

        // Create empty mesh for now - actual loading happens in hook
        var mesh = new PhosMesh();

        // Store file data in mesh for hook to process
        // This is a temporary solution until proper mesh loading is implemented
        return mesh;
    }

    private void ScaleMesh(PhosMesh mesh, float scale)
    {
        // Scale all vertex positions
        var positions = mesh.RawPositions;
        if (positions != null && positions.Length > 0)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] *= scale;
            }
        }
    }
}
