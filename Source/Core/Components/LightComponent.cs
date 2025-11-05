using Godot;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// Component that provides illumination.
/// </summary>
public partial class LightComponent : Component
{
    public enum LightType
    {
        Directional,
        Point,
        Spot
    }

    private Light3D _light;

    /// <summary>
    /// Type of light source (synchronized).
    /// </summary>
    public Sync<LightType> Type { get; private set; }

    /// <summary>
    /// Light color (synchronized).
    /// </summary>
    public Sync<Color> LightColor { get; private set; }

    /// <summary>
    /// Light intensity/energy (synchronized).
    /// </summary>
    public Sync<float> Energy { get; private set; }

    /// <summary>
    /// Light range for point and spot lights (synchronized).
    /// </summary>
    public Sync<float> Range { get; private set; }

    /// <summary>
    /// Whether the light casts shadows.
    /// </summary>
    public Sync<bool> CastShadow { get; private set; }

    public override string ComponentName => "Light";

    public LightComponent()
    {
        Type = new Sync<LightType>(this, LightType.Point);
        LightColor = new Sync<Color>(this, Colors.White);
        Energy = new Sync<float>(this, 1.0f);
        Range = new Sync<float>(this, 5.0f);
        CastShadow = new Sync<bool>(this, true);

        Type.OnChanged += UpdateLightType;
        LightColor.OnChanged += UpdateColor;
        Energy.OnChanged += UpdateEnergy;
        Range.OnChanged += UpdateRange;
        CastShadow.OnChanged += UpdateShadow;
    }

    public override void OnAwake()
    {
        UpdateLightType(Type.Value);
    }

    private void UpdateLightType(LightType type)
    {
        // Remove old light
        _light?.QueueFree();

        // Create new light of the correct type
        _light = type switch
        {
            LightType.Directional => new DirectionalLight3D(),
            LightType.Point => new OmniLight3D(),
            LightType.Spot => new SpotLight3D(),
            _ => new OmniLight3D()
        };

        Slot?.AddChild(_light);

        // Reapply all properties
        UpdateColor(LightColor.Value);
        UpdateEnergy(Energy.Value);
        UpdateRange(Range.Value);
        UpdateShadow(CastShadow.Value);
    }

    private void UpdateColor(Color color)
    {
        if (_light != null)
        {
            _light.LightColor = color;
        }
    }

    private void UpdateEnergy(float energy)
    {
        if (_light != null)
        {
            _light.LightEnergy = energy;
        }
    }

    private void UpdateRange(float range)
    {
        if (_light is OmniLight3D omni)
        {
            omni.OmniRange = range;
        }
        else if (_light is SpotLight3D spot)
        {
            spot.SpotRange = range;
        }
    }

    private void UpdateShadow(bool castShadow)
    {
        if (_light != null)
        {
            _light.ShadowEnabled = castShadow;
        }
    }

    public override void OnDestroy()
    {
        _light?.QueueFree();
        _light = null;
        base.OnDestroy();
    }
}
