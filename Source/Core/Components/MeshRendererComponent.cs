using Godot;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// Component that renders a 3D mesh.
/// Example of a Component using the Slot-Component architecture.
/// </summary>
public partial class MeshRendererComponent : Component
{
    private MeshInstance3D _meshInstance;

    /// <summary>
    /// The mesh to render (synchronized).
    /// </summary>
    public Sync<Mesh> MeshData { get; private set; }

    /// <summary>
    /// The material to apply to the mesh (synchronized).
    /// </summary>
    public Sync<Material> MaterialData { get; private set; }

    /// <summary>
    /// Whether to cast shadows.
    /// </summary>
    public Sync<bool> CastShadow { get; private set; }

    public override string ComponentName => "Mesh Renderer";

    public MeshRendererComponent()
    {
        MeshData = new Sync<Mesh>(this, null);
        MaterialData = new Sync<Material>(this, null);
        CastShadow = new Sync<bool>(this, true);

        // Hook up change events
        MeshData.OnChanged += UpdateMesh;
        MaterialData.OnChanged += UpdateMaterial;
        CastShadow.OnChanged += UpdateShadowCasting;
    }

    public override void OnAwake()
    {
        // Create the MeshInstance3D node
        _meshInstance = new MeshInstance3D();
        Slot?.AddChild(_meshInstance);
    }

    public override void OnStart()
    {
        base.OnStart();
        UpdateMesh(MeshData.Value);
        UpdateMaterial(MaterialData.Value);
        UpdateShadowCasting(CastShadow.Value);
    }

    private void UpdateMesh(Mesh mesh)
    {
        if (_meshInstance != null)
        {
            _meshInstance.Mesh = mesh;
        }
    }

    private void UpdateMaterial(Material material)
    {
        if (_meshInstance != null && material != null)
        {
            _meshInstance.MaterialOverride = material;
        }
    }

    private void UpdateShadowCasting(bool castShadow)
    {
        if (_meshInstance != null)
        {
            _meshInstance.CastShadow = castShadow
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    public override void OnDestroy()
    {
        _meshInstance?.QueueFree();
        _meshInstance = null;
        base.OnDestroy();
    }
}
