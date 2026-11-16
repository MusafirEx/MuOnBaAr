using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class RiderFormAirborneDefinitionTests
    {
        [Test]
        public void DefaultsMatchExistingGroundedJump()
        {
            FormDefinition form = ScriptableObject.CreateInstance<FormDefinition>();
            try
            {
                Assert.That(form.JumpHeight, Is.EqualTo(1.5f).Within(0.001f));
                Assert.That(form.CanFly, Is.False);
                Assert.That(form.FlySpeed, Is.EqualTo(4f).Within(0.001f));
                Assert.That(form.MaxFlyHeight, Is.EqualTo(10f).Within(0.001f));
                Assert.That(form.FlyHoldSeconds, Is.EqualTo(0.35f).Within(0.001f));
                Assert.That(form.FlyEnergyPerSecond, Is.EqualTo(5f).Within(0.001f));
            }
            finally { Object.DestroyImmediate(form); }
        }

        [Test]
        public void JumpHeightAndCanFlyAreIndependentPerForm()
        {
            FormDefinition normal = ScriptableObject.CreateInstance<FormDefinition>();
            FormDefinition flying = ScriptableObject.CreateInstance<FormDefinition>();
            try
            {
                var a = new SerializedObject(normal);
                a.FindProperty("jumpHeight").floatValue = 0f;
                a.ApplyModifiedPropertiesWithoutUndo();

                var b = new SerializedObject(flying);
                b.FindProperty("jumpHeight").floatValue = 3.2f;
                b.FindProperty("canFly").boolValue = true;
                b.FindProperty("flySpeed").floatValue = 6f;
                b.FindProperty("maxFlyHeight").floatValue = 3.5f;
                b.FindProperty("flyHoldSeconds").floatValue = 0.6f;
                b.FindProperty("flyEnergyPerSecond").floatValue = 8.5f;
                b.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(normal.JumpHeight, Is.Zero);
                Assert.That(normal.CanFly, Is.False);
                Assert.That(flying.JumpHeight, Is.EqualTo(3.2f).Within(0.001f));
                Assert.That(flying.CanFly, Is.True);
                Assert.That(flying.FlySpeed, Is.EqualTo(6f).Within(0.001f));
                Assert.That(flying.MaxFlyHeight, Is.EqualTo(3.5f).Within(0.001f));
                Assert.That(flying.FlyHoldSeconds, Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(flying.FlyEnergyPerSecond, Is.EqualTo(8.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(flying);
            }
        }

        [Test]
        public void FlightCeilingLimitsAscentWithinOneTick()
        {
            // Fixed world Y=10 for ALL riders regardless of starting height.
            float speed = RiderAirborneRules.LimitFlyingAscent(
                20f, 9.9f, 10f, 0.02f);
            Assert.That(speed, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(RiderAirborneRules.LimitFlyingAscent(
                4f, 10f, 10f, 0.02f), Is.Zero);
            Assert.That(RiderAirborneRules.LimitFlyingAscent(
                4f, 11f, 10f, 0.02f), Is.Zero);
            Assert.That(RiderAirborneRules.LimitFlyingAscent(
                -4f, 11f, 10f, 0.02f), Is.EqualTo(-4f));
            Assert.That(RiderAirborneRules.LimitFlyingAscent(
                4f, 4f, 10f, 0.02f), Is.EqualTo(4f));
        }

        [Test]
        public void NegativeSerializedHeightIsSafelyClamped()
        {
            FormDefinition form = ScriptableObject.CreateInstance<FormDefinition>();
            try
            {
                var serialized = new SerializedObject(form);
                serialized.FindProperty("jumpHeight").floatValue = -5f;
                serialized.FindProperty("flySpeed").floatValue = -1f;
                serialized.FindProperty("maxFlyHeight").floatValue = -2f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(form.JumpHeight, Is.Zero);
                Assert.That(form.FlySpeed, Is.GreaterThan(0f));
                Assert.That(form.MaxFlyHeight, Is.Zero);
            }
            finally { Object.DestroyImmediate(form); }
        }
    }
}
