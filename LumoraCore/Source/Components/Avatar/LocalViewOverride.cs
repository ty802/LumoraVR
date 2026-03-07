using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Overrides a slot's rendered transform when viewed from a specific rendering context.
///
/// Primary use: scale the local user's head to zero in UserView so the player
/// doesn't see their own avatar head, while other users and floor shadows are unaffected.
///
/// Usage:
///   var lvo = headVisual.AttachComponent&lt;LocalViewOverride&gt;();
///   lvo.Context.Value          = RenderingContext.UserView;
///   lvo.HasScaleOverride.Value = true;
///   lvo.ScaleOverride.Value    = float3.Zero;
/// </summary>
[ComponentCategory("Avatar")]
public class LocalViewOverride : ImplementableComponent<IHook>
{
    /// <summary>Which rendering context activates this override.</summary>
    public Sync<RenderingContext> Context { get; private set; }

    /// <summary>When true, PositionOverride replaces the slot's normal position.</summary>
    public Sync<bool>   HasPositionOverride { get; private set; }
    public Sync<float3> PositionOverride    { get; private set; }

    /// <summary>When true, RotationOverride replaces the slot's normal rotation.</summary>
    public Sync<bool>   HasRotationOverride { get; private set; }
    public Sync<floatQ> RotationOverride    { get; private set; }

    /// <summary>
    /// When true, ScaleOverride replaces the slot's normal scale.
    /// Set to float3.Zero to make the slot invisible in the target context
    /// while keeping shadow casting intact.
    /// </summary>
    public Sync<bool>   HasScaleOverride { get; private set; }
    public Sync<float3> ScaleOverride    { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Context             = new Sync<RenderingContext>(this, RenderingContext.UserView);
        HasPositionOverride = new Sync<bool>(this, false);
        PositionOverride    = new Sync<float3>(this, float3.Zero);
        HasRotationOverride = new Sync<bool>(this, false);
        RotationOverride    = new Sync<floatQ>(this, floatQ.Identity);
        HasScaleOverride    = new Sync<bool>(this, false);
        ScaleOverride       = new Sync<float3>(this, float3.One);
    }
}
