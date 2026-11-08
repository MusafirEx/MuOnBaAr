using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class FormChangeRulesTests
    {
        private ActorDefinition actor;
        private FormDefinition baseForm;
        private FormDefinition alternateForm;

        [SetUp]
        public void SetUp()
        {
            actor = ScriptableObject.CreateInstance<ActorDefinition>();
            baseForm = ScriptableObject.CreateInstance<FormDefinition>();
            alternateForm = ScriptableObject.CreateInstance<FormDefinition>();

            SetString(baseForm, "formId", "base");
            SetString(alternateForm, "formId", "alternate");
            SetInt(alternateForm, "changeEnergyCost", 25);
            SetFloat(alternateForm, "changeLockSeconds", 2f);

            var serialized = new SerializedObject(actor);
            var forms = serialized.FindProperty("forms");
            forms.arraySize = 2;
            forms.GetArrayElementAtIndex(0).objectReferenceValue = baseForm;
            forms.GetArrayElementAtIndex(1).objectReferenceValue = alternateForm;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(actor);
            Object.DestroyImmediate(baseForm);
            Object.DestroyImmediate(alternateForm);
        }

        [Test]
        public void ValidChangeUsesEnergyAndStartsTickCooldown()
        {
            var state = FormChangeState.Create(actor);
            var result = FormChangeRules.TryChange(actor, ref state, 1, 10, 60);

            Assert.That(result, Is.EqualTo(FormChangeResult.Success));
            Assert.That(state.CurrentFormIndex, Is.EqualTo(1));
            Assert.That(state.Energy, Is.EqualTo(75));
            Assert.That(state.NextChangeTick, Is.EqualTo(130));
            Assert.That(state.Health, Is.EqualTo(100));
        }

        [Test]
        public void SameFormIsRejectedWithoutMutation()
        {
            var state = FormChangeState.Create(actor);
            var before = state;
            var result = FormChangeRules.TryChange(actor, ref state, 0, 10, 60);

            Assert.That(result, Is.EqualTo(FormChangeResult.AlreadyActive));
            Assert.That(state.CurrentFormIndex, Is.EqualTo(before.CurrentFormIndex));
            Assert.That(state.Energy, Is.EqualTo(before.Energy));
        }

        [Test]
        public void InvalidIndexIsRejected()
        {
            var state = FormChangeState.Create(actor);
            var result = FormChangeRules.TryChange(actor, ref state, 42, 0, 60);

            Assert.That(result, Is.EqualTo(FormChangeResult.InvalidTarget));
            Assert.That(state.CurrentFormIndex, Is.EqualTo(0));
        }

        [Test]
        public void NotEnoughEnergyIsRejected()
        {
            var state = FormChangeState.Create(actor);
            state.Energy = 24;
            var result = FormChangeRules.TryChange(actor, ref state, 1, 0, 60);

            Assert.That(result, Is.EqualTo(FormChangeResult.NotEnoughEnergy));
            Assert.That(state.Energy, Is.EqualTo(24));
            Assert.That(state.CurrentFormIndex, Is.EqualTo(0));
        }

        [Test]
        public void NextChangeMustWaitForCooldown()
        {
            var state = FormChangeState.Create(actor);
            Assert.That(FormChangeRules.TryChange(actor, ref state, 1, 10, 60),
                Is.EqualTo(FormChangeResult.Success));
            Assert.That(FormChangeRules.TryChange(actor, ref state, 0, 129, 60),
                Is.EqualTo(FormChangeResult.OnCooldown));
            Assert.That(FormChangeRules.TryChange(actor, ref state, 0, 130, 60),
                Is.EqualTo(FormChangeResult.Success));
        }

        [Test]
        public void DefeatedActorCannotChange()
        {
            var state = FormChangeState.Create(actor);
            state.Health = 0;
            var result = FormChangeRules.TryChange(actor, ref state, 1, 0, 60);

            Assert.That(result, Is.EqualTo(FormChangeResult.Defeated));
            Assert.That(state.CurrentFormIndex, Is.EqualTo(0));
        }

        [Test]
        public void RestrictedTransitionsRequireAllowedSource()
        {
            SetBool(alternateForm, "restrictSourceForms", true);
            var state = FormChangeState.Create(actor);
            Assert.That(FormChangeRules.TryChange(actor, ref state, 1, 0, 60),
                Is.EqualTo(FormChangeResult.TransitionNotAllowed));

            var serialized = new SerializedObject(alternateForm);
            var sources = serialized.FindProperty("acceptedSourceForms");
            sources.arraySize = 1;
            sources.GetArrayElementAtIndex(0).objectReferenceValue = baseForm;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(FormChangeRules.TryChange(actor, ref state, 1, 0, 60),
                Is.EqualTo(FormChangeResult.Success));
        }

        private static void SetString(Object target, string key, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(key).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string key, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(key).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string key, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(key).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string key, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(key).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
