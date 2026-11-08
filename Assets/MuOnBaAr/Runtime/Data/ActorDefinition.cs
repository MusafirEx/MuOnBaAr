using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuOnBaAr.Runtime
{
    [CreateAssetMenu(fileName = "ActorDefinition", menuName = "MuOnBaAr/Definitions/Actor")]
    public sealed class ActorDefinition : ScriptableObject
    {
        [SerializeField] private string actorId = "actor";
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0)] private int maxEnergy = 100;
        [SerializeField, Min(0.01f)] private float moveSpeed = 5f;
        [SerializeField, Min(0)] private int startingFormIndex;
        [SerializeField] private FormDefinition[] forms = Array.Empty<FormDefinition>();

        public string ActorId => actorId;
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int MaxEnergy => Mathf.Max(0, maxEnergy);
        public float MoveSpeed => Mathf.Max(0.01f, moveSpeed);
        public int StartingFormIndex => startingFormIndex;
        public int FormCount => forms == null ? 0 : forms.Length;

        public bool TryGetForm(int index, out FormDefinition form)
        {
            form = null;
            if (forms == null || index < 0 || index >= forms.Length)
                return false;

            form = forms[index];
            return form != null;
        }

        public bool Validate(out string reason)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                reason = "Actor ID is empty.";
                return false;
            }

            if (!TryGetForm(startingFormIndex, out _))
            {
                reason = "Starting form is missing or out of range.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < FormCount; i++)
            {
                if (!TryGetForm(i, out var form) || string.IsNullOrWhiteSpace(form.FormId))
                {
                    reason = "Every form needs a valid definition and ID.";
                    return false;
                }

                if (!ids.Add(form.FormId))
                {
                    reason = "Form IDs must be unique within an actor.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            maxEnergy = Mathf.Max(0, maxEnergy);
            moveSpeed = Mathf.Max(0.01f, moveSpeed);
            startingFormIndex = Mathf.Max(0, startingFormIndex);
        }
    }
}
