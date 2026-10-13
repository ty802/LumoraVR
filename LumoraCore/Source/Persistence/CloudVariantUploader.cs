// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Persistence;

// Where a sync puts the blobs it uploads and the variants it registers: the service in a real sync,
// something standing in for it in a test.
public interface ICloudUploadSink
{
    // Stores bytes under their own sha256, skipping a blob the service already has. The hash, or null and why.
    Task<(string? Hash, string? Error)> StoreAsync(byte[] bytes, string extension, CancellationToken ct);

    // The ids of the variants registered for the blob baseHash, empty when there are none, null when the
    // service could not say.
    Task<IReadOnlyList<string>?> ListVariantsAsync(string baseHash, CancellationToken ct);

    // Records that a variant of the blob baseHash is the stored blob resultHash. Status is the service's
    // answer, 0 when none came: 404 when either blob is not stored, 403 when the caller is not who first
    // uploaded the base.
    Task<(bool Ok, int Status, string? Error)> RegisterVariantAsync(string baseHash, string variantId, string resultHash, long size,
        CancellationToken ct);
}

// The variants of a texture a save takes up with it, made on the machine that saves: the service only
// stores and hands them out, it never makes one.
public static class CloudVariantUploader
{
    private const int NoContent = 404;
    private const int NotUploader = 403;

    // For one texture the save uploaded under baseHash from localUri: makes the variants this machine
    // has not made yet, stores each with its metadata and registers them against baseHash. The stored
    // blobs come back for the item's manifest, so they count toward its owner's storage.
    //
    // The service is asked first. Once any variant of the texture is registered, the first registration
    // of an id stays and readers use it, so nothing is made and nothing is sent. Otherwise each variant
    // is registered before it is stored, and only a 404 (the service lacks the blob) stores it and
    // registers again. A 403 means someone else uploaded the texture first and only they may register
    // its variants, so nothing more of it goes.
    //
    // The base is already up, so a variant that does not make it only costs its readers the original:
    // it is logged and the rest still go. They go one at a time, read from the database as they are
    // sent, sidecar first and then the rungs smallest first, the order a reader wants them in.
    // Cancellation stops it between variants with the usual OperationCanceledException. -xlinka
    public static async Task<IReadOnlyList<CloudAssetEntry>> UploadAsync(LocalDB db, string localUri, string baseHash, ICloudUploadSink sink,
        CancellationToken ct)
    {
        var sent = new List<CloudAssetEntry>();
        if (db == null || sink == null || string.IsNullOrEmpty(localUri) || string.IsNullOrEmpty(baseHash))
            return sent;
        ct.ThrowIfCancellationRequested();

        var listed = await sink.ListVariantsAsync(baseHash, ct).ConfigureAwait(false);
        if (listed is { Count: > 0 })
            return sent;
        ct.ThrowIfCancellationRequested();

        var metadata = await TextureVariantStore.EnsureLadderAsync(db, localUri, ct).ConfigureAwait(false);
        if (metadata == null)
        {
            Logging.Logger.Warn($"CloudVariantUploader: no variants could be made for {Short(baseHash)}; its readers get the original");
            return sent;
        }

        var wanted = new List<(string Id, string? Uri, string Extension)>
        {
            (TextureVariantStore.MetadataSuffix, TextureVariantStore.GetMetadataUri(localUri), TextureVariantStore.MetadataExtension),
        };
        foreach (var id in TextureVariantStore.UploadPlan(metadata))
            wanted.Add((id.Identifier, TextureVariantStore.GetVariantUri(localUri, id), TextureVariantStore.VariantExtension));

        foreach (var (variantId, uri, extension) in wanted)
        {
            ct.ThrowIfCancellationRequested();
            var (entry, refused) = await SendAsync(db, uri, baseHash, variantId, extension, sink, ct).ConfigureAwait(false);
            if (entry != null)
                sent.Add(entry);
            if (refused)
            {
                Logging.Logger.Debug($"CloudVariantUploader: {Short(baseHash)} was first uploaded by another account, so this machine sends none of its variants");
                break;
            }
        }
        return sent;
    }

    // One variant read, registered, and stored when the service lacks it; null, with why logged, when any of
    // that did not happen. Refused when the service takes no variants of this texture from this account.
    private static async Task<(CloudAssetEntry? Entry, bool Refused)> SendAsync(LocalDB db, string? uri, string baseHash, string variantId,
        string extension, ICloudUploadSink sink, CancellationToken ct)
    {
        try
        {
            var bytes = uri == null ? null : await db.ReadAssetBytesAsync(uri).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return (Missed(variantId, baseHash, "it is not on this machine"), false);

            var result = ContentHash.FromBytes(bytes);
            ct.ThrowIfCancellationRequested();
            var (registered, status, registerError) = await sink.RegisterVariantAsync(baseHash, variantId, result, bytes.LongLength, ct)
                .ConfigureAwait(false);
            if (!registered && status == NoContent)
            {
                ct.ThrowIfCancellationRequested();
                var (stored, storeError) = await sink.StoreAsync(bytes, extension, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(stored))
                    return (Missed(variantId, baseHash, $"the store said no: {storeError ?? "no answer"}"), false);

                result = stored!;
                ct.ThrowIfCancellationRequested();
                (registered, status, registerError) = await sink.RegisterVariantAsync(baseHash, variantId, result, bytes.LongLength, ct)
                    .ConfigureAwait(false);
            }
            if (!registered && status == NotUploader)
                return (null, true);
            if (!registered)
                return (Missed(variantId, baseHash, $"it was not registered: {registerError ?? "no answer"}"), false);

            return (new CloudAssetEntry
            {
                Hash = result,
                Name = $"{baseHash}~{variantId}",
                Extension = extension,
                Type = "variant",
                SizeBytes = bytes.LongLength,
                LocalUri = uri!,
            }, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (Missed(variantId, baseHash, ex.Message), false);
        }
    }

    private static CloudAssetEntry? Missed(string variantId, string baseHash, string why)
    {
        Logging.Logger.Warn($"CloudVariantUploader: {variantId} of {Short(baseHash)} did not go up, {why}");
        return null;
    }

    private static string Short(string hash) => hash.Length > 12 ? hash.Substring(0, 12) : hash;
}
