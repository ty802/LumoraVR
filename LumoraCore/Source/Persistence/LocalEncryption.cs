// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Persistence;

/// <summary>
/// Encrypts local persistence blobs (saved worlds, inventory items, cached assets) at rest with
/// AES-256-GCM. GCM is authenticated, so a tampered file fails to decrypt instead of silently
/// loading altered data - you can't edit the ciphertext and have it accepted.
///
/// A single random master key encrypts the blobs; the master key is held at rest by a pluggable
/// <see cref="ISecretSealer"/>. The default sealer binds it to this machine + user account so a copied
/// vault won't open elsewhere; a platform layer can replace <see cref="Sealer"/> with an OS keystore
/// (e.g. DPAPI / libsecret) for stronger protection without changing any storage code. Truly
/// non-extractable keys for shared/published content require a server-issued key (a cloud concern).
/// </summary>
public static class LocalEncryption
{
    // "LVE1" magic so loaders can accept both encrypted and plain (legacy / local-home) blobs.
    private static readonly byte[] Magic = { (byte)'L', (byte)'V', (byte)'E', (byte)'1' };
    private const int NonceLen = 12;
    private const int TagLen = 16;
    private const int KeyLen = 32;

    private static readonly object _lock = new();
    private static byte[]? _masterKey;

    /// <summary>How the master key is sealed at rest. Swap for an OS-keystore sealer on the platform layer.</summary>
    public static ISecretSealer Sealer { get; set; } = new DerivedKeySealer();

    /// <summary>True if the blob carries our encryption header (vs a plain/legacy blob).</summary>
    public static bool IsEncrypted(byte[]? data)
        => data != null && data.Length >= Magic.Length
           && data[0] == Magic[0] && data[1] == Magic[1] && data[2] == Magic[2] && data[3] == Magic[3];

    /// <summary>Encrypt a blob: [magic][nonce][tag][ciphertext].</summary>
    public static byte[] Encrypt(byte[] plaintext)
    {
        var key = MasterKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagLen];

        using (var gcm = new AesGcm(key, TagLen))
            gcm.Encrypt(nonce, plaintext, cipher, tag);

        var output = new byte[Magic.Length + NonceLen + TagLen + cipher.Length];
        int offset = 0;
        Buffer.BlockCopy(Magic, 0, output, offset, Magic.Length); offset += Magic.Length;
        Buffer.BlockCopy(nonce, 0, output, offset, NonceLen); offset += NonceLen;
        Buffer.BlockCopy(tag, 0, output, offset, TagLen); offset += TagLen;
        Buffer.BlockCopy(cipher, 0, output, offset, cipher.Length);
        return output;
    }

    /// <summary>Decrypt a blob; plain (non-encrypted) blobs pass through unchanged. Throws on tamper.</summary>
    public static byte[] Decrypt(byte[] data)
    {
        if (!IsEncrypted(data))
            return data;

        var key = MasterKey();
        int offset = Magic.Length;
        var nonce = new byte[NonceLen];
        Buffer.BlockCopy(data, offset, nonce, 0, NonceLen); offset += NonceLen;
        var tag = new byte[TagLen];
        Buffer.BlockCopy(data, offset, tag, 0, TagLen); offset += TagLen;
        var cipher = new byte[data.Length - offset];
        Buffer.BlockCopy(data, offset, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using (var gcm = new AesGcm(key, TagLen))
            gcm.Decrypt(nonce, cipher, tag, plain);   // AuthenticationTagMismatchException on tamper
        return plain;
    }

    private static byte[] MasterKey()
    {
        lock (_lock)
            return _masterKey ??= LoadOrCreateMasterKey();
    }

    private static byte[] LoadOrCreateMasterKey()
    {
        var path = KeyPath();
        try
        {
            if (File.Exists(path))
                return Sealer.Unseal(File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            // A vault.key that won't unseal (different machine/user, corruption) is unrecoverable for
            // existing encrypted blobs; regenerate so new saves work rather than hard-failing.
            LumoraLogger.Warn($"LocalEncryption: could not unseal master key ({ex.Message}); regenerating.");
        }

        var master = RandomNumberGenerator.GetBytes(KeyLen);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Sealer.Seal(master));
            if (OperatingSystem.IsWindows())
            {
                try { File.SetAttributes(path, FileAttributes.Hidden); } catch { /* best effort */ }
            }
            else
            {
                // Owner read/write only - keep it out of reach of other users (and lean on the per-app
                // sandbox on Android).
                try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { /* best effort */ }
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"LocalEncryption: failed to persist master key: {ex.Message}");
        }
        return master;
    }

    private static string KeyPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LumoraVR", "vault.key");
}

/// <summary>Seals/unseals the master key at rest. Implement with an OS keystore for stronger protection.</summary>
public interface ISecretSealer
{
    byte[] Seal(byte[] secret);
    byte[] Unseal(byte[] sealed_);
}

/// <summary>
/// Default sealer: wraps the secret with a key derived (PBKDF2-SHA256) from this machine + user account
/// plus a per-install random salt. Anti-copy (a stolen vault.key won't open on another machine/user),
/// but not a hardened secret store - it can be recomputed by anything running as this user. For that,
/// replace with an OS-keystore sealer.
/// </summary>
public sealed class DerivedKeySealer : ISecretSealer
{
    private const int SaltLen = 16;
    private const int NonceLen = 12;
    private const int TagLen = 16;
    private const int KeyLen = 32;
    private const int Iterations = 200_000;

    public byte[] Seal(byte[] secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var wrap = DeriveWrapKey(salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var cipher = new byte[secret.Length];
        var tag = new byte[TagLen];
        using (var gcm = new AesGcm(wrap, TagLen))
            gcm.Encrypt(nonce, secret, cipher, tag);

        var output = new byte[SaltLen + NonceLen + TagLen + cipher.Length];
        int offset = 0;
        Buffer.BlockCopy(salt, 0, output, offset, SaltLen); offset += SaltLen;
        Buffer.BlockCopy(nonce, 0, output, offset, NonceLen); offset += NonceLen;
        Buffer.BlockCopy(tag, 0, output, offset, TagLen); offset += TagLen;
        Buffer.BlockCopy(cipher, 0, output, offset, cipher.Length);
        return output;
    }

    public byte[] Unseal(byte[] sealed_)
    {
        int offset = 0;
        var salt = new byte[SaltLen];
        Buffer.BlockCopy(sealed_, offset, salt, 0, SaltLen); offset += SaltLen;
        var nonce = new byte[NonceLen];
        Buffer.BlockCopy(sealed_, offset, nonce, 0, NonceLen); offset += NonceLen;
        var tag = new byte[TagLen];
        Buffer.BlockCopy(sealed_, offset, tag, 0, TagLen); offset += TagLen;
        var cipher = new byte[sealed_.Length - offset];
        Buffer.BlockCopy(sealed_, offset, cipher, 0, cipher.Length);

        var wrap = DeriveWrapKey(salt);
        var secret = new byte[cipher.Length];
        using (var gcm = new AesGcm(wrap, TagLen))
            gcm.Decrypt(nonce, cipher, tag, secret);
        return secret;
    }

    private static byte[] DeriveWrapKey(byte[] salt)
    {
        var factors = Encoding.UTF8.GetBytes($"{Environment.MachineName}␟{Environment.UserName}");
        return Rfc2898DeriveBytes.Pbkdf2(factors, salt, Iterations, HashAlgorithmName.SHA256, KeyLen);
    }
}
