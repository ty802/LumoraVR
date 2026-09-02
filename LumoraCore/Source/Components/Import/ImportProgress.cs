// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Localization;

namespace Lumora.Core.Components.Import;

public enum ImportStage
{
    Fetching,
    Decoding,
    Textures,
    Animations,
    Hierarchy,
    Skeleton,
    Meshes,
    Finishing,
    Complete,
    Failed
}

// One value carrying everything the in-world readout needs: which stage, how far through, and the
// N-of-M counts for the stages that HAVE counts (textures, mesh nodes). Reported from the importer,
// which runs off the world thread, so it stays a struct with no references to the data model. -xlinka
public readonly struct ImportProgress
{
    public readonly ImportStage Stage;

    // 0..1 overall, or negative when the stage cannot say.
    public readonly float Fraction;

    public readonly int Done;
    public readonly int Total;

    public ImportProgress(ImportStage stage, float fraction, int done = 0, int total = 0)
    {
        Stage = stage;
        Fraction = fraction;
        Done = done;
        Total = total;
    }

    public bool HasCounts => Total > 0;

    public LocaleText Describe()
    {
        return Stage switch
        {
            ImportStage.Fetching => "Import.Stage.Fetching".AsLocale("Fetching"),
            ImportStage.Decoding => "Import.Stage.Decoding".AsLocale("Decoding"),
            ImportStage.Textures => HasCounts
                ? "Import.Stage.TexturesCounted".AsLocale("Textures {0} of {1}", Done, Total)
                : "Import.Stage.Textures".AsLocale("Textures"),
            ImportStage.Animations => "Import.Stage.Animations".AsLocale("Animations"),
            ImportStage.Hierarchy => "Import.Stage.Hierarchy".AsLocale("Building slots"),
            ImportStage.Skeleton => "Import.Stage.Skeleton".AsLocale("Building skeleton"),
            ImportStage.Meshes => HasCounts
                ? "Import.Stage.MeshesCounted".AsLocale("Meshes {0} of {1}", Done, Total)
                : "Import.Stage.Meshes".AsLocale("Meshes"),
            ImportStage.Finishing => "Import.Stage.Finishing".AsLocale("Finishing"),
            ImportStage.Complete => "Import.Stage.Complete".AsLocale("Done"),
            ImportStage.Failed => "Import.Stage.Failed".AsLocale("Failed"),
            _ => LocaleText.Empty
        };
    }
}
