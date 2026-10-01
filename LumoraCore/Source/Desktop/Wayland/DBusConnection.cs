// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Desktop;

internal sealed class DBusMessage
{
    public const byte MethodCall = 1, MethodReturn = 2, Error = 3, Signal = 4;

    public byte Type;
    public uint Serial;
    public uint ReplySerial;
    public string? Path, Interface, Member, ErrorName, Sender, Signature;
    public object[] Body = Array.Empty<object>();
    public int[] Fds = Array.Empty<int>();

    public string ErrorText => Body.Length > 0 && Body[0] is string text ? $"{ErrorName}: {text}" : ErrorName ?? "D-Bus error";
}

// Just enough of a D-Bus client for the desktop portal: the session bus over its Unix socket, EXTERNAL
// authentication, method calls that wait for their reply, signal handlers, and file descriptors received
// alongside messages (the portal hands PipeWire over as one).
//
// Fds only travel as socket ancillary data, which the managed Socket does not surface, so every read goes
// through recvmsg and the fds it brings are queued for the message that claims them. -xlinka
[SupportedOSPlatform("linux")]
internal sealed unsafe class DBusConnection : IDisposable
{
    private const byte NoReplyExpected = 1;
    private const int SolSocket = 1;
    private const int ScmRights = 1;
    // Fds that arrive are not handed down to child processes: a video download spawned later would
    // otherwise keep a screen share alive in its pocket.
    private const int CloseOnExec = 0x40000000;
    private const int Interrupted = 4, TryAgain = 11;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    private readonly Socket _socket;
    private readonly object _sendGate = new();
    private readonly object _gate = new();
    private readonly Dictionary<uint, (ManualResetEventSlim Done, DBusMessage? Reply)> _pending = new();
    private readonly List<(string Path, string Interface, string Member, string? Sender, Action<DBusMessage> Handler)> _signals = new();
    private readonly ManualResetEventSlim _gone = new(false);
    private readonly Queue<int> _fds = new();
    private readonly Thread _reader;
    private int _serial;
    private volatile bool _closed;

    public string UniqueName { get; private set; } = string.Empty;

    // Set once the connection is gone, so a wait on an answer can stop waiting for one that cannot come.
    public WaitHandle Gone => _gone.WaitHandle;

    private DBusConnection(Socket socket)
    {
        _socket = socket;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "D-Bus reader" };
    }

    public static DBusConnection? OpenSession()
    {
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(runtime))
                return null;
            address = "unix:path=" + runtime + "/bus";
        }
        foreach (var candidate in address.Split(';'))
        {
            var endpoint = EndpointOf(candidate);
            if (endpoint == null)
                continue;
            Socket? socket = null;
            try
            {
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(endpoint);
                Authenticate(socket);
                var connection = new DBusConnection(socket);
                connection._reader.Start();
                var hello = connection.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", "");
                connection.UniqueName = hello.Body.Length > 0 ? (string)hello.Body[0] : string.Empty;
                return connection;
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"DBusConnection: {candidate}: {ex.Message}");
                socket?.Dispose();
            }
        }
        return null;
    }

    private static UnixDomainSocketEndPoint? EndpointOf(string address)
    {
        if (!address.StartsWith("unix:", StringComparison.Ordinal))
            return null;
        foreach (var part in address[5..].Split(','))
        {
            int equals = part.IndexOf('=');
            if (equals < 0)
                continue;
            string key = part[..equals], value = Unescape(part[(equals + 1)..]);
            if (key == "path")
                return new UnixDomainSocketEndPoint(value);
            if (key == "abstract")
                return new UnixDomainSocketEndPoint("\0" + value);
        }
        return null;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('%'))
            return value;
        var bytes = new List<byte>();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length)
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)value[i]);
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void Authenticate(Socket socket)
    {
        var uid = Encoding.ASCII.GetBytes(getuid().ToString(System.Globalization.CultureInfo.InvariantCulture));
        socket.Send(new byte[] { 0 });
        SendLine(socket, "AUTH EXTERNAL " + Convert.ToHexString(uid).ToLowerInvariant());
        string reply = ReadLine(socket);
        if (!reply.StartsWith("OK", StringComparison.Ordinal))
            throw new InvalidOperationException("the session bus refused us: " + reply);
        SendLine(socket, "NEGOTIATE_UNIX_FD");
        reply = ReadLine(socket);
        if (!reply.StartsWith("AGREE_UNIX_FD", StringComparison.Ordinal))
            throw new InvalidOperationException("the session bus will not pass file descriptors: " + reply);
        SendLine(socket, "BEGIN");
    }

    private static void SendLine(Socket socket, string line) => socket.Send(Encoding.ASCII.GetBytes(line + "\r\n"));

    private static string ReadLine(Socket socket)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (socket.Receive(one) == 1)
        {
            if (one[0] == '\n')
                break;
            if (one[0] != '\r')
                line.Append((char)one[0]);
        }
        return line.ToString();
    }

    // CALLS

    public DBusMessage Call(string destination, string path, string iface, string member, string signature, params object[] args)
    {
        var reply = TryCall(destination, path, iface, member, signature, args);
        if (reply.Type == DBusMessage.Error)
            throw new InvalidOperationException(reply.ErrorText);
        return reply;
    }

    public DBusMessage TryCall(string destination, string path, string iface, string member, string signature, params object[] args)
    {
        uint serial = (uint)Interlocked.Increment(ref _serial);
        var done = new ManualResetEventSlim(false);
        lock (_gate)
            _pending[serial] = (done, null);
        Send(Build(DBusMessage.MethodCall, serial, destination, path, iface, member, signature, args));
        bool answered = done.Wait(CallTimeout);
        lock (_gate)
        {
            var entry = _pending[serial];
            _pending.Remove(serial);
            if (!answered || entry.Reply == null)
                throw new TimeoutException($"{iface}.{member} got no answer");
            return entry.Reply;
        }
    }

    // For calls sent many times a second whose answer nobody needs: the bus is told not to send one.
    public void CallNoReply(string destination, string path, string iface, string member, string signature, params object[] args)
    {
        uint serial = (uint)Interlocked.Increment(ref _serial);
        var message = Build(DBusMessage.MethodCall, serial, destination, path, iface, member, signature, args);
        message[2] = NoReplyExpected;
        Send(message);
    }

    // Handlers run on the reader thread. The returned object removes the handler. With a sender, only
    // signals from that bus name are matched: the bus filters on the well-known name, and the handler
    // only runs for the unique name that owns it, so no other program on the bus can speak for it.
    public IDisposable OnSignal(string path, string iface, string member, Action<DBusMessage> handler, string? sender = null, string? senderUnique = null)
    {
        var entry = (path, iface, member, senderUnique, handler);
        lock (_gate)
            _signals.Add(entry);
        string from = sender != null ? $",sender='{sender}'" : string.Empty;
        Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "AddMatch", "s",
            $"type='signal'{from},interface='{iface}',member='{member}',path='{path}'");
        return new Unsubscribe(() =>
        {
            lock (_gate)
                _signals.Remove(entry);
        });
    }

    private sealed class Unsubscribe : IDisposable
    {
        private Action? _action;
        public Unsubscribe(Action action) => _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }

    private static byte[] Build(byte type, uint serial, string destination, string path, string iface, string member, string signature, object[] args)
    {
        var body = new DBusWriter();
        if (signature.Length > 0)
            body.WriteAll(signature, args);
        var bodyBytes = body.ToArray();

        var message = new DBusWriter();
        message.WriteByte((byte)'l');
        message.WriteByte(type);
        message.WriteByte(0);
        message.WriteByte(1);
        message.WriteUInt32((uint)bodyBytes.Length);
        message.WriteUInt32(serial);
        var fields = new List<object>
        {
            new object[] { (byte)1, new DBusVariant("o", new DBusObjectPath(path)) },
            new object[] { (byte)2, new DBusVariant("s", iface) },
            new object[] { (byte)3, new DBusVariant("s", member) },
            new object[] { (byte)6, new DBusVariant("s", destination) },
        };
        if (signature.Length > 0)
            fields.Add(new object[] { (byte)8, new DBusVariant("g", signature) });
        message.Write("a(yv)", fields);
        message.Align(8);
        var header = message.ToArray();
        var whole = new byte[header.Length + bodyBytes.Length];
        header.CopyTo(whole, 0);
        bodyBytes.CopyTo(whole, header.Length);
        return whole;
    }

    private void Send(byte[] message)
    {
        lock (_sendGate)
        {
            int sent = 0;
            while (sent < message.Length)
                sent += _socket.Send(message, sent, message.Length - sent, SocketFlags.None);
        }
    }

    // READING

    private void ReadLoop()
    {
        var buffer = new byte[1 << 16];
        int filled = 0;
        try
        {
            while (!_closed)
            {
                if (filled == buffer.Length)
                    Array.Resize(ref buffer, buffer.Length * 2);
                int read = Receive(buffer, filled);
                if (read <= 0)
                    break;
                filled += read;
                int used;
                while ((used = TryParse(buffer, filled)) > 0)
                {
                    Buffer.BlockCopy(buffer, used, buffer, 0, filled - used);
                    filled -= used;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_closed)
                LumoraLogger.Warn($"DBusConnection: reader stopped: {ex.Message}");
        }
        _closed = true;
        lock (_gate)
        {
            foreach (var entry in _pending.Values)
                entry.Done.Set();
        }
        _gone.Set();
    }

    private int Receive(byte[] buffer, int offset)
    {
        fixed (byte* data = &buffer[offset])
        {
            var control = stackalloc byte[256];
            var io = new IoVector { Base = data, Length = (nuint)(buffer.Length - offset) };
            var header = new MessageHeader { Io = &io, IoCount = 1, Control = control, ControlLength = 256 };
            nint read;
            do
            {
                read = recvmsg((int)_socket.Handle, &header, CloseOnExec);
            }
            while (read < 0 && Marshal.GetLastPInvokeError() is Interrupted or TryAgain && !_closed);
            if (read <= 0)
                return (int)read;
            byte* at = control;
            byte* end = control + (long)header.ControlLength;
            while (at + sizeof(ControlHeader) <= end)
            {
                var c = (ControlHeader*)at;
                if (c->Length < (nuint)sizeof(ControlHeader))
                    break;
                if (c->Level == SolSocket && c->Type == ScmRights)
                {
                    int count = (int)((c->Length - (nuint)sizeof(ControlHeader)) / 4);
                    int* fds = (int*)(at + sizeof(ControlHeader));
                    lock (_gate)
                    {
                        for (int i = 0; i < count; i++)
                            _fds.Enqueue(fds[i]);
                    }
                }
                at += (c->Length + 7) & ~(nuint)7;
            }
            return (int)read;
        }
    }

    // Returns how many bytes the first whole message in the buffer used, or 0 for not yet a whole one.
    private int TryParse(byte[] data, int length)
    {
        if (length < 16)
            return 0;
        if (data[0] != 'l')
            throw new NotSupportedException("big-endian D-Bus messages are not read here");
        uint bodyLength = BitConverter.ToUInt32(data, 4);
        uint fieldsLength = BitConverter.ToUInt32(data, 12);
        int headerEnd = (int)((16 + fieldsLength + 7) & ~7u);
        int total = headerEnd + (int)bodyLength;
        if (length < total)
            return 0;

        var message = new DBusMessage { Type = data[1], Serial = BitConverter.ToUInt32(data, 8) };
        var reader = new DBusReader(data, 12);
        var fields = (List<object>)reader.Read("a(yv)");
        uint fdCount = 0;
        foreach (object[] field in fields)
        {
            switch ((byte)field[0])
            {
                case 1: message.Path = (string)field[1]; break;
                case 2: message.Interface = (string)field[1]; break;
                case 3: message.Member = (string)field[1]; break;
                case 4: message.ErrorName = (string)field[1]; break;
                case 5: message.ReplySerial = (uint)field[1]; break;
                case 7: message.Sender = (string)field[1]; break;
                case 8: message.Signature = (string)field[1]; break;
                case 9: fdCount = (uint)field[1]; break;
            }
        }
        if (!string.IsNullOrEmpty(message.Signature) && bodyLength > 0)
            message.Body = new DBusReader(data, headerEnd).ReadAll(message.Signature, total);
        if (fdCount > 0)
        {
            var fds = new int[fdCount];
            lock (_gate)
            {
                for (int i = 0; i < fds.Length; i++)
                    fds[i] = _fds.Count > 0 ? _fds.Dequeue() : -1;
            }
            message.Fds = fds;
        }
        Dispatch(message);
        return total;
    }

    private void Dispatch(DBusMessage message)
    {
        if (message.Type is DBusMessage.MethodReturn or DBusMessage.Error)
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(message.ReplySerial, out var entry))
                {
                    _pending[message.ReplySerial] = (entry.Done, message);
                    entry.Done.Set();
                    return;
                }
            }
            CloseFds(message);
            return;
        }
        if (message.Type != DBusMessage.Signal)
            return;
        Action<DBusMessage>[] handlers;
        lock (_gate)
        {
            handlers = _signals.FindAll(s => s.Path == message.Path && s.Interface == message.Interface && s.Member == message.Member
                    && (s.Sender == null || s.Sender == message.Sender))
                .ConvertAll(s => s.Handler).ToArray();
        }
        foreach (var handler in handlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"DBusConnection: {message.Interface}.{message.Member} handler failed: {ex.Message}");
            }
        }
    }

    private static void CloseFds(DBusMessage message)
    {
        foreach (int fd in message.Fds)
        {
            if (fd >= 0)
                close(fd);
        }
    }

    // The reader is woken by the shutdown and waited out before the socket goes, so it can never read
    // from a number the system has handed to something else in between.
    public void Dispose()
    {
        _closed = true;
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        if (Thread.CurrentThread != _reader && _reader.IsAlive)
            _reader.Join(1000);
        _socket.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoVector
    {
        public byte* Base;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MessageHeader
    {
        public void* Name;
        public uint NameLength;
        public IoVector* Io;
        public nuint IoCount;
        public void* Control;
        public nuint ControlLength;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControlHeader
    {
        public nuint Length;
        public int Level;
        public int Type;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern nint recvmsg(int socket, MessageHeader* message, int flags);

    [DllImport("libc")]
    private static extern uint getuid();

    [DllImport("libc")]
    public static extern int close(int fd);
}
