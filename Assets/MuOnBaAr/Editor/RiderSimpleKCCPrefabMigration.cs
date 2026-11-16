using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    // Approved Fasa 2H migration, runs once when code reloads and only if the
    // generated player asset still has the legacy CharacterController/NetworkTransform.
    internal static class RiderSimpleKCCPrefabMigration
    {
        private const string Path =
            "Assets/MuOnBaAr/PrototypeGenerated/NetworkRiderPlayer.prefab";

        [InitializeOnLoadMethod]
        private static void ScheduleMigration()
        {
            EditorApplication.delayCall += MigrateWhenReady;
        }

        [MenuItem("Tools/MuOnBaAr/Migrate Prototype Rider to Simple KCC")]
        public static void MigrateWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null)
                return;

            bool needsMigration = prefab.GetComponent<SimpleKCC>() == null ||
                prefab.GetComponent<CharacterController>() != null ||
                prefab.GetComponent<NetworkTransform>() != null;
            if (!needsMigration)
                return;

            GameObject contents = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                if (contents.GetComponent<NetworkObject>() == null)
                {
                    Debug.LogError("[MuOnBaAr][2H] Aborted: no NetworkObject on player prefab.");
                    return;
                }

                CharacterController oldController =
                    contents.GetComponent<CharacterController>();
                if (oldController != null)
                    Object.DestroyImmediate(oldController, true);

                NetworkTransform oldNetworkTransform =
                    contents.GetComponent<NetworkTransform>();
                if (oldNetworkTransform != null)
                    Object.DestroyImmediate(oldNetworkTransform, true);

                Rigidbody rb = contents.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = contents.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                if (contents.GetComponent<SimpleKCC>() == null)
                    contents.AddComponent<SimpleKCC>();

                PrefabUtility.SaveAsPrefabAsset(contents, Path);
                Debug.Log("[MuOnBaAr][2H] SimpleKCC prefab migration completed: " +
                    "legacy CharacterController and NetworkTransform removed. " +
                    "Backup original prefab in version control before shipping.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
