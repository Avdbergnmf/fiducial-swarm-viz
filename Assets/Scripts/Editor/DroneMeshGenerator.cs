// Editor utility script to generate the low-poly dart/paper-plane drone mesh and configure EntityPrefab.
// Accessible via Unity menu 'Swarm > Create Drone Mesh'.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SwarmViewer.Editor
{
    public static class DroneMeshGenerator
    {
        public const string MeshAssetPath = "Assets/Meshes/Drone.asset";
        public const string PrefabAssetPath = "Assets/Prefabs/EntityPrefab.prefab";

        [MenuItem("Swarm/Create Drone Mesh")]
        public static void GenerateDroneMeshAndAssign()
        {
            EnsureDirectories();

            // 1. Build low-poly dart mesh
            Mesh droneMesh = BuildDartMesh();

            // Save mesh asset to Assets/Meshes/Drone.asset
            Mesh existingAsset = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (existingAsset == null)
            {
                AssetDatabase.CreateAsset(droneMesh, MeshAssetPath);
            }
            else
            {
                EditorUtility.CopySerialized(droneMesh, existingAsset);
                droneMesh = existingAsset;
            }

            AssetDatabase.SaveAssets();

            // 2. Assign to EntityPrefab
            ConfigureEntityPrefab(droneMesh);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Swarm] Drone mesh created at '{MeshAssetPath}' and assigned to '{PrefabAssetPath}'.");
        }

        static void EnsureDirectories()
        {
            if (!Directory.Exists("Assets/Meshes"))
                Directory.CreateDirectory("Assets/Meshes");
            if (!Directory.Exists("Assets/Prefabs"))
                Directory.CreateDirectory("Assets/Prefabs");
        }

        public static Mesh BuildDartMesh()
        {
            var mesh = new Mesh { name = "Drone" };

            // Low-poly delta wing / dart paper-plane (length 1.5, wingspan 1.0, thickness 0.3)
            // Nose along +Z (+0.75), Tail at -Z (-0.75), Wings at +/-X (0.5), Up is +Y
            Vector3 N  = new Vector3( 0.0f,  0.00f,  0.75f); // Nose
            Vector3 LW = new Vector3(-0.5f,  0.00f, -0.65f); // Left Wingtip
            Vector3 RW = new Vector3( 0.5f,  0.00f, -0.65f); // Right Wingtip
            Vector3 TS = new Vector3( 0.0f,  0.18f, -0.15f); // Top Spine crest
            Vector3 BB = new Vector3( 0.0f, -0.12f, -0.15f); // Bottom Belly keel
            Vector3 TT = new Vector3( 0.0f,  0.10f, -0.75f); // Tail Top
            Vector3 TB = new Vector3( 0.0f, -0.06f, -0.75f); // Tail Bottom

            // 10 Triangles with flat shading (distinct face normals)
            var verts = new List<Vector3>(30);
            var tris = new List<int>(30);
            var uvs = new List<Vector2>(30);

            void AddTri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                int baseIdx = verts.Count;
                verts.Add(p0);
                verts.Add(p1);
                verts.Add(p2);

                tris.Add(baseIdx);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);

                uvs.Add(new Vector2(p0.x + 0.5f, (p0.z + 0.75f) / 1.5f));
                uvs.Add(new Vector2(p1.x + 0.5f, (p1.z + 0.75f) / 1.5f));
                uvs.Add(new Vector2(p2.x + 0.5f, (p2.z + 0.75f) / 1.5f));
            }

            // Top surface (4 tris)
            AddTri(N, TS, LW);
            AddTri(N, RW, TS);
            AddTri(TS, TT, LW);
            AddTri(TS, RW, TT);

            // Bottom surface (4 tris)
            AddTri(N, LW, BB);
            AddTri(N, BB, RW);
            AddTri(BB, LW, TB);
            AddTri(BB, TB, RW);

            // Trailing edge / back (2 tris)
            AddTri(TT, TB, LW);
            AddTri(TT, RW, TB);

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return mesh;
        }

        static void ConfigureEntityPrefab(Mesh droneMesh)
        {
            if (!File.Exists(PrefabAssetPath))
            {
                Debug.LogWarning($"[Swarm] Prefab not found at {PrefabAssetPath}. Make sure default assets were created.");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabAssetPath);
            try
            {
                // Root MeshFilter
                var meshFilter = prefabRoot.GetComponent<MeshFilter>();
                if (meshFilter == null)
                    meshFilter = prefabRoot.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = droneMesh;

                // Replace CapsuleCollider with BoxCollider
                var capsuleCol = prefabRoot.GetComponent<CapsuleCollider>();
                if (capsuleCol != null)
                    Object.DestroyImmediate(capsuleCol);

                var boxCol = prefabRoot.GetComponent<BoxCollider>();
                if (boxCol == null)
                    boxCol = prefabRoot.AddComponent<BoxCollider>();

                boxCol.center = new Vector3(0f, 0.03f, 0f);
                boxCol.size = new Vector3(1.0f, 0.30f, 1.50f);
                boxCol.isTrigger = false;

                // Child Outline MeshFilter
                Transform outlineT = prefabRoot.transform.Find("Outline");
                if (outlineT != null)
                {
                    var outlineFilter = outlineT.GetComponent<MeshFilter>();
                    if (outlineFilter == null)
                        outlineFilter = outlineT.gameObject.AddComponent<MeshFilter>();
                    outlineFilter.sharedMesh = droneMesh;
                    outlineT.localScale = new Vector3(1.08f, 1.25f, 1.06f);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
