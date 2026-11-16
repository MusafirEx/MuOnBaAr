using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    public sealed class AbilityDefinitionEditorWindow : DefinitionEditorWindowBase
    {
        protected override System.Type AssetType => typeof(AbilityDefinition);
        protected override string DisplayTitle => "Ability Definition Editor";
        protected override string DefaultAssetName => "NewAbility";

        [MenuItem("Tools/MuOnBaAr/Definitions/Ability Definition Editor")]
        public static void Open()
        {
            var window = GetWindow<AbilityDefinitionEditorWindow>();
            window.InitializeWindow();
            window.Show();
        }

        protected override void DrawDiagnostics(ScriptableObject asset)
        {
            var ability = asset as AbilityDefinition;
            if (ability == null) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Ability Overview", EditorStyles.boldLabel);
            float length = ability.StartupSeconds + ability.ActiveSeconds +
                           ability.RecoverySeconds;
            EditorGUILayout.LabelField("Total Skill Animation Duration",
                length.ToString("0.###") + " s");
            if (string.IsNullOrWhiteSpace(ability.AbilityId))
                EditorGUILayout.HelpBox("Ability ID cannot be blank.", MessageType.Warning);
            if (length <= 0f)
                EditorGUILayout.HelpBox(
                    "Startup + Active + Recovery equals zero. Set a positive " +
                    "duration to avoid a one-tick skill.", MessageType.Warning);
            EditorGUILayout.HelpBox(
                "Runtime currently uses the ability timing and Form skill slot " +
                "to play Skill0, Skill1 or Skill2. Energy consumption, cooldown, " +
                "damage and Animator Trigger execution are not yet wired.",
                MessageType.None);
        }
    }
}
