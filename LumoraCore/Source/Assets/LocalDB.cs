// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Lumora.Core.Logging;
using Lumora.Core.Persistence;
using Lumora.Core.Phos;

namespace Lumora.Core.Assets;

/// <summary>
/// Local database for storing imported assets.
/// Provides local:// URI scheme for locally stored assets.
/// </summary>
public class LocalDB : IDisposable
{
    public enum ImportLocation
    {
        /// <summary>Reference file at original location</summary>
        Original,
        /// <summary>Copy file to local cache</summary>
        Copy,
        /// <summary>Move file to local cache</summary>
        Move
    }

    private readonly string _basePath;
    private readonly string _machineId;
    private readonly Dictionary<string, LocalAssetRecord> _assetRecords = new();
    private readonly object _lock = new();
    private bool _initialized;

    /// <summary>
    /// Encrypt asset cache bytes at rest (AES-GCM via <see cref="LocalEncryption"/>), the same store
    /// that already protects records.json. Reads always go through <see cref="ReadAssetBytesAsync"/>,
    /// which transparently decrypts and passes plaintext (legacy / externally-written) files through
    /// unchanged - so flipping this on is a forward migration with no rewrite of existing cache files.
    ///
    /// OFF by default: turning it on is only safe once every cache-byte READER goes through
    /// <see cref="ReadAssetBytesAsync"/> (the engine's local asset gather path) instead of reading the
    /// resolved <see cref="LocalAssetRecord.FilePath"/> directly, AND the peer asset transfer DECRYPTS
    /// before sending (the master key is machine-bound, so ciphertext can't be shipped to a joiner).
    /// Until those two sites are routed through here, leave this false. - xlinka
    /// </summary>
    public bool EncryptAssetsAtRest { get; set; }

    /// <summary>
    /// Get the machine-unique ID for this local database.
    /// </summary>
    public string MachineId => _machineId;

    /// <summary>
    /// Get the base path for local asset storage.
    /// </summary>
    public string BasePath => _basePath;

    public LocalDB(string basePath = null!)
    {
        _basePath = basePath ?? GetDefaultBasePath();
        _machineId = GetOrCreateMachineId();
    }

    /// <summary>
    /// Initialize the local database.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        // Ensure directories exist
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(GetAssetCachePath());
        Directory.CreateDirectory(GetTempPath());

        // Load existing asset records
        await LoadAssetRecordsAsync();

        _initialized = true;
        Logger.Log($"LocalDB: Initialized at '{_basePath}' with machine ID '{_machineId}'");
    }

    /// <summary>
    /// Import a local file into the asset database.
    /// </summary>
    public async Task<string> ImportLocalAssetAsync(string filePath, ImportLocation location = ImportLocation.Copy)
    {
        if (!File.Exists(filePath))
        {
            Logger.Error($"LocalDB: File not found: {filePath}");
            return null!;
        }

        // Calculate content hash for deduplication
        var hash = await ComputeFileHashAsync(filePath);
        var localUri = $"local://{_machineId}/{hash}";

        lock (_lock)
        {
            // Check if already imported
            if (_assetRecords.TryGetValue(hash, out var existing))
            {
                Logger.Log($"LocalDB: Asset already imported: {localUri}");
                return localUri;
            }
        }

        // Determine target path
        var extension = Path.GetExtension(filePath);
        var targetPath = Path.Combine(GetAssetCachePath(), hash + extension);

        try
        {
            // Original references the source in place - we don't own that file, so it stays plaintext.
            // Copy/Move land bytes in OUR cache and get wrapped when at-rest encryption is enabled. - xlinka
            bool encrypted = false;
            long plainSize;
            switch (location)
            {
                case ImportLocation.Original:
                    // Just reference the original file
                    targetPath = filePath;
                    plainSize = new FileInfo(targetPath).Length;
                    break;

                case ImportLocation.Copy:
                    plainSize = await WriteCacheFileAsync(targetPath, filePath, deleteSource: false);
                    encrypted = EncryptAssetsAtRest;
                    break;

                case ImportLocation.Move:
                    plainSize = await WriteCacheFileAsync(targetPath, filePath, deleteSource: true);
                    encrypted = EncryptAssetsAtRest;
                    break;

                default:
                    plainSize = 0;
                    break;
            }

            // Create asset record
            var record = new LocalAssetRecord
            {
                Hash = hash,
                LocalUri = localUri,
                FilePath = targetPath,
                OriginalPath = filePath,
                OriginalFileName = Path.GetFileName(filePath),
                ImportedAt = DateTime.UtcNow,
                FileSize = plainSize,
                Encrypted = encrypted
            };

            lock (_lock)
            {
                _assetRecords[hash] = record;
            }

            // Save records
            await SaveAssetRecordsAsync();

            Logger.Log($"LocalDB: Imported '{Path.GetFileName(filePath)}' -> {localUri}");
            return localUri;
        }
        catch (Exception ex)
        {
            Logger.Error($"LocalDB: Failed to import '{filePath}': {ex.Message}");
            return null!;
        }
    }

    /// <summary>
    /// Save an in-memory byte buffer as a content-addressed local:// asset and return its URI. The hash
    /// of the bytes IS the address, so saving identical content twice is a no-op that returns the same
    /// URI (true content addressing). Used by the mesh-as-asset path (SaveMeshAsync) but format-agnostic.
    /// </summary>
    public async Task<string> SaveAssetAsync(byte[] data, string extension = ".lmesh")
    {
        if (data == null)
        {
            Logger.Error("LocalDB: SaveAssetAsync called with null data");
            return null!;
        }

        if (!string.IsNullOrEmpty(extension) && !extension.StartsWith("."))
            extension = "." + extension;

        var hash = ComputeBytesHash(data);
        var localUri = $"local://{_machineId}/{hash}";

        lock (_lock)
        {
            if (_assetRecords.TryGetValue(hash, out _))
            {
                Logger.Log($"LocalDB: Asset already saved: {localUri}");
                return localUri;
            }
        }

        var targetPath = Path.Combine(GetAssetCachePath(), hash + extension);
        bool encrypted = EncryptAssetsAtRest;
        try
        {
            // Hash addresses the PLAINTEXT (above), so dedup stays content-stable regardless of the
            // at-rest encryption toggle; only the bytes on disk are wrapped. - xlinka
            var onDisk = encrypted ? LocalEncryption.Encrypt(data) : data;
            await File.WriteAllBytesAsync(targetPath, onDisk);
        }
        catch (Exception ex)
        {
            Logger.Error($"LocalDB: Failed to save asset {localUri}: {ex.Message}");
            return null!;
        }

        var record = new LocalAssetRecord
        {
            Hash = hash,
            LocalUri = localUri,
            FilePath = targetPath,
            OriginalPath = localUri,
            OriginalFileName = hash + extension,
            ImportedAt = DateTime.UtcNow,
            FileSize = data.LongLength,
            Encrypted = encrypted
        };

        lock (_lock)
        {
            _assetRecords[hash] = record;
        }

        await SaveAssetRecordsAsync();
        Logger.Log($"LocalDB: Saved {data.Length}-byte asset -> {localUri}");
        return localUri;
    }

    /// <summary>Serialize a PhosMesh to .lmesh bytes and save it as a content-addressed local:// asset.</summary>
    public Task<string> SaveMeshAsync(PhosMesh mesh)
    {
        if (mesh == null)
            return Task.FromResult<string>(null!);
        return SaveAssetAsync(PhosMeshSerializer.Serialize(mesh), ".lmesh");
    }

    /// <summary>
    /// Get the local file path for a local:// URI.
    /// </summary>
    public string GetFilePath(string localUri)
    {
        if (!localUri.StartsWith("local://"))
            return null!;

        // Parse URI: local://[machineId]/[hash]
        var parts = localUri.Substring(8).Split('/');
        if (parts.Length < 2)
            return null!;

        var hash = parts[1];

        lock (_lock)
        {
            if (_assetRecords.TryGetValue(hash, out var record))
            {
                return record.FilePath;
            }
        }

        return null!;
    }

    /// <summary>
    /// Check if a local URI exists in the database.
    /// </summary>
    public bool Exists(string localUri)
    {
        var path = GetFilePath(localUri);
        return path != null && File.Exists(path);
    }

    /// <summary>
    /// Get a temporary file path for import operations.
    /// </summary>
    public string GetTempFilePath(string extension = null!)
    {
        var fileName = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrEmpty(extension))
        {
            if (!extension.StartsWith("."))
                extension = "." + extension;
            fileName += extension;
        }
        return Path.Combine(GetTempPath(), fileName);
    }

    /// <summary>
    /// Clean up old temporary files.
    /// </summary>
    public void CleanupTempFiles(TimeSpan maxAge = default)
    {
        if (maxAge == default)
            maxAge = TimeSpan.FromHours(24);

        var tempPath = GetTempPath();
        if (!Directory.Exists(tempPath))
            return;

        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var file in Directory.GetFiles(tempPath))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Get all imported asset records.
    /// </summary>
    public IReadOnlyList<LocalAssetRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return new List<LocalAssetRecord>(_assetRecords.Values);
        }
    }

    private string GetDefaultBasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LumoraVR", "LocalDB");
    }

    private string GetAssetCachePath() => Path.Combine(_basePath, "Assets");
    private string GetTempPath() => Path.Combine(_basePath, "Temp");

    private string GetOrCreateMachineId()
    {
        // Prefer the install's self-certifying machine identity so local:// asset URIs carry the SAME id
        // the session stamps on User.MachineID. The asset transferer resolves which connected peer owns a
        // local:// asset from that id; when the two differed, a peer-imported mesh/texture could never be
        // relayed to other users. The id is base64url (no '/'), so it doesn't disturb URI parsing, and the
        // transferer reads it case-sensitively from the URI's original string.
        try
        {
            var identityId = Lumora.Core.Security.MachineIdentity.Local.MachineId;
            if (!string.IsNullOrEmpty(identityId))
                return identityId;
        }
        catch (Exception ex)
        {
            Logger.Warn($"LocalDB: machine identity unavailable, using a local fallback id: {ex.Message}");
        }

        // Fallback: a stable random id persisted next to the cache.
        var idPath = Path.Combine(_basePath, ".machine_id");
        try
        {
            Directory.CreateDirectory(_basePath);

            if (File.Exists(idPath))
            {
                return File.ReadAllText(idPath).Trim();
            }

            var id = Guid.NewGuid().ToString("N").Substring(0, 16);
            File.WriteAllText(idPath, id);
            return id;
        }
        catch
        {
            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }
    }

    // Write a source file into the cache at targetPath, encrypting at rest when enabled, and return the
    // PLAINTEXT byte length. With encryption off we keep the cheap File.Copy/Move (no read-into-memory).
    // With it on we must read -> wrap -> write, since copy alone can't transform the bytes. - xlinka
    private async Task<long> WriteCacheFileAsync(string targetPath, string sourcePath, bool deleteSource)
    {
        if (!EncryptAssetsAtRest)
        {
            if (deleteSource)
                await Task.Run(() => File.Move(sourcePath, targetPath, true));
            else
                await Task.Run(() => File.Copy(sourcePath, targetPath, true));
            return new FileInfo(targetPath).Length;
        }

        var plain = await File.ReadAllBytesAsync(sourcePath);
        await File.WriteAllBytesAsync(targetPath, LocalEncryption.Encrypt(plain));
        if (deleteSource)
        {
            try { File.Delete(sourcePath); } catch { /* best effort - source already consumed */ }
        }
        return plain.LongLength;
    }

    /// <summary>
    /// Read a cached asset's bytes, transparently decrypting if it was stored encrypted. A plaintext or
    /// legacy file is returned as-is (<see cref="LocalEncryption.Decrypt"/> detects the header), so this
    /// is safe to call for every local:// read and doubles as the migration path. Returns null when the
    /// URI doesn't resolve. - xlinka
    /// </summary>
    public async Task<byte[]?> ReadAssetBytesAsync(string localUri)
    {
        var path = GetFilePath(localUri);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        var raw = await File.ReadAllBytesAsync(path);
        try
        {
            return LocalEncryption.Decrypt(raw);
        }
        catch (Exception ex)
        {
            Logger.Error($"LocalDB: failed to decrypt cached asset '{localUri}': {ex.Message}");
            return null;
        }
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
        return Convert.ToHexString(hashBytes).ToLower();
    }

    private static string ComputeBytesHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLower();
    }

    private async Task LoadAssetRecordsAsync()
    {
        var recordsPath = Path.Combine(_basePath, "records.json");
        if (!File.Exists(recordsPath))
            return;

        try
        {
            // Transparently handle both the encrypted store and a plain/legacy records.json.
            var raw = await File.ReadAllBytesAsync(recordsPath);
            var json = Encoding.UTF8.GetString(LocalEncryption.Decrypt(raw));
            var records = JsonSerializer.Deserialize<List<LocalAssetRecord>>(json);
            if (records == null) return;

            lock (_lock)
            {
                foreach (var record in records)
                {
                    if (!string.IsNullOrEmpty(record.Hash))
                        _assetRecords[record.Hash] = record;
                }
            }

            Logger.Log($"LocalDB: Loaded {records.Count} asset records");
        }
        catch (Exception ex)
        {
            Logger.Warn($"LocalDB: Failed to load records: {ex.Message}");
        }
    }

    private async Task SaveAssetRecordsAsync()
    {
        var recordsPath = Path.Combine(_basePath, "records.json");
        try
        {
            List<LocalAssetRecord> snapshot;
            lock (_lock)
                snapshot = new List<LocalAssetRecord>(_assetRecords.Values);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            // The records store (asset list, original paths, per-asset keys) is encrypted at rest.
            var bytes = LocalEncryption.Encrypt(Encoding.UTF8.GetBytes(json));
            await File.WriteAllBytesAsync(recordsPath, bytes);
        }
        catch (Exception ex)
        {
            Logger.Warn($"LocalDB: Failed to save records: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Cleanup temp files older than 1 hour on dispose
        CleanupTempFiles(TimeSpan.FromHours(1));
    }
}

/// <summary>
/// Record of a locally imported asset.
/// </summary>
public class LocalAssetRecord
{
    public string Hash { get; set; } = null!;
    public string LocalUri { get; set; } = null!;
    public string FilePath { get; set; } = null!;
    public string OriginalPath { get; set; } = null!;
    public string OriginalFileName { get; set; } = null!;
    public DateTime ImportedAt { get; set; }
    public long FileSize { get; set; }

    /// <summary>
    /// True when this cache file is wrapped with <see cref="LocalEncryption"/> (AES-GCM) at rest.
    /// Stored in the encrypted records store. Reads go through <see cref="LocalDB.ReadAssetBytesAsync"/>,
    /// which detects the encryption header regardless of this flag, so a plaintext/legacy file still
    /// reads correctly even if the flag is stale. <see cref="FileSize"/> is the PLAINTEXT length.
    /// </summary>
    public bool Encrypted { get; set; }

    /// <summary>
    /// Reserved. The current model uses a single machine-bound master key (see <see cref="LocalEncryption"/>),
    /// not a per-asset key, so this stays null. Kept for a future per-asset / server-issued key scheme.
    /// </summary>
    public byte[]? EncryptionKey { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
}
