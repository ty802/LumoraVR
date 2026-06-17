// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Bootstrap;

/// <summary>
/// Platform-backed <see cref="ISecretSealer"/> for the vault master key, per OS:
///  - Windows: DPAPI (CurrentUser) - the OS holds the key under the user login, not recomputable from
///    our binary.
///  - Linux desktop: the Secret Service (GNOME Keyring / KWallet) via the libsecret `secret-tool` CLI
///    (process-based, so no fragile native marshaling) - the key lives in the OS keyring.
///  - Android / headless / anything without the above: the engine's machine+user derived sealer
///    (Android additionally relies on per-app sandbox storage). Android Keystore proper needs a JNI
///    plugin (on-device work) - TODO, not shipped blind.
/// A 1-byte scheme tag records which path sealed the key so unseal always matches it.
/// </summary>
public sealed class PlatformSecretSealer : ISecretSealer
{
    private const byte SchemeDerived = 0;
    private const byte SchemeDpapi = 1;
    private const byte SchemeKeyring = 2;   // Linux Secret Service (key stored in the keyring, not the file)

    private readonly DerivedKeySealer _derived = new();

    public byte[] Seal(byte[] secret)
    {
        // Inline IsWindows() (not a cached flag) so the platform analyzer sees the DPAPI guard.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return Prefix(SchemeDpapi, DpapiProtect(secret));
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"PlatformSecretSealer: DPAPI seal failed ({ex.Message}); using derived sealer.");
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Store the key in the OS keyring; the on-disk vault.key then holds only the scheme tag.
            if (SecretService.TryStore(secret))
                return new[] { SchemeKeyring };
            LumoraLogger.Warn("PlatformSecretSealer: Secret Service unavailable (secret-tool); using derived sealer.");
        }
        return Prefix(SchemeDerived, _derived.Seal(secret));
    }

    public byte[] Unseal(byte[] sealed_)
    {
        if (sealed_ == null || sealed_.Length < 1)
            throw new InvalidOperationException("Empty sealed key.");

        byte scheme = sealed_[0];
        var payload = new byte[sealed_.Length - 1];
        Buffer.BlockCopy(sealed_, 1, payload, 0, payload.Length);

        switch (scheme)
        {
            case SchemeDpapi:
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("This vault key was sealed with Windows DPAPI and can't be opened on this platform.");
                return DpapiUnprotect(payload);

            case SchemeKeyring:
                return SecretService.TryLookup()
                    ?? throw new InvalidOperationException("Vault key not found in the OS keyring.");

            default:
                return _derived.Unseal(payload);
        }
    }

    private static byte[] Prefix(byte scheme, byte[] payload)
    {
        var output = new byte[payload.Length + 1];
        output[0] = scheme;
        Buffer.BlockCopy(payload, 0, output, 1, payload.Length);
        return output;
    }

    // --- Windows DPAPI (CurrentUser) via crypt32, no extra NuGet ---

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] DpapiProtect(byte[] data) => DpapiCall(data, protect: true);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] DpapiUnprotect(byte[] data) => DpapiCall(data, protect: false);

    private static byte[] DpapiCall(byte[] data, bool protect)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        try
        {
            input.cbData = data.Length;
            input.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, input.pbData, data.Length);

            bool ok = protect
                ? CryptProtectData(ref input, "LumoraVR vault key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
            if (!ok)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    // --- Linux Secret Service via the libsecret `secret-tool` CLI (no native marshaling) ---
    // The key (base64) is stored in / read from the OS keyring under fixed attributes. Any failure
    // (tool missing, no running keyring, headless) returns false/null so the caller falls back.
    private static class SecretService
    {
        private const string Tool = "secret-tool";

        public static bool TryStore(byte[] secret)
        {
            try
            {
                var psi = new ProcessStartInfo(Tool)
                {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("store");
                psi.ArgumentList.Add("--label=LumoraVR Vault Key");
                psi.ArgumentList.Add("service");
                psi.ArgumentList.Add("lumoravr");
                psi.ArgumentList.Add("item");
                psi.ArgumentList.Add("vaultkey");

                using var proc = Process.Start(psi);
                if (proc == null)
                    return false;
                proc.StandardInput.WriteLine(Convert.ToBase64String(secret));
                proc.StandardInput.Close();
                if (!proc.WaitForExit(5000))
                {
                    try { proc.Kill(); } catch { /* ignore */ }
                    return false;
                }
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static byte[]? TryLookup()
        {
            try
            {
                var psi = new ProcessStartInfo(Tool)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("lookup");
                psi.ArgumentList.Add("service");
                psi.ArgumentList.Add("lumoravr");
                psi.ArgumentList.Add("item");
                psi.ArgumentList.Add("vaultkey");

                using var proc = Process.Start(psi);
                if (proc == null)
                    return null;
                var output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(5000))
                {
                    try { proc.Kill(); } catch { /* ignore */ }
                    return null;
                }
                if (proc.ExitCode != 0)
                    return null;
                var b64 = output.Trim();
                return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64);
            }
            catch
            {
                return null;
            }
        }
    }
}
