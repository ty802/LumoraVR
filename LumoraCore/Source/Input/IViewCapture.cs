// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Input;

// Grabs what the LOCAL VIEW is showing right now as JPEG bytes, injected by the platform layer at
// startup the way the clipboard and the colour sampler are. The engine has no renderer of its own,
// so this is the only route to a picture of a world: the session thumbnail a host publishes and the
// sidecar written beside a saved world both come through here.
//
// One call per event (a save, a thumbnail heartbeat), never per frame: the implementation pulls the
// viewport image back off the GPU and encodes it, which stalls. A platform with no view (headless)
// leaves InputInterface.ViewCapture null, and every caller has to cope with that rather than assume
// a picture exists. -xlinka
public interface IViewCapture
{
    bool TryCapture(int width, int height, out byte[] jpeg);
}
