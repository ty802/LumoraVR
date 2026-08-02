// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

public class FocusManager
{
    private World _focusedWorld = null!;
    private World _previousFocusedWorld = null!;
    private World _userspaceWorld = null!;

    public World FocusedWorld
    {
        get => _focusedWorld;
        private set
        {
            if (_focusedWorld == value) return;

            var oldWorld = _focusedWorld;
            // Only record real worlds as "previous"; null transitions don't count
            // so closing the current focused world can still find a sensible target. - xlinka
            if (oldWorld != null)
            {
                _previousFocusedWorld = oldWorld;
            }
            _focusedWorld = value;

            OnFocusedWorldChanged?.Invoke(oldWorld!, value);
            LumoraLogger.Log($"FocusManager: Focused world changed from '{oldWorld?.WorldName.Value ?? "none"}' to '{value?.WorldName.Value ?? "none"}'");
        }
    }

    // The natural back target when closing the focused world.
    public World PreviousFocusedWorld => _previousFocusedWorld;

    // Always rendered on top of the focused world.
    public World UserspaceWorld
    {
        get => _userspaceWorld;
        set
        {
            if (_userspaceWorld == value) return;

            _userspaceWorld = value;
            LumoraLogger.Log($"FocusManager: Userspace world set to '{value?.WorldName.Value ?? "none"}'");
        }
    }

    public event Action<World, World> OnFocusedWorldChanged = null!;

    public void SwitchToWorld(World world)
    {
        if (world == null)
        {
            LumoraLogger.Warn("FocusManager: Cannot switch to null world");
            return;
        }

        if (world == _userspaceWorld)
        {
            LumoraLogger.Warn("FocusManager: Cannot switch focus to userspace world (it's always an overlay)");
            return;
        }

        FocusedWorld = world;
    }

    // [FocusedWorld, UserspaceWorld] when both exist.
    public World[] GetActiveWorlds()
    {
        if (_focusedWorld != null && _userspaceWorld != null)
        {
            return new[] { _focusedWorld, _userspaceWorld };
        }
        else if (_focusedWorld != null)
        {
            return new[] { _focusedWorld };
        }
        else if (_userspaceWorld != null)
        {
            return new[] { _userspaceWorld };
        }
        else
        {
            return Array.Empty<World>();
        }
    }

    public bool IsFocused(World world)
    {
        return _focusedWorld == world;
    }

    public bool IsUserspace(World world)
    {
        return _userspaceWorld == world;
    }
}

