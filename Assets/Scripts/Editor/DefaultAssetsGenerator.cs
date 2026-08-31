// Editor utility script to generate project materials and entity prefab under Assets/Materials and Assets/Prefabs.
// Accessible via Unity menu 'Swarm > Create Default Assets'.

using System.IO;
using UnityEditor;
using UnityEngine;
using SwarmViewer;

namespace SwarmViewer.Editor
{
    public static class DefaultAssetsGenerator
    {
        [MenuItem("Swarm/Create Default Assets")]
        public static void CreateAssets()
        {
            EnsureDirectories();

            // 1. Create Materials
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");

            Material friendlyMat = CreateOrUpdateMaterial("Assets/Materials/Friendly.mat", litShader, new Color(0.25f, 0.60f, 1.00f), 0.3f);
            Material hostileMat = CreateOrUpdateMaterial("Assets/Materials/Hostile.mat", litShader, new Color(1.00f, 0.25f, 0.20f), 0.3f);
            Material civilianMat = CreateOrUpdateMaterial("Assets/Materials/Civilian.mat", litShader, new Color(0.80f, 0.80f, 0.80f), 0.3f);
            Material wreckageMat = CreateOrUpdateMaterial("Assets/Materials/Wreckage.mat", litShader, new Color(0.45f, 0.35f, 0.25f), 0.3f);
            Material unknownMat = CreateOrUpdateMaterial("Assets/Materials/Unknown.mat", litShader, new Color(0.35f, 0.35f, 0.40f), 0.3f);
            Material compromisedMat = CreateOrUpdateMaterial("Assets/Materials/Compromised.mat", litShader, new Color(1.00f, 0.75f, 0.15f), 0.3f);
            Material selectedMat = CreateOrUpdateMaterial("Assets/Materials/Selected.mat", litShader, new Color(1.00f, 1.00f, 1.00f), 0.6f);

            Material groundMat = CreateOrUpdateMaterial("Assets/Materials/Ground.mat", litShader, new Color(0.12f, 0.12f, 0.14f), 0.05f);
            Material arenaBoundsMat = CreateOrUpdateMaterial("Assets/Materials/ArenaBounds.mat", litShader, new Color(0.35f, 0.40f, 0.45f), 0.1f);
            Material assetMat = CreateOrUpdateMaterial("Assets/Materials/Asset.mat", litShader, new Color(1.00f, 0.65f, 0.10f), 0.2f);
            
            Material trailMat = CreateOrUpdateMaterial("Assets/Materials/Trail.mat", unlitShader, new Color(1f, 1f, 1f, 0.8f), 0f);

            // Outline material: bright unlit yellow/cyan with Cull Front if possible
            Material outlineMat = CreateOrUpdateMaterial("Assets/Materials/Outline.mat", unlitShader, new Color(1.00f, 0.90f, 0.20f, 1f), 0f);
            if (outlineMat.HasProperty("_Cull"))
            {
                outlineMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            }

            // 2. Create Entity Prefab
            CreateEntityPrefab(friendlyMat, outlineMat, trailMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Swarm] Default materials and entity prefab created successfully.");
        }

        static void EnsureDirectories()
        {
            if (!Directory.Exists("Assets/Materials"))
                Directory.CreateDirectory("Assets/Materials");
            if (!Directory.Exists("Assets/Prefabs"))
                Directory.CreateDirectory("Assets/Prefabs");
        }

        static Material CreateOrUpdateMaterial(string path, Shader shader, Color color, float smoothness)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void CreateEntityPrefab(Material baseMat, Material outlineMat, Material trailMat)
        {
            string prefabPath = "Assets/Prefabs/EntityPrefab.prefab";

            // Temporary GameObject
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "EntityPrefab";
            root.layer = LayerMask.NameToLayer("Picking");
            if (root.layer == -1) root.layer = 0;

            var meshRenderer = root.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = baseMat;

            var collider = root.GetComponent<CapsuleCollider>();
            collider.isTrigger = false;

            var entityView = root.AddComponent<EntityView>();

            // Add TrailRenderer
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 8f;
            trail.minVertexDistance = 0.5f;
            trail.startWidth = 1.2f;
            trail.endWidth = 0.05f;
            trail.sharedMaterial = trailMat;

            // Add Outline child
            GameObject outlineObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            outlineObj.name = "Outline";
            outlineObj.transform.SetParent(root.transform, false);
            outlineObj.transform.localScale = Vector3.one * 1.08f;
            Object.DestroyImmediate(outlineObj.GetComponent<Collider>());
            var outlineRenderer = outlineObj.GetComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMat;
            outlineObj.SetActive(false);

            // Save as Prefab
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }
    }
}
