using MultiplayerARPG.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Dresses the kit's loading canvas (`Demo/Prefabs/UI/Global/CanvasLoading.prefab`) as a
    /// full-screen loading screen: the `OpenMMORPG-loading` picture behind a map title, a status
    /// line and the kit's progress bar, with a <see cref="UILoadingScreenFade"/> on the root to
    /// make it appear for every scene change (the kit's own wiring never showed it - see that
    /// class).
    ///
    /// The kit's `MapLoading` panel stays the kit's `rootObject` on both loading components and
    /// keeps the bar; it goes transparent so the picture shows through, and the picture lives on
    /// a sibling behind it so it can fade out after the kit has switched the panel off. The
    /// picture is cropped to the screen rather than stretched (`AspectRatioFitter` envelope), so
    /// a 16:10 monitor or a phone loses a sliver of edge instead of squashing the art.
    ///
    /// The text sits straight on the picture with an outline; there is no darkening strip
    /// under it. One was tried three ways (gradient sprite, runtime texture, vertex-coloured
    /// graphic) and none of them darkened the picture in the editor's game view, although a
    /// coloured strip showed and the picture itself blends normally over the backing. Left out
    /// rather than shipped unverified; the outlined text reads fine on this art.
    ///
    /// Idempotent: finds its objects by name and restyles them, so it can be rerun after
    /// `Skin UI` (which frames the bar and leaves the transparent panel alone).
    /// </summary>
    public static class DemoLoadingScreenBuilder
    {
        private const string PrefabPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/Global/CanvasLoading.prefab";
        private const string PicturePath = "Assets/OpenMMORPG/Demo/Textures/OpenMMORPG-loading.jpg";
        // Left by the abandoned shade (see the class summary); removed if still about.
        private const string LegacyShadePath = "Assets/OpenMMORPG/Demo/Textures/UI/LoadingShade.png";
        private const string LegacyShadeName = "Shade";

        private const string KitRootName = "MapLoading";
        private const string BackdropName = "DemoLoadingBackdrop";
        private const string PictureName = "Picture";
        private const string BarName = "LoadingBar";
        private const string ProgressName = "TextProgress";
        private const string TitleName = "DemoLoadingTitle";
        private const string StatusName = "DemoLoadingStatus";
        private const string FrameName = "--DemoFrame";

        // Canvas reference resolution is 800x600 matched on width: 1 unit = 2.4px at 1080p.
        private const float BarWidth = 440f;
        private const float BarHeight = 18f;
        private const float BarBottom = 58f;

        [MenuItem("Open MMORPG/Demo/Build Loading Screen")]
        public static void Build()
        {
            Sprite picture = EnsurePictureSprite();
            if (picture == null)
            {
                Debug.LogError($"[{nameof(DemoLoadingScreenBuilder)}] No sprite at {PicturePath}.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(LegacyShadePath) != null)
                AssetDatabase.DeleteAsset(LegacyShadePath);

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform kitRoot = root.transform.Find(KitRootName);
                if (kitRoot == null)
                {
                    Debug.LogError($"[{nameof(DemoLoadingScreenBuilder)}] {PrefabPath} has no `{KitRootName}` - the kit's rootObject.");
                    return;
                }

                // The kit's panel: a solid dark quad from the template. Transparent, it still
                // blocks clicks (raycast target), and the skin no longer frames it.
                var kitPanel = kitRoot.GetComponent<Image>();
                if (kitPanel != null)
                {
                    kitPanel.color = new Color(0f, 0f, 0f, 0f);
                    kitPanel.raycastTarget = true;
                }
                Transform screenFrame = kitRoot.Find(FrameName);
                if (screenFrame != null)
                    Object.DestroyImmediate(screenFrame.gameObject);

                CanvasGroup overlay = BuildBackdrop(root.transform, picture);
                Text title = BuildTitle(kitRoot);
                Text status = BuildStatus(kitRoot);
                StyleBar(kitRoot, root);

                // Behind the kit's panel, which holds the bar; the MMO's ack spinner stays on top.
                overlay.transform.SetSiblingIndex(0);
                kitRoot.SetSiblingIndex(1);

                WireKit(root, status);

                var screen = root.GetComponent<UILoadingScreenFade>();
                if (screen == null)
                    screen = root.AddComponent<UILoadingScreenFade>();
                screen.kitRoot = kitRoot.gameObject;
                screen.overlay = overlay;
                screen.uiTextTitle = title;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoLoadingScreenBuilder)}] Loading screen built into {PrefabPath}.");
        }

        /// <summary>The picture and its shade, on a sibling of the kit's panel so it can fade.</summary>
        private static CanvasGroup BuildBackdrop(Transform canvas, Sprite picture)
        {
            RectTransform backdrop = Stretch(FindOrCreate(canvas, BackdropName));
            var group = backdrop.GetComponent<CanvasGroup>();
            if (group == null)
                group = backdrop.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = false;

            // Letterbox safety behind the picture: never seen while the fitter covers the screen.
            var backing = backdrop.GetComponent<Image>();
            if (backing == null)
                backing = backdrop.gameObject.AddComponent<Image>();
            backing.sprite = null;
            backing.color = Color.black;
            backing.raycastTarget = true;

            RectTransform pictureRect = FindOrCreate(backdrop, PictureName);
            pictureRect.anchorMin = new Vector2(0.5f, 0.5f);
            pictureRect.anchorMax = new Vector2(0.5f, 0.5f);
            pictureRect.pivot = new Vector2(0.5f, 0.5f);
            pictureRect.anchoredPosition = Vector2.zero;
            var pictureImage = pictureRect.GetComponent<Image>();
            if (pictureImage == null)
                pictureImage = pictureRect.gameObject.AddComponent<Image>();
            pictureImage.sprite = picture;
            pictureImage.type = Image.Type.Simple;
            pictureImage.preserveAspect = false;
            pictureImage.color = Color.white;
            pictureImage.raycastTarget = false;
            var fitter = pictureRect.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = pictureRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = picture.rect.width / picture.rect.height;

            Transform legacyShade = backdrop.Find(LegacyShadeName);
            if (legacyShade != null)
                Object.DestroyImmediate(legacyShade.gameObject);

            pictureRect.SetSiblingIndex(0);
            return group;
        }

        private static Text BuildTitle(Transform kitRoot)
        {
            RectTransform rect = BottomCentre(FindOrCreate(kitRoot, TitleName), new Vector2(700f, 40f), BarBottom + BarHeight + 10f);
            Text text = EnsureText(rect, 28, FontStyle.Bold, Color.white);
            text.text = "Loading";
            return text;
        }

        private static Text BuildStatus(Transform kitRoot)
        {
            RectTransform rect = BottomCentre(FindOrCreate(kitRoot, StatusName), new Vector2(700f, 20f), BarBottom - 24f);
            Text text = EnsureText(rect, 13, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f, 1f));
            text.text = "Loading...";
            return text;
        }

        /// <summary>The kit's bar, widened and sat on the shade; the gauge takes the theme's lit metal.</summary>
        private static void StyleBar(Transform kitRoot, GameObject root)
        {
            Transform bar = kitRoot.Find(BarName);
            if (bar == null)
                return;
            var barRect = (RectTransform)bar;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, BarBottom);
            barRect.sizeDelta = new Vector2(BarWidth, BarHeight);
            var backing = bar.GetComponent<Image>();
            if (backing != null)
                backing.color = new Color(0.06f, 0.07f, 0.09f, 0.9f);

            var net = root.GetComponent<UINetworkSceneLoading>();
            Image gage = net != null ? net.imageGage : null;
            if (gage != null)
            {
                Color32 lit = DemoPalette.FrameLit;
                gage.color = new Color(lit.r / 255f, lit.g / 255f, lit.b / 255f, 1f);
            }

            Transform progress = bar.Find(ProgressName);
            if (progress != null)
            {
                var text = progress.GetComponent<Text>();
                if (text != null)
                {
                    text.fontSize = 12;
                    text.color = Color.white;
                }
            }
        }

        /// <summary>The kit's status slot was empty; the loading messages read as the player's, not the engine's.</summary>
        private static void WireKit(GameObject root, Text status)
        {
            var wrapper = status.GetComponent<TextWrapper>();
            if (wrapper == null)
                wrapper = status.gameObject.AddComponent<TextWrapper>();
            wrapper.unityText = status;

            var net = root.GetComponent<UINetworkSceneLoading>();
            if (net != null)
            {
                net.uiTextStatus = wrapper;
                net.msgSceneLoading.defaultText = "Loading...";
                net.msgSceneLoaded.defaultText = "Ready";
                net.msgGetFileSize.defaultText = "Downloading...";
                net.msgFileLoading.defaultText = "Downloading...";
                net.msgFileLoaded.defaultText = "Downloaded";
            }
        }

        private static Text EnsureText(RectTransform rect, int size, FontStyle style, Color color)
        {
            var text = rect.GetComponent<Text>();
            if (text == null)
                text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var outline = rect.GetComponent<Outline>();
            if (outline == null)
                outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1f, 1f);
            outline.useGraphicAlpha = true;
            return text;
        }

        private static RectTransform BottomCentre(RectTransform rect, Vector2 size, float bottom)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static RectTransform FindOrCreate(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null)
                return (RectTransform)found;
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>The picture ships as a plain texture; the UI wants a sprite, uncompressed-quality and un-mipped.</summary>
        private static Sprite EnsurePictureSprite()
        {
            var importer = AssetImporter.GetAtPath(PicturePath) as TextureImporter;
            if (importer == null)
                return null;
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }
            if (importer.maxTextureSize < 2048)
            {
                importer.maxTextureSize = 2048;
                dirty = true;
            }
            if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                dirty = true;
            }
            if (dirty)
                importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(PicturePath);
        }
    }
}
