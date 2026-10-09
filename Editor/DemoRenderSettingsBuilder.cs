using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the demo's lighter render settings, `DemoURP_Low`, from the full ones, `DemoURP`.
    ///
    /// The Quality levels of a project all share one render pipeline asset unless each is given its own,
    /// so before this the Quality setting changed nothing on a machine that was struggling. The low tier
    /// is measured (2026-10-07, the village at noon, a 12.2 ms frame): SSAO costs 0.6 ms of main-thread
    /// time and 1,300 draw calls, because its depth-normals prepass draws the scene a second time, and
    /// sun shadows 0.3 ms and 1,000 draw calls, of which two cascades instead of four save about a third.
    /// So the low tier drops SSAO, takes two cascades, a smaller shadow map, a shorter shadow distance and
    /// hard edges. It keeps everything the island depends on: the depth and opaque textures the sea reads,
    /// HDR, and Forward+ for the torches.
    ///
    /// Idempotent: run it again and the low assets are put back to this recipe. `DemoURP` is the source of
    /// truth and is never written. Which quality levels use which is applied by the demo's Welcome window
    /// (`Use Demo Render Pipeline`), which ships, so a buyer gets the same mapping.
    /// </summary>
    public static class DemoRenderSettingsBuilder
    {
        private const string Dir = "Assets/OpenMMORPG/Demo/Settings";
        private const string FullAsset = Dir + "/DemoURP.asset";
        private const string FullRenderer = Dir + "/DemoURP_Renderer.asset";
        private const string LowAsset = Dir + "/DemoURP_Low.asset";
        private const string LowRenderer = Dir + "/DemoURP_Low_Renderer.asset";

        private const int LowCascades = 2;
        private const int LowShadowResolution = 1024;
        private const float LowShadowDistance = 40f;

        [MenuItem("Open MMORPG/Demo/Build Low Render Settings")]
        public static void BuildLow()
        {
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(FullAsset) == null)
            {
                Debug.LogError($"[{nameof(DemoRenderSettingsBuilder)}] Missing \"{FullAsset}\".");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(LowRenderer) == null)
                AssetDatabase.CopyAsset(FullRenderer, LowRenderer);
            if (AssetDatabase.LoadAssetAtPath<Object>(LowAsset) == null)
                AssetDatabase.CopyAsset(FullAsset, LowAsset);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(LowRenderer);
            int off = 0;
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature != null && feature.GetType().Name.Contains("AmbientOcclusion"))
                {
                    feature.SetActive(false);
                    ++off;
                }
            }
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LowAsset);
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_ShadowCascadeCount").intValue = LowCascades;
            serialized.FindProperty("m_MainLightShadowmapResolution").intValue = LowShadowResolution;
            serialized.FindProperty("m_ShadowDistance").floatValue = LowShadowDistance;
            serialized.FindProperty("m_SoftShadowsSupported").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoRenderSettingsBuilder)}] Built \"{LowAsset}\": {off} SSAO feature(s) off, " +
                      $"{LowCascades} cascades at {LowShadowResolution}, shadow distance {LowShadowDistance}, hard shadows. " +
                      "Apply it to the quality levels with the Welcome window's Use Demo Render Pipeline.");
        }
    }
}
