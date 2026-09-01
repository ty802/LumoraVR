// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Persistence;

// Saves/loads a worker tagged with its concrete type, so the loader knows what to instantiate:
// { "Type": typeName, "Data": worker.Save() }. On load the caller reads the type, creates the worker
// (a component via the slot, a child slot directly), then hands the data to worker.Load.
//
// EXTRACTION IS ALSO WHERE TYPES ARE RESOLVED. A caller only ever sees a type name it can instantiate,
// which is what lets a replacement, a rename, and preservation of an unrecognised type all happen
// without every load site knowing about any of them.
internal static class WorkerSaveLoad
{
    // Members of the preserved payload, written by the placeholder's ordinary member save. Keeping the
    // strings here keeps the two halves of the round trip in one file.
    private const string PreservedTypeMember = nameof(UnresolvedComponent.MissingType);
    private const string PreservedVersionMember = nameof(UnresolvedComponent.MissingTypeVersion);
    private const string PreservedDataMember = nameof(UnresolvedComponent.Data);

    public static DataTreeDictionary SaveWorker(Worker worker, SaveControl control)
    {
        var dictionary = new DataTreeDictionary();

        // A preserved component writes itself back out AS WHAT IT WAS. The file it came from and the
        // file it goes into are then the same file as far as that component is concerned, so a build
        // that does know the type opens either one and finds it intact. -xlinka
        if (worker is UnresolvedComponent { HasPreservedData: true } unresolved
            && !string.IsNullOrEmpty(unresolved.MissingType.Value))
        {
            dictionary.Add("Type", DataTreeValue.RawString(unresolved.MissingType.Value));
            dictionary.Add("Data", unresolved.Data.Node!);
            return dictionary;
        }

        dictionary.Add("Type", DataTreeValue.RawString(worker.WorkerTypeName));
        dictionary.Add("Data", worker.Save(control));
        return dictionary;
    }

    // Pass the load's control whenever there is one: without it a migration cannot see which version
    // the file stamped for the type, and version-ranged entries fall back to the ambient load.
    public static (string typeName, DataTreeNode data) ExtractWorker(DataTreeNode node, LoadControl? control)
    {
        var dictionary = (DataTreeDictionary)node;
        var typeName = ((DataTreeValue)dictionary["Type"]).Extract<string>();
        var data = dictionary["Data"];


        var replacement = control?.ResolveReplacement(typeName, data)
            ?? LoadControl.ResolveWithoutVersion(typeName, data);

        if (replacement.Type != null)
            return (replacement.Type.FullName!, replacement.Data);

        // Nothing here can load it. Hand back a placeholder carrying the original name and the original
        // member data, shaped as the placeholder's own members so the caller's ordinary
        // attach-then-Load does the rest.
        //
        // The UNTRANSFORMED data goes in, under the name the file used. A migration that fired on the
        // way here was aiming at a type this build turns out not to have, so keeping its half-applied
        // output would hand the build that DOES have the old type something that file never said. -xlinka
        LumoraLogger.Warn($"WorkerSaveLoad: type '{typeName}' does not resolve in this build - preserving its data as an inert placeholder.");

        var preserved = new DataTreeDictionary();
        preserved.Add(PreservedTypeMember, DataTreeValue.RawString(typeName));
        preserved.Add(PreservedVersionMember, control?.GetSavedTypeVersion(typeName) ?? 0);
        preserved.Add(PreservedDataMember, data);
        return (typeof(UnresolvedComponent).FullName!, preserved);
    }
}
