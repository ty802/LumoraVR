// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// How a camera moves. First person rides the subject's eyes; third person keeps a set distance from their
// face; group pulls back far enough to fit everyone standing with them; stationary stays where it was put
// and turns to keep them framed; manual is carried and left like any object.
public enum PhotoCameraMode
{
    FirstPerson,
    ThirdPerson,
    Group,
    Stationary,
    Manual,
}

// What a photo is: one picture, a picture per eye side by side, all the way round, or one object on its own
// on a clear background.
public enum PhotoShape
{
    Flat,
    Stereo,
    Panorama,
    Object,
}
