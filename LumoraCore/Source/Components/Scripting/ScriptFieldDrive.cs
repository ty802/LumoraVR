// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Scripting;

// The link a running script holds a value field with, one per drive node. Put there by the host as the
// script's author and never saved; each machine shows the script's value through it, locally. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptFieldDrive<T> : Component, IScriptDrive
{
    public readonly SyncRef<ScriptHost> Host = new();
    public readonly Sync<string> Node = new();

    // Host-only: whether the host granted the link. A link the host refuses looks granted on every other
    // machine until the refusal arrives, and showing it there would show what its author may not write.
    public readonly Sync<bool> Granted = new();
    public readonly HoldingFieldDrive<T> Drive = new();
    // While these are set it holds: every other write is refused and told here, on the machine that tried.
    public readonly SyncRef<IField<T>> Heard = new();
    public readonly SyncRef<IField<int>> HeardCount = new();

    public override void OnAwake()
    {
        base.OnAwake();
        Granted.MarkHostOnly();
        Drive.LocalValueOnly = true;
        Drive.Holds = () => Heard.Target != null;
        Drive.SetupValueSetHook(Refused);
    }

    private void Refused(SyncField<T> field, T value)
    {
        if (Heard.Target is not { } heard)
            return;
        using (World?.DataModelPermissions?.EnterSystemBypass())
        {
            heard.Value = value;
            if (HeardCount.Target is { } count)
                count.Value = count.Value + 1;
        }
    }

    void IScriptDrive.Report(IWorldElement? heard, IWorldElement? heardCount)
    {
        var field = heard as IField<T>;
        if (!ReferenceEquals(Heard.Target, field))
            Heard.Target = field!;
        var count = heardCount as IField<int>;
        if (!ReferenceEquals(HeardCount.Target, count))
            HeardCount.Target = count!;
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

    void IScriptDrive.Aim(IWorldElement? target)
    {
        var field = target as IField<T>;
        if (!ReferenceEquals(Drive.Target, field))
            Drive.DriveTarget(field);
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
        Drive.SetValue(value is T typed ? typed : default!);
        return true;
    }
}
