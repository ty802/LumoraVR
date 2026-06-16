// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Components.Import;

public delegate void FolderImportMethod(Slot root, string path);

public sealed class FolderImporterInfo
{
    public string Name { get; }
    public string Description { get; }
    public FolderImportMethod ImportMethod { get; }

    public FolderImporterInfo(string name, string description, FolderImportMethod importMethod)
    {
        Name = name;
        Description = description;
        ImportMethod = importMethod;
    }
}

// Registry for folder-level importers. Subsystems (avatar slices, image sequences,
// audio batches, etc.) register their own entries; FolderImportDialog renders one
// button per entry. - xlinka
public static class FolderImporter
{
    private static readonly List<FolderImporterInfo> _importers = new();

    public static IReadOnlyList<FolderImporterInfo> FolderImporters => _importers;

    public static void Register(FolderImporterInfo info)
    {
        if (info == null) throw new ArgumentNullException(nameof(info));
        _importers.Add(info);
    }

    public static void Register(string name, string description, FolderImportMethod method)
    {
        Register(new FolderImporterInfo(name, description, method));
    }

    public static void Clear() => _importers.Clear();
}
