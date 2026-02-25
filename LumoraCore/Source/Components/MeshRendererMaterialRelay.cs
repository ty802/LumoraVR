namespace Lumora.Core.Components;

/// <summary>
/// Relays material changes to a mesh renderer.
/// Attach to the rig root to control materials on skinned meshes.
/// </summary>
[ComponentCategory("Rendering")]
public class MeshRendererMaterialRelay : Component
{
    /// <summary>
    /// The renderer whose materials this relay controls.
    /// </summary>
    public SyncRef<SkinnedMeshRenderer> Renderer { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Renderer = new SyncRef<SkinnedMeshRenderer>(this, null);
    }
}
