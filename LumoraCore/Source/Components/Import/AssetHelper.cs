// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;

namespace Lumora.Core.Components.Import;

public static class AssetHelper
{
    private static readonly Dictionary<string, AssetClass> _byExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // Texture
        { ".png", AssetClass.Texture }, { ".jpg", AssetClass.Texture }, { ".jpeg", AssetClass.Texture },
        { ".webp", AssetClass.Texture }, { ".bmp", AssetClass.Texture }, { ".gif", AssetClass.Texture },
        { ".tga", AssetClass.Texture }, { ".tif", AssetClass.Texture }, { ".tiff", AssetClass.Texture },
        { ".exr", AssetClass.Texture }, { ".hdr", AssetClass.Texture }, { ".psd", AssetClass.Texture },

        // Model
        { ".obj", AssetClass.Model }, { ".fbx", AssetClass.Model }, { ".glb", AssetClass.Model },
        { ".gltf", AssetClass.Model }, { ".dae", AssetClass.Model }, { ".blend", AssetClass.Model },
        { ".stl", AssetClass.Model }, { ".3ds", AssetClass.Model }, { ".ply", AssetClass.Model },
        { ".x3d", AssetClass.Model },

        // PointCloud
        { ".pts", AssetClass.PointCloud }, { ".xyz", AssetClass.PointCloud }, { ".pcd", AssetClass.PointCloud },

        // Video
        { ".mp4", AssetClass.Video }, { ".mov", AssetClass.Video }, { ".avi", AssetClass.Video },
        { ".webm", AssetClass.Video }, { ".mkv", AssetClass.Video }, { ".m4v", AssetClass.Video },
        { ".wmv", AssetClass.Video }, { ".flv", AssetClass.Video }, { ".ts", AssetClass.Video },
        { ".ogv", AssetClass.Video },
        // Manifests, not containers: what a live or adaptive stream actually hands out.
        { ".m3u8", AssetClass.Video }, { ".mpd", AssetClass.Video },

        // Audio
        { ".mp3", AssetClass.Audio }, { ".wav", AssetClass.Audio }, { ".wave", AssetClass.Audio },
        { ".ogg", AssetClass.Audio }, { ".oga", AssetClass.Audio },
        { ".flac", AssetClass.Audio }, { ".m4a", AssetClass.Audio }, { ".aac", AssetClass.Audio },
        { ".opus", AssetClass.Audio }, { ".aiff", AssetClass.Audio },

        // Font
        { ".ttf", AssetClass.Font }, { ".otf", AssetClass.Font },
        { ".woff", AssetClass.Font }, { ".woff2", AssetClass.Font },

        // Subtitle
        { ".srt", AssetClass.Subtitle }, { ".vtt", AssetClass.Subtitle }, { ".ass", AssetClass.Subtitle },
        { ".ssa", AssetClass.Subtitle },

        // Animation
        { ".bvh", AssetClass.Animation },

        // Lumora object / scene
        { ".lumora", AssetClass.Object }, { ".lumoraobj", AssetClass.Object },
        { ".lumoraworld", AssetClass.Object },

        // Text
        { ".txt", AssetClass.Text }, { ".md", AssetClass.Text }, { ".rst", AssetClass.Text },
        { ".log", AssetClass.Text }, { ".csv", AssetClass.Text }, { ".tsv", AssetClass.Text },
        { ".json", AssetClass.Text }, { ".xml", AssetClass.Text }, { ".yaml", AssetClass.Text },
        { ".yml", AssetClass.Text }, { ".toml", AssetClass.Text }, { ".ini", AssetClass.Text },

        // Document
        { ".pdf", AssetClass.Document }, { ".doc", AssetClass.Document }, { ".docx", AssetClass.Document },
        { ".rtf", AssetClass.Document }, { ".odt", AssetClass.Document }, { ".epub", AssetClass.Document },

        // Volume / cubemap
        { ".cube", AssetClass.Volume },

        // Shared object packages. Self-contained archives: every mesh, texture and clip the object
        // references travels inside the file, so importing one never needs the network.
        { ".resonitepackage", AssetClass.Package },

        // Unity packages: a project folder's worth of models, materials and textures, read by its own
        // dialog because what comes across is decided per model, not per file.
        { ".unitypackage", AssetClass.Package },

        // Shader
        { ".gdshader", AssetClass.Shader }, { ".lumshader", AssetClass.Shader }, { ".glsl", AssetClass.Shader },
        { ".hlsl", AssetClass.Shader }, { ".shader", AssetClass.Shader },
    };

    // Protocols that only ever carry live media. There is no file behind one of these, so a URL using
    // one is a video whatever its path looks like. -xlinka
    private static readonly HashSet<string> _streamingSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rtp", "mms", "rtsp", "rtmp", "srt", "udp",
    };

    public static bool IsStreamingProtocol(Uri? uri)
        => uri != null && uri.IsAbsoluteUri && _streamingSchemes.Contains(uri.Scheme);

    // Pages that hold a video behind a player rather than serving one. The address of the page is not
    // the address of the stream, so these have to go through the service resolver before anything can
    // play them. Matched on the host so every regional domain of a service lands in the same bucket.
    public static bool IsVideoStreamingService(Uri? uri)
    {
        if (uri == null || !uri.IsAbsoluteUri)
            return false;

        string host = uri.Host;
        if (string.IsNullOrEmpty(host))
            return false;

        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host.Substring(4);
        if (host.StartsWith("m.", StringComparison.OrdinalIgnoreCase))
            host = host.Substring(2);

        if (host.StartsWith("youtube.", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("vimeo.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("twitch.tv", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("twitter.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("x.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("soundcloud.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("reddit.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("dropbox.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("mixer.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("dailymotion.", StringComparison.OrdinalIgnoreCase))
            return true;

        // Only a post carries media there; the rest of the site is text the resolver would choke on.
        return host.StartsWith("bsky.app", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Contains("post", StringComparison.OrdinalIgnoreCase);
    }

    public static AssetClass IdentifyClass(string path)
    {
        if (string.IsNullOrEmpty(path)) return AssetClass.Unknown;
        try
        {
            if (Directory.Exists(path)) return AssetClass.Folder;
        }
        catch { }

        // A URL is not a file path and Path.GetExtension does not know that: a query string lands in
        // the extension, so "clip.mp4?token=..." used to identify as nothing at all.
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (IsStreamingProtocol(uri))
                return AssetClass.Video;

            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                if (IsVideoStreamingService(uri))
                    return AssetClass.Video;
                if (HasVideoExtension(uri.AbsolutePath) || QueryHasVideoExtension(uri))
                    return AssetClass.Video;
            }
        }

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return AssetClass.Unknown;
        return _byExtension.TryGetValue(ext, out var c) ? c : AssetClass.Unknown;
    }

    // A direct link handed out by a CDN often names the real file in a parameter rather than in the
    // path, and the path itself is an opaque id.
    private static bool QueryHasVideoExtension(Uri uri)
    {
        string query = uri.Query;
        if (string.IsNullOrEmpty(query))
            return false;

        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            int equals = pair.IndexOf('=');
            if (equals < 0 || equals == pair.Length - 1)
                continue;
            string value;
            try
            {
                value = Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
            catch (UriFormatException)
            {
                continue;
            }
            if (HasVideoExtension(value))
                return true;
        }
        return false;
    }

    private static bool HasVideoExtension(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        string ext;
        try
        {
            ext = Path.GetExtension(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return !string.IsNullOrEmpty(ext)
            && _byExtension.TryGetValue(ext, out var c)
            && c == AssetClass.Video;
    }
}
