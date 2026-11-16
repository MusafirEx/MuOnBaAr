using UnityEngine;

namespace MuOnBaAr.Runtime
{
    public enum AbilityExecutionMode { Normal = 0, Combo = 1, Charge = 2 }

    public enum ChargeReleaseType
    {
        Melee = 0,
        Projectiles = 1,
        Beam = 2,  // Reserved: no beam gameplay implementation yet.
        Laser = 3  // Reserved: no laser gameplay implementation yet.
    }

    public enum NormalAbilityReleaseType
    {
        AutoLegacy = 0, // Existing behavior: Projectile.Enabled otherwise melee.
        Melee = 1,
        Projectile = 2,
        Beam = 3, // Reserved.
        Laser = 4 // Reserved.
    }

    [System.Serializable]
    public sealed class AbilityChargeThreshold
    {
        [Tooltip("Minimum HOST CombatTime seconds charged to unlock this entry.")]
        [SerializeField, Min(0f)] private float minimumChargeSeconds;
        [SerializeField] private ChargeReleaseType releaseType = ChargeReleaseType.Melee;
        [SerializeField] private HitReactionType hitReaction = HitReactionType.Body;
        [Tooltip("Used only when Hit Reaction = Stun. Scaled CombatTime seconds.")]
        [SerializeField, Min(0.05f)] private float stunDurationSeconds = 2f;
        [Tooltip("Distance the victim is pushed away from the attacker. 0 disables knockback.")]
        [SerializeField, Min(0f)] private float hitKnockbackDistance = 0f;
        [SerializeField, Min(0.05f)] private float hitKnockbackSeconds = 0.25f;
        [Tooltip("Multiplier applied to the ability's Base Damage.")]
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;
        [Tooltip("Author optional absolute damage instead of multiplier.")]
        [SerializeField] private bool useCustomDamage;
        [SerializeField, Min(0)] private int customDamage = 10;
        [Tooltip("Used only when Release Type is Melee.")]
        [SerializeField, Min(0.1f)] private float meleeRange = 2.75f;
        [Tooltip("Each enabled entry launches one separate projectile. Limited by the network projectile pool.")]
        [SerializeField] private CombatProjectileSettings[] projectiles =
            System.Array.Empty<CombatProjectileSettings>();
        [Header("Release Movement Steps (local X/Z)")]
        [SerializeField] private CombatMovementStep[] movementSteps =
            System.Array.Empty<CombatMovementStep>();
        [Header("Release Combat VFX Timeline")]
        [SerializeField] private CombatVfxCue[] vfxTimeline =
            System.Array.Empty<CombatVfxCue>();
        [Header("Release Combat Audio Timeline")]
        [SerializeField] private CombatAudioCue[] audioTimeline =
            System.Array.Empty<CombatAudioCue>();

        public CombatMovementStep[] MovementSteps => movementSteps;
        public CombatVfxCue[] VfxTimeline => vfxTimeline;
        public CombatAudioCue[] AudioTimeline => audioTimeline;
        public float MinimumChargeSeconds => Mathf.Max(0f, minimumChargeSeconds);
        public ChargeReleaseType ReleaseType => releaseType;
        public HitReactionType HitReaction => hitReaction;
        public float StunDurationSeconds => Mathf.Max(0.05f, stunDurationSeconds);
        public float HitKnockbackDistance => Mathf.Max(0f, hitKnockbackDistance);
        public float HitKnockbackSeconds => Mathf.Max(0.05f, hitKnockbackSeconds);
        public float MeleeRange => Mathf.Max(0.1f, meleeRange);
        public int CalculateDamage(int baseDamage) => Mathf.Max(0,
            useCustomDamage ? customDamage :
                Mathf.RoundToInt(Mathf.Max(0, baseDamage) * Mathf.Max(0f, damageMultiplier)));
        public CombatProjectileSettings[] Projectiles => projectiles;
        public CombatProjectileSettings GetProjectile(int index)
        {
            return projectiles != null && index >= 0 && index < projectiles.Length
                ? projectiles[index] : null;
        }
    }

    [CreateAssetMenu(fileName = "AbilityDefinition", menuName = "MuOnBaAr/Definitions/Ability")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [SerializeField] private string abilityId = "ability";
        [SerializeField, Min(0)] private int energyCost;
        [SerializeField, Min(0f)] private float cooldownSeconds = 1f;
        [SerializeField, Min(0f)] private float startupSeconds = 0.1f;
        [SerializeField, Min(0f)] private float activeSeconds = 0.2f;
        [SerializeField, Min(0f)] private float recoverySeconds = 0.3f;
        [SerializeField, Min(0)] private int baseDamage = 10;
        [Header("Hit Reaction Applied To Victim")]
        [SerializeField] private HitReactionType hitReaction = HitReactionType.Body;
        [Tooltip("Used only when Hit Reaction = Stun. Scaled CombatTime seconds.")]
        [SerializeField, Min(0.05f)] private float stunDurationSeconds = 2f;
        [Tooltip("Distance the victim is pushed away from attacker. 0 disables knockback.")]
        [SerializeField, Min(0f)] private float hitKnockbackDistance = 0f;
        [SerializeField, Min(0.05f)] private float hitKnockbackSeconds = 0.25f;
        [SerializeField] private string animatorTrigger;
        [Header("Facing Direction")]
        [Tooltip("If enabled, face the locked opponent (or camera direction without a lock) " +
            "once when this ability starts. If disabled, keep current facing.")]
        [SerializeField] private bool lookTarget;
        [Header("Ability Execution")]
        [SerializeField] private AbilityExecutionMode executionMode = AbilityExecutionMode.Normal;
        [Tooltip("Auto Legacy preserves existing Projectile.Enabled or melee behavior." +
            " Beam/Laser are reserved and currently do not deal damage.")]
        [SerializeField] private NormalAbilityReleaseType normalReleaseType =
            NormalAbilityReleaseType.AutoLegacy;
        [Header("Charge Ability")]
        [SerializeField, Min(0.1f)] private float maximumChargeSeconds = 3f;
        [SerializeField, Min(0f)] private float chargeStartAnimationSeconds = 0.2f;
        [Tooltip("Ordered by Minimum Charge Seconds at runtime; array order does not need to be sorted." +
            " At least one threshold is recommended. Each threshold owns an independent projectile array.")]
        [SerializeField] private AbilityChargeThreshold[] chargeThresholds =
            System.Array.Empty<AbilityChargeThreshold>();
        [SerializeField] private bool autoReleaseAtMax = true;
        [Header("Optional Skill Combo (RMB/E/R repeated presses)")]
        [Tooltip("Optional follow-up stages. Empty = existing single-stage ability." +
            " Stage 0 uses this Ability. Element 0 = Combo1, Element 1 = Combo2.")]
        [SerializeField] private FormComboAttack[] comboFollowUps =
            System.Array.Empty<FormComboAttack>();
        [Tooltip("Earliest normalized time when the second press can be buffered.")]
        [SerializeField, Range(0f, 1f)] private float chainInputStartNormalized = 0.45f;
        [Tooltip("Normalized time when buffered Combo1 begins.")]
        [SerializeField, Range(0f, 1f)] private float chainTransitionNormalized = 0.85f;
        [Tooltip("Latest normalized time that accepts a follow-up press.")]
        [SerializeField, Range(0f, 1f)] private float chainInputEndNormalized = 1f;
        [Header("Movement Steps (local X/Z, multiple entries)")]
        [SerializeField] private CombatMovementStep[] movementSteps =
            System.Array.Empty<CombatMovementStep>();
        // Retain previous asset values as fallback for existing abilities.
        [HideInInspector, SerializeField, Min(0f)] private float forwardStepDistance;
        [HideInInspector, SerializeField, Range(0f, 1f)] private float stepStartNormalized;
        [HideInInspector, SerializeField, Range(0f, 1f)] private float stepEndNormalized = 0.5f;
        [Header("Hit / Projectile")]
        [SerializeField, Min(0.1f)] private float hitRange = 2.75f;
        [SerializeField] private CombatProjectileSettings projectile = new CombatProjectileSettings();
        [Tooltip("Optional normal-release burst. A nonempty array overrides legacy Projectile.")]
        [SerializeField] private CombatProjectileSettings[] projectiles =
            System.Array.Empty<CombatProjectileSettings>();
        [Header("Combat VFX Timeline (multiple entries)")]
        [SerializeField] private CombatVfxCue[] vfxTimeline = System.Array.Empty<CombatVfxCue>();
        [Header("Combat Audio Timeline (multiple clips)")]
        [SerializeField] private CombatAudioCue[] audioTimeline =
            System.Array.Empty<CombatAudioCue>();
        [Header("Body Part Visibility Timeline")]
        [Tooltip("Body Part Index references the active FormDefinition's collected mesh list.")]
        [SerializeField] private BodyPartVisibilityCue[] bodyPartTimeline =
            System.Array.Empty<BodyPartVisibilityCue>();
        [Header("Clock Up (optional ability)")]
        [SerializeField] private bool clockUpEnabled;
        [Tooltip("Time from Skill acceptance to Clock Up activation, in scaled " +
            "animation/CombatTime seconds. Zero preserves immediate activation.")]
        [SerializeField, Min(0f)] private float clockUpActivationDelaySeconds;
        [SerializeField, Min(0.1f)] private float clockUpDurationSeconds = 5f;
        [SerializeField, Range(0f, 1f)] private float clockUpOtherRiderScale = 0.1f;
        [SerializeField, Range(0f, 3f)] private float clockUpCasterScale = 1f;
        [Header("Clock Up VFX (optional, pooled locally)")]
        [SerializeField] private GameObject clockUpStartVfxPrefab;
        [SerializeField] private GameObject clockUpActiveVfxPrefab;
        [SerializeField] private GameObject clockUpEndVfxPrefab;
        [SerializeField, Min(0.05f)] private float clockUpStartVfxSeconds = 0.7f;
        [SerializeField, Min(0.05f)] private float clockUpEndVfxSeconds = 0.7f;
        [Tooltip("Position relative to the rider root, applied to each VFX.")]
        [SerializeField] private Vector3 clockUpVfxOffset = Vector3.up;

        [Header("Hook (single target pull)")]
        [SerializeField] private bool hookEnabled;
        [SerializeField, Min(0.1f)] private float hookRange = 10f;
        [SerializeField, Min(0.1f)] private float hookPullSpeed = 8f;
        [SerializeField, Min(0.1f)] private float hookDurationSeconds = 1.5f;
        [SerializeField, Min(0.1f)] private float hookStopDistance = 1.2f;

        [Header("Gravity Pull (area pull)")]
        [SerializeField] private bool gravityPullEnabled;
        [SerializeField, Min(0.1f)] private float gravityPullRadius = 6f;
        [SerializeField, Min(0.1f)] private float gravityPullSpeed = 6f;
        [SerializeField, Min(0.1f)] private float gravityPullDurationSeconds = 2f;
        [SerializeField, Min(0.05f)] private float gravityPullStopDistance = 0.5f;
        [Tooltip("Center relative to caster rotation. (0,0,0) means caster position.")]
        [SerializeField] private Vector3 gravityPullCenterOffset;

        public string AbilityId => abilityId;
        public int EnergyCost => Mathf.Max(0, energyCost);
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public float StartupSeconds => Mathf.Max(0f, startupSeconds);
        public float ActiveSeconds => Mathf.Max(0f, activeSeconds);
        public float RecoverySeconds => Mathf.Max(0f, recoverySeconds);
        public int BaseDamage => Mathf.Max(0, baseDamage);
        public HitReactionType HitReaction => hitReaction;
        public float StunDurationSeconds => Mathf.Max(0.05f, stunDurationSeconds);
        public float HitKnockbackDistance => Mathf.Max(0f, hitKnockbackDistance);
        public float HitKnockbackSeconds => Mathf.Max(0.05f, hitKnockbackSeconds);
        public string AnimatorTrigger => animatorTrigger;
        public bool LookTarget => lookTarget;
        public AbilityExecutionMode ExecutionMode => executionMode;
        public NormalAbilityReleaseType NormalReleaseType => normalReleaseType;
        public bool UsesNormalProjectile =>
            normalReleaseType == NormalAbilityReleaseType.Projectile ||
            (normalReleaseType == NormalAbilityReleaseType.AutoLegacy &&
             HasEnabledProjectiles);
        public bool HasEnabledProjectiles =>
            CombatProjectileSettings.HasEnabledEntries(projectiles, projectile);
        public bool NormalReleaseImplemented =>
            normalReleaseType != NormalAbilityReleaseType.Beam &&
            normalReleaseType != NormalAbilityReleaseType.Laser;
        public bool IsChargeAbility => executionMode == AbilityExecutionMode.Charge;
        public float MaximumChargeSeconds => Mathf.Max(0.1f, maximumChargeSeconds);
        public float ChargeStartAnimationSeconds => Mathf.Max(0f, chargeStartAnimationSeconds);
        public bool AutoReleaseAtMax => autoReleaseAtMax;
        public int ChargeThresholdCount => chargeThresholds == null ? 0 : chargeThresholds.Length;
        public AbilityChargeThreshold GetChargeThreshold(int index) =>
            chargeThresholds != null && index >= 0 && index < chargeThresholds.Length
                ? chargeThresholds[index] : null;

        // Returns the index of the highest unlocked threshold. If the designer
        // starts all thresholds above zero, release still uses the earliest one.
        // Ties resolve to the last element in Inspector order.
        public int SelectChargeThreshold(float elapsed)
        {
            int best = -1;
            float bestMin = float.NegativeInfinity;
            int earliest = -1;
            float earliestMin = float.PositiveInfinity;
            if (chargeThresholds == null) return -1;
            for (int i = 0; i < chargeThresholds.Length; ++i)
            {
                AbilityChargeThreshold threshold = chargeThresholds[i];
                if (threshold == null) continue;
                float minimum = threshold.MinimumChargeSeconds;
                if (minimum < earliestMin)
                {
                    earliestMin = minimum;
                    earliest = i;
                }
                if (minimum <= elapsed && minimum >= bestMin)
                {
                    bestMin = minimum;
                    best = i;
                }
            }
            return best >= 0 ? best : earliest;
        }

        // UI label is a threshold count rank, not an array index.
        public int CalculateChargeLevel(float elapsed)
        {
            int current = SelectChargeThreshold(elapsed);
            if (current < 0) return 1;
            float min = chargeThresholds[current].MinimumChargeSeconds;
            int rank = 0;
            for (int i = 0; i < ChargeThresholdCount; ++i)
                if (chargeThresholds[i] != null &&
                    chargeThresholds[i].MinimumChargeSeconds <= min)
                    ++rank;
            return Mathf.Max(1, rank);
        }
        public bool HasComboFollowUps => executionMode != AbilityExecutionMode.Charge &&
            comboFollowUps != null && comboFollowUps.Length > 0;
        public float ChainInputStartNormalized => Mathf.Clamp01(chainInputStartNormalized);
        public float ChainTransitionNormalized =>
            Mathf.Clamp(chainTransitionNormalized, ChainInputStartNormalized, 1f);
        public float ChainInputEndNormalized =>
            Mathf.Clamp(chainInputEndNormalized, ChainInputStartNormalized, 1f);
        public bool TryGetComboFollowUp(int index, out FormComboAttack stage)
        {
            stage = null;
            if (comboFollowUps == null || index < 0 || index >= comboFollowUps.Length)
                return false;
            stage = comboFollowUps[index];
            return stage != null;
        }
        public float DurationSeconds => Mathf.Max(0.1f,
            StartupSeconds + ActiveSeconds + RecoverySeconds);
        public CombatMovementStep[] MovementSteps => movementSteps;
        public float ForwardStepDistance => Mathf.Max(0f, forwardStepDistance);
        public float StepStartNormalized => Mathf.Clamp01(stepStartNormalized);
        public float StepEndNormalized => Mathf.Max(StepStartNormalized,
            Mathf.Clamp01(stepEndNormalized));
        public float HitRange => Mathf.Max(0.1f, hitRange);
        public CombatProjectileSettings Projectile => projectile;
        public CombatProjectileSettings[] Projectiles => projectiles;
        public CombatProjectileSettings GetProjectile(int index) =>
            index >= 0 ? (projectiles != null && index < projectiles.Length
                ? projectiles[index] : null) : projectile;
        public CombatVfxCue[] VfxTimeline => vfxTimeline;
        public CombatAudioCue[] AudioTimeline => audioTimeline;
        public BodyPartVisibilityCue[] BodyPartTimeline => bodyPartTimeline;
        public bool ClockUpEnabled => clockUpEnabled;
        public float ClockUpActivationDelaySeconds =>
            Mathf.Max(0f, clockUpActivationDelaySeconds);
        public float ClockUpDurationSeconds => Mathf.Max(0.1f, clockUpDurationSeconds);
        public float ClockUpOtherRiderScale => Mathf.Clamp01(clockUpOtherRiderScale);
        public float ClockUpCasterScale => Mathf.Clamp(clockUpCasterScale, 0f, 3f);
        public GameObject ClockUpStartVfxPrefab => clockUpStartVfxPrefab;
        public GameObject ClockUpActiveVfxPrefab => clockUpActiveVfxPrefab;
        public GameObject ClockUpEndVfxPrefab => clockUpEndVfxPrefab;
        public float ClockUpStartVfxSeconds => Mathf.Max(0.05f, clockUpStartVfxSeconds);
        public float ClockUpEndVfxSeconds => Mathf.Max(0.05f, clockUpEndVfxSeconds);
        public Vector3 ClockUpVfxOffset => clockUpVfxOffset;
        public bool HookEnabled => hookEnabled;
        public float HookRange => Mathf.Max(0.1f, hookRange);
        public float HookPullSpeed => Mathf.Max(0.1f, hookPullSpeed);
        public float HookDurationSeconds => Mathf.Max(0.1f, hookDurationSeconds);
        public float HookStopDistance => Mathf.Max(0.1f, hookStopDistance);
        public bool GravityPullEnabled => gravityPullEnabled;
        public float GravityPullRadius => Mathf.Max(0.1f, gravityPullRadius);
        public float GravityPullSpeed => Mathf.Max(0.1f, gravityPullSpeed);
        public float GravityPullDurationSeconds => Mathf.Max(0.1f, gravityPullDurationSeconds);
        public float GravityPullStopDistance => Mathf.Max(0.05f, gravityPullStopDistance);
        public Vector3 GravityPullCenterOffset => gravityPullCenterOffset;
    }
}
