using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    // Only draw the execution-specific group for the selected ability mode.
    // Serialized fields remain intact when switching modes.
    [CustomEditor(typeof(AbilityDefinition))]
    public sealed class AbilityDefinitionEditor : UnityEditor.Editor
    {
        private void Draw(string name)
        {
            SerializedProperty prop = serializedObject.FindProperty(name);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromScriptableObject(
                    (AbilityDefinition)target), typeof(MonoScript), false);

            Draw("abilityId");
            Draw("executionMode");
            SerializedProperty modeProperty = serializedObject.FindProperty("executionMode");
            AbilityExecutionMode mode = modeProperty != null
                ? (AbilityExecutionMode)modeProperty.enumValueIndex
                : AbilityExecutionMode.Normal;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Common Ability", EditorStyles.boldLabel);
            Draw("energyCost");
            Draw("cooldownSeconds");
            Draw("startupSeconds");
            Draw("activeSeconds");
            Draw("recoverySeconds");
            Draw("baseDamage");
            Draw("energyGainOnHit");
            EditorGUILayout.LabelField("Hit Reaction Applied To Victim", EditorStyles.boldLabel);
            Draw("hitReaction");
            SerializedProperty reaction = serializedObject.FindProperty("hitReaction");
            if (reaction != null && (HitReactionType)reaction.enumValueIndex == HitReactionType.Stun)
                Draw("stunDurationSeconds");
            Draw("hitKnockbackDistance");
            Draw("hitKnockbackSeconds");
            Draw("animatorTrigger");
            Draw("lookTarget");

            if (mode == AbilityExecutionMode.Charge)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Charge Ability", EditorStyles.boldLabel);
                Draw("maximumChargeSeconds");
                Draw("chargeStartAnimationSeconds");
                Draw("autoReleaseAtMax");
                Draw("chargeThresholds");
                EditorGUILayout.HelpBox("Each threshold has its own Release Type, Melee Range, " +
                    "Projectiles[], Movement Steps[], VFX and Audio timelines. " +
                    "Beam/Laser are reserved and not implemented.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Normal / Base Release", EditorStyles.boldLabel);
                Draw("normalReleaseType");
                SerializedProperty releaseProperty = serializedObject.FindProperty("normalReleaseType");
                NormalAbilityReleaseType release = releaseProperty != null
                    ? (NormalAbilityReleaseType)releaseProperty.enumValueIndex
                    : NormalAbilityReleaseType.AutoLegacy;
                Draw("hitRange");
                if (release == NormalAbilityReleaseType.AutoLegacy ||
                    release == NormalAbilityReleaseType.Projectile)
                {
                    Draw("projectile");
                    Draw("projectiles");
                    EditorGUILayout.HelpBox(
                        "Projectiles[] replaces the legacy Projectile when Size > 0. " +
                        "Each enabled entry fires one shot. Release Delay Seconds is " +
                        "relative to the ability Startup Seconds, using scaled CombatTime.",
                        MessageType.Info);
                }
                if (release == NormalAbilityReleaseType.Beam ||
                    release == NormalAbilityReleaseType.Laser)
                    EditorGUILayout.HelpBox("Beam/Laser are reserved; no damage is currently generated.",
                        MessageType.Warning);

                Draw("movementSteps");
                Draw("vfxTimeline");
                Draw("audioTimeline");
                Draw("bodyPartTimeline");

                if (mode == AbilityExecutionMode.Combo)
                {
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField("Ability Combo", EditorStyles.boldLabel);
                    Draw("comboFollowUps");
                    Draw("chainInputStartNormalized");
                    Draw("chainTransitionNormalized");
                    Draw("chainInputEndNormalized");
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Optional Ability Effects", EditorStyles.boldLabel);
            Draw("clockUpEnabled");
            SerializedProperty clock = serializedObject.FindProperty("clockUpEnabled");
            if (clock != null && clock.boolValue)
            {
                Draw("clockUpActivationDelaySeconds");
                Draw("clockUpDurationSeconds");
                Draw("clockUpOtherRiderScale");
                Draw("clockUpCasterScale");
                Draw("clockUpStartVfxPrefab");
                Draw("clockUpActiveVfxPrefab");
                Draw("clockUpEndVfxPrefab");
                Draw("clockUpStartVfxSeconds");
                Draw("clockUpEndVfxSeconds");
                Draw("clockUpVfxOffset");
            }
            Draw("hookEnabled");
            SerializedProperty hook = serializedObject.FindProperty("hookEnabled");
            if (hook != null && hook.boolValue)
            {
                Draw("hookRange");
                Draw("hookPullSpeed");
                Draw("hookDurationSeconds");
                Draw("hookStopDistance");
            }
            Draw("gravityPullEnabled");
            SerializedProperty gravity = serializedObject.FindProperty("gravityPullEnabled");
            if (gravity != null && gravity.boolValue)
            {
                Draw("gravityPullRadius");
                Draw("gravityPullSpeed");
                Draw("gravityPullDurationSeconds");
                Draw("gravityPullStopDistance");
                Draw("gravityPullCenterOffset");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
