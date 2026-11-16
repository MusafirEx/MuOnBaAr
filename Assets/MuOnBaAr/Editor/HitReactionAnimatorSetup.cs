using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// Non-destructive graph migration: duplicate the existing Status hit
    /// animations into a dedicated child state machine for network reactions.
    /// The old Status state machine remains unchanged for legacy references.
    /// </summary>
    public static class HitReactionAnimatorSetup
    {
        [MenuItem("Tools/MuOnBaAr/Animator/Create Hit Reaction Sub-State Machine")]
        private static void Create()
        {
            AnimatorController controller =
                Selection.activeObject as AnimatorController;
            if (controller == null || controller.layers.Length == 0)
            {
                Debug.LogWarning("[MuOnBaAr][HitReaction] Select an Animator Controller asset.");
                return;
            }
            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorStateMachine status = null;
            AnimatorStateMachine hit = null;
            foreach (ChildAnimatorStateMachine child in root.stateMachines)
            {
                if (child.stateMachine.name == "Status")
                    status = child.stateMachine;
                if (child.stateMachine.name == "Hit Reaction")
                    hit = child.stateMachine;
            }
            if (status == null)
            {
                Debug.LogWarning("[MuOnBaAr][HitReaction] Missing Status on " + controller.name);
                return;
            }
            Undo.RegisterCompleteObjectUndo(controller, "Create Hit Reaction states");
            if (hit == null)
                hit = root.AddStateMachine("Hit Reaction", new Vector3(500f, 360f));
            string[] names = { "HitHead", "HItBody", "HitFoot", "Hit2Left",
                "Hit2Right", "GotPull", "Stunt" };
            for (int i = 0; i < names.Length; ++i)
            {
                bool exists = false;
                foreach (ChildAnimatorState s in hit.states)
                    if (s.state.name == names[i]) { exists = true; break; }
                if (exists) continue;
                AnimatorState source = null;
                foreach (ChildAnimatorState s in status.states)
                    if (s.state.name == names[i]) { source = s.state; break; }
                if (source == null)
                {
                    Debug.LogWarning("[MuOnBaAr][HitReaction] Missing source state " + names[i]);
                    continue;
                }
                AnimatorState dst = hit.AddState(names[i],
                    new Vector3(130f + i % 3 * 230f, 90f + i / 3 * 90f));
                dst.motion = source.motion;
                dst.speed = source.speed;
                dst.writeDefaultValues = source.writeDefaultValues;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[MuOnBaAr][HitReaction] Dedicated sub-state machine ready: " +
                controller.name + ". Original Status states preserved.");
        }
    }
}
