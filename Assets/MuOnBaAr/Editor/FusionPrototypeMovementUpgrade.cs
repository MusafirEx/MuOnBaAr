using System.IO;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>Opt-in migration for the already-generated prototype player prefab.</summary>
    internal static class FusionPrototypeMovementUpgrade
    {
        private const string PrefabPath =
            "Assets/MuOnBaAr/PrototypeGenerated/NetworkRiderPlayer.prefab";

        [MenuItem("Tools/MuOnBaAr/Upgrade Existing Prototype Rider (Third Person)")]
        private static void Upgrade()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("MuOnBaAr", "Exit Play Mode first.", "OK");
                return;
            }

            if (!File.Exists(PrefabPath))
            {
                EditorUtility.DisplayDialog(
                    "MuOnBaAr", "Prototype player prefab was not found. " +
                    "Create the prototype scene first.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Upgrade Prototype Rider",
                    "Update NetworkRiderPlayer prefab with a capsule CharacterController " +
                    "and move its placeholder mesh above ground? " +
                    "This edits the existing prefab, not Photon configuration.",
                    "Upgrade", "Cancel"))
                return;

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                CharacterController controller = root.GetComponent<CharacterController>();
                if (controller == null)
                    controller = root.AddComponent<CharacterController>();

                controller.height = 2f;
                controller.radius = 0.35f;
                controller.center = Vector3.up;
                controller.stepOffset = 0.35f;
                controller.slopeLimit = 50f;
                controller.skinWidth = 0.05f;
                controller.minMoveDistance = 0f;

                Transform visuals = root.transform.Find("VisualRoot");
                if (visuals != null)
                    visuals.localPosition = Vector3.up;

                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                {
                    Debug.LogError("Could not save upgraded prototype player prefab.");
                    return;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("MuOnBaAr: Player prefab upgraded for third-person collisions. " +
                      "The camera attaches automatically to the local rider at runtime.");
        }

        [MenuItem("Tools/MuOnBaAr/Upgrade Existing Prototype Rider (Third Person)", true)]
        private static bool UpgradeIsAvailable()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
