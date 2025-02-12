using Godot;
using System;
using System.Linq;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager
    {
        public enum ServerType
        {
            NotAServer,
            Standard,
            Local
        }

        private static ServerType? _serverType;

        //TODO: move this somewhere else? 
        //LOOK I MOVED IT linka 2/12/2025
        public static ServerType CurrentServerType
        {
            get
            {
                if (_serverType.HasValue) return _serverType.Value;

                var args = OS.GetCmdlineArgs();
                var isLocalHomeServer = args.Any(i => i.Equals("--run-home-server", StringComparison.CurrentCultureIgnoreCase));
                if (isLocalHomeServer)
                {
                    _serverType = ServerType.Local;
                    return ServerType.Local;
                }

                var isServer = args.Any(i => i.Equals("--run-server", System.StringComparison.CurrentCultureIgnoreCase));
                if (isServer)
                {
                    _serverType = ServerType.Standard;
                    return ServerType.Standard;
                }

                _serverType = ServerType.NotAServer;
                return ServerType.NotAServer;
            }
        }
    }
}