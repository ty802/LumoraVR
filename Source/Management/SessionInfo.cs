using System.Net;
using LiteNetLib;
using Aquamarine.Source.Logging;

namespace Aquamarine.Source.Management;

public class SessionInfo
{
    static SessionInfo()
    {
        try
        {
            SessionServer = NetUtils.MakeEndPoint("backend.lumoravr.com", 8000);
            RelayServer = NetUtils.MakeEndPoint("backend.lumoravr.com", 8090);
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"Failed to resolve session endpoints, falling back to localhost: {ex.Message}");
            SessionServer = new IPEndPoint(IPAddress.Loopback, 8000);
            RelayServer = new IPEndPoint(IPAddress.Loopback, 8090);
        }
    }

    public static IPEndPoint SessionServer { get; private set; }
    public static IPEndPoint RelayServer { get; private set; }
    public static string SessionList { get; set; } = "https://api.lumoravr.com/api/sessions";

    public string Name { get; set; }
    public string SessionIdentifier { get; set; }
    public string WorldIdentifier { get; set; }
    public bool Direct { get; set; }
}
