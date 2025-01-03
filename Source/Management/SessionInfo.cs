using System.Net;
using LiteNetLib;

namespace Aquamarine.Source.Management;

public class SessionInfo
{
    public static IPEndPoint SessionServer = NetUtils.MakeEndPoint("relay.xlinka.com", 8000);
    public static string SessionList = "https://api.xlinka.com/sessions/";
    
    public string Name { get; set; }
    public bool PublicIP { get; set; }
    public string SessionIdentifier { get; set; }
    public string IP { get; set; }
    public int Port { get; set; }
}
