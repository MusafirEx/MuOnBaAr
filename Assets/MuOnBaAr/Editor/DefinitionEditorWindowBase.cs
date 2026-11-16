using System;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// Shared ScriptableObject browser. Editing goes through Unity's SerializedObject
    /// inspector, retaining Undo, prefab/asset serialization and custom property drawers.
    /// </summary>
    public abstract class DefinitionEditorWindowBase : EditorWindow
    {
        private UnityEngine.Object _selected;
        private UnityEditor.Editor _inspector;
        private Vector2 _listScroll;
        private Vector2 _inspectorScroll;
        private string _filter = "";
        private string[] _assetPaths = Array.Empty<string>();
        private int _assetVersion;

        protected abstract Type AssetType { get; }
        protected abstract string DisplayTitle { get; }
        protected abstract string DefaultAssetName { get; }
        protected virtual void DrawDiagnostics(ScriptableObject asset) { }

        protected void InitializeWindow()
        {
            minSize = new Vector2(620, 380);
            titleContent = new GUIContent(DisplayTitle);
            RefreshAssets();
            if (Selection.activeObject != null &&
                AssetType.IsInstanceOfType(Selection.activeObject))
                SelectAsset(Selection.activeObject);
        }

        protected virtual void OnDisable()
        {
            DestroyInspector();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject != null &&
                AssetType.IsInstanceOfType(Selection.activeObject))
            {
                SelectAsset(Selection.activeObject);
                Repaint();
            }
        }

        private void OnProjectChange()
        {
            RefreshAssets();
            Repaint();
        }

        private void RefreshAssets()
        {
            string[] ids = AssetDatabase.FindAssets("t:" + AssetType.Name);
            var paths = new System.Collections.Generic.List<string>(ids.Length);
            foreach (string id in ids)
            {
                string path = AssetDatabase.GUIDToAssetPath(id);
                if (!string.IsNullOrEmpty(path) &&
                    AssetType.IsInstanceOfType(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            _assetPaths = paths.ToArray();
            _assetVersion++;
        }

        private void SelectAsset(UnityEngine.Object asset)
        {
            if (_selected == asset) return;
            DestroyInspector();
            _selected = asset;
            _inspectorScroll = Vector2.zero;
            if (_selected != null)
                _inspector = UnityEditor.Editor.CreateEditor(_selected);
        }

        private void DestroyInspector()
        {
            if (_inspector != null)
                DestroyImmediate(_inspector);
            _inspector = null;
        }

        private void CreateAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create " + DisplayTitle, DefaultAssetName, "asset",
                "Select a folder and asset file name for the new definition.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                EditorUtility.DisplayDialog("Existing asset",
                    "That asset path is already used.", "OK");
                return;
            }

            ScriptableObject asset = ScriptableObject.CreateInstance(AssetType);
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            Undo.RegisterCreatedObjectUndo(asset, "Create " + DisplayTitle);
            AssetDatabase.SaveAssets();
            RefreshAssets();
            SelectAsset(asset);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(DisplayTitle, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(56)))
                    CreateAsset();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    RefreshAssets();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(245)))
                {
                    EditorGUILayout.LabelField("Definitions (" + _assetPaths.Length + ")", EditorStyles.boldLabel);
                    _filter = EditorGUILayout.TextField("Search", _filter);
                    _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                    foreach (string path in _assetPaths)
                    {
                        string assetName = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (!string.IsNullOrEmpty(_filter) &&
                            assetName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            path.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, AssetType);
                        if (asset == null) continue;
                        bool active = _selected == asset;
                        if (GUILayout.Toggle(active, assetName, "Button") && !active)
                            SelectAsset(asset);
                    }
                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.Space(6);
                using (new EditorGUILayout.VerticalScope())
                {
                    UnityEngine.Object picked = EditorGUILayout.ObjectField(
                        "Selected Asset", _selected, AssetType, false);
                    if (picked != _selected)
                        SelectAsset(picked);
                    if (_selected == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Select an existing definition on the left, drag one into " +
                            "Selected Asset, or click New.", MessageType.Info);
                        return;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(_selected),
                            EditorStyles.miniLabel);
                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                            EditorGUIUtility.PingObject(_selected);
                    }
                    _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
                    if (_inspector == null)
                        _inspector = UnityEditor.Editor.CreateEditor(_selected);
                    if (_inspector != null)
                        _inspector.OnInspectorGUI();
                    DrawDiagnostics(_selected as ScriptableObject);
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }
}
