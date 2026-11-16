using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    public sealed class FormDefinitionEditorWindow : DefinitionEditorWindowBase
    {
        protected override System.Type AssetType => typeof(FormDefinition);
        protected override string DisplayTitle => "Form Definition Editor";
        protected override string DefaultAssetName => "NewForm";

        [MenuItem("Tools/MuOnBaAr/Definitions/Form Definition Editor")]
        public static void Open()
        {
            var window = GetWindow<FormDefinitionEditorWindow>();
            window.InitializeWindow();
            window.Show();
        }

        protected override void DrawDiagnostics(ScriptableObject asset)
        {
            var form = asset as FormDefinition;
            if (form == null) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Form Overview", EditorStyles.boldLabel);
            if (string.IsNullOrWhiteSpace(form.FormId))
                EditorGUILayout.HelpBox("Form ID cannot be blank.", MessageType.Warning);
            EditorGUILayout.LabelField("Skill Slots",
                form.Abilities == null ? "0" : form.Abilities.Length.ToString());
            EditorGUILayout.LabelField("Combo Definitions", form.ComboAttackCount.ToString());
            if (form.ComboAttackCount == 0)
                EditorGUILayout.HelpBox(
                    "No Combo settings: gameplay uses the existing Combo0 duration " +
                    "and zero forward step.", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Combo Attacks element 0 configures the current Combo0. " +
                    "Forward Step Distance is in meters; Step Start/End are " +
                    "normalized 0..1 timing fractions. Additional elements " +
                    "are reserved for future combo-chain implementation.",
                    MessageType.None);
            if (form.Abilities != null && form.Abilities.Length > 3)
                EditorGUILayout.HelpBox(
                    "This Form has more than three skills. Current gameplay " +
                    "has input bindings only for slots 0, 1 and 2.",
                    MessageType.Info);
        }
    }
}
