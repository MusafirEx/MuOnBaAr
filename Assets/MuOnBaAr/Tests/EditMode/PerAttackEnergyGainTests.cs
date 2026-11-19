using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class PerAttackEnergyGainTests
    {
        [Test]
        public void ComboStagesCanHaveDifferentRewards()
        {
            FormDefinition form = ScriptableObject.CreateInstance<FormDefinition>();
            try
            {
                SerializedObject serialized = new SerializedObject(form);
                SerializedProperty stages = serialized.FindProperty("comboAttacks");
                stages.arraySize = 2;
                stages.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("energyGainOnHit").intValue = 2;
                stages.GetArrayElementAtIndex(1)
                    .FindPropertyRelative("energyGainOnHit").intValue = 10;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(form.TryGetComboAttack(0, out FormComboAttack light), Is.True);
                Assert.That(form.TryGetComboAttack(1, out FormComboAttack heavy), Is.True);
                Assert.That(light.EnergyGainOnHit, Is.EqualTo(2));
                Assert.That(heavy.EnergyGainOnHit, Is.EqualTo(10));
                Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                    30, 100, light.EnergyGainOnHit, 1), Is.EqualTo(32));
                Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                    30, 100, heavy.EnergyGainOnHit, 1), Is.EqualTo(40));
            }
            finally
            {
                Object.DestroyImmediate(form);
            }
        }

        [Test]
        public void AbilitiesCanHaveIndependentRewards()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                SerializedObject serialized = new SerializedObject(ability);
                serialized.FindProperty("energyGainOnHit").intValue = 7;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ability.EnergyGainOnHit, Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ChargeThresholdInheritsOrOverridesAbilityReward()
        {
            AbilityDefinition ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            try
            {
                SerializedObject serialized = new SerializedObject(ability);
                serialized.FindProperty("energyGainOnHit").intValue = 4;
                SerializedProperty thresholds = serialized.FindProperty("chargeThresholds");
                thresholds.arraySize = 2;
                SerializedProperty heavy = thresholds.GetArrayElementAtIndex(1);
                heavy.FindPropertyRelative("useCustomEnergyGainOnHit").boolValue = true;
                heavy.FindPropertyRelative("energyGainOnHit").intValue = 12;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(ability.GetChargeThreshold(0)
                    .ResolveEnergyGainOnHit(ability.EnergyGainOnHit), Is.EqualTo(4));
                Assert.That(ability.GetChargeThreshold(1)
                    .ResolveEnergyGainOnHit(ability.EnergyGainOnHit), Is.EqualTo(12));
            }
            finally
            {
                Object.DestroyImmediate(ability);
            }
        }

        [Test]
        public void ZeroRewardDoesNotRestoreEnergy()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, 0, 12), Is.EqualTo(40));
        }

        [Test]
        public void EnergyNeverExceedsMaximum()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                97, 100, 10, 1), Is.EqualTo(100));
        }

        [Test]
        public void MissedAttackCannotRestoreEnergy()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, 10, 0), Is.EqualTo(40));
        }
    }
}
