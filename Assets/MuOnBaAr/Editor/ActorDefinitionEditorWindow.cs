using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    public sealed class ActorDefinitionEditorWindow : DefinitionEditorWindowBase
    {
        protected override System.Type AssetType => typeof(ActorDefinition);
        protected override string DisplayTitle => "Actor Definition Editor";
        protected override string DefaultAssetName => "NewActor";

        [MenuItem("Tools/MuOnBaAr/Definitions/Actor Definition Editor")]
        public static void Open()
        {
            var window = GetWindow<ActorDefinitionEditorWindow>();
            window.InitializeWindow();
            window.Show();
        }

        protected override void DrawDiagnostics(ScriptableObject asset)
        {
            var actor = asset as ActorDefinition;
            if (actor == null) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Actor Validation", EditorStyles.boldLabel);
            if (actor.Validate(out string reason))
                EditorGUILayout.HelpBox(
                    "Valid actor. Forms: " + actor.FormCount +
                    " | Starting Form: " + (actor.StartingFormIndex + 1),
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
            EditorGUILayout.HelpBox(
                "Starting Form Index is zero-based. Forms are referenced assets, " +
                "not embedded copies. Open the Form editor to configure each Form.",
                MessageType.None);
        }
    }
}
