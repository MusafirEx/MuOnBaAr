using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    [CustomEditor(typeof(FormDefinition))]
    public sealed class FormDefinitionBodyPartsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                ((FormDefinition)target).VisualPrefab == null))
            {
                if (GUILayout.Button("Collect Body Parts From Visual Prefab"))
                {
                    foreach (Object selected in targets)
                        ((FormDefinition)selected).CollectBodyPartsFromPrefab();
                    serializedObject.Update();
                }
            }
            EditorGUILayout.HelpBox(
                "Collect mesh GameObjects after assigning Visual Prefab. " +
                "Use the collected list's array indices in Combo / Ability " +
                "Body Part Visibility Timeline. Paths are kept stable on refresh. " +
                "Only render-mesh GameObjects below the prefab root are collected.",
                MessageType.Info);
        }
    }
}
