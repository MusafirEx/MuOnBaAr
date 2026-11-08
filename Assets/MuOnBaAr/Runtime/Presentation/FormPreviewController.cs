using UnityEngine;

namespace MuOnBaAr.Runtime
{
    // Local-only prototype, not a network controller.
    // Useful for validating definitions and visual swaps before Fusion integration.
    public sealed class FormPreviewController : MonoBehaviour
    {
        [SerializeField] private ActorDefinition actor;
        [SerializeField] private Transform visualParent;
        [SerializeField] private bool allowKeyboardPreview = true;
        [SerializeField] private KeyCode cycleKey = KeyCode.Tab;

        private const int PreviewTicksPerSecond = 60;
        private FormChangeState state;
        private GameObject currentVisual;
        private bool initialized;
        private float startTime;

        public int CurrentFormIndex => state.CurrentFormIndex;

        private void Start()
        {
            if (actor == null || !actor.Validate(out var error))
            {
                Debug.LogWarning("Form preview needs a valid ActorDefinition: " + error, this);
                enabled = false;
                return;
            }

            state = FormChangeState.Create(actor);
            startTime = Time.time;
            initialized = true;
            UpdatePresentation();
        }

        private void Update()
        {
            if (allowKeyboardPreview && Input.GetKeyDown(cycleKey))
                CycleForm();
        }

        [ContextMenu("Cycle Form (Play Mode)")]
        public void CycleForm()
        {
            if (!initialized || actor.FormCount < 2)
                return;

            for (int offset = 1; offset < actor.FormCount; offset++)
            {
                int candidate = (state.CurrentFormIndex + offset) % actor.FormCount;
                if (TryChangeForm(candidate) == FormChangeResult.Success)
                    break;
            }
        }

        public FormChangeResult TryChangeForm(int index)
        {
            if (!initialized)
                return FormChangeResult.InvalidSetup;

            int tick = Mathf.FloorToInt((Time.time - startTime) * PreviewTicksPerSecond);
            var result = FormChangeRules.TryChange(
                actor, ref state, index, tick, PreviewTicksPerSecond);

            if (result == FormChangeResult.Success)
                UpdatePresentation();

            return result;
        }

        private void UpdatePresentation()
        {
            if (currentVisual != null)
                Destroy(currentVisual);

            currentVisual = null;
            if (!actor.TryGetForm(state.CurrentFormIndex, out var form) ||
                form.VisualPrefab == null)
                return;

            Transform parent = visualParent != null ? visualParent : transform;
            currentVisual = Instantiate(form.VisualPrefab, parent);
            currentVisual.transform.localPosition = Vector3.zero;
            currentVisual.transform.localRotation = Quaternion.identity;

            var animator = currentVisual.GetComponentInChildren<Animator>();
            if (animator != null && form.AnimatorController != null)
                animator.runtimeAnimatorController = form.AnimatorController;
        }
    }
}
