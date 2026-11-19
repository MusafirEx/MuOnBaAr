using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// One-time, idempotent ACL00 locomotion loop setup. Changes only four
    /// motion-specific PEMT importer overrides; the source PEMT data, rig,
    /// Animator state machine, Fusion logic and non-locomotion clips are untouched.
    /// </summary>
    [InitializeOnLoad]
    internal static class ACL00LocomotionLoopInstaller
    {
        private const string MotionSource =
            "Assets/Model/chr/ACL/ACL00/motion/0000.pemt";

        // PEMT clip names are hex ("0000_Motion_100"), not decimal IDs.
        private const string FlyMotionSource =
            "Assets/Model/chr/ACL/ACL02/motion/0000.pemt";
        private const int FlyforwardMotionId = 0x13B;

        private static readonly int[] LoopMotionIds =
        {
            0x100, // Idle
            0x105, // Walk
            0x103, // Run
            0x106  // Sprint
        };

        static ACL00LocomotionLoopInstaller()
        {
            // Runs only after the user approves the script installation and
            // Unity reloads assemblies. Further domain reloads do no edits.
            EditorApplication.delayCall += ApplyIfNeeded;
        }

        [MenuItem("Tools/MuOnBaAr/Apply ACL00 Locomotion Loops")]
        private static void ApplyFromMenu()
        {
            ApplyIfNeeded();
        }

        private static void ApplyIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            // The flight clip is stored in ACL02, not in ACL00's PEMT.
            // Reimport only if its explicit per-motion Loop Time override differs.
            ApplyFlyforwardLoopIfNeeded();

            AssetImporter importer = AssetImporter.GetAtPath(MotionSource);
            if (importer == null)
            {
                Debug.LogWarning("[MuOnBaAr][ACL00] PEMT file not found: " +
                    MotionSource + "; no changes applied.");
                return;
            }

            // The EMD2Unity package owns these importer settings. Do not
            // modify importers of another type or override global defaults.
            if (importer.GetType().Name != "PemtImporter")
            {
                Debug.LogWarning("[MuOnBaAr][ACL00] Unexpected PEMT importer: " +
                    importer.GetType().FullName + "; no changes applied.");
                return;
            }

            SerializedObject data = new SerializedObject(importer);
            SerializedProperty overrides = data.FindProperty("motionLoopOverrides");
            if (overrides == null || !overrides.isArray)
            {
                Debug.LogError("[MuOnBaAr][ACL00] PEMT importer does not expose " +
                    "motionLoopOverrides; no changes applied.");
                return;
            }

            bool changed = false;
            for (int idIndex = 0; idIndex < LoopMotionIds.Length; idIndex++)
            {
                int motionId = LoopMotionIds[idIndex];
                SerializedProperty entry = null;
                for (int i = 0; i < overrides.arraySize; i++)
                {
                    SerializedProperty candidate = overrides.GetArrayElementAtIndex(i);
                    SerializedProperty candidateId = candidate.FindPropertyRelative("motionId");
                    if (candidateId != null && candidateId.intValue == motionId)
                    {
                        entry = candidate;
                        break;
                    }
                }

                if (entry == null)
                {
                    int index = overrides.arraySize;
                    overrides.InsertArrayElementAtIndex(index);
                    entry = overrides.GetArrayElementAtIndex(index);
                    changed = true;
                }

                SerializedProperty idProperty = entry.FindPropertyRelative("motionId");
                SerializedProperty loopTime = entry.FindPropertyRelative("loopTime");
                SerializedProperty loopPose = entry.FindPropertyRelative("loopPose");
                if (idProperty == null || loopTime == null || loopPose == null)
                {
                    Debug.LogError("[MuOnBaAr][ACL00] Unexpected motion loop " +
                        "entry structure. No changes applied.");
                    return;
                }

                if (idProperty.intValue != motionId)
                {
                    idProperty.intValue = motionId;
                    changed = true;
                }
                if (!loopTime.boolValue)
                {
                    loopTime.boolValue = true;
                    changed = true;
                }
                // The user requested Loop Time; no pose-blending changes.
                if (loopPose.boolValue)
                {
                    loopPose.boolValue = false;
                    changed = true;
                }
            }

            if (!changed)
                return;

            data.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();

            int found = 0;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(MotionSource))
            {
                AnimationClip clip = asset as AnimationClip;
                if (clip == null)
                    continue;

                for (int i = 0; i < LoopMotionIds.Length; i++)
                {
                    string suffix = "_Motion_" + LoopMotionIds[i].ToString("X3");
                    if (!clip.name.EndsWith(suffix, System.StringComparison.Ordinal))
                        continue;
                    found++;
                    if (!AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                        Debug.LogWarning("[MuOnBaAr][ACL00] Imported clip " +
                            clip.name + " has not enabled Loop Time.");
                }
            }

            Debug.Log("[MuOnBaAr][ACL00] Applied Loop Time on motion IDs " +
                "0x100 Idle, 0x105 Walk, 0x103 Run, 0x106 Sprint. " +
                "Imported clips found: " + found + "/4. " +
                "Other PEMT motions and Animator transitions unchanged.");
        }

        // ACL02's motion 0x13B is the continuous Flyforward movement clip.
        // Keep all other ACL02 motions and importer settings unchanged.
        private static void ApplyFlyforwardLoopIfNeeded()
        {
            AssetImporter importer = AssetImporter.GetAtPath(FlyMotionSource);
            if (importer == null)
            {
                Debug.LogWarning("[MuOnBaAr][Flyforward] PEMT not found: " +
                    FlyMotionSource + "; no flight loop changes applied.");
                return;
            }
            if (importer.GetType().Name != "PemtImporter")
            {
                Debug.LogWarning("[MuOnBaAr][Flyforward] Unexpected importer: " +
                    importer.GetType().FullName + "; no changes applied.");
                return;
            }

            SerializedObject data = new SerializedObject(importer);
            SerializedProperty overrides = data.FindProperty("motionLoopOverrides");
            if (overrides == null || !overrides.isArray)
            {
                Debug.LogError("[MuOnBaAr][Flyforward] The PEMT importer does not expose " +
                    "motionLoopOverrides; no changes applied.");
                return;
            }

            int index = -1;
            for (int i = 0; i < overrides.arraySize; i++)
            {
                SerializedProperty candidate = overrides.GetArrayElementAtIndex(i);
                SerializedProperty motionId = candidate.FindPropertyRelative("motionId");
                if (motionId != null && motionId.intValue == FlyforwardMotionId)
                {
                    index = i;
                    break;
                }
            }

            // Validate fields before editing, including when the array is empty.
            bool newlyAdded = index < 0;
            if (newlyAdded)
            {
                index = overrides.arraySize;
                overrides.InsertArrayElementAtIndex(index);
            }
            SerializedProperty entry = overrides.GetArrayElementAtIndex(index);
            SerializedProperty id = entry.FindPropertyRelative("motionId");
            SerializedProperty loopTime = entry.FindPropertyRelative("loopTime");
            SerializedProperty loopPose = entry.FindPropertyRelative("loopPose");
            if (id == null || loopTime == null || loopPose == null)
            {
                Debug.LogError("[MuOnBaAr][Flyforward] Unexpected motion loop entry; " +
                    "no changes applied.");
                return;
            }

            bool changed = newlyAdded || id.intValue != FlyforwardMotionId ||
                !loopTime.boolValue || loopPose.boolValue;
            if (changed)
            {
                id.intValue = FlyforwardMotionId;
                loopTime.boolValue = true;
                loopPose.boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }

            // Verify the resulting imported clip, not just importer serialized settings.
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(FlyMotionSource))
            {
                AnimationClip clip = asset as AnimationClip;
                if (clip == null || clip.name != "0000_Motion_13B") continue;
                if (!AnimationUtility.GetAnimationClipSettings(clip).loopTime)
                    Debug.LogWarning("[MuOnBaAr][Flyforward] Override exists but " +
                        "0000_Motion_13B is still not looping after import.");
                else if (changed)
                    Debug.Log("[MuOnBaAr][Flyforward] Loop Time enabled for " +
                        "0000_Motion_13B (ACL02 PEMT); Loop Pose remains disabled.");
                return;
            }
            Debug.LogWarning("[MuOnBaAr][Flyforward] Imported clip 0000_Motion_13B " +
                "not found in ACL02 PEMT; verify the importer's generated subassets.");
        }
    }
}
