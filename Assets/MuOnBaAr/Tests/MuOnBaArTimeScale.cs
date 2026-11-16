using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// An opt-in gameplay time domain. Never modifies UnityEngine.Time.timeScale.
    /// Keep the scale server-authoritative in multiplayer; this component itself
    /// does not replicate state over Photon Fusion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuOnBaArTimeScale : MonoBehaviour
    {
        public static MuOnBaArTimeScale Instance { get; private set; }

        [Header("World Gameplay Time")]
        [SerializeField, Range(0f, 3f)] private float worldScale = 1f;
        [SerializeField] private bool persistAcrossScenes;

        public float WorldScale => worldScale;

        // A participant can opt out of World Time, or apply its own
        // additional multiplier without changing Unity global time.
        public static float GetScale(Transform subject)
        {
            if (subject == null) return GetScale();
            MuOnBaArTimeParticipant participant =
                subject.GetComponentInParent<MuOnBaArTimeParticipant>();
            return participant == null ? GetScale() :
                GetScale(participant.IgnoreWorldScale) * participant.LocalMultiplier;
        }

        public static float ScaledDeltaTime(Transform subject)
        {
            return Time.deltaTime * GetScale(subject);
        }

        public static float ScaledFixedDeltaTime(Transform subject)
        {
            return Time.fixedDeltaTime * GetScale(subject);
        }

        // IMPORTANT: only multiply gameplay actions, velocities or step
        // deltas once. Fusion Runner.DeltaTime itself is NOT changed.
        public static float ScaledNetworkDeltaTime(float networkDeltaTime,
            Transform subject)
        {
            return networkDeltaTime * GetScale(subject);
        }

        // UI, input collection, camera, and networking use regular time instead.
        public static float GetScale(bool ignoreWorldScale = false)
        {
            return ignoreWorldScale || Instance == null ? 1f : Instance.worldScale;
        }

        public static float ScaledDeltaTime(bool ignoreWorldScale = false)
        {
            return Time.deltaTime * GetScale(ignoreWorldScale);
        }

        public static float ScaledFixedDeltaTime(bool ignoreWorldScale = false)
        {
            return Time.fixedDeltaTime * GetScale(ignoreWorldScale);
        }

        // Pass Runner.DeltaTime in Fusion FixedUpdateNetwork.
        public static float ScaledNetworkDeltaTime(float networkDeltaTime,
            bool ignoreWorldScale = false)
        {
            return networkDeltaTime * GetScale(ignoreWorldScale);
        }

        // Server/host should be the only caller in a multiplayer match.
        public void SetWorldScale(float scale)
        {
            worldScale = Mathf.Clamp(scale, 0f, 3f);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate MuOnBaArTimeScale: disabling duplicate.", this);
                enabled = false;
                return;
            }

            Instance = this;
            if (persistAcrossScenes)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnValidate()
        {
            worldScale = Mathf.Clamp(worldScale, 0f, 3f);
        }
    }
}
