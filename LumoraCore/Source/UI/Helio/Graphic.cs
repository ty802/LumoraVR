// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;
using Lumora.Core.Math;

namespace Helio.UI;

public abstract class Graphic : UIComputeComponent
{
    public abstract bool RequiresPreGraphicsCompute { get; }

    // if false, batcher can reorder graphics on the same rect for fewer drawcalls - xlinka
    public virtual bool RequirePreciseSameLevelSorting => true;

    public abstract void ComputeGraphic(GraphicsChunk.RenderData renderData);

    public abstract bool IsPointInside(in float2 point);

    public virtual ValueTask PreGraphicsCompute() => default;
}
