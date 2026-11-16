using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// One-time targeted ACL00 Locomotion transition fix.
    /// Preserves the user's state layout, clips, skill/combo graphs and thresholds.
    /// </summary>
    [InitializeOnLoad]
    internal static class ACL00LocomotionExitTimeFix
    {
        private const string ControllerPath = "Assets/chr/ACL/ACL00/ACL00.controller";
        private const string BackupPath = "Assets/chr/ACL/ACL00/ACL00_before_phase2h_exit_time.controller";
        private const string SessionKey = "MuOnBaAr.Phase2H.ACL00LocomotionExitTimeFix.v1";

        static ACL00LocomotionExitTimeFix()
        {
            EditorApplication.delayCall += ApplyOnce;
        }

        [MenuItem("Tools/MuOnBaAr/Phase 2H/Disable ACL00 Locomotion Exit Time")]
        private static void ApplyManually()
        {
            Apply(true);
        }

        private static void ApplyOnce()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ApplyOnce;
                return;
            }

            SessionState.SetBool(SessionKey, true);
            Apply(false);
        }

        private static void Apply(bool manual)
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogWarning("[MuOnBaAr][2H Animator] ACL00 controller not found; no changes.");
                return;
            }

            if (controller.parameters == null ||
                Array.Find(controller.parameters,
                    p => p.name == "Locomotion" && p.type == AnimatorControllerParameterType.Float) == null)
            {
                Debug.LogWarning("[MuOnBaAr][2H Animator] Locomotion Float missing; skipped.");
                return;
            }

            AnimatorStateMachine locomotion = null;
            foreach (var layer in controller.layers)
            {
                foreach (var child in layer.stateMachine.stateMachines)
                {
                    if (child.stateMachine != null && child.stateMachine.name == "Locomotion")
                    {
                        locomotion = child.stateMachine;
                        break;
                    }
                }

                if (locomotion != null)
                    break;
            }

            if (locomotion == null)
            {
                Debug.LogWarning("[MuOnBaAr][2H Animator] Locomotion sub-state machine not found; skipped.");
                return;
            }

            AnimatorState idle = null, run = null, sprint = null;
            foreach (var child in locomotion.states)
            {
                if (child.state.name == "Idle") idle = child.state;
                if (child.state.name == "Run") run = child.state;
                if (child.state.name == "Sprint") sprint = child.state;
            }

            if (idle == null || run == null || sprint == null)
            {
                Debug.LogWarning("[MuOnBaAr][2H Animator] Missing Idle/Run/Sprint; skipped.");
                return;
            }

            int count = 0;
            count += CountExitTimeTransitions(idle, run, sprint);
            count += CountExitTimeTransitions(run, idle, sprint);
            count += CountExitTimeTransitions(sprint, idle, run);
            if (count == 0)
            {
                Debug.Log("[MuOnBaAr][2H Animator] All Locomotion transitions already instant.");
                return;
            }

            // Back up the authored Animator asset *before* changing transition settings.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BackupPath) == null &&
                !AssetDatabase.CopyAsset(ControllerPath, BackupPath))
            {
                Debug.LogError("[MuOnBaAr][2H Animator] Failed to back up ACL00; aborted.");
                return;
            }

            Undo.RecordObject(controller, "MuOnBaAr Phase 2H Locomotion Exit Time");
            int changed = 0;
            changed += DisableExitTimeTransitions(idle, run, sprint);
            changed += DisableExitTimeTransitions(run, idle, sprint);
            changed += DisableExitTimeTransitions(sprint, idle, run);
            AssetDatabase.SaveAssets();
            Debug.Log("[MuOnBaAr][2H Animator] Disabled Has Exit Time on " + changed +
                " Idle/Run/Sprint transitions. Thresholds/clips/combo/skills untouched." +
                (manual ? " (manual)" : " (post-approval)"));
        }

        private static int CountExitTimeTransitions(
            AnimatorState source, AnimatorState a, AnimatorState b)
        {
            int n = 0;
            foreach (var t in source.transitions)
                if ((t.destinationState == a || t.destinationState == b) && t.hasExitTime)
                    n++;
            return n;
        }

        private static int DisableExitTimeTransitions(
            AnimatorState source, AnimatorState a, AnimatorState b)
        {
            int n = 0;
            foreach (var t in source.transitions)
            {
                if ((t.destinationState != a && t.destinationState != b) || !t.hasExitTime)
                    continue;
                Undo.RecordObject(t, "Disable Locomotion Has Exit Time");
                t.hasExitTime = false;
                EditorUtility.SetDirty(t);
                n++;
            }
            return n;
        }
    }
}
