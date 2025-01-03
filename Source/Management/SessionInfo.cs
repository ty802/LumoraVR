using System.Net;
using LiteNetLib;

namespace Aquamarine.Source.Management;

public class SessionInfo
{
    public static IPEndPoint SessionServer = NetUtils.MakeEndPoint("127.0.0.1", 8000);
    public static string SessionList = "http://127.0.0.1:8001";
    
    public string Name { get; set; }
    public bool PublicIP { get; set; }
    public string SessionIdentifier { get; set; }
    public string IP { get; set; }
    public int Port { get; set; }
}
