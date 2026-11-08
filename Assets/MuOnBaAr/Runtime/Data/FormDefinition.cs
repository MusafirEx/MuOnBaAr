using System;
using UnityEngine;

namespace MuOnBaAr.Runtime
{
    [CreateAssetMenu(fileName = "FormDefinition", menuName = "MuOnBaAr/Definitions/Form")]
    public sealed class FormDefinition : ScriptableObject
    {
        [SerializeField] private string formId = "form";
        [SerializeField] private string displayName = "Form";
        [SerializeField, Min(0.1f)] private float movementMultiplier = 1f;
        [SerializeField, Min(0.1f)] private float damageMultiplier = 1f;
        [SerializeField, Min(0)] private int changeEnergyCost = 0;
        [SerializeField, Min(0f)] private float changeLockSeconds = 1f;
        [SerializeField] private bool restrictSourceForms;
        [SerializeField] private FormDefinition[] acceptedSourceForms = Array.Empty<FormDefinition>();
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private AbilityDefinition[] abilities = Array.Empty<AbilityDefinition>();

        public string FormId => formId;
        public string DisplayName => displayName;
        public float MovementMultiplier => movementMultiplier;
        public float DamageMultiplier => damageMultiplier;
        public int ChangeEnergyCost => Mathf.Max(0, changeEnergyCost);
        public float ChangeLockSeconds => Mathf.Max(0f, changeLockSeconds);
        public GameObject VisualPrefab => visualPrefab;
        public RuntimeAnimatorController AnimatorController => animatorController;
        public AbilityDefinition[] Abilities => abilities;

        public bool AcceptsSource(FormDefinition previous)
        {
            if (!restrictSourceForms)
                return true;

            if (previous == null || acceptedSourceForms == null)
                return false;

            for (int i = 0; i < acceptedSourceForms.Length; i++)
            {
                if (acceptedSourceForms[i] == previous)
                    return true;
            }

            return false;
        }

        private void OnValidate()
        {
            movementMultiplier = Mathf.Max(0.1f, movementMultiplier);
            damageMultiplier = Mathf.Max(0.1f, damageMultiplier);
            changeEnergyCost = Mathf.Max(0, changeEnergyCost);
            changeLockSeconds = Mathf.Max(0f, changeLockSeconds);
        }
    }
}
