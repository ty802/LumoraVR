// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core.Assets;

namespace Lumora.Core.Photos;

// Local photo files are independent of world permissions, sign-in and inventory settings.
public static class PhotoLibrary
{
    // The host supplies the OS Pictures directory (including redirected/XDG folders).
    public static string? PicturesDirectory { get; set; }

    // Platform libraries, such as Steam, receive the same encoded photo even if the disk save fails.
    public static event Action<byte[], ImageEncoding>? PhotoTaken;

    public static async Task<string?> SaveAsync(byte[] image, ImageEncoding encoding)
    {
        if (image.Length == 0)
            return null;

        string? saved = null;
        try
        {
            string? pictures = PicturesDirectory;
            if (string.IsNullOrWhiteSpace(pictures))
                pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                    throw new IOException("The Pictures directory could not be located.");
                pictures = Path.Combine(home, "Pictures");
            }
            string folder = Path.GetFullPath(Path.Combine(pictures, "Lumora"));
            // Include an ID so simultaneous shots never overwrite one another.
            string path = Path.Combine(folder, $"Lumora_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}{encoding.Extension()}");
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(folder);
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
                await file.WriteAsync(image).ConfigureAwait(false);
            }).ConfigureAwait(false);
            saved = path;
            Logging.Logger.Log($"Photos: saved to {path}");
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Photos: could not save to Pictures/Lumora: {ex.Message}");
        }

        try { PhotoTaken?.Invoke(image, encoding); }
        catch (Exception ex) { Logging.Logger.Warn($"Photos: screenshot library failed: {ex.Message}"); }
        return saved;
    }
}
