// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Scripting;

// The link a running script holds a reference field with, one per drive node, the way ScriptFieldDrive
// holds a value field. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptReferenceDrive<T> : Component, IScriptDrive where T : class, IWorldElement
{
    public readonly SyncRef<ScriptHost> Host = new();
    public readonly Sync<string> Node = new();

    // Host-only: whether the host granted the link. A link the host refuses looks granted on every other
    // machine until the refusal arrives, and showing it there would show what its author may not write.
    public readonly Sync<bool> Granted = new();
    public readonly DriveRef<T> Drive = new();

    public override void OnAwake()
    {
        base.OnAwake();
        Granted.MarkHostOnly();
        Drive.LocalValueOnly = true;
    }

    ScriptHost? IScriptDrive.Owner
    {
        get => Host.Target;
        set => Host.Target = value!;
    }

    string IScriptDrive.NodeId
    {
        get => Node.Value ?? string.Empty;
        set => Node.Value = value;
    }

    bool IScriptDrive.IsGone => IsDestroyed;

    void IScriptDrive.Report(IWorldElement? heard, IWorldElement? heardCount)
    {
    }

    void IScriptDrive.Aim(IWorldElement? target)
    {
        var reference = target as SyncRef<T>;
        if (!ReferenceEquals(Drive.Target, reference))
            Drive.DriveTarget(reference);
    }

    void IScriptDrive.Confirm()
    {
        if (World is not { IsAuthority: true } world || Granted.Value == Drive.IsLinkValid)
            return;
        using (world.DataModelPermissions?.EnterSystemBypass())
            Granted.Value = Drive.IsLinkValid;
    }

    bool IScriptDrive.Show(object? value)
    {
        if (!Drive.IsLinkValid || World is not { IsAuthority: true } && !Granted.Value)
            return false;
        Drive.SetValue(value as T);
        return true;
    }
}
