using Godot;
using Lumora.Core.Persistence;
public partial class PathResolverSetup : Node {

    public override void _EnterTree()
    {
        base._EnterTree();
        // I HATE GODOT
        PathResolver.initialize(
            #if WINDOWS
                OS.GetCacheDir(),
            #elif LINUX
                OS.GetUserDataDir(),
            #else
                OS.GetDataDir(),
            #endif
            #if WINDOWS
                OS.GetDataDir(),
            #else
                OS.GetConfigDir(),
            #endif
                OS.GetCacheDir()
            );
    }
}
