using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// Attach to a character, projectile or moving environment root to
    /// opt out of World Time or add a local rate. Does not change Unity Time.
    /// Animator speed is local presentation only; combat and projectile
    /// simulation must explicitly use MuOnBaArTimeScale.GetScale(transform).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuOnBaArTimeParticipant : MonoBehaviour
    {
        [Header("Time Domain")]
        [SerializeField] private bool ignoreWorldScale;
        [SerializeField, Range(0f, 3f)] private float localMultiplier = 1f;
        [Header("Animator (optional)")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private bool automaticallyScaleAnimator = true;
        [SerializeField, Min(0f)] private float baseAnimatorSpeed = 1f;

        public bool IgnoreWorldScale => ignoreWorldScale;
        public float LocalMultiplier => Mathf.Max(0f, localMultiplier);
        public float EffectiveScale => MuOnBaArTimeScale.GetScale(transform);

        public void SetIgnoreWorldScale(bool value) => ignoreWorldScale = value;
        public void SetLocalMultiplier(float value)
        {
            localMultiplier = Mathf.Clamp(value, 0f, 3f);
        }

        private void Awake()
        {
            if (targetAnimator == null)
                targetAnimator = GetComponentInChildren<Animator>();
        }

        private void LateUpdate()
        {
            if (automaticallyScaleAnimator && targetAnimator != null)
                targetAnimator.speed = baseAnimatorSpeed * EffectiveScale;
        }

        private void OnDisable()
        {
            if (targetAnimator != null && automaticallyScaleAnimator)
                targetAnimator.speed = baseAnimatorSpeed;
        }

        private void OnValidate()
        {
            localMultiplier = Mathf.Clamp(localMultiplier, 0f, 3f);
            baseAnimatorSpeed = Mathf.Max(0f, baseAnimatorSpeed);
        }
    }
}
