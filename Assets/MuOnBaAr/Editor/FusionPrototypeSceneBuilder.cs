using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fusion;
using MuOnBaAr.Networking;
using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MuOnBaAr.Editor
{
    // Explicit, opt-in editor tool. Does not touch Photon configuration or project secrets.
    internal static class FusionPrototypeSceneBuilder
    {
        private const string GeneratedFolder = "Assets/MuOnBaAr/PrototypeGenerated";
        private const string ScenePath = "Assets/Scenes/MuOnBaArPrototype.unity";
        private const string PlayerPath = GeneratedFolder + "/NetworkRiderPlayer.prefab";
        private const string ActorPath = GeneratedFolder + "/PrototypeActor.asset";
        private const string BaseFormPath = GeneratedFolder + "/PrototypeBalanced.asset";
        private const string SpeedFormPath = GeneratedFolder + "/PrototypeSpeed.asset";

        [MenuItem("Tools/MuOnBaAr/Create Fusion Prototype Scene")]
        private static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("MuOnBaAr", "Exit Play Mode first.", "OK");
                return;
            }

            string[] reserved = { ScenePath, PlayerPath, ActorPath, BaseFormPath, SpeedFormPath };
            if (reserved.Any(path => AssetDatabase.LoadAssetAtPath<Object>(path) != null ||
                                     File.Exists(path)))
            {
                EditorUtility.DisplayDialog(
                    "MuOnBaAr",
                    "Prototype assets or scene already exist. Existing content will not be overwritten.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Create Fusion Prototype",
                    "Create a new two-player test scene, a network player prefab, " +
                    "and two sample forms? The active scene will be saved if needed " +
                    "and the generated scene added to Build Settings.",
                    "Create", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/MuOnBaAr", "PrototypeGenerated");

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            FormDefinition balanced = NewForm(
                BaseFormPath, "balanced", "Balanced", 1f, 0);
            FormDefinition speed = NewForm(
                SpeedFormPath, "speed", "Speed", 1.6f, 5);

            ActorDefinition actor = ScriptableObject.CreateInstance<ActorDefinition>();
            var actorProperties = new SerializedObject(actor);
            actorProperties.FindProperty("actorId").stringValue = "prototype_rider";
            actorProperties.FindProperty("maxHealth").intValue = 100;
            actorProperties.FindProperty("maxEnergy").intValue = 100;
            actorProperties.FindProperty("moveSpeed").floatValue = 4.5f;
            var forms = actorProperties.FindProperty("forms");
            forms.arraySize = 2;
            forms.GetArrayElementAtIndex(0).objectReferenceValue = balanced;
            forms.GetArrayElementAtIndex(1).objectReferenceValue = speed;
            actorProperties.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(actor, ActorPath);

            NetworkObject playerPrefab = NewPlayerPrefab(actor);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            var launcherObject = new GameObject("Fusion Session Launcher");
            var launcher = launcherObject.AddComponent<FusionPrototypeLauncher>();
            var launcherProperties = new SerializedObject(launcher);
            launcherProperties.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
            launcherProperties.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Could not save Fusion prototype scene.", launcherObject);
                return;
            }

            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Any(entry => entry.path == ScenePath))
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();

            Selection.activeGameObject = launcherObject;
            Debug.Log(
                "MuOnBaAr Fusion prototype scene created. Enter Play Mode and HOST, " +
                "then JOIN from a Windows build. See Docs/Fusion-2.1.3-Prototype-Setup.md.");
        }

        private static FormDefinition NewForm(
            string path, string id, string title, float moveMultiplier, int energyCost)
        {
            FormDefinition form = ScriptableObject.CreateInstance<FormDefinition>();
            var serialized = new SerializedObject(form);
            serialized.FindProperty("formId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = title;
            serialized.FindProperty("movementMultiplier").floatValue = moveMultiplier;
            serialized.FindProperty("changeEnergyCost").intValue = energyCost;
            serialized.FindProperty("changeLockSeconds").floatValue = 0.4f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(form, path);
            return form;
        }

        private static NetworkObject NewPlayerPrefab(ActorDefinition actor)
        {
            var root = new GameObject("NetworkRiderPlayer");
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.35f;
            controller.center = Vector3.up;
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.05f;
            var rider = root.AddComponent<NetworkRiderController>();

            var visuals = new GameObject("VisualRoot").transform;
            visuals.SetParent(root.transform, false);
            visuals.localPosition = Vector3.up;
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            placeholder.name = "Placeholder";
            placeholder.transform.SetParent(visuals, false);
            var collider = placeholder.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var properties = new SerializedObject(rider);
            properties.FindProperty("actor").objectReferenceValue = actor;
            properties.FindProperty("visualRoot").objectReferenceValue = visuals;
            properties.FindProperty("placeholderRenderer").objectReferenceValue =
                placeholder.GetComponent<Renderer>();
            properties.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Object.DestroyImmediate(root);
            return prefab != null ? prefab.GetComponent<NetworkObject>() : null;
        }
    }
}
