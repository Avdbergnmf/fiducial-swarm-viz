// Sim NED → viewer left-handed y-up. Same two functions as
// tools/build_viewer_data.py (FORMAT.md). Brain logs are NED; the .bin is not.

using UnityEngine;

namespace SwarmViewer
{
    public static class SwarmCoord
    {
        /// <summary>viewer = (east, -down, north).</summary>
        public static Vector3 Ned(float north, float east, float down = 0f) =>
            new(east, -down, north);
    }
}
