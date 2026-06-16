// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Persistence;

/// <summary>
/// Saves/loads a worker tagged with its concrete type, so the loader knows what to instantiate.
/// A worker is written as <c>{ "Type": typeName, "Data": worker.Save() }</c>; on load the caller
/// reads the type, creates the worker (a component via the slot, a child slot directly), then
/// hands the data to <c>worker.Load</c>.
/// </summary>
internal static class WorkerSaveLoad
{
    public static DataTreeDictionary SaveWorker(Worker worker, SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Type", DataTreeValue.RawString(worker.WorkerTypeName));
        dictionary.Add("Data", worker.Save(control));
        return dictionary;
    }

    public static (string typeName, DataTreeNode data) ExtractWorker(DataTreeNode node)
    {
        var dictionary = (DataTreeDictionary)node;
        var typeName = ((DataTreeValue)dictionary["Type"]).Extract<string>();
        return (typeName, dictionary["Data"]);
    }
}
