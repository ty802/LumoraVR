// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets.Interop;

public static partial class PackageTypeMap
{
    static partial void AliasesK8(Dictionary<string, string> aliases)
    {
    }
}

public static partial class PackageMemberMapper
{
    static partial void MemberAliasesK8(Dictionary<string, Dictionary<string, string>> table)
    {
        // The space and the ways up are read by ApplyK8.
        WorldK8.Alias(table, "UserCarrier", ("NestIntoSpace", "ReturnOnLeave"), ("IgnoreParentUser", "IgnoreOwnUser"),
            ("ParentSpace", WorldK8.Taken), ("Filters", WorldK8.Taken));
    }

    static partial void ApplyK8(Component component, DataTreeDictionary data, Action<ISyncRef, string> queueReference,
        Action<object, string, IReadOnlyList<string>> queueReferenceList, Func<Uri, Uri?> remapUrl)
    {
        switch (component)
        {
            case UserCarrier carrier when data.TryGetNode("ParentSpace") != null || data.TryGetNode("Filters") != null:
                WorldK8.Carrier(carrier, data, queueReference);
                break;
        }
    }

    private static class WorldK8
    {
        public const string Taken = "\u0000";

        public static void Alias(Dictionary<string, Dictionary<string, string>> table, string ours, params (string Package, string Ours)[] pairs)
        {
            if (!table.TryGetValue(ours, out var names))
                table[ours] = names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (package, mine) in pairs)
                names[package] = mine;
        }

        private static DataTreeNode? Stored(DataTreeDictionary data, string member)
            => (data.TryGetNode(member) as DataTreeDictionary)?.TryGetNode("Data");

        // A space the package names by a slot, the slot's parent, or a fallback when it names none, which is
        // the world. Ours leaves the reference empty for its own default: the world for a way up, the carrier's
        // own slot for where riders hang, so a carrier's fallback is written out as the world root.
        private static void Space(DataTreeDictionary? space, SyncRef<Slot> into, Slot? own, World? world, Action<ISyncRef, string> queueReference)
        {
            if (space == null)
                return;
            bool parent = Stored(space, "UseParentSpace") is DataTreeValue { Value: bool p } && p;
            if (Stored(space, "LocalSpace") is DataTreeValue { Value: string id } && id.Length > 0 && !IsNullId(id))
            {
                queueReference(new Then(target =>
                {
                    if (target is not Slot slot || slot.IsDestroyed)
                        return false;
                    var chosen = parent && slot.Parent is { } up ? up : slot;
                    if (!ReferenceEquals(chosen, own))
                        into.Target = chosen;
                    return true;
                }), id);
                return;
            }
            if (own != null && world?.RootSlot is { } top)
                into.Target = top;
        }

        // Where riders hang, and the ways up a body is taken in by.
        public static void Carrier(UserCarrier carrier, DataTreeDictionary data, Action<ISyncRef, string> queueReference)
        {
            Space(data.TryGetNode("ParentSpace") as DataTreeDictionary, carrier.CarrySpace, carrier.Slot, carrier.World, queueReference);
            if (Stored(data, "Filters") is not DataTreeList filters)
                return;
            foreach (var entry in filters)
            {
                if (entry is not DataTreeDictionary filter)
                    continue;
                var alignment = carrier.Alignments.Add();
                if (Stored(filter, "Direction") is DataTreeList d && ReadNumbers(d) is { Length: >= 3 } v)
                    alignment.Direction.Value = PackageHandedness.Point(new float3(v[0], v[1], v[2]));
                if (Stored(filter, "MaximumAngle") is DataTreeValue { Value: IConvertible angle } && angle is not string and not bool)
                    alignment.MaxAngle.Value = System.Math.Clamp(Convert.ToSingle(angle, CultureInfo.InvariantCulture), 0f, 180f);
                Space(filter.TryGetNode("DirectionSpace") as DataTreeDictionary, alignment.Space, null, carrier.World, queueReference);
            }
        }

        // A reference the importer resolves like any other, its target handed on.
        private sealed class Then : ISyncRef
        {
            private readonly Func<IWorldElement, bool> _then;

            public Then(Func<IWorldElement, bool> then) => _then = then;

            public RefID Value { get => default; set { } }
            public IWorldElement Target { get => null!; set { } }
            public IWorldElement RawTarget => null!;
            public Type TargetType => typeof(IWorldElement);
            public ReferenceState State => default;
            public void Clear() { }
            public bool TrySet(IWorldElement target) => target != null && _then(target);
        }
    }
}
