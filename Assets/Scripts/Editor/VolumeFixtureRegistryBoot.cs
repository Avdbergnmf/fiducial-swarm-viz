// Edit-mode boot so two fixture spheres in the Scene view still get a registry
// without Play. Play mode uses RuntimeInitializeOnLoadMethod on the component.

using UnityEditor;
using UnityEngine;

namespace SwarmViewer.Editor
{
    [InitializeOnLoad]
    static class VolumeFixtureRegistryBoot
    {
        static VolumeFixtureRegistryBoot()
        {
            EditorApplication.delayCall += VolumeFixtureRegistry.Ensure;
        }
    }
}
