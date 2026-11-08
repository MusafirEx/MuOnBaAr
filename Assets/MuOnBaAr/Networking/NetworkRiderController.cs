using Fusion;
using MuOnBaAr.Runtime;
using UnityEngine;

namespace MuOnBaAr.Networking
{
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
    public sealed class NetworkRiderController : NetworkBehaviour
    {
        [SerializeField] private ActorDefinition actor;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Renderer placeholderRenderer;

        [Networked] public int CurrentFormIndex { get; private set; }
        [Networked] public int Health { get; private set; }
        [Networked] public int Energy { get; private set; }
        [Networked] public int NextChangeTick { get; private set; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private GameObject spawnedVisual;
        private int displayedFormIndex = -1;

        public override void Spawned()
        {
            if (actor == null)
            {
                Debug.LogError("NetworkRiderController requires an ActorDefinition.", this);
                return;
            }

            if (!actor.Validate(out var reason))
            {
                Debug.LogError("NetworkRiderController requires valid actor data: " + reason, this);
                return;
            }

            if (Object.HasStateAuthority)
            {
                FormChangeState initial = FormChangeState.Create(actor);
                CurrentFormIndex = initial.CurrentFormIndex;
                Health = initial.Health;
                Energy = initial.Energy;
                NextChangeTick = initial.NextChangeTick;
            }

            displayedFormIndex = -1;
            RefreshFormVisual();
        }

        public override void FixedUpdateNetwork()
        {
            if (actor == null || !GetInput(out RiderNetworkInput input))
                return;

            // Both State and Input Authority simulate their own movement.
            // A NetworkTransform on the same root replicates motion to proxies.
            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            if (actor.TryGetForm(CurrentFormIndex, out var currentForm))
            {
                float speed = actor.MoveSpeed * currentForm.MovementMultiplier;
                Vector3 direction = new Vector3(move.x, 0f, move.y);
                transform.position += direction * (speed * Runner.DeltaTime);
            }

            // Only State Authority commits health, energy and form selection.
            if (!Object.HasStateAuthority)
                return;

            bool pressed = input.Buttons.WasPressed(
                PreviousButtons, RiderInputButton.CycleForm);
            PreviousButtons = input.Buttons;

            if (!pressed || actor.FormCount < 2)
                return;

            int target = (CurrentFormIndex + 1) % actor.FormCount;
            FormChangeState state = new FormChangeState
            {
                CurrentFormIndex = CurrentFormIndex,
                Health = Health,
                Energy = Energy,
                NextChangeTick = NextChangeTick
            };

            int ticksPerSecond = Mathf.Max(1, Mathf.RoundToInt(1f / Runner.DeltaTime));
            FormChangeResult result = FormChangeRules.TryChange(
                actor, ref state, target, Runner.Tick.Raw, ticksPerSecond);

            if (result != FormChangeResult.Success)
                return;

            CurrentFormIndex = state.CurrentFormIndex;
            Energy = state.Energy;
            NextChangeTick = state.NextChangeTick;
        }

        public override void Render()
        {
            if (actor != null && CurrentFormIndex != displayedFormIndex)
                RefreshFormVisual();
        }

        private void RefreshFormVisual()
        {
            if (!actor.TryGetForm(CurrentFormIndex, out var form))
                return;

            displayedFormIndex = CurrentFormIndex;

            if (spawnedVisual != null)
                Destroy(spawnedVisual);

            spawnedVisual = null;
            if (form.VisualPrefab != null)
            {
                Transform parent = visualRoot != null ? visualRoot : transform;
                spawnedVisual = Instantiate(form.VisualPrefab, parent);
                spawnedVisual.transform.localPosition = Vector3.zero;
                spawnedVisual.transform.localRotation = Quaternion.identity;
                var animator = spawnedVisual.GetComponentInChildren<Animator>();
                if (animator != null && form.AnimatorController != null)
                    animator.runtimeAnimatorController = form.AnimatorController;
            }

            // If there are no models yet, tint a placeholder to verify sync.
            if (placeholderRenderer == null && spawnedVisual == null)
                placeholderRenderer = GetComponentInChildren<Renderer>();

            if (placeholderRenderer != null)
            {
                placeholderRenderer.enabled = spawnedVisual == null;
                Color color = Color.HSVToRGB(
                    (CurrentFormIndex * 0.37f) % 1f, 0.75f, 0.95f);
                var block = new MaterialPropertyBlock();
                placeholderRenderer.GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                placeholderRenderer.SetPropertyBlock(block);
            }
        }

        private void OnGUI()
        {
            if (Object == null || !Object.HasInputAuthority || actor == null)
                return;

            string formName = actor.TryGetForm(CurrentFormIndex, out var form)
                ? form.DisplayName : "Unknown";
            GUI.Label(new Rect(20f, 178f, 440f, 28f),
                "Local Rider: " + formName + " | HP " + Health +
                " | Energy " + Energy + " | TAB: Change Form");
        }
    }
}
