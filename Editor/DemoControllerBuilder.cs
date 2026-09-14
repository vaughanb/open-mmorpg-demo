using Insthync.CameraAndInput;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the player controller the demo plays through: third person, over the shoulder,
    /// aiming with a crosshair.
    ///
    /// The kit ships two controllers. The default one is the MMO scheme — WASD or click to
    /// move, with a locked target that attacks aim themselves at. The shooter one aims where
    /// the camera looks. This demo wants the second, and not only for feel: two of its three
    /// classes fight at range, the bow already declares `isHeadshotInstantDeath`, which means
    /// nothing unless a player can actually place a shot, and **only this controller can hold
    /// a shot before loosing it** — charging is started here and nowhere else in the kit.
    ///
    /// What changes for the player, beyond aiming: no click-to-move and no click-to-talk, so
    /// NPCs, loot and harvestables are reached by walking up and pressing the activate key.
    /// The kit's own detectors handle that, at the ranges `GameInstance` already carries
    /// (3m to talk, 2m to pick up).
    /// </summary>
    public static class DemoControllerBuilder
    {
        private const string PrefabDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay";
        private const string ControllerPath = PrefabDir + "/ShooterPlayerCharacterController.prefab";
        private const string GameInstancePath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";

        /// <summary>
        /// How far the camera sits behind the character, and where it looks. Over the right
        /// shoulder, high enough to see over it, and close enough that the crosshair means
        /// something at bow range.
        /// </summary>
        private const float ZoomDistance = 3.2f;

        private static readonly Vector3 ShoulderOffset = new Vector3(0.65f, 1.35f, 0f);

        /// <summary>
        /// Size of the crosshair's gap at rest, in canvas units, and the thickness and length
        /// of each of its four ticks. The kit drives the gap by resizing the rect, so the
        /// ticks are anchored to its edges and travel outwards as a shot spreads.
        /// </summary>
        private const float TickThickness = 2f;

        private const float TickLength = 9f;

        [MenuItem("Open MMORPG/Demo/Build Player Controller")]
        public static void Build()
        {
            var root = new GameObject("ShooterPlayerCharacterController");
            try
            {
                var controller = root.AddComponent<ShooterPlayerCharacterController>();
                RectTransform crosshair = BuildCrosshair(root);
                Wire(controller, crosshair);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ControllerPath);
                AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
                PointGameInstanceAt(saved);
                Debug.Log($"[{nameof(DemoControllerBuilder)}] Built {ControllerPath} and pointed " +
                          "GameInstance at it. The demo now plays in third person over the shoulder.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds the crosshair inside the controller itself.
        ///
        /// It cannot live in the scene's HUD: the controller is a prefab that `GameInstance`
        /// instantiates at run time, and a prefab cannot hold a reference to a scene object.
        /// Its own canvas has no raycaster, so it never swallows a click meant for the UI
        /// behind it.
        /// </summary>
        private static RectTransform BuildCrosshair(GameObject root)
        {
            var canvasGo = new GameObject("CrosshairCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under the rest of the interface, so a window drawn over the middle of the
            // screen covers the crosshair rather than being pricked by it.
            canvas.sortingOrder = -1;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var crosshairGo = new GameObject("Crosshair", typeof(RectTransform));
            crosshairGo.transform.SetParent(canvasGo.transform, false);
            var crosshair = crosshairGo.GetComponent<RectTransform>();
            crosshair.anchorMin = crosshair.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.pivot = new Vector2(0.5f, 0.5f);
            crosshair.anchoredPosition = Vector2.zero;
            crosshair.sizeDelta = new Vector2(20f, 20f);

            // Anchored to the four edges and pivoted outwards, so widening the rect moves
            // them apart — which is what the kit's spread is expressed as.
            Tick(crosshair, "Up", new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), true);
            Tick(crosshair, "Down", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), true);
            Tick(crosshair, "Left", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), false);
            Tick(crosshair, "Right", new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), false);
            return crosshair;
        }

        private static void Tick(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, bool upright)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = upright
                ? new Vector2(TickThickness, TickLength)
                : new Vector2(TickLength, TickThickness);
            var image = go.GetComponent<Image>();
            // No sprite: an Image with none draws a plain quad, which is all a tick is.
            image.color = new Color(1f, 1f, 1f, 0.85f);
            image.raycastTarget = false;
        }

        private static void Wire(ShooterPlayerCharacterController controller, RectTransform crosshair)
        {
            var serialized = new SerializedObject(controller);
            Set(serialized, "gameplayCameraPrefab", Camera("GameplayCamera"));
            Set(serialized, "minimapCameraPrefab", Camera("MinimapCamera"));
            serialized.FindProperty("crosshairRect").objectReferenceValue = crosshair;

            // Adventure rather than Combat: the character faces where it walks until a shot
            // is taken, instead of standing permanently side-on to the camera. A village is
            // most of this demo, and Combat mode makes walking through one look like a
            // stand-off.
            serialized.FindProperty("mode").enumValueIndex =
                (int)ShooterPlayerCharacterController.ControllerMode.Adventure;
            serialized.FindProperty("canSwitchViewMode").boolValue = false;
            serialized.FindProperty("viewMode").enumValueIndex = (int)ShooterControllerViewMode.Tps;
            serialized.FindProperty("tpsZoomDistance").floatValue = ZoomDistance;
            serialized.FindProperty("tpsTargetOffset").vector3Value = ShoulderOffset;
            serialized.FindProperty("sprintActiveMode").enumValueIndex =
                (int)ShooterPlayerCharacterController.ExtraMoveActiveMode.Hold;
            serialized.FindProperty("crouchActiveMode").enumValueIndex =
                (int)ShooterPlayerCharacterController.ExtraMoveActiveMode.Toggle;
            serialized.FindProperty("crawlActiveMode").enumValueIndex =
                (int)ShooterPlayerCharacterController.ExtraMoveActiveMode.None;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static FollowCameraControls Camera(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoControllerBuilder)}] No {name} prefab under {PrefabDir}.");
                return null;
            }
            return prefab.GetComponent<FollowCameraControls>();
        }

        private static void Set(SerializedObject serialized, string field, Object value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[{nameof(DemoControllerBuilder)}] No \"{field}\" on the controller.");
                return;
            }
            property.objectReferenceValue = value;
        }

        /// <summary>
        /// Hands the new controller to <c>GameInstance</c>, which is the one place the game
        /// decides how it is played.
        /// </summary>
        private static void PointGameInstanceAt(GameObject controller)
        {
            GameObject instance = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoControllerBuilder)}] No GameInstance prefab to point at it.");
                return;
            }
            var serialized = new SerializedObject(instance.GetComponent<GameInstance>());
            SerializedProperty property = serialized.FindProperty("defaultControllerPrefab");
            property.objectReferenceValue = controller.GetComponent<BasePlayerCharacterController>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();
        }
    }
}
