﻿﻿﻿﻿﻿using System.Linq;
using Godot;

namespace Aquamarine.Source.Management
{
    public partial class ServerManager
    {
        // Server type enum
        public enum ServerType
        {
            None,
            Local,
            Standard,
            NotAServer
        }

        // Helper method to determine server type
        public static ServerType CurrentServerType
        {
            get
            {
                var args = OS.GetCmdlineArgs();
                if (args.Contains("--run-home-server"))
                {
                    return ServerType.Local;
                }
                else if (args.Contains("--run-server"))
                {
                    return ServerType.Standard;
                }
                return ServerType.NotAServer;
            }
        }
    }
}
