Steam Frame standalone uses the same Linux x86_64 build as desktop Linux. Launch
`Lumora-Frame.sh` from that bundle on Frame. The release workflow and local Steam
build script copy it beside the game executable. It selects Godot's mobile
renderer before engine startup and enables Lumora's standalone headset settings.

When launching the executable through Steam instead, set its launch options to:

```text
%command% --rendering-method mobile --xr-mode on -- --steam-frame --vr
```

The `--steam-frame` flag alone cannot switch the renderer: Godot has already
created it before Lumora's C# device detection runs. The generic Linux export does
not carry the `steam_frame` custom feature used by the project setting override.

These startup settings do not replace testing on the headset. For a failure,
capture the game log, including OpenXR initialization, the selected renderer,
and both controller interaction profiles. Add `--Lumora-Debug` for input details.
