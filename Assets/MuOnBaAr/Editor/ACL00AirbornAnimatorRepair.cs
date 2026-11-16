using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// Repair only proven broken Airborn links in ACL00.controller.
    /// Run manually from Tools > MuOnBaAr > Animator > Repair ACL00 Airborn.
    /// Existing clips, states, combo and hit-reaction settings are preserved.
    /// Idempotent: re-running does not duplicate already-correct transitions.
    /// </summary>
    public static class ACL00AirbornAnimatorRepair
    {
        private const string ControllerPath =
            "Assets/Model/chr/ACL/ACL00.controller";

        [MenuItem("Tools/MuOnBaAr/Animator/Repair ACL00 Airborn")]
        public static void Repair()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ACL00 Airborn Repair] Exit Play Mode first.");
                return;
            }

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || controller.layers.Length == 0)
            {
                Debug.LogError("[ACL00 Airborn Repair] Controller not found: " +
                    ControllerPath);
                return;
            }

            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorStateMachine air = SubMachine(root, "Airborn");
            AnimatorStateMachine locomotion = SubMachine(root, "Locomotion");
            if (air == null || locomotion == null)
            {
                Debug.LogError("[ACL00 Airborn Repair] Missing Airborn or Locomotion " +
                    "sub-state machine. Nothing changed.");
                return;
            }

            AnimatorState jump = State(air, "Jump");
            AnimatorState floatState = State(air, "Float");
            AnimatorState flyforward = State(air, "Flyforward");
            AnimatorState stopFly = State(air, "StopFly");
            AnimatorState jumpLand = State(air, "JumpLand");
            if (jump == null || floatState == null || flyforward == null ||
                stopFly == null || jumpLand == null)
            {
                Debug.LogError("[ACL00 Airborn Repair] Some required Airborn states " +
                    "are missing. Nothing changed.");
                return;
            }

            // Undo covers changes to existing sub-assets. AddTransition
            // independently registers Undo for newly created transitions.
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]
            {
                controller, root, air, locomotion, jump,
                floatState, flyforward, stopFly, jumpLand
            }, "Repair ACL00 Airborn transitions");

            int fixedCount = 0;

            // Broken unconditional StopFly -> Flyforward is an infinite loop.
            foreach (AnimatorStateTransition transition in stopFly.transitions)
            {
                if (transition == null ||
                    transition.destinationState != flyforward ||
                    transition.conditions.Length != 0)
                    continue;
                Undo.RecordObject(transition, "Condition StopFly to Flyforward");
                transition.AddCondition(AnimatorConditionMode.If, 0f, "CanFly");
                transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                transition.AddCondition(AnimatorConditionMode.Greater,
                    0.1f, "Locomotion");
                EditorUtility.SetDirty(transition);
                fixedCount++;
            }

            // Do not enter forward-flying states without active powered flight.
            foreach (AnimatorStateTransition transition in floatState.transitions)
            {
                if (transition == null ||
                    transition.destinationState != flyforward ||
                    HasCondition(transition, "CanFly",
                        AnimatorConditionMode.If))
                    continue;
                Undo.RecordObject(transition, "Gate Float to Flyforward");
                transition.AddCondition(AnimatorConditionMode.If, 0f, "CanFly");
                EditorUtility.SetDirty(transition);
                fixedCount++;
            }
            foreach (AnimatorStateTransition transition in flyforward.transitions)
            {
                if (transition == null ||
                    transition.destinationState != stopFly ||
                    HasCondition(transition, "CanFly",
                        AnimatorConditionMode.If))
                    continue;
                Undo.RecordObject(transition, "Gate Flyforward to StopFly");
                transition.AddCondition(AnimatorConditionMode.If, 0f, "CanFly");
                EditorUtility.SetDirty(transition);
                fixedCount++;
            }

            // Correct the previous repair: a falling rider MUST NOT Float.
            // Float represents stationary powered hover, never normal falling.
            fixedCount += RemoveTransitionIf(jump, floatState,
                "CanFly", AnimatorConditionMode.IfNot);
            fixedCount += RemoveTransitionIf(flyforward, floatState,
                "CanFly", AnimatorConditionMode.IfNot);
            fixedCount += RemoveTransitionIf(stopFly, floatState,
                "CanFly", AnimatorConditionMode.IfNot);

            // Add the authored hover gate. NetworkRiderController sets this
            // only when FlightActive and Y speed ~0 and no horizontal movement.
            bool foundHoverParameter = false;
            foreach (AnimatorControllerParameter p in controller.parameters)
                if (p.name == "IsHovering" &&
                    p.type == AnimatorControllerParameterType.Bool)
                    foundHoverParameter = true;
            if (!foundHoverParameter)
            {
                controller.AddParameter("IsHovering",
                    AnimatorControllerParameterType.Bool);
                fixedCount++;
            }

            // Existing transitions must never enter Float while climbing,
            // descending, or moving. Keep the owner's actual clips intact.
            foreach (AnimatorStateTransition transition in jump.transitions)
            {
                if (transition == null ||
                    transition.destinationState != floatState ||
                    !HasCondition(transition, "CanFly",
                        AnimatorConditionMode.If) ||
                    HasCondition(transition, "IsHovering",
                        AnimatorConditionMode.If)) continue;
                Undo.RecordObject(transition, "Require hover for Jump Float");
                transition.AddCondition(AnimatorConditionMode.If,
                    0f, "IsHovering");
                EditorUtility.SetDirty(transition);
                fixedCount++;
            }
            foreach (AnimatorStateTransition transition in stopFly.transitions)
            {
                if (transition == null ||
                    transition.destinationState != floatState) continue;
                if (!HasCondition(transition, "CanFly",
                        AnimatorConditionMode.If))
                {
                    Undo.RecordObject(transition, "Require flight for Float");
                    transition.AddCondition(AnimatorConditionMode.If,
                        0f, "CanFly");
                    EditorUtility.SetDirty(transition);
                    fixedCount++;
                }
                if (!HasCondition(transition, "IsHovering",
                        AnimatorConditionMode.If))
                {
                    Undo.RecordObject(transition, "Require stationary hover");
                    transition.AddCondition(AnimatorConditionMode.If,
                        0f, "IsHovering");
                    EditorUtility.SetDirty(transition);
                    fixedCount++;
                }
            }
            foreach (AnimatorStateTransition transition in flyforward.transitions)
            {
                if (transition == null ||
                    transition.destinationState != stopFly ||
                    HasCondition(transition, "IsHovering",
                        AnimatorConditionMode.If)) continue;
                Undo.RecordObject(transition, "Stop flight only when hovering");
                transition.AddCondition(AnimatorConditionMode.If,
                    0f, "IsHovering");
                EditorUtility.SetDirty(transition);
                fixedCount++;
            }

            // Recover from Energy depletion / Form change while midair by
            // returning to Jump's held airborne pose, never the hover clip.
            fixedCount += EnsureTransition(floatState, jump,
                "CanFly", AnimatorConditionMode.IfNot,
                "Grounded", AnimatorConditionMode.IfNot);
            fixedCount += EnsureTransition(flyforward, jump,
                "CanFly", AnimatorConditionMode.IfNot,
                "Grounded", AnimatorConditionMode.IfNot);
            fixedCount += EnsureTransition(stopFly, jump,
                "CanFly", AnimatorConditionMode.IfNot,
                "Grounded", AnimatorConditionMode.IfNot);
            // Ascending vertically from a hover switches to moving Flight.
            fixedCount += EnsureTransition(floatState, flyforward,
                "IsHovering", AnimatorConditionMode.IfNot,
                "CanFly", AnimatorConditionMode.If);

            // Landing transitions remain available.
            fixedCount += EnsureTransition(flyforward, jumpLand,
                "Grounded", AnimatorConditionMode.If);
            fixedCount += EnsureTransition(stopFly, jumpLand,
                "Grounded", AnimatorConditionMode.If);

            // CRITICAL: JumpLand currently has no way to return to Locomotion.
            bool hasExit = false;
            foreach (AnimatorStateTransition transition in jumpLand.transitions)
            {
                if (transition != null &&
                    transition.destinationStateMachine == locomotion)
                {
                    hasExit = true;
                    break;
                }
            }
            if (!hasExit)
            {
                AnimatorStateTransition landingExit =
                    jumpLand.AddTransition(locomotion);
                landingExit.hasExitTime = true;
                landingExit.exitTime = 0.95f;
                landingExit.hasFixedDuration = true;
                landingExit.duration = 0.08f;
                fixedCount++;
            }

            if (fixedCount == 0)
            {
                Debug.Log("[ACL00 Airborn Repair] Graph already repaired; " +
                    "no duplicate transitions created.", controller);
                return;
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(air);
            EditorUtility.SetDirty(jump);
            EditorUtility.SetDirty(floatState);
            EditorUtility.SetDirty(flyforward);
            EditorUtility.SetDirty(stopFly);
            EditorUtility.SetDirty(jumpLand);
            AssetDatabase.SaveAssets();
            Debug.Log("[ACL00 Airborn Repair] Updated " + fixedCount +
                " transitions in " + ControllerPath +
                ". Test short Space Jump, long Space Flight, Left Alt descent, " +
                "landing and energy depletion.", controller);
        }

        private static int RemoveTransitionIf(AnimatorState source,
            AnimatorState destination, string parameter,
            AnimatorConditionMode mode)
        {
            int removed = 0;
            foreach (AnimatorStateTransition transition in source.transitions)
            {
                if (transition == null || transition.destinationState !=
                    destination || !HasCondition(transition, parameter, mode))
                    continue;
                Undo.RecordObject(source, "Remove incorrect Float transition");
                source.RemoveTransition(transition);
                EditorUtility.SetDirty(source);
                removed++;
            }
            return removed;
        }

        private static AnimatorStateMachine SubMachine(
            AnimatorStateMachine root, string name)
        {
            foreach (ChildAnimatorStateMachine child in root.stateMachines)
                if (child.stateMachine != null &&
                    child.stateMachine.name == name)
                    return child.stateMachine;
            return null;
        }

        private static AnimatorState State(
            AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states)
                if (child.state != null && child.state.name == name)
                    return child.state;
            return null;
        }

        private static bool HasCondition(AnimatorStateTransition transition,
            string parameter, AnimatorConditionMode mode)
        {
            foreach (AnimatorCondition condition in transition.conditions)
                if (condition.parameter == parameter && condition.mode == mode)
                    return true;
            return false;
        }

        private static bool HasTransition(AnimatorState from, AnimatorState to,
            string parameter, AnimatorConditionMode mode)
        {
            foreach (AnimatorStateTransition transition in from.transitions)
                if (transition != null &&
                    transition.destinationState == to &&
                    HasCondition(transition, parameter, mode))
                    return true;
            return false;
        }

        private static int EnsureTransition(AnimatorState from, AnimatorState to,
            string parameter, AnimatorConditionMode mode,
            string secondParameter = null,
            AnimatorConditionMode secondMode = AnimatorConditionMode.If)
        {
            foreach (AnimatorStateTransition existing in from.transitions)
                if (existing != null && existing.destinationState == to &&
                    HasCondition(existing, parameter, mode) &&
                    (string.IsNullOrEmpty(secondParameter) ||
                     HasCondition(existing, secondParameter, secondMode)))
                    return 0;

            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.08f;
            transition.AddCondition(mode, 0f, parameter);
            if (!string.IsNullOrEmpty(secondParameter))
                transition.AddCondition(secondMode, 0f, secondParameter);
            return 1;
        }
    }
}
