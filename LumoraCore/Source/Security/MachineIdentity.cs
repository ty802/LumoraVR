// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Security.Cryptography;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Security;

/// <summary>
/// This install's cryptographic machine identity: an RSA keypair generated once and persisted, with
/// the private half encrypted at rest. The MachineID is the hash of the PUBLIC key, so it's
/// self-certifying. You can't claim a MachineID you don't hold the key for, and the join handshake
/// makes you sign a fresh nonce with the private half to prove you actually hold it. That's what turns
/// a MachineID from a free-text string anyone can type into something a ban can actually pin to.
///
/// This is the machine layer only. Proving WHICH logged-in account you are is a separate cloud-backed
/// layer that sits on top of this later (the join handshake already leaves room for it). -xlinka
/// </summary>
public sealed class MachineIdentity
{
    private static readonly Lazy<MachineIdentity> _local = new(LoadOrCreate);

    /// <summary>The machine identity for this install, lazily loaded or created on first use.</summary>
    public static MachineIdentity Local => _local.Value;

    private readonly RSA _rsa;

    /// <summary>Public key as DER-encoded SubjectPublicKeyInfo. Safe to hand to a host over the wire.</summary>
    public byte[] PublicKey { get; }

    /// <summary>Self-certifying machine id: url-safe base64 of SHA256(public key).</summary>
    public string MachineId { get; }

    private MachineIdentity(RSA rsa)
    {
        _rsa = rsa;
        PublicKey = rsa.ExportSubjectPublicKeyInfo();
        MachineId = ComputeMachineId(PublicKey);
    }

    /// <summary>Sign a challenge nonce with our private machine key (RSA SHA256 PKCS1).</summary>
    public byte[] SignChallenge(byte[] nonce)
        => _rsa.SignData(nonce, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    /// <summary>Compute the self-certifying machine id for a DER SPKI public key.</summary>
    public static string ComputeMachineId(byte[] publicKeySpki)
    {
        var hash = SHA256.HashData(publicKeySpki);
        // url-safe base64, no padding. 32-byte hash lands at 43 chars, well under the 128 wire cap.
        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>True if the claimed machine id really is the hash of the presented public key.</summary>
    public static bool IsMachineIdForKey(string? machineId, byte[]? publicKeySpki)
    {
        if (string.IsNullOrEmpty(machineId) || publicKeySpki == null || publicKeySpki.Length == 0)
            return false;
        return string.Equals(machineId, ComputeMachineId(publicKeySpki), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verify a joiner's challenge answer. The signature has to be valid for the nonce under the
    /// presented public key, AND that public key has to hash to the claimed MachineID. Both together
    /// prove the joiner holds the private key behind the MachineID they're claiming, no more, no less.
    /// </summary>
    public static bool VerifyChallenge(string? claimedMachineId, byte[]? publicKeySpki, byte[]? nonce, byte[]? signature)
    {
        if (!IsMachineIdForKey(claimedMachineId, publicKeySpki))
            return false;
        if (nonce == null || nonce.Length == 0 || signature == null || signature.Length == 0)
            return false;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            return rsa.VerifyData(nonce, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            // Garbage key/signature should read as "nope", not blow up the join handler. -xlinka
            LumoraLogger.Warn($"MachineIdentity: challenge verify threw ({ex.Message}); treating as failed.");
            return false;
        }
    }

    private static MachineIdentity LoadOrCreate()
    {
        var path = KeyPath();
        try
        {
            if (File.Exists(path))
            {
                var pkcs8 = LocalEncryption.Decrypt(File.ReadAllBytes(path));
                var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(pkcs8, out _);
                return new MachineIdentity(rsa);
            }
        }
        catch (Exception ex)
        {
            // A key we can't read (corruption, vault rotated) just means a new identity. The old
            // MachineID stops working, which is the same as a reinstall. Not worth hard-failing. -xlinka
            LumoraLogger.Warn($"MachineIdentity: couldn't load existing key ({ex.Message}); generating a new one.");
        }

        var created = RSA.Create(2048);
        try
        {
            var pkcs8 = created.ExportPkcs8PrivateKey();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, LocalEncryption.Encrypt(pkcs8));
            if (OperatingSystem.IsWindows())
            {
                try { File.SetAttributes(path, FileAttributes.Hidden); } catch { /* best effort */ }
            }
            else
            {
                try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { /* best effort */ }
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"MachineIdentity: failed to persist machine key ({ex.Message}); identity won't survive a restart.");
        }
        return new MachineIdentity(created);
    }

    private static string KeyPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LumoraVR", "machine_identity.key");
}
