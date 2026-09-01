// Editor utility script to generate project materials, drone mesh, and entity prefab under Assets/Materials, Assets/Meshes and Assets/Prefabs.
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

            Material friendlyMat = CreateOrUpdateMaterial("Assets/Materials/Friendly.mat", litShader, Palette.Friendly, 0.3f);
            Material hostileMat = CreateOrUpdateMaterial("Assets/Materials/Hostile.mat", litShader, Palette.Hostile, 0.3f);
            Material civilianMat = CreateOrUpdateMaterial("Assets/Materials/Civilian.mat", litShader, Palette.Civilian, 0.3f);
            Material wreckageMat = CreateOrUpdateMaterial("Assets/Materials/Wreckage.mat", litShader, Palette.Wreckage, 0.3f);
            Material unknownMat = CreateOrUpdateMaterial("Assets/Materials/Unknown.mat", litShader, Palette.Unknown, 0.3f);
            Material compromisedMat = CreateOrUpdateMaterial("Assets/Materials/Compromised.mat", litShader, Palette.Compromised, 0.3f);
            Material selectedMat = CreateOrUpdateMaterial("Assets/Materials/Selected.mat", litShader, Palette.Selected, 0.6f);

            Material groundMat = CreateOrUpdateMaterial("Assets/Materials/Ground.mat", litShader, new Color(0.12f, 0.12f, 0.14f), 0.05f);
            Material arenaBoundsMat = CreateOrUpdateMaterial("Assets/Materials/ArenaBounds.mat", litShader, new Color(0.35f, 0.40f, 0.45f), 0.1f);
            Material assetMat = CreateOrUpdateMaterial("Assets/Materials/Asset.mat", litShader, Palette.Asset, 0.2f);
            
            Material trailMat = CreateOrUpdateMaterial("Assets/Materials/Trail.mat", unlitShader, new Color(1f, 1f, 1f, 0.85f), 0f);
            MakeTransparent(trailMat);

            // Outline material: bright unlit yellow/cyan with Cull Front if possible
            Material outlineMat = CreateOrUpdateMaterial("Assets/Materials/Outline.mat", unlitShader, Palette.Selected, 0f);
            if (outlineMat.HasProperty("_Cull"))
            {
                outlineMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            }

            // 2. Create Drone Mesh
            DroneMeshGenerator.GenerateDroneMeshAndAssign();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Swarm] Default materials and entity prefab created successfully.");
        }

        static void EnsureDirectories()
        {
            if (!Directory.Exists("Assets/Materials"))
                Directory.CreateDirectory("Assets/Materials");
            if (!Directory.Exists("Assets/Meshes"))
                Directory.CreateDirectory("Assets/Meshes");
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

        static void MakeTransparent(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 3000;
            EditorUtility.SetDirty(mat);
        }
    }
}

