using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Puts the island's foliage on the wind shader.
    ///
    /// Two kinds of material, because wind treats two kinds of thing differently - see
    /// <c>DemoWindFoliage.hlsl</c>:
    ///
    /// * **Trees** (the bark and canopy materials) are switched to the wind shader in place. They
    ///   bend by height above the object's pivot and are only ever used by whole trees, so one
    ///   material per species is right. Canopies get leaf flutter; bark does not, which is what keeps
    ///   a trunk and its crown moving together.
    ///
    /// * **Small plants** - grass, clover, ferns, flowers, bushes - get a <c>_Small</c> copy of the
    ///   material, and only the *terrain* prefabs are pointed at it. The terrain welds details into
    ///   patch meshes with no pivot, so these bend by height above the ground itself, with a much
    ///   softer curve than a trunk. The originals stay on plain Lit: they are what the pack's
    ///   models carry everywhere else (the menu hillside, any prop dropped in by hand), where a
    ///   ground-relative shader has no terrain to ask and nothing to do.
    ///
    /// The split cannot be one material per name: <c>Leaves_NormalTree</c> is the canopy of a
    /// twelve-metre tree and also the foliage of a one-metre bush, and those want opposite strengths.
    /// </summary>
    public static class DemoWindMaterials
    {
        public const string ShaderName = "OpenMMORPG/Demo/Wind Foliage";
        public const string DetailShaderName = "Hidden/OpenMMORPG/Demo/Terrain Detail Wind";
        private const string StockDetailShaderPath = "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainDetailLit.shader";
        public const string Suffix = "_Small";
        private const string MaterialDir = DemoArtCollector.ArtDir + "/Nature/Materials";

        /// <summary>Wood. Bends, never shivers.</summary>
        private static readonly string[] Trunks = { "Bark_NormalTree", "Bark_DeadTree", "Bark_TwistedTree" };

        /// <summary>Tree foliage. Bends and shivers.</summary>
        private static readonly string[] Canopies = { "Leaves_NormalTree", "Leaves_Pine", "Leaves_TwistedTree" };

        /// <summary>
        /// Materials that get a small-plant copy for the terrain's bushes and details. Mushrooms are
        /// left out on purpose - a fungus does not sway.
        /// </summary>
        private static readonly string[] Plants = { "Leaves", "Flowers", "Grass", "Leaves_NormalTree" };

        [MenuItem("Open MMORPG/Demo/Apply Foliage Wind")]
        public static void Apply()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoWindMaterials)}] Shader \"{ShaderName}\" not found.");
                return;
            }

            int switched = 0;
            foreach (string name in Trunks)
                switched += SwitchInPlace(name, shader, leaf: false) ? 1 : 0;
            foreach (string name in Canopies)
                switched += SwitchInPlace(name, shader, leaf: true) ? 1 : 0;

            int variants = 0;
            foreach (string name in Plants)
                variants += SmallVariant(name, null) != null ? 1 : 0;

            int prefabs = RepointTerrainPrefabs();
            bool details = UseWindDetailShader(true);
            AssetDatabase.SaveAssets();

            // A terrain keeps what it drew its trees and details with; without this the open scene
            // goes on showing the old materials until it is reloaded.
            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                terrain.Flush();

            Debug.Log($"[{nameof(DemoWindMaterials)}] Wind shader on {switched} tree materials, " +
                      $"{variants} small-plant materials, {prefabs} terrain prefabs repointed" +
                      (details ? "; terrain details draw with the wind shader." : "; terrain detail shader NOT switched."));
        }

        /// <summary>Puts URP's own terrain detail shader back. The way out of <see cref="Apply"/>.</summary>
        [MenuItem("Open MMORPG/Demo/Restore Stock Terrain Detail Shader")]
        public static void RestoreStockDetailShader()
        {
            if (UseWindDetailShader(false))
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[{nameof(DemoWindMaterials)}] Terrain details are back on URP's stock detail shader; the grass no longer moves.");
            }
        }

        /// <summary>
        /// Points the pipeline's terrain detail shader at ours (or back at the stock one).
        ///
        /// **Why a pipeline setting and not a material.** The island's undergrowth is mesh detail that
        /// is not GPU-instanced, and the terrain draws that with URP's hidden detail shader, using
        /// only the prefab's main texture - the prefab's own material is never drawn, so no shader on
        /// it can ever move the grass. URP exposes the shader as
        /// <see cref="UniversalRenderPipelineRuntimeTerrainShaders.terrainDetailLitShader"/>, serialised
        /// in the URP Global Settings asset (and so carried into builds), and settable from the Editor.
        /// Instancing the details instead would let a material carry the wind, but draws the meadow far
        /// denser and makes every blade cast a shadow - a different island, not a windy one.
        ///
        /// It is a project-wide setting: any other terrain's mesh details draw with this shader too,
        /// which is inert (wind strength is zero) wherever there is no FoliageWind.
        /// </summary>
        private static bool UseWindDetailShader(bool wind)
        {
            Shader shader = wind
                ? Shader.Find(DetailShaderName)
                : AssetDatabase.LoadAssetAtPath<Shader>(StockDetailShaderPath);
            if (shader == null)
            {
                Debug.LogWarning($"[{nameof(DemoWindMaterials)}] Terrain detail shader not found ({(wind ? DetailShaderName : StockDetailShaderPath)}).");
                return false;
            }
            if (!UnityEngine.Rendering.GraphicsSettings.TryGetRenderPipelineSettings(
                    out UnityEngine.Rendering.Universal.UniversalRenderPipelineRuntimeTerrainShaders settings))
            {
                Debug.LogWarning($"[{nameof(DemoWindMaterials)}] URP has no terrain shader settings in this project.");
                return false;
            }
            if (settings.terrainDetailLitShader != shader)
            {
                settings.terrainDetailLitShader = shader;
                // The setting lives in the URP Global Settings asset; the setter does not always mark it.
                var global = UnityEngine.Rendering.GraphicsSettings
                    .GetSettingsForRenderPipeline<UnityEngine.Rendering.Universal.UniversalRenderPipeline>();
                if (global != null)
                    EditorUtility.SetDirty(global);
            }
            return true;
        }

        private static bool SwitchInPlace(string name, Shader shader, bool leaf)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
            if (material == null)
            {
                Debug.LogWarning($"[{nameof(DemoWindMaterials)}] No material \"{name}\" under {MaterialDir}; run Collect Demo Art, then this again.");
                return false;
            }
            Configure(material, shader, ground: false, leaf: leaf);
            EditorUtility.SetDirty(material);
            return true;
        }

        /// <summary>
        /// The small-plant copy of a material, made if it is not there. Taken from the demo's own
        /// copy of the original when there is one, from <paramref name="fallback"/> when the art has
        /// not been collected yet.
        ///
        /// **Only ever created, never refreshed from the original.** A copy that exists is the
        /// user's to tune (a greener grass, a different cut-off), and re-copying the original over it
        /// on every rebuild would eat that. All a rerun does to an existing copy is make sure it is
        /// still on the wind shader with the small-plant keywords.
        /// </summary>
        private static Material SmallVariant(string name, Material fallback)
        {
            string path = $"{MaterialDir}/{name}{Suffix}.mat";
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (variant == null)
            {
                var original = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat") ?? fallback;
                if (original == null)
                    return null;
                // A copy keeps the original's keywords (alpha clip, normal map) and queue.
                variant = new Material(original);
                AssetDatabase.CreateAsset(variant, path);
            }
            Configure(variant, Shader.Find(ShaderName), ground: true, leaf: true);
            EditorUtility.SetDirty(variant);
            return variant;
        }

        private static void Configure(Material material, Shader shader, bool ground, bool leaf)
        {
            material.shader = shader;
            material.enableInstancing = true;
            // Changing shader clears the disabled-pass list. URP keeps the object motion vector pass off
            // by default (its own material upgrade does), and it is the wrong pass for wind anyway - see
            // the note in DemoWindFoliage.shader - so put it back the way the pack's materials had it.
            material.SetShaderPassEnabled("MotionVectors", false);
            SetKeyword(material, "_WIND_GROUND", "_WindGround", ground);
            SetKeyword(material, "_WIND_LEAF", "_WindLeaf", leaf);
        }

        private static void SetKeyword(Material material, string keyword, string property, bool on)
        {
            material.SetFloat(property, on ? 1f : 0f);
            if (on)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        /// <summary>
        /// Swaps the materials on a flattened bush or detail prefab for their small-plant copies.
        /// Called by <see cref="DemoTreePrefabBuilder"/> so a rebuild of those prefabs keeps the wind.
        /// </summary>
        public static Material[] ForSmallPlant(Material[] materials)
        {
            var result = new Material[materials.Length];
            for (int i = 0; i < materials.Length; ++i)
            {
                Material source = materials[i];
                result[i] = source != null && System.Array.IndexOf(Plants, Base(source.name)) >= 0
                    ? SmallVariant(Base(source.name), source) ?? source
                    : source;
            }
            return result;
        }

        private static string Base(string name)
        {
            return name.EndsWith(Suffix) ? name.Substring(0, name.Length - Suffix.Length) : name;
        }

        /// <summary>Points every already-built bush and detail prefab at the small-plant materials.</summary>
        private static int RepointTerrainPrefabs()
        {
            int changed = 0;
            var names = new List<string>(DemoTreePrefabBuilder.Bushes);
            names.AddRange(DemoTreePrefabBuilder.Details);
            foreach (string name in names)
            {
                string path = $"{DemoTreePrefabBuilder.OutputDir}/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    continue;

                // Edited in place rather than rebuilt: the terrain's tree and detail prototypes
                // point at these prefabs, and a prefab saved afresh could come back under a new id.
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var renderer = contents.GetComponent<MeshRenderer>();
                    if (renderer == null)
                        continue;
                    Material[] before = renderer.sharedMaterials;
                    Material[] after = ForSmallPlant(before);
                    bool same = true;
                    for (int i = 0; i < before.Length; ++i)
                        same &= before[i] == after[i];
                    if (same)
                        continue;
                    renderer.sharedMaterials = after;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    ++changed;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            return changed;
        }
    }
}
