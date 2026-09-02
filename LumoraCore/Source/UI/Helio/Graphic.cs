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

    // True if ComputeGraphic trims/culls its geometry against RenderData.GeometryClipRect. Only those graphics
    // can take a clip window that MOVES WITH the chunk (a mask inside scrolled content) - it has to be baked
    // into the geometry, since a material rect is a fixed canvas-space window. A graphic that says false keeps
    // that window on its material instead, where it goes stale as the chunk scrolls, which is what everything
    // did before. Default false so a new graphic is never silently left unclipped. -xlinka
    public virtual bool TrimsGeometryToClip => false;

    // Canvas-local box this graphic's geometry stays inside, or null when we can't say. The chunk batcher
    // uses it to work out whether two graphics on the same material can share a surface without changing
    // what covers what, so it MUST be a superset of what ComputeGraphic emits - too big only costs a draw
    // call, too small puts things behind each other. Null is the honest answer for anything that draws
    // outside its rect (a ring sized by radius, a plotted line with thickness) and gets the old behaviour:
    // a surface of its own. Called on the main thread after PrepareCompute, so snapshots are current. -xlinka
    public virtual Rect? MeasureBounds() => null;

    public abstract void ComputeGraphic(GraphicsChunk.RenderData renderData);

    public abstract bool IsPointInside(in float2 point);

    public virtual ValueTask PreGraphicsCompute() => default;
}
