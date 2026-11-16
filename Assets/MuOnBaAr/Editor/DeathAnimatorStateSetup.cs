using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
namespace MuOnBaAr.Editor
{
    // Explicit menu; no controller assets are touched until invoked in Unity.
    public static class DeathAnimatorStateSetup
    {
        [MenuItem("Tools/MuOnBaAr/Animator/Create Death State on Selected Controller")]
        private static void Create()
        {
            AnimatorController controller = null;
            AnimationClip clip = null;
            foreach (Object obj in Selection.objects)
            {
                if (obj is AnimatorController c) controller = c;
                else if (obj is AnimationClip a) clip = a;
            }
            if (controller == null || controller.layers.Length == 0)
            {
                Debug.LogWarning("[MuOnBaAr][Death] Select an Animator Controller " +
                    "and optionally a Death AnimationClip.");
                return;
            }
            AnimatorStateMachine root = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in root.states)
                if (child.state.name == "Death")
                {
                    Debug.Log("[MuOnBaAr][Death] Existing Base Layer.Death preserved.");
                    return;
                }
            if (clip == null) clip = FindDeathMotion(root);
            if (clip == null)
            {
                Debug.LogWarning("[MuOnBaAr][Death] No Death/Die/KO clip found. " +
                    "Select controller AND Death AnimationClip, then retry.");
                return;
            }
            Undo.RegisterCompleteObjectUndo(controller, "Create Death state");
            AnimatorState death = root.AddState("Death", new Vector3(650f, 380f));
            death.motion = clip;
            death.writeDefaultValues = false;
            // No exit transition. Runtime sends rider to Locomotion at respawn.
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[MuOnBaAr][Death] Created Base Layer.Death on " + controller.name);
        }

        private static AnimationClip FindDeathMotion(AnimatorStateMachine machine)
        {
            foreach (ChildAnimatorState item in machine.states)
            {
                string name = item.state.name.ToLowerInvariant();
                if ((name.Contains("death") || name.Contains("die") ||
                    name.Contains("dead") || name.Contains("ko")) &&
                    item.state.motion is AnimationClip c)
                    return c;
            }
            foreach (ChildAnimatorStateMachine nested in machine.stateMachines)
            {
                AnimationClip clip = FindDeathMotion(nested.stateMachine);
                if (clip != null) return clip;
            }
            return null;
        }
    }
}
