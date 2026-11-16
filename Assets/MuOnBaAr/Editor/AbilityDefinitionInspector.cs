using UnityEditor;
using UnityEngine;
using MuOnBaAr.Runtime;

namespace MuOnBaAr.Editor
{
    // Legacy helper retained for reference only. The mode-aware
    // AbilityDefinitionEditor is the sole active CustomEditor.
    public sealed class AbilityDefinitionInspector : UnityEditor.Editor
    {
        private static readonly string[] HiddenFeatureFields =
        {
            "m_Script",
            "clockUpEnabled", "clockUpActivationDelaySeconds",
            "clockUpDurationSeconds", "clockUpOtherRiderScale",
            "clockUpCasterScale", "clockUpStartVfxPrefab",
            "clockUpActiveVfxPrefab", "clockUpEndVfxPrefab",
            "clockUpStartVfxSeconds", "clockUpEndVfxSeconds",
            "clockUpVfxOffset",
            "hookEnabled", "hookRange", "hookPullSpeed",
            "hookDurationSeconds", "hookStopDistance",
            "gravityPullEnabled", "gravityPullRadius", "gravityPullSpeed",
            "gravityPullDurationSeconds", "gravityPullStopDistance",
            "gravityPullCenterOffset"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, HiddenFeatureFields);
            EditorGUILayout.Space();

            DrawFeature("Clock Up", "clockUpEnabled",
                "clockUpActivationDelaySeconds", "clockUpDurationSeconds",
                "clockUpOtherRiderScale", "clockUpCasterScale",
                "clockUpStartVfxPrefab", "clockUpActiveVfxPrefab",
                "clockUpEndVfxPrefab", "clockUpStartVfxSeconds",
                "clockUpEndVfxSeconds", "clockUpVfxOffset");

            DrawFeature("Hook (Single Target)", "hookEnabled",
                "hookRange", "hookPullSpeed", "hookDurationSeconds",
                "hookStopDistance");

            DrawFeature("Gravity Pull (Area)", "gravityPullEnabled",
                "gravityPullRadius", "gravityPullSpeed",
                "gravityPullDurationSeconds", "gravityPullStopDistance",
                "gravityPullCenterOffset");

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFeature(string title, string toggleName,
            params string[] detailNames)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            SerializedProperty toggle = serializedObject.FindProperty(toggleName);
            if (toggle == null) return;
            EditorGUILayout.PropertyField(toggle, new GUIContent("Enabled"));
            // For multi-edit with different toggles, expose all fields rather
            // than accidentally hiding information for some selected assets.
            if (!toggle.boolValue && !toggle.hasMultipleDifferentValues)
                return;
            ++EditorGUI.indentLevel;
            foreach (string propertyName in detailNames)
            {
                SerializedProperty detail = serializedObject.FindProperty(propertyName);
                if (detail != null)
                    EditorGUILayout.PropertyField(detail, true);
            }
            --EditorGUI.indentLevel;
            EditorGUILayout.Space();
        }
    }
}
