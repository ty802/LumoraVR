// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Lumora.Core.Desktop;

// Desktop duplication for the picture, SendInput for the hands. -xlinka
[SupportedOSPlatform("windows")]
internal sealed class WindowsDesktopBackend : IDesktopBackend
{
    private readonly WindowsDesktopInput _input = new();

    public string Name => "Windows";

    public IDesktopInput? Input => _input;

    public IReadOnlyList<DesktopDisplay> Displays() => DxgiDesktopCapture.Displays();

    public DesktopCapture CreateCapture(DesktopDisplay display) => new DxgiDesktopCapture(display, _input);
}
