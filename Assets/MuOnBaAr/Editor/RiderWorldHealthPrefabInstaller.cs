using MuOnBaAr.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// One-time, idempotent prefab upgrade. Automatically installs the child
    /// World Space Canvas after scripts reload. Can also be rerun via Tools.
    /// Existing gameplay components and prefab references are preserved.
    /// </summary>
    [InitializeOnLoad]
    internal static class RiderWorldHealthPrefabInstaller
    {
        private const string PrefabPath =
            "Assets/MuOnBaAr/PrototypeGenerated/NetworkRiderPlayer.prefab";
        private const string CanvasName = "OverheadHealthCanvas";

        static RiderWorldHealthPrefabInstaller()
        {
            EditorApplication.delayCall += InstallIfMissing;
        }

        [MenuItem("Tools/MuOnBaAr/Configure Rider World Health Canvas")]
        private static void InstallMenu()
        {
            InstallIfMissing();
        }

        private static void InstallIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                Debug.LogWarning("[MuOnBaAr][UI] Rider prefab not found at " +
                    PrefabPath + "; world health Canvas was not installed.");
                return;
            }

            // TMP 3.0.7 throws in TMP_Settings.defaultFontAsset when its
            // resource asset is missing. Guard the Settings instance first.
            // The legacy health prefab remains untouched until Essentials exist.
            TMP_Settings tmpSettings = TMP_Settings.instance;
            if (tmpSettings == null)
            {
                Debug.LogWarning("[MuOnBaAr][UI] TMP Settings asset is missing. " +
                    "Import it with Window > TextMeshPro > Import TMP Essential Resources, " +
                    "then run Tools > MuOnBaAr > Configure Rider World Health Canvas. " +
                    "No prefab changes were made.");
                return;
            }

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
                font = Resources.Load<TMP_FontAsset>(
                    "Fonts & Materials/LiberationSans SDF");
            if (font == null)
            {
                Debug.LogWarning("[MuOnBaAr][UI] TMP default font is missing. " +
                    "Import TMP Essential Resources, then rerun " +
                    "Tools > MuOnBaAr > Configure Rider World Health Canvas. " +
                    "No prefab changes were made.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Could not open Rider prefab.");
                return;
            }

            try
            {
                Transform existingCanvas = root.transform.Find(CanvasName);
                if (existingCanvas != null)
                {
                    // Upgrade the exact existing prefab subtree. Preserve all
                    // RectTransforms, Slider objects, visuals, and prefab links.
                    if (!MigrateLegacyText(existingCanvas, font))
                        return;
                    if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    {
                        Debug.LogError("[MuOnBaAr][UI] Could not save TMP migration.");
                        return;
                    }

                    AssetDatabase.SaveAssets();
                    Debug.Log("[MuOnBaAr][UI] Existing Rider World Health Canvas " +
                        "migrated to TMP; original Slider and Canvas preserved.");
                    return;
                }

                RectTransform canvasRoot = CreateRect(
                    CanvasName, root.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(240f, 120f),
                    Vector2.zero);
                canvasRoot.localPosition = new Vector3(0f, 2.45f, 0f);
                canvasRoot.localRotation = Quaternion.identity;
                canvasRoot.localScale = Vector3.one * 0.0045f;
                Canvas canvas = canvasRoot.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 5;
                canvasRoot.gameObject.AddComponent<CanvasScaler>();

                RectTransform panel = CreateRect(
                    "HealthPanel", canvasRoot, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(230f, 64f),
                    Vector2.zero);
                Image panelBackground = panel.gameObject.AddComponent<Image>();
                panelBackground.color = new Color(0.06f, 0.08f, 0.12f, 0.82f);
                panelBackground.raycastTarget = false;

                TextMeshProUGUI name = CreateTMPText(
                    "PlayerNameHP", panel, font, 17,
                    new Vector2(220f, 24f), new Vector2(0f, 12f));
                name.text = "Rider  HP 100/100";

                RectTransform sliderRoot = CreateRect(
                    "HealthSlider", panel, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(210f, 14f),
                    new Vector2(0f, -15f));
                Slider slider = sliderRoot.gameObject.AddComponent<Slider>();
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.wholeNumbers = false;
                slider.direction = Slider.Direction.LeftToRight;
                slider.interactable = false;
                slider.transition = Selectable.Transition.None;

                RectTransform background = CreateStretch(
                    "Background", sliderRoot, Vector2.zero, Vector2.zero);
                Image backgroundImage = background.gameObject.AddComponent<Image>();
                backgroundImage.color = new Color(0.18f, 0.18f, 0.18f, 0.96f);
                backgroundImage.raycastTarget = false;

                RectTransform fillArea = CreateStretch(
                    "Fill Area", sliderRoot, new Vector2(2f, 2f),
                    new Vector2(-2f, -2f));
                RectTransform fill = CreateStretch(
                    "Fill", fillArea, Vector2.zero, Vector2.zero);
                Image fillImage = fill.gameObject.AddComponent<Image>();
                fillImage.color = new Color(0.25f, 0.85f, 0.3f, 1f);
                fillImage.raycastTarget = false;
                slider.fillRect = fill;
                slider.SetValueWithoutNotify(1f);

                RectTransform flashRect = CreateRect(
                    "FeedbackFlash", panel, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(218f, 5f),
                    new Vector2(0f, 34f));
                Image flash = flashRect.gameObject.AddComponent<Image>();
                flash.color = Color.red;
                flash.raycastTarget = false;
                flash.enabled = false;

                TextMeshProUGUI damage = CreateTMPText(
                    "DamagePopup", panel, font, 22,
                    new Vector2(125f, 30f), new Vector2(0f, 55f));
                damage.fontStyle = FontStyles.Bold;
                damage.color = new Color(1f, 0.4f, 0.3f, 1f);
                damage.enabled = false;

                TextMeshProUGUI status = CreateTMPText(
                    "StatusText", panel, font, 16,
                    new Vector2(225f, 22f), new Vector2(0f, -48f));
                status.color = new Color(1f, 0.85f, 0.26f, 1f);
                status.text = "";

                RiderWorldHealthBar bar =
                    canvasRoot.gameObject.AddComponent<RiderWorldHealthBar>();
                SerializedObject serialized = new SerializedObject(bar);
                serialized.FindProperty("healthSlider").objectReferenceValue = slider;
                serialized.FindProperty("healthFill").objectReferenceValue = fillImage;
                serialized.FindProperty("playerNameHealth").objectReferenceValue = name;
                serialized.FindProperty("feedbackFlash").objectReferenceValue = flash;
                serialized.FindProperty("damagePopup").objectReferenceValue = damage;
                serialized.FindProperty("statusText").objectReferenceValue = status;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null)
                {
                    Debug.LogError("[MuOnBaAr][UI] Could not save Rider World Health Canvas prefab.");
                    return;
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[MuOnBaAr][UI] World Space Slider / TMP health UI " +
                    "installed on NetworkRiderPlayer prefab; legacy overhead OnGUI removed.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static RectTransform CreateRect(
            string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static RectTransform CreateStretch(
            string name, Transform parent, Vector2 offsetMin,
            Vector2 offsetMax)
        {
            RectTransform rect = CreateRect(name, parent,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static TextMeshProUGUI CreateTMPText(
            string name, Transform parent, TMP_FontAsset font, int fontSize,
            Vector2 size, Vector2 position)
        {
            RectTransform rect = CreateRect(name, parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                size, position);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static bool MigrateLegacyText(Transform canvasRoot, TMP_FontAsset font)
        {
            Transform panel = canvasRoot.Find("HealthPanel");
            if (panel == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Existing health Canvas has no HealthPanel. " +
                    "Preserved prefab without changes.");
                return false;
            }

            Slider slider = panel.Find("HealthSlider") != null
                ? panel.Find("HealthSlider").GetComponent<Slider>() : null;
            Transform fillObject = panel.Find("HealthSlider/Fill Area/Fill");
            Image healthFill = fillObject != null ? fillObject.GetComponent<Image>() : null;
            Image flash = GetPanelComponent<Image>(panel, "FeedbackFlash");
            if (slider == null || healthFill == null || flash == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Existing HealthSlider / Fill / " +
                    "FeedbackFlash missing; aborting TMP upgrade without saving.");
                return false;
            }

            bool changed = false;
            TextMeshProUGUI name = ConvertLegacyText(
                panel, "PlayerNameHP", font, ref changed);
            TextMeshProUGUI popup = ConvertLegacyText(
                panel, "DamagePopup", font, ref changed);
            TextMeshProUGUI status = ConvertLegacyText(
                panel, "StatusText", font, ref changed);
            if (name == null || popup == null || status == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Missing health UI labels; " +
                    "existing prefab was not saved.");
                return false;
            }

            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[MuOnBaAr][UI] Existing health Canvas component missing.");
                return false;
            }

            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                changed = true;
            }

            RiderWorldHealthBar bar =
                canvasRoot.GetComponent<RiderWorldHealthBar>();
            if (bar == null)
            {
                bar = canvasRoot.gameObject.AddComponent<RiderWorldHealthBar>();
                changed = true;
            }

            SerializedObject serialized = new SerializedObject(bar);
            changed |= SetReference(serialized, "healthSlider", slider);
            changed |= SetReference(serialized, "healthFill", healthFill);
            changed |= SetReference(serialized, "playerNameHealth", name);
            changed |= SetReference(serialized, "feedbackFlash", flash);
            changed |= SetReference(serialized, "damagePopup", popup);
            changed |= SetReference(serialized, "statusText", status);
            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();

            // No asset rewrite or duplicate Canvas when already migrated.
            return changed;
        }

        private static T GetPanelComponent<T>(Transform panel, string name)
            where T : Component
        {
            Transform found = panel.Find(name);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static bool SetReference(
            SerializedObject serialized, string fieldName, UnityEngine.Object reference)
        {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
                throw new System.InvalidOperationException(
                    "World Health Bar serialized field missing: " + fieldName);
            if (property.objectReferenceValue == reference)
                return false;

            property.objectReferenceValue = reference;
            return true;
        }

        private static TextMeshProUGUI ConvertLegacyText(
            Transform panel, string name, TMP_FontAsset font, ref bool changed)
        {
            Transform child = panel.Find(name);
            if (child == null)
                return null;

            TextMeshProUGUI existing = child.GetComponent<TextMeshProUGUI>();
            if (existing != null)
            {
                if (existing.font == null)
                {
                    existing.font = font;
                    changed = true;
                }
                return existing;
            }

            Text legacy = child.GetComponent<Text>();
            if (legacy == null)
                return null;

            string content = legacy.text;
            int size = legacy.fontSize;
            Color tint = legacy.color;
            bool bold = legacy.fontStyle == FontStyle.Bold;
            bool enabled = legacy.enabled;
            // Remove the old Graphic before creating the new TMP Graphic.
            UnityEngine.Object.DestroyImmediate(legacy, true);

            TextMeshProUGUI label = child.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = content;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.color = tint;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.enabled = enabled;
            changed = true;
            return label;
        }
    }
}
