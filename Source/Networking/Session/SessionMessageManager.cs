using System;
using System.IO;
using Aquamarine.Source.Networking.Messages;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Networking.Session;

/// <summary>
/// Manages incoming and outgoing messages for a session.
/// Simplified version of the session's SessionMessageManager.
/// </summary>
public class SessionMessageManager : IDisposable
{
    public Session Session { get; private set; }

    // Statistics
    public int TotalReceivedDeltas { get; private set; }
    public int TotalReceivedFulls { get; private set; }
    public int TotalReceivedStreams { get; private set; }
    public int TotalSentDeltas { get; private set; }
    public int TotalSentFulls { get; private set; }
    public int TotalSentStreams { get; private set; }

    public SessionMessageManager(Session session)
    {
        Session = session;
    }

    /// <summary>
    /// Process incoming raw data from a connection.
    /// </summary>
    public void ProcessIncomingData(IConnection connection, byte[] data, int length)
    {
        try
        {
            using var ms = new MemoryStream(data, 0, length);
            using var reader = new BinaryReader(ms);

            // Read message type
            MessageType messageType = (MessageType)reader.ReadByte();

            switch (messageType)
            {
                case MessageType.Delta:
                    TotalReceivedDeltas++;
                    var delta = DeltaBatch.Decode(reader);
                    Session.Sync?.ProcessDeltaBatch(connection, delta);
                    break;

                case MessageType.Full:
                    TotalReceivedFulls++;
                    var full = FullBatch.Decode(reader);
                    Session.Sync?.ProcessFullBatch(connection, full);
                    break;

                case MessageType.Stream:
                    TotalReceivedStreams++;
                    var stream = StreamMessage.Decode(reader);
                    Session.Sync?.ProcessStreamMessage(connection, stream);
                    break;

                case MessageType.Control:
                    var control = ControlMessage.Decode(reader);
                    ProcessControlMessage(connection, control);
                    break;

                case MessageType.Confirmation:
                    var confirmation = ConfirmationMessage.Decode(reader);
                    Session.Sync?.ProcessConfirmation(connection, confirmation);
                    break;

                default:
                    AquaLogger.Warn($"Unknown message type: {messageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            AquaLogger.Error($"Failed to process incoming data: {ex.Message}");
        }
    }

    private void ProcessControlMessage(IConnection connection, ControlMessage message)
    {
        switch (message.SubType)
        {
            case ControlMessageType.JoinRequest:
                AquaLogger.Log("Received JoinRequest");
                // Authority should handle this in SessionConnectionManager
                break;

            case ControlMessageType.JoinGrant:
                AquaLogger.Log("Received JoinGrant");
                ProcessJoinGrant(message);
                break;

            case ControlMessageType.Leave:
                AquaLogger.Log($"User leaving: {connection.Identifier}");
                break;

            default:
                AquaLogger.Warn($"Unhandled control message: {message.SubType}");
                break;
        }
    }

    private void ProcessJoinGrant(ControlMessage message)
    {
        var grantData = JoinGrantData.Decode(message.Data);

        AquaLogger.Log($"Received JoinGrant - UserID: {grantData.AssignedUserID}");

        // Create local user
        var localUser = new Core.User(Session.World, grantData.AssignedUserID);
        localUser.UserID.Value = grantData.AssignedUserID.ToString();
        localUser.AllocationIDStart.Value = grantData.AllocationIDStart;
        localUser.AllocationIDEnd.Value = grantData.AllocationIDEnd;

        Session.World.SetLocalUser(localUser);

        // Update world state version from authority
        Session.World.SetStateVersion(grantData.StateVersion);

        // Notify world that join grant was received
        Session.World.OnJoinGrantReceived();
    }

    /// <summary>
    /// Send a message to a specific connection.
    /// </summary>
    public void SendToConnection(IConnection connection, byte[] data, bool reliable)
    {
        try
        {
            connection.Send(data, data.Length, reliable, background: false);
        }
        catch (Exception ex)
        {
            AquaLogger.Error($"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a message to a connection (alias for SendToConnection).
    /// </summary>
    public void SendMessage(IConnection connection, byte[] data, bool reliable)
    {
        SendToConnection(connection, data, reliable);
    }

    /// <summary>
    /// Broadcast a message to all connections.
    /// </summary>
    public void BroadcastMessage(byte[] data, bool reliable)
    {
        var connections = Session.Connections.GetAllConnections();
        foreach (var connection in connections)
        {
            SendToConnection(connection, data, reliable);
        }
    }

    /// <summary>
    /// Broadcast a message to all connections except one.
    /// </summary>
    public void BroadcastMessageExcept(IConnection exceptConnection, byte[] data, bool reliable)
    {
        var connections = Session.Connections.GetAllConnections();
        foreach (var connection in connections)
        {
            if (connection != exceptConnection)
            {
                SendToConnection(connection, data, reliable);
            }
        }
    }

    public void Dispose()
    {
        Session = null;
    }
}
