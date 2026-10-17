// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Source.Godot.Bootstrap;

// One client owns a data directory. URI launches hand their activation to that client before opening
// databases or starting the Lumora bootstrap. Separate LUMORA_DATA_DIR profiles can run independently.
public sealed class DiscordLaunchRelay : IDisposable
{
    private const int MaxMessageBytes = 4096;
    private readonly FileStream _lock;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly Task _listen;

    private DiscordLaunchRelay(FileStream fileLock, string pipeName)
    {
        _lock = fileLock;
        _listen = Task.Run(() => ListenAsync(pipeName));
    }

    // Null means an existing client acknowledged the handoff. A failed handoff throws: starting a
    // second client against the same data directory would lose the invite and open the stores twice.
    public static DiscordLaunchRelay? Start(string dataDirectory, string activation)
    {
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        Directory.CreateDirectory(directory);
        string identity = OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\n" + identity)));
        string pipe = "Lumora.Discord." + hash[..24];
        string lockPath = Path.Combine(directory, ".discord-launch.lock");
        FileStream? TryLock()
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return null; }
        }
        var file = TryLock();
        if (file != null)
            return new DiscordLaunchRelay(file, pipe);
        try
        {
            ForwardAsync(pipe, activation).GetAwaiter().GetResult();
            return null;
        }
        catch
        {
            // The previous process may have exited between the lock check and the pipe connection.
            file = TryLock();
            if (file != null)
                return new DiscordLaunchRelay(file, pipe);
            throw;
        }
    }

    public bool TryRead(out string activation) => _messages.TryDequeue(out activation!);

    private static async Task ForwardAsync(string name, string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length > MaxMessageBytes)
            throw new ArgumentException("Discord launch URI is too long.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await pipe.WriteAsync(length, timeout.Token).ConfigureAwait(false);
        await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        byte[] ack = new byte[1];
        await pipe.ReadExactlyAsync(ack, timeout.Token).ConfigureAwait(false);
        if (ack[0] != 1)
            throw new IOException("The running client did not accept the Discord launch.");
    }

    private async Task ListenAsync(string name)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                byte[] header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length < 0 || length > MaxMessageBytes)
                    continue;
                byte[] bytes = new byte[length];
                await pipe.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
                _messages.Enqueue(Encoding.UTF8.GetString(bytes));
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException)
            {
                if (!_stop.IsCancellationRequested)
                    await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listen.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _lock.Dispose();
        _stop.Dispose();
    }
}
