using System;
using System.Text;
using MuOnBaAr.Runtime;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Editor
{
    /// <summary>
    /// Read-only project-wide ability audit. No assets or scenes are modified.
    /// Menu: Tools > MuOnBaAr > Ability System > Audit Definitions.
    /// </summary>
    public static class AbilitySystemAudit
    {
        [MenuItem("Tools/MuOnBaAr/Ability System/Audit Definitions")]
        private static void AuditAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:AbilityDefinition");
            int warnings = 0;
            int checkedAbilities = 0;
            var report = new StringBuilder(4096);
            report.AppendLine("[MuOnBaAr] Ability System Audit - Phase C/D");
            report.AppendLine("Mode selection, charge thresholds, projectile config and cue windows.");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AbilityDefinition ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
                if (ability == null) continue;
                ++checkedAbilities;
                report.AppendLine("\n" + path + " | " + ability.AbilityId +
                    " | Mode=" + ability.ExecutionMode);
                if (ability.ExecutionMode == AbilityExecutionMode.Charge)
                {
                    if (ability.ChargeThresholdCount == 0)
                        Warn(report, ref warnings, "Charge has no thresholds; release falls back to melee.");
                    if (ability.AutoReleaseAtMax && ability.MaximumChargeSeconds <= 0f)
                        Warn(report, ref warnings, "MaximumChargeSeconds must be positive.");
                    for (int i = 0; i < ability.ChargeThresholdCount; ++i)
                    {
                        AbilityChargeThreshold t = ability.GetChargeThreshold(i);
                        if (t == null)
                        {
                            Warn(report, ref warnings, "Threshold[" + i + "] is null.");
                            continue;
                        }
                        report.AppendLine("  Threshold[" + i + "] >= " +
                            t.MinimumChargeSeconds.ToString("F2") + "s, Type=" +
                            t.ReleaseType + ", Projectiles=" +
                            (t.Projectiles == null ? 0 : t.Projectiles.Length));
                        if (t.MinimumChargeSeconds > ability.MaximumChargeSeconds)
                            Warn(report, ref warnings, "Threshold[" + i +
                                "] exceeds maximum charge and cannot be reached.");
                        if (t.ReleaseType == ChargeReleaseType.Beam ||
                            t.ReleaseType == ChargeReleaseType.Laser)
                            Warn(report, ref warnings, "Threshold[" + i +
                                "] uses future Beam/Laser (no runtime damage).");
                        if (t.ReleaseType == ChargeReleaseType.Projectiles)
                        {
                            int valid = 0;
                            CombatProjectileSettings[] shots = t.Projectiles;
                            if (shots != null)
                                for (int j = 0; j < shots.Length; ++j)
                                {
                                    CombatProjectileSettings shot = shots[j];
                                    if (shot != null && shot.Enabled)
                                    {
                                        ++valid;
                                        if (shot.VisualPrefab == null)
                                            Warn(report, ref warnings, "Threshold[" + i +
                                                "] Projectile[" + j + "] missing visual prefab.");
                                    }
                                }
                            if (valid == 0)
                                Warn(report, ref warnings, "Threshold[" + i +
                                    "] is Projectiles but has no enabled projectiles.");
                            if (valid > 16)
                                Warn(report, ref warnings, "Threshold[" + i +
                                    "] has >16 enabled projectiles; pool can overflow.");
                        }
                        CheckTimelines(t.MovementSteps, t.VfxTimeline, t.AudioTimeline,
                            ability.DurationSeconds, "Threshold[" + i + "]",
                            report, ref warnings);
                    }
                }
                else
                {
                    if (ability.NormalReleaseType == NormalAbilityReleaseType.Beam ||
                        ability.NormalReleaseType == NormalAbilityReleaseType.Laser)
                        Warn(report, ref warnings, "Normal release uses unimplemented Beam/Laser.");
                    if (ability.NormalReleaseType == NormalAbilityReleaseType.Projectile &&
                        !ability.HasEnabledProjectiles)
                        Warn(report, ref warnings,
                            "Normal release set to Projectile but no enabled shots in Projectiles[] / legacy Projectile.");
                    CheckProjectiles(ability.Projectiles, ability.Projectile,
                        "Normal release", report, ref warnings);
                    if (ability.HasComboFollowUps)
                        for (int i = 0; ability.TryGetComboFollowUp(i, out var stage); ++i)
                            CheckProjectiles(stage.Projectiles, stage.Projectile,
                                "Combo follow-up[" + i + "]", report, ref warnings);
                    if (ability.ExecutionMode == AbilityExecutionMode.Combo &&
                        !ability.HasComboFollowUps)
                        Warn(report, ref warnings, "Combo mode has no follow-up stages.");
                    CheckTimelines(ability.MovementSteps, ability.VfxTimeline,
                        ability.AudioTimeline, ability.DurationSeconds,
                        "Ability", report, ref warnings);
                }
            }
            report.AppendLine("\nSummary: " + checkedAbilities +
                " abilities, " + warnings + " warning(s).");
            if (warnings > 0) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
            EditorUtility.DisplayDialog("MuOnBaAr Ability Audit",
                checkedAbilities + " Ability Definitions inspected.\n" +
                warnings + " warning(s).\nSee Unity Console for details.", "OK");
        }

        private static void CheckProjectiles(CombatProjectileSettings[] entries,
            CombatProjectileSettings legacy, string context,
            StringBuilder report, ref int warnings)
        {
            if (entries == null || entries.Length == 0)
            {
                if (legacy != null && legacy.Enabled && legacy.VisualPrefab == null)
                    Warn(report, ref warnings, context + " legacy projectile missing visual prefab.");
                return;
            }
            int enabled = 0;
            for (int i = 0; i < entries.Length; ++i)
            {
                CombatProjectileSettings shot = entries[i];
                if (shot == null || !shot.Enabled) continue;
                ++enabled;
                if (shot.VisualPrefab == null)
                    Warn(report, ref warnings, context + " Projectile[" + i +
                        "] missing visual prefab.");
            }
            if (enabled > 16)
                Warn(report, ref warnings, context + " has " + enabled +
                    " enabled shots; only 16 can fly at once. Add release delays.");
        }

        private static void CheckTimelines(CombatMovementStep[] movement,
            CombatVfxCue[] vfx, CombatAudioCue[] audio, float duration,
            string context, StringBuilder output, ref int warnings)
        {
            if (movement != null)
                for (int i = 0; i < movement.Length; ++i)
                {
                    CombatMovementStep step = movement[i];
                    if (step != null && step.EndNormalized <= step.StartNormalized)
                        Warn(output, ref warnings, context + " Movement[" + i +
                            "] has an empty/reversed normalized window.");
                }
            if (vfx != null)
                for (int i = 0; i < vfx.Length; ++i)
                {
                    CombatVfxCue cue = vfx[i];
                    if (cue == null) continue;
                    if (cue.Prefab == null)
                        Warn(output, ref warnings, context + " VFX[" + i + "] missing prefab.");
                    if (cue.StartSeconds >= duration)
                        Warn(output, ref warnings, context + " VFX[" + i +
                            "] starts after action duration.");
                }
            if (audio != null)
                for (int i = 0; i < audio.Length; ++i)
                {
                    CombatAudioCue cue = audio[i];
                    if (cue == null) continue;
                    if (cue.Clip == null)
                        Warn(output, ref warnings, context + " Audio[" + i + "] missing clip.");
                    if (cue.StartSeconds >= duration)
                        Warn(output, ref warnings, context + " Audio[" + i +
                            "] starts after action duration.");
                }
        }

        private static void Warn(StringBuilder report, ref int count, string message)
        {
            ++count;
            report.AppendLine("  WARNING: " + message);
        }
    }
}
