using UnityEditor;
using UnityEngine;
using MuOnBaAr.Networking;

namespace MuOnBaAr.Editor
{
    public static class ProjectileSamplePrefabSetup
    {
        private const string Path = "Assets/MuOnBaAr/Prefab/ProjectileSample.prefab";

        [MenuItem("Tools/MuOnBaAr/Projectile/Upgrade Selected Sample Prefab")]
        public static void Upgrade()
        {
            if (AssetDatabase.GetAssetPath(Selection.activeObject) != Path)
            {
                Debug.LogWarning("[MuOnBaAr] Select ProjectileSample.prefab first.");
                return;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(Path);
                ProjectileSampleVisual script = root.GetComponent<ProjectileSampleVisual>();
                if (script == null) script = root.AddComponent<ProjectileSampleVisual>();

                Transform child = root.transform.Find("SpinningVisual");
                if (child == null)
                {
                    GameObject go = new GameObject("SpinningVisual");
                    child = go.transform;
                    child.SetParent(root.transform, false);
                    child.localScale = new Vector3(0.4f, 0.4f, 0.85f);
                }

                MeshFilter originalMesh = root.GetComponent<MeshFilter>();
                MeshRenderer originalRenderer = root.GetComponent<MeshRenderer>();
                MeshFilter visualMesh = child.GetComponent<MeshFilter>();
                if (visualMesh == null) visualMesh = child.gameObject.AddComponent<MeshFilter>();
                if (originalMesh != null) visualMesh.sharedMesh = originalMesh.sharedMesh;
                MeshRenderer visualRenderer = child.GetComponent<MeshRenderer>();
                if (visualRenderer == null) visualRenderer = child.gameObject.AddComponent<MeshRenderer>();
                if (originalRenderer != null)
                {
                    visualRenderer.sharedMaterials = originalRenderer.sharedMaterials;
                    originalRenderer.enabled = false;
                }

                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;

                SerializedObject serialized = new SerializedObject(script);
                serialized.FindProperty("spinningVisual").objectReferenceValue = child;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Path);
                AssetDatabase.SaveAssets();
                Debug.Log("[MuOnBaAr] ProjectileSample prefab upgraded: spinning child, visual script and disabled colliders.");
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/MuOnBaAr/Projectile/Upgrade Selected Sample Prefab", true)]
        public static bool CanUpgrade()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                AssetDatabase.GetAssetPath(Selection.activeObject) == Path;
        }
    }
}
