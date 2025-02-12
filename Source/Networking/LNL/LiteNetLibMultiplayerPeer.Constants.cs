using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aquamarine.Source.Networking
{
    public partial class LiteNetLibMultiplayerPeer
    {
        public const string RoomKey = "Aquamarine"; //TODO
        public const byte ControlChannel = 0b00111111;
        public const byte ControlSetLocalID = 0x01;
    }
}