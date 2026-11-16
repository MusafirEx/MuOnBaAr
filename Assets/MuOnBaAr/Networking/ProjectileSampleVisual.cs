using UnityEngine;

namespace MuOnBaAr.Networking
{
    /// <summary>
    /// Cosmetic effect for prototype projectiles. The NetworkRiderController
    /// remains responsible for authoritative position, rotation and collision.
    /// Attach this script to the visual-only projectile prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileSampleVisual : MonoBehaviour
    {
        [SerializeField] private Transform spinningVisual;
        [SerializeField, Min(0f)] private float rotationDegreesPerSecond = 180f;
        [SerializeField, Range(0f, 0.3f)] private float pulseAmplitude = 0.08f;
        [SerializeField, Min(0f)] private float pulseFrequency = 4f;
        [SerializeField, Range(0f, 3f)] private float playbackScale = 1f;

        private Vector3 originalLocalScale;
        private float elapsedGameplayTime;

        private void Awake()
        {
            if (spinningVisual == null)
            {
                var render = GetComponentInChildren<Renderer>();
                if (render != null && render.transform != transform)
                    spinningVisual = render.transform;
            }

            if (spinningVisual != null)
                originalLocalScale = spinningVisual.localScale;
        }

        public void SetPlaybackScale(float scale)
        {
            playbackScale = Mathf.Clamp(scale, 0f, 3f);
        }

        private void Update()
        {
            if (spinningVisual == null)
                return;

            float scaledDelta = Time.deltaTime * playbackScale;
            elapsedGameplayTime += scaledDelta;
            spinningVisual.Rotate(0f, 0f, rotationDegreesPerSecond * scaledDelta,
                Space.Self);

            float pulse = 1f + Mathf.Sin(elapsedGameplayTime *
                pulseFrequency * Mathf.PI * 2f) * pulseAmplitude;
            spinningVisual.localScale = originalLocalScale * pulse;
        }

        private void OnDisable()
        {
            if (spinningVisual != null)
                spinningVisual.localScale = originalLocalScale;
        }
    }
}
