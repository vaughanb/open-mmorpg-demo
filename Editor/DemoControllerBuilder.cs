using System.Collections.Generic;
using Insthync.CameraAndInput;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the player controller the demo plays through.
    ///
    /// The kit ships two controllers. The default one is the MMO scheme — WASD or click to
    /// move, with a locked target that attacks aim themselves at. The shooter one aims where
    /// the camera looks. The demo plays the first, with the mouse and keys most MMO players
    /// already know, by <see cref="DemoPlayerController"/> and <see cref="ConfigureKeys"/>;
    /// the shooter build is kept under its own menu item as the way back to
    /// over-the-shoulder aiming.
    ///
    /// Either way there is no click-to-move and no click-to-talk, so NPCs, loot and
    /// harvestables are reached by walking up and pressing the activate key. The kit's own
    /// detectors handle that, at the ranges `GameInstance` already carries (3m to talk,
    /// 2m to pick up).
    /// </summary>
    public static class DemoControllerBuilder
    {
        private const string PrefabDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay";
        private const string ControllerPath = PrefabDir + "/ShooterPlayerCharacterController.prefab";
        private const string PlayerControllerPath = PrefabDir + "/DemoPlayerController.prefab";
        private const string KitControllerPath = PrefabDir + "/PlayerCharacterController.prefab";
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

        /// <summary>
        /// The controller the demo plays through now (2026-09-14): the kit's default,
        /// target-based one, given the mouse conventions most MMO players already know by
        /// <see cref="DemoPlayerController"/>. The shooter build below is kept as a menu
        /// item of its own, the way back to over-the-shoulder aiming.
        ///
        /// It starts from the kit's own controller prefab and copies every serialized
        /// field across - the camera prefabs, the target marker, the UI blocking rules -
        /// then sets what the demo does differently: both schemes on so a click selects
        /// while WASD moves; no first-click attack, so a left click only targets; Tab
        /// reaching thirty metres and a target kept to forty; and no ground marker, since
        /// the controller cancels ground clicks.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Player Controller")]
        public static void Build()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(KitControllerPath);
            if (template == null)
            {
                Debug.LogError($"[{nameof(DemoControllerBuilder)}] No kit controller prefab at {KitControllerPath}.");
                return;
            }
            var root = new GameObject("DemoPlayerController");
            try
            {
                var controller = root.AddComponent<MultiplayerARPG.Demo.DemoPlayerController>();
                CopySerialized(template.GetComponent<PlayerCharacterController>(), controller);
                var templateIndicator = template.GetComponent<CharacterTargetIndicator>();
                if (templateIndicator != null)
                    CopySerialized(templateIndicator, root.AddComponent<CharacterTargetIndicator>());

                FollowCameraControls gameplayCamera = Camera("GameplayCamera");
                ConfigureCollision(gameplayCamera);
                ConfigureZoom(gameplayCamera);
                ConfigureUnderwater(gameplayCamera);

                var serialized = new SerializedObject(controller);
                Set(serialized, "gameplayCameraPrefab", gameplayCamera);
                Set(serialized, "minimapCameraPrefab", Camera("MinimapCamera"));
                serialized.FindProperty("controllerMode").enumValueIndex =
                    (int)PlayerCharacterController.PlayerCharacterControllerMode.Both;
                serialized.FindProperty("pointClickSetTargetImmediately").boolValue = false;
                serialized.FindProperty("wasdLockAttackTarget").boolValue = true;
                serialized.FindProperty("distanceToLockActionTarget").floatValue = 30f;
                serialized.FindProperty("wasdClearTargetDistance").floatValue = 40f;
                serialized.FindProperty("targetObjectPrefab").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerControllerPath);
                AssetDatabase.ImportAsset(PlayerControllerPath, ImportAssetOptions.ForceUpdate);
                PointGameInstanceAt(saved);
                ConfigureKeys();
                Debug.Log($"[{nameof(DemoControllerBuilder)}] Built {PlayerControllerPath} and pointed " +
                          "GameInstance at it. The demo now plays with the target-based controller and " +
                          "the usual MMO mouse and keys.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Every serialized field of one component onto another that has the same fields, script aside.</summary>
        private static void CopySerialized(Component from, Component to)
        {
            if (from == null || to == null)
                return;
            var source = new SerializedObject(from);
            var target = new SerializedObject(to);
            SerializedProperty property = source.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                    continue;
                if (target.FindProperty(property.propertyPath) != null)
                    target.CopyFromSerializedProperty(property);
            }
            target.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The camera sits further back than the shooter's and the wheel walks it in and
        /// out a metre a notch, from just behind the head to a field's width.
        ///
        /// The kit's prefab shipped <c>zoomSpeed</c> at 500. The scroll axis the demo reads
        /// (the legacy "Mouse ScrollWheel", sensitivity 0.1) reports 0.1 a notch, so that
        /// was fifty metres a notch: every notch slammed the camera into whichever limit was
        /// nearer, and the game seemed to have two zoom levels. The near limit is kept
        /// outside the character; the kit never hides the model, so "first person" here is
        /// the inside of its head.
        /// </summary>
        private const float ZoomMetresPerNotch = 1f;

        private const float ScrollAxisPerNotch = 0.1f;

        private static void ConfigureZoom(FollowCameraControls camera)
        {
            if (camera == null)
                return;
            var serialized = new SerializedObject(camera);
            serialized.FindProperty("targetOffset").vector3Value = new Vector3(0f, 1.2f, 0f);
            serialized.FindProperty("zoomDistance").floatValue = 6f;
            serialized.FindProperty("startZoomDistance").floatValue = 6f;
            serialized.FindProperty("limitZoomDistance").boolValue = true;
            serialized.FindProperty("minZoomDistance").floatValue = 2f;
            serialized.FindProperty("maxZoomDistance").floatValue = 14f;
            serialized.FindProperty("zoomSpeed").floatValue = ZoomMetresPerNotch / ScrollAxisPerNotch;
            serialized.FindProperty("zoomSpeedScale").floatValue = 1f;
            serialized.FindProperty("smoothZoom").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(camera);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Open MMORPG/Demo/Build Player Controller (Shooter)")]
        public static void BuildShooter()
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
            FollowCameraControls gameplayCamera = Camera("GameplayCamera");
            ConfigureCollision(gameplayCamera);
            ConfigureUnderwater(gameplayCamera);
            Set(serialized, "gameplayCameraPrefab", gameplayCamera);
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

        /// <summary>
        /// The camera's wall-hit spring: a sphere cast from the character to where the
        /// camera wants to be, stopped at the first thing solid. The kit ships it on, but
        /// with a metre's dead zone below which hits are ignored and a half-metre sphere,
        /// which in a two-metre passage or a house means the camera is inside the wall
        /// before the spring is allowed to act. The dead zone is closed up and the sphere
        /// shrunk, and the mask is everything solid: not the characters, whose capsules
        /// would otherwise stop the camera on a passing bandit, nor the layers the kit
        /// keeps for triggers and UI; the trees stay in, as the kit had them. The dungeon's ceilings are on Ignore Raycast on
        /// purpose - see DemoDungeonBuilder - so that layer is kept in.
        /// </summary>
        private static void ConfigureCollision(FollowCameraControls camera)
        {
            if (camera == null)
                return;
            var serialized = new SerializedObject(camera);
            serialized.FindProperty("enableWallHitSpring").boolValue = true;
            serialized.FindProperty("minDistanceToPerformWallHitSpring").floatValue = 0.25f;
            serialized.FindProperty("wallHitSpringPushForwardDistance").floatValue = 0.2f;
            serialized.FindProperty("wallHitSpringRadius").floatValue = 0.3f;
            int mask = ~0;
            foreach (string layer in new[] { "Player", "Monster", "Npc", "Vehicle", "ItemDrop",
                                             "CharacterUI", "MiniMap", "WarpPortalOrSafeArea", "DamageEntity",
                                             "Ragdoll", "UI", "TransparentFX", "Water" })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0)
                    mask &= ~(1 << index);
            }
            serialized.FindProperty("wallHitLayerMask").intValue = mask;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(camera);
            AssetDatabase.SaveAssets();
        }


        private const string UnderwaterMaterialPath =
            "Assets/OpenMMORPG/Demo/Materials/Underwater.mat";

        /// <summary>
        /// Puts <see cref="MultiplayerARPG.Demo.DemoUnderwater"/> on the gameplay camera,
        /// with the material it tints the screen through.
        ///
        /// **The material has to exist as an asset.** The component could find the shader
        /// by name and make one at runtime, and it would work in the editor and then draw
        /// nothing in a build: a shader that nothing references by asset is not included
        /// in the player, and `Shader.Find` returns null for it. A material asset is the
        /// reference that pulls it in.
        ///
        /// A material already on the right shader is left alone, the same rule the sea
        /// material follows, so the underwater colour can be tuned in the inspector
        /// without a rebuild putting it back.
        /// </summary>
        private static void ConfigureUnderwater(FollowCameraControls camera)
        {
            if (camera == null)
                return;

            Shader shader = Shader.Find("Demo/Underwater");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoControllerBuilder)}] Demo/Underwater is missing; " +
                               "the camera will not tint under water.");
                return;
            }

            Material tint = AssetDatabase.LoadAssetAtPath<Material>(UnderwaterMaterialPath);
            if (tint == null || tint.shader != shader)
            {
                AssetDatabase.DeleteAsset(UnderwaterMaterialPath);
                DemoItemBuilder.EnsureFolder(
                    System.IO.Path.GetDirectoryName(UnderwaterMaterialPath).Replace('\\', '/'));
                tint = new Material(shader);
                AssetDatabase.CreateAsset(tint, UnderwaterMaterialPath);
            }

            GameObject go = camera.gameObject;
            var underwater = go.GetComponent<MultiplayerARPG.Demo.DemoUnderwater>();
            if (underwater == null)
                underwater = go.AddComponent<MultiplayerARPG.Demo.DemoUnderwater>();
            underwater.overlayMaterial = tint;
            // Whichever clip the Underwater family holds, or none - the fog and the tint
            // work without it, and Wire Audio reports the family as missing.
            AudioClip[] loop = DemoAudioWiring.Clips(DemoAudioWiring.Underwater);
            underwater.underwaterLoop = loop.Length > 0 ? loop[0] : null;
            EditorUtility.SetDirty(underwater);
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();
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
        /// The keys under the left hand, the way most MMOs have them: Space jumps and T
        /// attacks the target, or the nearest enemy through the kit's WASD lock; Tab cycles
        /// enemies as the kit already had it. The kit ships Space as attack and C as jump.
        ///
        /// These are the key settings on <c>GameInstance</c>'s <see cref="InputSettingManager"/>,
        /// which are what the demo actually reads: the prefab's Input Actions reference
        /// points at an asset this fork never carried, so the Input System path finds no
        /// action and the legacy path with these settings runs instead. The demo's own
        /// <c>Demo/InputActions.inputactions</c> is not referenced by anything. The dead
        /// reference is cleared so the inspector stops showing a missing asset.
        /// </summary>
        private static void ConfigureKeys()
        {
            GameObject instance = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            InputSettingManager manager = instance == null ? null : instance.GetComponent<InputSettingManager>();
            if (manager == null)
            {
                Debug.LogError($"[{nameof(DemoControllerBuilder)}] No InputSettingManager on the GameInstance prefab.");
                return;
            }
            var keys = new Dictionary<string, KeyCode>
            {
                { "Attack", KeyCode.T },
                { "Jump", KeyCode.Space },
                { "FindEnemy", KeyCode.Tab },
            };
            var serialized = new SerializedObject(manager);
            SerializedProperty settings = serialized.FindProperty("settings");
            for (int i = 0; i < settings.arraySize; i++)
            {
                SerializedProperty element = settings.GetArrayElementAtIndex(i);
                if (keys.TryGetValue(element.FindPropertyRelative("keyName").stringValue, out KeyCode key))
                    element.FindPropertyRelative("keyCode").intValue = (int)key;
            }
            SerializedProperty asset = serialized.FindProperty("inputActionAsset");
            if (asset != null)
                asset.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();
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
