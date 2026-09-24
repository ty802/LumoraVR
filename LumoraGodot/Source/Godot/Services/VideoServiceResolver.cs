// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Lumora.Core.Media;
using LumoraLogger = Lumora.Core.Logging.Logger;
using NetHttpClient = System.Net.Http.HttpClient;
using HttpCompletionOption = System.Net.Http.HttpCompletionOption;

namespace Lumora.Source.Godot.Services;

// Turns a service page (a video site URL) into a direct stream URL by asking yt-dlp. The core never
// sees the process, the JSON or the format list; it asks for a stream and gets one back or a null it
// can report as a failed load.
//
// The binary is looked for beside the executable first, so a packaged build can ship a known-good
// copy, and only downloaded into user://tools when there is nothing there. Sites change constantly,
// so the copy we own updates itself once per process - with a backup, because an interrupted update
// leaves a truncated exe that would break playback for good. -xlinka
public sealed class VideoServiceResolver : IVideoServiceResolver
{
    private const int ProcessTimeoutMs = 60_000;
    private const long MinimumBinaryBytes = 1_000_000;
    private const string LogPrefix = "[yt-dlp]";

    private const string WindowsReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string LinuxReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux";
    private const string DenoWindowsUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
    private const string DenoLinuxUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-unknown-linux-gnu.zip";

    private static readonly NetHttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly SemaphoreSlim BinaryGate = new(1, 1);
    private static bool _updateAttempted;
    private static string[]? _jsRuntimeArguments;

    public async Task<VideoServiceResolution?> ResolveAsync(Uri url, VideoServiceRequest request, CancellationToken cancellationToken)
    {
        if (url == null)
            return null;

        // Off the caller's thread for good: this waits on a child process for up to a minute and the
        // world thread cannot afford a single frame of that.
        return await Task.Run(async () =>
        {
            try
            {
                string? binary = await EnsureBinaryAsync(cancellationToken).ConfigureAwait(false);
                if (binary == null)
                    return null;

                var arguments = new List<string> { "-J", "--no-playlist" };
                foreach (var runtime in await EnsureJsRuntimesAsync(cancellationToken).ConfigureAwait(false))
                {
                    arguments.Add("--js-runtimes");
                    arguments.Add(runtime);
                }
                string? browser = CookieBrowserName(request.Cookies);
                if (browser != null)
                {
                    arguments.Add("--cookies-from-browser");
                    arguments.Add(browser);
                }
                arguments.Add(url.ToString());

                var result = await RunAsync(binary, arguments, ProcessTimeoutMs, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0 || result.StandardOutput.Length == 0)
                {
                    // yt-dlp's own last ERROR line is the message worth showing on the player: "could
                    // not copy the Chrome cookie database" tells someone what to change, an exit code does not.
                    string reason = result.Error ?? $"yt-dlp exit {result.ExitCode}";
                    LumoraLogger.Warn($"{LogPrefix} could not resolve {url}: {reason}");
                    throw new InvalidOperationException(reason);
                }

                return Parse(result.StandardOutput, request);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"{LogPrefix} resolve failed for {url}: {ex.Message}");
                throw new InvalidOperationException(ex.Message);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    // JSON

    private static VideoServiceResolution? Parse(string json, VideoServiceRequest request)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? title = ReadString(root, "title");
        bool isLive = ReadBool(root, "is_live");
        // The Twitch extractor builds its title from the channel name and a timestamp and keeps the
        // real stream title in the description, so that is what a viewer expects to read.
        string? extractor = ReadString(root, "extractor_key") ?? ReadString(root, "extractor");
        if (extractor != null && extractor.Contains("twitch", StringComparison.OrdinalIgnoreCase))
        {
            string? description = ReadString(root, "description");
            if (!string.IsNullOrWhiteSpace(description))
                title = description.Trim();
        }

        var formats = new List<FormatInfo>();
        if (root.TryGetProperty("formats", out var formatArray) && formatArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in formatArray.EnumerateArray())
            {
                var format = ReadFormat(element);
                if (format != null)
                    formats.Add(format);
            }
        }

        if (formats.Count == 0)
        {
            // A single-stream extractor answers with the URL on the root object and no format list.
            string? direct = ReadString(root, "url");
            if (direct == null || !Uri.TryCreate(direct, UriKind.Absolute, out var directUri))
                return null;

            return new VideoServiceResolution(
                directUri,
                title,
                ReadInt(root, "width") > 0,
                true,
                ReadString(root, "vcodec"),
                ReadString(root, "acodec"),
                ReadInt(root, "width"),
                ReadInt(root, "height"),
                isLive);
        }

        // HLS renditions are the first choice: one input carrying picture and sound on one clock, so a
        // seek stays a seek. The service's own master playlist is NOT what gets opened, though. The
        // decoder recognises HLS by scanning the first 512 bytes for a handful of tags, and a master
        // that opens with a dozen 600-byte audio-track lines (YouTube, with its dubs) fails that scan
        // and lands in the raw MPEG-PS demuxer as "garbage". So the chosen video rendition and its
        // audio are written into a compact master of our own, which also pins the codec and size the
        // way the ranking picked them instead of leaving that to the demuxer's bandwidth guess. -xlinka
        if (!request.PreferAudioOnly)
        {
            var master = WriteHlsMaster(formats, request, out var masterVideo, out var masterAudio);
            if (master != null && masterVideo != null)
            {
                LumoraLogger.Log($"{LogPrefix} HLS {masterVideo.Id} ({masterVideo.Width}x{masterVideo.Height} {masterVideo.Vcodec})"
                    + (masterAudio != null ? $" + audio {masterAudio.Id} ({masterAudio.Note})" : string.Empty) + $" for '{title}'");
                return new VideoServiceResolution(
                    master,
                    title,
                    true,
                    masterAudio != null || masterVideo.HasAudio,
                    masterVideo.Vcodec,
                    masterAudio?.Acodec ?? masterVideo.Acodec ?? "hls",
                    masterVideo.Width,
                    masterVideo.Height,
                    isLive);
            }
        }

        var picked = request.PreferAudioOnly ? PickAudioOnly(formats) : PickAudioVideo(formats, request);
        if (picked == null || !Uri.TryCreate(picked.Url, UriKind.Absolute, out var streamUri))
            return null;

        // Muxed renditions are what the ranking prefers, but a service that only splits (YouTube
        // above 360p, and increasingly at 360p too) would otherwise play as a silent picture. The
        // best audio-only rendition rides along as a second stream for the decoder to slave.
        FormatInfo? audio = null;
        Uri? audioUri = null;
        if (picked.HasVideo && !picked.HasAudio)
        {
            var candidate = PickAudioOnly(formats);
            if (candidate != null && candidate.HasAudio && !candidate.HasVideo
                && Uri.TryCreate(candidate.Url, UriKind.Absolute, out var parsed))
            {
                audio = candidate;
                audioUri = parsed;
            }
        }

        LumoraLogger.Log($"{LogPrefix} picked format {picked.Id} ({picked.Width}x{picked.Height} {picked.Vcodec}/{picked.Acodec})"
            + (audio != null ? $" + audio {audio.Id} ({audio.Acodec})" : string.Empty) + $" for '{title}'");

        return new VideoServiceResolution(
            streamUri,
            title,
            picked.HasVideo,
            picked.HasAudio || audio != null,
            picked.Vcodec,
            audio?.Acodec ?? picked.Acodec,
            picked.Width,
            picked.Height,
            isLive,
            audioUri);
    }

    private static FormatInfo? ReadFormat(JsonElement element)
    {
        string? url = ReadString(element, "url");
        if (string.IsNullOrEmpty(url))
            return null;

        return new FormatInfo(
            ReadString(element, "format_id") ?? string.Empty,
            url!,
            ReadString(element, "vcodec"),
            ReadString(element, "acodec"),
            ReadInt(element, "width"),
            ReadInt(element, "height"),
            ReadDouble(element, "abr"),
            ReadDouble(element, "asr"),
            ReadDouble(element, "fps"),
            ReadString(element, "format_note"),
            ReadString(element, "ext"),
            ReadString(element, "protocol"),
            ReadDouble(element, "tbr"));
    }

    // One video rendition inside the caps (the ranking's pick among the HLS ones) and, when that
    // rendition carries no sound of its own, one audio rendition as its audio group. The playlist
    // URLs come from the service and expire with it, so the file is rewritten on every resolve and
    // old ones are swept.
    private static Uri? WriteHlsMaster(List<FormatInfo> formats, VideoServiceRequest request, out FormatInfo? video, out FormatInfo? audio)
    {
        video = null;
        audio = null;

        var hls = formats.Where(f => f.IsHls).ToList();
        if (hls.Count == 0)
            return null;

        video = PickAudioVideo(hls, request);
        if (video == null || !video.HasVideo || !Uri.TryCreate(video.Url, UriKind.Absolute, out _))
            return null;

        if (!video.HasAudio)
        {
            FormatInfo? best = null;
            foreach (var format in hls)
            {
                if (format.HasVideo || !format.HasAudio || !Uri.TryCreate(format.Url, UriKind.Absolute, out _))
                    continue;
                if (best == null || CompareAudio(format, best) > 0)
                    best = format;
            }
            if (best == null)
                return null;
            audio = best;
        }

        var text = new StringBuilder();
        text.Append("#EXTM3U\n#EXT-X-VERSION:6\n#EXT-X-INDEPENDENT-SEGMENTS\n");

        string codecs = string.Join(",", new[] { video.Vcodec, audio?.Acodec ?? video.Acodec }
            .Where(c => !string.IsNullOrEmpty(c) && !string.Equals(c, "none", StringComparison.OrdinalIgnoreCase)));

        if (audio != null)
        {
            string name = (audio.Note ?? "audio").Replace("\"", string.Empty).Replace(",", " ");
            text.Append("#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"").Append(name)
                .Append("\",DEFAULT=YES,AUTOSELECT=YES,URI=\"").Append(audio.Url).Append("\"\n");
        }

        int bandwidth = (int)System.Math.Max(200_000, (video.Tbr > 0 ? video.Tbr : 5000) * 1000);
        text.Append("#EXT-X-STREAM-INF:BANDWIDTH=").Append(bandwidth)
            .Append(",RESOLUTION=").Append(video.Width).Append('x').Append(video.Height);
        if (codecs.Length > 0)
            text.Append(",CODECS=\"").Append(codecs).Append('"');
        if (audio != null)
            text.Append(",AUDIO=\"a\"");
        text.Append('\n').Append(video.Url).Append('\n');

        try
        {
            string dir = ProjectSettings.GlobalizePath("user://Cache/hls");
            Directory.CreateDirectory(dir);
            SweepOldMasters(dir);

            string name;
            using (var sha = SHA1.Create())
                name = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(video.Url))).Substring(0, 16).ToLowerInvariant();
            string path = Path.Combine(dir, name + ".m3u8");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            return new Uri(path);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"{LogPrefix} could not write the HLS master: {ex.Message}");
            video = null;
            audio = null;
            return null;
        }
    }

    private static void SweepOldMasters(string dir)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var file in Directory.EnumerateFiles(dir, "*.m3u8"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Debug($"{LogPrefix} master sweep skipped: {ex.Message}");
        }
    }

    // FORMAT RANKING

    // Best audio: a codec the desktop decoder is certain about, then the highest sample rate. Nothing
    // else about an audio-only pick matters to a world - it is going to come out of one emitter.
    private static FormatInfo? PickAudioOnly(List<FormatInfo> formats)
    {
        FormatInfo? best = null;
        foreach (var format in formats.OrderBy(f => f.Width))
        {
            if (!format.HasAudio || format.HasVideo)
                continue;
            if (best == null || CompareAudio(format, best) > 0)
                best = format;
        }
        // A stream with no audio-only rendition at all still has to play something.
        return best ?? PickAudioVideo(formats, new VideoServiceRequest(null, null, false, CookiesBrowser.None));
    }

    // The "original" note marks the un-dubbed track on a service that offers machine dubs; every
    // other language is a re-voice nobody asked for.
    private static int CompareAudio(FormatInfo a, FormatInfo b)
    {
        int original = (a.IsOriginal ? 1 : 0).CompareTo(b.IsOriginal ? 1 : 0);
        if (original != 0) return original;
        int plain = (a.IsDrc ? 0 : 1).CompareTo(b.IsDrc ? 0 : 1);
        if (plain != 0) return plain;
        int rank = AudioCodecRank(a.Acodec).CompareTo(AudioCodecRank(b.Acodec));
        if (rank != 0) return rank;
        return a.Asr.CompareTo(b.Asr);
    }

    // Only renditions with a picture compete here; a sound-only track is the audio pick's business,
    // and letting one in is how an "original" dub note outranks every video the service has. A muxed
    // rendition beats a video-only one, an "original" note beats a re-encode, and after that it is
    // the biggest picture that still fits the caps. Width-ascending order means an equal score keeps
    // the later (larger) entry without a second sort. -xlinka
    private static FormatInfo? PickAudioVideo(List<FormatInfo> formats, VideoServiceRequest request)
    {
        int maxWidth = request.MaxWidth ?? (request.MaxHeight.HasValue ? int.MaxValue : 1920);
        int maxHeight = request.MaxHeight ?? (request.MaxWidth.HasValue ? int.MaxValue : 1080);

        FormatInfo? best = null;
        FormatInfo? smallest = null;

        foreach (var format in formats.OrderBy(f => f.Width))
        {
            if (!format.HasVideo)
                continue;
            smallest ??= format;

            if (format.Width > maxWidth || format.Height > maxHeight)
                continue;
            if (best == null || CompareVideo(format, best) >= 0)
                best = format;
        }

        // Everything the service offers is bigger than the caps, so take the least oversized one
        // rather than refusing to play the video at all.
        return best ?? smallest ?? formats.FirstOrDefault();
    }

    private static int CompareVideo(FormatInfo a, FormatInfo b)
    {
        int muxed = (a.HasVideo && a.HasAudio ? 1 : 0).CompareTo(b.HasVideo && b.HasAudio ? 1 : 0);
        if (muxed != 0) return muxed;

        int original = (a.IsOriginal ? 1 : 0).CompareTo(b.IsOriginal ? 1 : 0);
        if (original != 0) return original;

        int size = ((long)a.Width * a.Height).CompareTo((long)b.Width * b.Height);
        if (size != 0) return size;

        int codec = VideoCodecRank(a.Vcodec).CompareTo(VideoCodecRank(b.Vcodec));
        if (codec != 0) return codec;

        return a.Abr.CompareTo(b.Abr);
    }

    private static int VideoCodecRank(string? vcodec)
    {
        if (string.IsNullOrEmpty(vcodec)) return 0;
        return vcodec!.StartsWith("avc1", StringComparison.OrdinalIgnoreCase)
            || vcodec.StartsWith("h264", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static int AudioCodecRank(string? acodec)
    {
        if (string.IsNullOrEmpty(acodec)) return 0;
        return acodec!.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    // BINARY

    private static async Task<string?> EnsureBinaryAsync(CancellationToken cancellationToken)
    {
        await BinaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string fileName = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";

            string? shipped = ShippedPath(fileName);
            if (shipped != null && File.Exists(shipped))
                return shipped;

            string toolsDir = ProjectSettings.GlobalizePath("user://tools");
            string owned = Path.Combine(toolsDir, fileName);

            if (!File.Exists(owned))
            {
                if (!await DownloadAsync(toolsDir, owned, cancellationToken).ConfigureAwait(false))
                    return null;
            }

            if (!_updateAttempted)
            {
                _updateAttempted = true;
                await UpdateAsync(owned, cancellationToken).ConfigureAwait(false);
            }

            return owned;
        }
        finally
        {
            BinaryGate.Release();
        }
    }

    // yt-dlp cannot read YouTube without a JavaScript runtime any more: the player's signature
    // challenge is solved by running its script, and without one the extractor is left with the few
    // formats that need no signature, which in practice is one audio track. deno is the runtime it
    // prefers, so deno lives beside yt-dlp the way yt-dlp itself does (shipped in RuntimeData, else
    // fetched once into user://tools). A node or bun already on the machine is offered too, so a
    // failed deno download does not take YouTube down with it. Decided once per process. -xlinka
    private static async Task<string[]> EnsureJsRuntimesAsync(CancellationToken cancellationToken)
    {
        if (_jsRuntimeArguments != null)
            return _jsRuntimeArguments;

        await BinaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_jsRuntimeArguments != null)
                return _jsRuntimeArguments;

            var runtimes = new List<string>();
            string denoName = OperatingSystem.IsWindows() ? "deno.exe" : "deno";

            string? deno = ShippedPath(denoName);
            if (deno == null || !File.Exists(deno))
                deno = FindOnPath(denoName);
            if (deno == null)
            {
                string toolsDir = ProjectSettings.GlobalizePath("user://tools");
                string owned = Path.Combine(toolsDir, denoName);
                if (File.Exists(owned) || await DownloadDenoAsync(toolsDir, owned, cancellationToken).ConfigureAwait(false))
                    deno = owned;
            }
            if (deno != null)
                runtimes.Add("deno:" + deno);

            foreach (var extra in new[] { "node", "bun" })
            {
                string? found = FindOnPath(OperatingSystem.IsWindows() ? extra + ".exe" : extra);
                if (found != null)
                    runtimes.Add(extra + ":" + found);
            }

            if (runtimes.Count == 0)
                LumoraLogger.Warn($"{LogPrefix} no JavaScript runtime available, YouTube will resolve to audio only");
            else
                LumoraLogger.Log($"{LogPrefix} JavaScript runtimes: {string.Join(", ", runtimes)}");

            _jsRuntimeArguments = runtimes.ToArray();
            return _jsRuntimeArguments;
        }
        finally
        {
            BinaryGate.Release();
        }
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        foreach (var dir in path!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    private static async Task<bool> DownloadDenoAsync(string toolsDir, string target, CancellationToken cancellationToken)
    {
        string url = OperatingSystem.IsWindows() ? DenoWindowsUrl : DenoLinuxUrl;
        string archivePath = target + ".zip.part";
        try
        {
            Directory.CreateDirectory(toolsDir);
            LumoraLogger.Log($"{LogPrefix} downloading {url}");

            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using var file = File.Create(archivePath);
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            string wanted = Path.GetFileName(target);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    LumoraLogger.Warn($"{LogPrefix} {wanted} is not in the deno archive");
                    return false;
                }
                string temp = target + ".part";
                entry.ExtractToFile(temp, true);
                File.Move(temp, target, true);
            }
            MakeExecutable(target);

            LumoraLogger.Log($"{LogPrefix} installed {target} ({new FileInfo(target).Length} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"{LogPrefix} deno download failed: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); }
            catch (Exception ex) { LumoraLogger.Debug($"{LogPrefix} archive cleanup failed: {ex.Message}"); }
        }
    }

    private static string? ShippedPath(string fileName)
    {
        try
        {
            string? exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
            if (string.IsNullOrEmpty(exeDir))
                return null;
            return Path.Combine(exeDir!, "RuntimeData", fileName);
        }
        catch (Exception ex)
        {
            LumoraLogger.Debug($"{LogPrefix} executable path unavailable: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> DownloadAsync(string toolsDir, string target, CancellationToken cancellationToken)
    {
        string releaseUrl = OperatingSystem.IsWindows() ? WindowsReleaseUrl : LinuxReleaseUrl;
        try
        {
            Directory.CreateDirectory(toolsDir);
            LumoraLogger.Log($"{LogPrefix} downloading {releaseUrl}");

            using var response = await Http.GetAsync(releaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Written beside the target and moved into place, so a dropped connection can never leave
            // a half-downloaded binary that every later run would try to execute.
            string temp = target + ".part";
            using (var file = File.Create(temp))
            {
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, target, true);
            MakeExecutable(target);

            LumoraLogger.Log($"{LogPrefix} installed {target} ({new FileInfo(target).Length} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"{LogPrefix} download failed: {ex.Message}");
            return false;
        }
    }

    private static async Task UpdateAsync(string binary, CancellationToken cancellationToken)
    {
        string backup = binary + ".bak";
        try
        {
            File.Copy(binary, backup, true);
            var result = await RunAsync(binary, new List<string> { "--update-to", "nightly" }, ProcessTimeoutMs, cancellationToken).ConfigureAwait(false);
            LumoraLogger.Log($"{LogPrefix} update exit {result.ExitCode}");

            long size = new FileInfo(binary).Length;
            if (size < MinimumBinaryBytes)
            {
                // An update that replaced the binary with something too small to be yt-dlp took the
                // player's ability to watch anything with it; the backup is the only way back.
                LumoraLogger.Warn($"{LogPrefix} update left a {size} byte binary, restoring the backup");
                File.Copy(backup, binary, true);
                MakeExecutable(binary);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"{LogPrefix} update failed: {ex.Message}");
            try
            {
                if (File.Exists(backup))
                {
                    File.Copy(backup, binary, true);
                    MakeExecutable(binary);
                }
            }
            catch (Exception restore)
            {
                LumoraLogger.Error($"{LogPrefix} could not restore the backup: {restore.Message}");
            }
        }
        finally
        {
            try { if (File.Exists(backup)) File.Delete(backup); }
            catch (Exception ex) { LumoraLogger.Debug($"{LogPrefix} backup cleanup failed: {ex.Message}"); }
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"{LogPrefix} could not mark {path} executable: {ex.Message}");
        }
    }

    // PROCESS

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string? Error);

    private static async Task<ProcessResult> RunAsync(string binary, List<string> arguments, int timeoutMs, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var output = new StringBuilder();
        string? lastError = null;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            LumoraLogger.Warn($"{LogPrefix} {e.Data}");
            if (e.Data.StartsWith("ERROR:", StringComparison.Ordinal))
                lastError = e.Data.Substring(6).Trim();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LumoraLogger.Warn($"{LogPrefix} timed out after {timeoutMs} ms, killing the process");
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) { LumoraLogger.Debug($"{LogPrefix} kill failed: {ex.Message}"); }
            return new ProcessResult(-1, string.Empty, $"yt-dlp did not answer within {timeoutMs / 1000} s");
        }

        return new ProcessResult(process.ExitCode, output.ToString(), lastError);
    }

    // JSON HELPERS

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : 0;

    private static double ReadDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result) ? result : 0.0;

    private static string? CookieBrowserName(CookiesBrowser browser) => browser switch
    {
        CookiesBrowser.Chrome => "chrome",
        CookiesBrowser.Firefox => "firefox",
        CookiesBrowser.Edge => "edge",
        CookiesBrowser.Brave => "brave",
        CookiesBrowser.Chromium => "chromium",
        CookiesBrowser.Opera => "opera",
        CookiesBrowser.Safari => "safari",
        CookiesBrowser.Vivaldi => "vivaldi",
        _ => null,
    };

    private sealed record FormatInfo(
        string Id,
        string Url,
        string? Vcodec,
        string? Acodec,
        int Width,
        int Height,
        double Abr,
        double Asr,
        double Fps,
        string? Note,
        string? Ext,
        string? Protocol,
        double Tbr)
    {
        public bool HasVideo => !IsNone(Vcodec) && Width > 0;
        // An HLS sound-only rendition is listed with its video codec as "none" and no audio codec at
        // all, so an unknown codec on a picture-less HLS entry still means "this is the sound".
        public bool HasAudio => Acodec != null
            ? !IsNone(Acodec)
            : IsHls && string.Equals(Vcodec, "none", StringComparison.OrdinalIgnoreCase);
        public bool IsOriginal => Note != null && Note.Contains("original", StringComparison.OrdinalIgnoreCase);
        public bool IsHls => Protocol != null && Protocol.StartsWith("m3u8", StringComparison.OrdinalIgnoreCase);
        // A dynamic-range-compressed twin of a track; the plain one is the one to play.
        public bool IsDrc => Id.EndsWith("-drc", StringComparison.OrdinalIgnoreCase)
            || (Note != null && Note.Contains("DRC", StringComparison.Ordinal));

        private static bool IsNone(string? codec)
            => string.IsNullOrEmpty(codec) || string.Equals(codec, "none", StringComparison.OrdinalIgnoreCase);
    }
}
