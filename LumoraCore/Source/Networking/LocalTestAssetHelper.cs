// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;

namespace Lumora.Core.Networking;

public static class LocalTestAssetHelper
{
    private const bool EnableLocalTest = true; //DISABLE THIS IN PROD

    private const string LocalTestSchema = "localtest://";

    public static bool ValidPath(string path)
    {
        if (!EnableLocalTest) return false;
        if (!path.StartsWith(LocalTestSchema)) return false;
        return true;
    }

    public static byte[] GetLocalTestAssetData(string path)
    {
        if (!ValidPath(path))
            return null;

        string relativePath = path.Substring(LocalTestSchema.Length);
        string fullPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, relativePath);

        if (!File.Exists(fullPath))
        {
            Logging.Logger.Warn($"LocalTestAssetHelper: File not found at '{fullPath}'");
            return null;
        }

        return File.ReadAllBytes(fullPath);
    }
}
