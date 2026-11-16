using Fusion;
using Fusion.Addons.SimpleKCC;
using MuOnBaAr.Runtime;
using UnityEngine;

namespace MuOnBaAr.Networking
{
    public struct RiderProjectileState : INetworkStruct
    {
        public NetworkBool Active;
        public int Sequence;
        public int FormIndex;
        public NetworkBool FromSkill;
        public int SourceSlot;
        // -1 means ordinary skill/combo; otherwise the charge threshold array index.
        public int ChargeThresholdIndex;
        public int ChargeProjectileIndex;
        public int SkillStageIndex;
        public Vector3 Position;
        public Vector3 Velocity;
        public float HitRadius;
        public int Damage;
        public int HitReaction;
        public float StunDurationSeconds;
        public float KnockbackDistance;
        public float KnockbackSeconds;
        public float ExpiryCombatTime;
    }

    // A scheduled request is Fusion state: delayed shots survive authoritative resimulation.
    public struct RiderScheduledProjectileState : INetworkStruct
    {
        public NetworkBool Active;
        public float ReleaseCombatTime;
        public int FormIndex;
        public NetworkBool FromSkill;
        public int SourceSlot;
        public int SkillStageIndex;
        public int ChargeThresholdIndex;
        public int ProjectileIndex;
        public int Damage;
        public int HitReaction;
        public float StunDurationSeconds;
        public float KnockbackDistance;
        public float KnockbackSeconds;
    }

    [RequireComponent(typeof(NetworkObject), typeof(SimpleKCC))]
    public sealed class NetworkRiderController : NetworkBehaviour
    {
        [SerializeField] private ActorDefinition actor;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Renderer placeholderRenderer;
        [Header("Spawned Form Visual Alignment")]
        [Tooltip("Local-only model offset. ACL00's animation root is at Y=0 while the " +
            "prototype rider is spawned at Y=1. This does not move the NetworkObject " +
            "or CharacterController.")]
        [SerializeField] private Vector3 spawnedVisualLocalOffset = new Vector3(0f, -1f, 0f);
        [SerializeField, Range(30f, 1080f)] private float turnSpeed = 540f;
        [Header("Airborne Physics (Clock Up scaled)")]
        [Tooltip("Jump Height, Can Fly and Fly Speed are authored on Form Definition.")]
        [SerializeField, Range(-40f, -1f)] private float terminalFallSpeed = -30f;
        [SerializeField, Range(-25f, -1f)] private float gravity = -19.62f;
        [Header("Basic Combat (Prototype)")]
        [SerializeField, Min(1)] private int basicAttackDamage = 10;
        [SerializeField, Range(0.5f, 5f)] private float attackRange = 2.75f;
        [SerializeField, Range(10f, 180f)] private float attackHalfAngle = 75f;
        [SerializeField, Range(0.1f, 3f)] private float attackCooldownSeconds = 0.6f;
        [SerializeField, Range(2f, 20f)] private float lockRange = 9f;
        [Header("Line of Sight")]
        [Tooltip("Collision layers considered solid by authoritative melee attacks.")]
        [SerializeField] private LayerMask meleeLineOfSightLayers = ~0;
        [Tooltip("Use camera LOS to acquire and maintain local target lock.")]
        [SerializeField] private bool requireTargetLockLineOfSight = true;
        [Tooltip("Collision layers considered solid by camera target lock.")]
        [SerializeField] private LayerMask targetLockVisibilityLayers = ~0;
        [Header("KO and Respawn")]
        [SerializeField, Range(1f, 15f)] private float respawnDelaySeconds = 3f;
        [SerializeField, Range(0f, 10f)] private float respawnProtectionSeconds = 1.5f;
        [Header("Diagnostics")]
        [SerializeField] private bool enableCombatDebugLogs = true;
        [Header("Local Combat Feedback")]
        [SerializeField] private bool enableCombatFeedback = true;
        [SerializeField, Range(0.05f, 1f)] private float flashDurationSeconds = 0.18f;
        [SerializeField, Range(0.25f, 3f)] private float damagePopupDurationSeconds = 1f;

        private RiderWorldHealthBar worldHealthBar;
        private SimpleKCC simpleKCC;
        private int nextKCCDiagnosticTick;
        private float localAnimatorSpeedNormalized;
        private RiderThirdPersonCamera localCamera;
        private NetworkRiderController localLockedTarget;
        // Local-only highlight: displays on this client's locked opponent.
        private NetworkRiderController lastVisualLockedTarget;
        private GameObject targetLockMarker;
        private Vector3 originalSpawnPosition;
        private Quaternion originalSpawnRotation;
        private int lastLoggedProtectionExpiryTick;
        private bool feedbackInitialized;
        private int lastObservedHealth;
        private int lastObservedAttackSequence;
        private int lastObservedFeedbackForm = -1;
        private int lastFeedbackTint = -1;
        private int lastDamagePopupAmount;
        private float lastHitFeedbackTime = float.NegativeInfinity;
        private float lastAttackFeedbackTime = float.NegativeInfinity;
        private MaterialPropertyBlock feedbackMaterialBlock;

        // Host-owned world time value; replicated with the rider so predicted
        // locomotion uses the same scale on client and host.
        [Networked] public float ReplicatedWorldScale { get; private set; }
        // Independent per-rider gameplay clock. HOST advances it with the
        // effective time multiplier; global Fusion ticks stay unscaled.
        // The clock is exposed now for the next phased combat timing migration.
        [Networked] public float CombatTime { get; private set; }
        // Clock Up expiry is unscaled Fusion tick time, independent of CombatTime.
        [Networked] public int ClockUpEndTick { get; private set; }
        [Networked] public float ClockUpOtherScale { get; private set; }
        [Networked] public float ClockUpCasterScale { get; private set; }
        // Replicate the source Ability so each client can select the same VFX.
        [Networked] public int ClockUpSequence { get; private set; }
        [Networked] private int ClockUpSourceForm { get; set; }
        [Networked] private int ClockUpSourceSlot { get; set; }
        [Networked] private int PendingClockUpSourceForm { get; set; }
        [Networked] private int PendingClockUpSourceSlot { get; set; }
        // Delay until the Skill animation reaches the authored CombatTime point.
        // Settings are snapshotted when the HOST accepts the ability.
        [Networked] private NetworkBool PendingClockUpArmed { get; set; }
        [Networked] private float PendingClockUpAtCombatTime { get; set; }
        [Networked] private float PendingClockUpDurationSeconds { get; set; }
        [Networked] private float PendingClockUpOtherScale { get; set; }
        [Networked] private float PendingClockUpCasterScale { get; set; }

        // Server-authoritative pull gameplay. Caster owns timing and settings;
        // target riders combine forces through their existing SimpleKCC.Move.
        [Networked] private NetworkBool PullPending { get; set; }
        [Networked] private float PullPendingAtCombatTime { get; set; }
        [Networked] private int PullPendingFormIndex { get; set; }
        [Networked] private int PullPendingAbilitySlot { get; set; }
        [Networked] private float PullStartCombatTime { get; set; }
        [Networked] private float HookEndCombatTime { get; set; }
        [Networked] private NetworkId HookTargetId { get; set; }
        [Networked] private float HookPullSpeed { get; set; }
        [Networked] private float HookStopDistance { get; set; }
        [Networked] private float GravityEndCombatTime { get; set; }
        [Networked] private float GravityPullRadius { get; set; }
        [Networked] private float GravityPullSpeed { get; set; }
        [Networked] private float GravityPullStopDistance { get; set; }
        [Networked] private Vector3 GravityPullCenterOffset { get; set; }
        // Gameplay-time action locks; unlike Fusion ticks these continue for
        // the full visual duration when the world clock is slowed or frozen.
        [Networked] private float ComboLockUntilCombatTime { get; set; }
        [Networked] private float SkillLockUntilCombatTime { get; set; }
        [Networked] private float ComboStartCombatTime { get; set; }
        [Networked] private float SkillStartCombatTime { get; set; }
        [Networked] private float NextComboCombatTime { get; set; }
        [Networked] private float NextSkill0CombatTime { get; set; }
        [Networked] private float NextSkill1CombatTime { get; set; }
        [Networked] private float NextSkill2CombatTime { get; set; }
        [Networked] public int CurrentFormIndex { get; private set; }
        [Networked] public int Health { get; private set; }
        [Networked] public int Energy { get; private set; }
        [Networked] public int Level { get; private set; }
        [Networked] public int Experience { get; private set; }
        public int MaxHealth => actor != null ? actor.MaxHealth : 1;
        public int MaxEnergy => actor != null ? actor.MaxEnergy : 0;
        public int MaxLevel => actor != null ? actor.MaxLevel : 1;
        public int ExperienceToNextLevel => actor != null
            ? actor.ExperienceToNextLevel(Level) : 0;
        public Sprite CharacterIcon => actor != null ? actor.CharacterIcon : null;
        public int GetFormRequiredLevel(int index)
        {
            return actor != null && actor.TryGetForm(index, out var form)
                ? form.RequiredLevel : int.MaxValue;
        }
        public bool IsFormLevelUnlocked(int index) => Level >= GetFormRequiredLevel(index);
        [Networked] public int NextChangeTick { get; private set; }
        [Networked] public int NextAttackTick { get; private set; }
        [Networked] public int FormChangeLockedUntilTick { get; private set; }
        // 0=idle, 1=old form animating Start, 2=new form animating End.
        // State Authority owns the phase, input locks and actual Form switch.
        [Networked] public int FormTransitionPhase { get; private set; }
        [Networked] public int FormTransitionSequence { get; private set; }
        [Networked] private int FormTransitionTargetIndex { get; set; }
        [Networked] private float FormTransitionPhaseStartTime { get; set; }
        // Phase duration is selected by HOST from the source/target Form asset,
        // not inferred from animation data on a remote CLIENT.
        [Networked] private float FormTransitionPhaseDuration { get; set; }
        private int observedFormTransitionSequence = -1;
        private int observedFormTransitionPhase = -1;
        private static readonly int FormStartStateId =
            Animator.StringToHash("Base Layer.FormChangeStart");
        private static readonly int FormEndStateId =
            Animator.StringToHash("Base Layer.FormChangeEnd");
        [Header("Double-tap Dodge")]
        [SerializeField, Min(0.05f)] private float dodgeDurationSeconds = 0.48f;
        [SerializeField, Min(0f)] private float dodgeDistanceMetres = 2f;
        [SerializeField, Min(0f)] private float dodgeCooldownSeconds = 0.3f;
        [Networked] private int DodgeSequence { get; set; }
        [Networked] private int DodgeDirection { get; set; }
        [Networked] private float DodgeStartCombatTime { get; set; }
        [Networked] private float DodgeEndCombatTime { get; set; }
        [Networked] private float NextDodgeCombatTime { get; set; }
        private int observedDodgeSequence;
        private bool dodgeAnimationActive;
        [Networked] public int AttackSequence { get; private set; }
        [Networked] public int ComboIndex { get; private set; }
        [Networked] private NetworkBool ComboNextBuffered { get; set; }
        // Server-authoritative attack timing and displacement; used by predicted KCC.
        [Networked] public int AttackStartTick { get; private set; }
        [Networked] public int AttackActiveUntilTick { get; private set; }
        [Networked] private float AttackStepDistance { get; set; }
        [Networked] private float AttackStepStart { get; set; }
        [Networked] private float AttackStepEnd { get; set; }
        [Networked] private int SkillStartTick { get; set; }
        [Networked] private float SkillStepDistance { get; set; }
        [Networked] private float SkillStepStart { get; set; }
        [Networked] private float SkillStepEnd { get; set; }
        [Networked] private int NextSkill0Tick { get; set; }
        [Networked] private int NextSkill1Tick { get; set; }
        [Networked] private int NextSkill2Tick { get; set; }
        // One pending action impact, 16 active shots and 32 delayed releases per rider.
        // Host applies damage; peers render replicated projectile visuals.
        [Networked] private int PendingImpactTick { get; set; }
        [Networked] private NetworkBool PendingImpactArmed { get; set; }
        [Networked] private float PendingImpactCombatTime { get; set; }
        [Networked] private int PendingImpactDamage { get; set; }
        [Networked] private int PendingImpactHitReaction { get; set; }
        [Networked] private int PendingImpactHitAreaMode { get; set; }
        [Networked] private float PendingImpactHitHalfAngle { get; set; }
        [Networked] private float PendingImpactStunSeconds { get; set; }
        [Networked] private float PendingImpactKnockbackDistance { get; set; }
        [Networked] private float PendingImpactKnockbackSeconds { get; set; }
        [Networked] private int HitReactionSequence { get; set; }
        [Networked] private int HitReactionIndex { get; set; }
        [Networked] private float HitReactionEndCombatTime { get; set; }
        [Networked] private float StunEndCombatTime { get; set; }
        // Authoritative gameplay-seconds Y speed; only displacement is scaled to world seconds.
        [Networked] private float AirVerticalSpeed { get; set; }
        [Networked] private int AirMotionState { get; set; } // 0 ground, 1 rise, 2 fall, 3 flight
        [Networked] private NetworkBool FlightActive { get; set; }
        [Networked] private NetworkBool FlyHoldPending { get; set; }
        [Networked] private float FlyHoldStartCombatTime { get; set; }
        [Networked] private float FlyEnergyFraction { get; set; }
        private int observedAirMotionState = -1;
        private int lastPlayedAirborneStateHash;
        private int missingAirborneStateMask;
        private AnimatorControllerParameter[] cachedAirborneAnimatorParameters =
            System.Array.Empty<AnimatorControllerParameter>();
        // Existing imported controllers use the 'Airborn' sub-state name.
        // Keep legacy paths as fallbacks for other Forms.
        private static readonly string[] AirborneStatePrefixes =
        {
            "Base Layer.Airborn.", "Base Layer.Airborne.",
            "Base Layer.Locomotion.Airborn.",
            "Base Layer.Locomotion.Airborne.",
            "Base Layer.Locomotion.", "Base Layer."
        };
        private static readonly string[] JumpStateNames =
        {
            "StartJump", "Jump", "JumpStart", "Jumping", "JumpUp", "TakeOff",
            "Ascend"
        };
        private static readonly string[] FallStateNames =
        {
            "Fall", "Falling", "FallLoop", "JumpFall", "Drop",
            "Descend"
        };
        private static readonly string[] FlyStateNames =
        {
            "Fly", "Flying", "FlyLoop", "Flight", "Hover",
            "FlyIdle", "Float", "Flyforward", "FlyForward"
        };
        private static readonly int StuntBoolId = Animator.StringToHash("Stunt");
        // HOST-snapshotted hit impulse. Direction stays fixed even when the
        // attacker moves away; collision and gravity are handled by SimpleKCC.
        [Networked] private Vector3 KnockbackAwayDirection { get; set; }
        [Networked] private float KnockbackDistance { get; set; }
        [Networked] private float KnockbackStartCombatTime { get; set; }
        [Networked] private float KnockbackEndCombatTime { get; set; }
        [Header("Hit Reaction")]
        [Tooltip("Maximum gameplay lock for hit reaction in scaled CombatTime seconds.")]
        [SerializeField, Min(0.05f)] private float hitReactionSeconds = 0.55f;
        private int observedHitReactionSequence;
        private bool hitReactionAnimationPlaying;
        private static readonly int HitReactionIdleId =
            Animator.StringToHash("Base Layer.Locomotion.Idle");
        [Networked] private float PendingImpactRange { get; set; }
        [Networked] private NetworkBool PendingImpactProjectile { get; set; }
        [Networked] private NetworkBool PendingImpactSkill { get; set; }
        [Networked] private int PendingImpactSlot { get; set; }
        [Networked] private int PendingChargeThresholdIndex { get; set; }
        [Networked] private int ReleasedChargeThresholdIndex { get; set; }
        private const int ProjectileCapacity = 16;
        [Networked, Capacity(16)] private NetworkArray<RiderProjectileState> Shots => default;
        private const int ScheduledProjectileCapacity = 32;
        [Networked, Capacity(32)] private NetworkArray<RiderScheduledProjectileState>
            ScheduledShots => default;
        [Networked] private int ProjectileSequence { get; set; }
        private readonly GameObject[] shotVisuals = new GameObject[ProjectileCapacity];
        private readonly int[] shotVisualSequences = new int[ProjectileCapacity];
        private ProjectilePoolManager projectilePool;
        private GameObject clockUpStartVisual;
        private GameObject clockUpActiveVisual;
        private GameObject clockUpEndVisual;
        private float clockUpStartVisualTime;
        private float clockUpEndVisualTime;
        private int observedClockUpSequence;
        private bool observedClockUpActive;
        private int observedClockUpSourceForm = -1;
        private int observedClockUpSourceSlot = -1;
        private readonly System.Collections.Generic.HashSet<string> missingSpawnSockets =
            new System.Collections.Generic.HashSet<string>();
        [Networked] public int SkillSequence { get; private set; }
        [Networked] public int SkillIndex { get; private set; }
        [Networked] public int SkillComboStage { get; private set; }
        [Networked] private NetworkBool SkillComboNextBuffered { get; set; }
        [Networked] public NetworkBool ChargeActive { get; private set; }
        [Networked] public int ChargeLevel { get; private set; }
        [Networked] private float ChargeStartCombatTime { get; set; }
        [Networked] private int ChargeSequence { get; set; }
        [Networked] private NetworkBool SkillWasCharged { get; set; }
        private int observedChargeSequence;
        private bool chargeLoopPlaying;
        [Networked] public int SkillLockedUntilTick { get; private set; }
        [Networked] public int RespawnTick { get; private set; }
        [Networked] public int InvulnerableUntilTick { get; private set; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }
        [Networked] private float ReplicatedAnimatorSpeedNormalized { get; set; }

        // Cached mesh GameObjects for local-only visibility animation.
        // Network state already replicates attack/skill sequence and CombatTime.
        private GameObject[] formBodyObjects = System.Array.Empty<GameObject>();
        private bool[] formBodyDefaultStates = System.Array.Empty<bool>();
        private bool[] formBodyDesiredStates = System.Array.Empty<bool>();
        private int warnedBodyPartIndex = -1;
        private GameObject spawnedVisual;
        private Animator spawnedFormAnimator;
        private bool supportsLocomotion;
        private Vector3 previousRenderPosition;
        private bool hasRenderPosition;
        private static readonly int LocomotionId = Animator.StringToHash("Locomotion");
        private static readonly int BasicCombo0StateId =
            Animator.StringToHash("Base Layer.Attack Combo.Combo0");
        private static readonly int LocomotionIdleStateId =
            Animator.StringToHash("Base Layer.Locomotion.Idle");
        private int observedAnimationAttackSequence;
        private bool attackAnimationInitialized;
        private bool basicAttackAnimationActive;
        private float basicAttackAnimationRemainingTime;
        [SerializeField, Min(0.1f)] private float basicAttackAnimationLength = 0.8666667f;
        [SerializeField, Range(0f, 0.25f)] private float attackAnimationCrossFade = 0.06f;
        private bool missingComboStateLogged;
        private int observedSkillSequence;
        private float skillAnimationRemainingTime;
        private bool skillAnimationActive;
        private int observedComboAudioSequence;
        private int observedSkillAudioSequence;
        private readonly System.Collections.Generic.HashSet<int> firedComboAudioCues =
            new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.HashSet<int> firedSkillAudioCues =
            new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.List<AudioSource> activeCombatAudio =
            new System.Collections.Generic.List<AudioSource>();
        private CombatAudioPoolManager combatAudioPool;
        // Cosmetic action cues are locally scheduled against replicated CombatTime.
        private int observedComboVfxSequence;
        private int observedSkillVfxSequence;
        private readonly System.Collections.Generic.HashSet<int> firedComboCues =
            new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.HashSet<int> firedSkillCues =
            new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.List<GameObject> timedActionVisuals =
            new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<float> timedActionVisualEnds =
            new System.Collections.Generic.List<float>();
        [Networked] public float CurrentSkillDuration { get; private set; }
        private int displayedFormIndex = -1;

        public int FormCount => actor != null ? actor.FormCount : 0;
        public string GetFormName(int index)
        {
            return actor != null && actor.TryGetForm(index, out var form)
                ? form.DisplayName : "Unknown";
        }

        public string HudFormName =>
            actor != null && actor.TryGetForm(CurrentFormIndex, out var form)
                ? form.DisplayName : "Unknown";

        public string HudLockedTarget =>
            localLockedTarget != null && localLockedTarget.Object != null
                ? "Rider HP " + localLockedTarget.Health : "None";

        private GameObject GetTargetLockMarker()
        {
            if (targetLockMarker == null)
            {
                Transform marker = transform.Find("OverheadHealthCanvas/HealthPanel/TargetLock");
                if (marker != null) targetLockMarker = marker.gameObject;
            }
            return targetLockMarker;
        }

        private void SetTargetLockMarkerVisible(bool visible)
        {
            GameObject marker = GetTargetLockMarker();
            if (marker != null && marker.activeSelf != visible)
                marker.SetActive(visible);
        }

        private void RefreshLocalTargetLockMarker()
        {
            if (lastVisualLockedTarget == localLockedTarget)
                return;

            if (lastVisualLockedTarget != null)
                lastVisualLockedTarget.SetTargetLockMarkerVisible(false);

            lastVisualLockedTarget = localLockedTarget;
            if (lastVisualLockedTarget != null)
                lastVisualLockedTarget.SetTargetLockMarkerVisible(true);
        }

        public override void Spawned()
        {
            // The prefab marker starts hidden on every client, including
            // remote-player instances; local input later reveals only its target.
            SetTargetLockMarkerVisible(false);
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
                ReplicatedWorldScale = MuOnBaArTimeScale.GetScale();
                CombatTime = 0f;
                ClockUpEndTick = 0;
                ClockUpOtherScale = 1f;
                ClockUpCasterScale = 1f;
                ClockUpSequence = 0;
                ClockUpSourceForm = 0;
                ClockUpSourceSlot = 0;
                PendingClockUpSourceForm = 0;
                PendingClockUpSourceSlot = 0;
                PendingClockUpArmed = false;
                PendingClockUpAtCombatTime = 0f;
                PendingClockUpDurationSeconds = 0f;
                PendingClockUpOtherScale = 1f;
                PendingClockUpCasterScale = 1f;
                PullPending = false;
                PullStartCombatTime = 0f;
                HookEndCombatTime = 0f;
                HookTargetId = default(NetworkId);
                GravityEndCombatTime = 0f;
                ComboLockUntilCombatTime = 0f;
                SkillLockUntilCombatTime = 0f;
                ComboStartCombatTime = 0f;
                SkillStartCombatTime = 0f;
                NextComboCombatTime = 0f;
                NextSkill0CombatTime = NextSkill1CombatTime = NextSkill2CombatTime = 0f;
                PendingImpactArmed = false;
                PendingImpactHitReaction = (int)HitReactionType.None;
                PendingImpactStunSeconds = 0f;
                StunEndCombatTime = 0f;
                AirVerticalSpeed = 0f;
                AirMotionState = 0;
                FlightActive = false;
                FlyHoldPending = false;
                FlyHoldStartCombatTime = 0f;
                FlyEnergyFraction = 0f;
                PendingImpactKnockbackDistance = 0f;
                PendingImpactKnockbackSeconds = 0.25f;
                KnockbackAwayDirection = Vector3.zero;
                KnockbackDistance = 0f;
                KnockbackStartCombatTime = 0f;
                KnockbackEndCombatTime = 0f;
                HitReactionSequence = 0;
                HitReactionIndex = 0;
                HitReactionEndCombatTime = 0f;
                PendingChargeThresholdIndex = -1;
                ReleasedChargeThresholdIndex = -1;
                PendingImpactCombatTime = 0f;
                CurrentFormIndex = initial.CurrentFormIndex;
                Health = initial.Health;
                Energy = initial.Energy;
                Level = 1;
                Experience = 0;
                NextChangeTick = initial.NextChangeTick;
                NextAttackTick = 0;
                FormChangeLockedUntilTick = 0;
                FormTransitionPhase = 0;
                FormTransitionSequence = 0;
                FormTransitionTargetIndex = -1;
                FormTransitionPhaseStartTime = 0f;
                FormTransitionPhaseDuration = 0f;
                DodgeSequence = 0;
                DodgeDirection = 0;
                DodgeStartCombatTime = 0f;
                DodgeEndCombatTime = 0f;
                NextDodgeCombatTime = 0f;
                AttackSequence = 0;
                ComboIndex = 0;
                ComboNextBuffered = false;
                AttackStartTick = 0;
                AttackActiveUntilTick = 0;
                AttackStepDistance = 0f;
                AttackStepStart = 0f;
                AttackStepEnd = 0f;
                SkillStartTick = 0;
                SkillStepDistance = 0f;
                SkillStepStart = 0f;
                SkillStepEnd = 0f;
                NextSkill0Tick = NextSkill1Tick = NextSkill2Tick = 0;
                PendingImpactTick = 0;
                ProjectileSequence = 0;
                for (int i = 0; i < ProjectileCapacity; ++i)
                    Shots.Set(i, default(RiderProjectileState));
                for (int i = 0; i < ScheduledProjectileCapacity; ++i)
                    ScheduledShots.Set(i, default(RiderScheduledProjectileState));
                SkillSequence = 0;
                SkillIndex = 0;
                SkillComboStage = 0;
                SkillComboNextBuffered = false;
                ChargeActive = false;
                ChargeLevel = 0;
                ChargeStartCombatTime = 0f;
                ChargeSequence = 0;
                SkillWasCharged = false;
                SkillLockedUntilTick = 0;
                CurrentSkillDuration = 0f;
                RespawnTick = 0;
                InvulnerableUntilTick = 0;
                originalSpawnPosition = transform.position;
                originalSpawnRotation = transform.rotation;
            }

            simpleKCC = GetComponent<SimpleKCC>();
            if (simpleKCC == null)
            {
                Debug.LogError("NetworkRiderPlayer requires Photon SimpleKCC on the prefab. " +
                    "Run Tools > MuOnBaAr > Migrate Prototype Rider to Simple KCC.", this);
                return;
            }
            // Explicitly enable the controller and collider after the legacy
            // prefab was migrated. Avoid relying on newly added component defaults.
            simpleKCC.SetActive(true);
            simpleKCC.SetShape(EKCCShape.Capsule, 0.35f, 2f);
            // Internal KCC gravity follows unscaled Runner.DeltaTime. Disable
            // it and integrate vertical gameplay speed against CombatTime.
            // SimpleKCC still owns collision, ground checks and step logic.
            simpleKCC.SetGravity(0f);
            simpleKCC.SetCollisionLayerMask(~0);
            Debug.Log("[MuOnBaAr][2H KCC] Spawn player=" + Object.InputAuthority +
                " stateAuthority=" + Object.HasStateAuthority +
                " inputAuthority=" + Object.HasInputAuthority +
                " active=" + simpleKCC.IsActive +
                " grounded=" + simpleKCC.IsGrounded +
                " position=" + simpleKCC.Position.ToString("F3"), this);
            displayedFormIndex = -1;
            for (int i = 0; i < ProjectileCapacity; ++i)
                shotVisualSequences[i] = 0;
            observedAnimationAttackSequence = AttackSequence;
            observedDodgeSequence = DodgeSequence;
            observedHitReactionSequence = HitReactionSequence;
            hitReactionAnimationPlaying = false;
            observedAirMotionState = -1;
            observedSkillSequence = SkillSequence;
            observedFormTransitionSequence = FormTransitionSequence;
            observedFormTransitionPhase = FormTransitionPhase;
            observedChargeSequence = ChargeSequence;
            chargeLoopPlaying = false;
            observedComboAudioSequence = AttackSequence;
            observedSkillAudioSequence = SkillSequence;
            observedComboVfxSequence = AttackSequence;
            observedSkillVfxSequence = SkillSequence;
            observedClockUpSequence = 0;
            observedClockUpActive = false;
            skillAnimationActive = false;
            attackAnimationInitialized = true;
            basicAttackAnimationActive = false;
            missingComboStateLogged = false;
            localAnimatorSpeedNormalized = 0f;
            if (Object.HasStateAuthority)
                ReplicatedAnimatorSpeedNormalized = 0f;
            feedbackInitialized = false;
            lastFeedbackTint = -1;
            lastObservedFeedbackForm = -1;
            // The child World Space Canvas belongs to each spawned Rider prefab.
            worldHealthBar = GetComponentInChildren<RiderWorldHealthBar>(true);
            projectilePool = ProjectilePoolManager.FindOrCreate();
            RefreshFormVisual();

            if (Object.HasInputAuthority)
                BindLocalCamera();

            if (Object.HasStateAuthority)
                LogCombat("SPAWN", "hp=" + Health + " energy=" + Energy +
                    " form=" + CurrentFormIndex + " position=" +
                    originalSpawnPosition.ToString("F2"));
        }

        public override void FixedUpdateNetwork()
        {
            // Host owns collision, movement and damage in this prototype.
            // Do not replay non-networked CharacterController state on clients.
            if (actor == null || Object == null ||
                (!Object.HasStateAuthority && !Object.HasInputAuthority))
                return;

            // Keep the global gameplay clock host-authoritative. Unity's
            // actual Time.timeScale and Fusion Runner.DeltaTime are unchanged.
            if (Object.HasStateAuthority)
            {
                ReplicatedWorldScale = ComputeClockUpWorldScale();
                // This clock stops at WorldScale=0, but network ticks,
                // player input and UI continue normally.
                CombatTime += Runner.DeltaTime * GetRiderTimeScale();
                ProcessPendingClockUp();
                ProcessPendingPull();
                ProcessFormTransition();
            }

            // Damage/projectile lifecycle is always resolved by HOST.
            if (Object.HasStateAuthority)
            {
                ProcessPendingImpact();
                ProcessScheduledProjectiles();
                SimulateProjectile();
            }

            // One event per protection window, rather than one event per frame.
            if (Object.HasStateAuthority && InvulnerableUntilTick > 0 &&
                Runner.Tick.Raw >= InvulnerableUntilTick &&
                lastLoggedProtectionExpiryTick != InvulnerableUntilTick)
            {
                lastLoggedProtectionExpiryTick = InvulnerableUntilTick;
                LogCombat("PROTECTION_EXPIRED", "hp=" + Health +
                    " protectedUntilTick=" + InvulnerableUntilTick);
            }

            // KO countdown is authoritative even when a client's input is missing.
            if (Health <= 0)
            {
                localAnimatorSpeedNormalized = 0f;
                if (Object.HasStateAuthority)
                    ReplicatedAnimatorSpeedNormalized = 0f;
                if (Object.HasStateAuthority)
                {
                    if (RespawnTick == 0)
                        BeginKnockout(Runner.Tick.Raw);

                    if (RiderRespawnRules.ShouldRespawn(
                            Health, Runner.Tick.Raw, RespawnTick))
                        RespawnAtOriginalSpawn();

                    if (GetInput(out RiderNetworkInput knockedOutInput))
                        PreviousButtons = knockedOutInput.Buttons;
                }
                // KO disables steering, but a launched body still falls until
                // touching collision. The authoritative respawn resets Y speed.
                if (Health <= 0 && simpleKCC != null)
                    simpleKCC.Move(AdvanceAirborne(false, false, false));
                return;
            }

            if (!GetInput(out RiderNetworkInput input))
            {
                // A missing packet cannot fake a button release.
                if (Object.HasStateAuthority)
                    ProcessCharge(true, new Vector2(transform.forward.x, transform.forward.z));
                localAnimatorSpeedNormalized = 0f;
                if (Object.HasStateAuthority)
                    ReplicatedAnimatorSpeedNormalized = 0f;
                if (simpleKCC != null)
                    simpleKCC.Move(GetPullVelocityFromOtherRiders() +
                        GetKnockbackVelocity() + AdvanceAirborne(false, false, false));
                return;
            }

            // Resolve authoritative actions BEFORE movement to avoid one free
            // WASD step on the first frame of Combo0.
            bool pressed = false;
            bool confirmedForm = false;
            bool jumpPressed = false;
            if (Object.HasStateAuthority)
            {
                pressed = input.Buttons.WasPressed(
                    PreviousButtons, RiderInputButton.CycleForm);
                confirmedForm = input.Buttons.WasPressed(
                    PreviousButtons, RiderInputButton.ConfirmForm);
                bool attackPressed = input.Buttons.WasPressed(
                    PreviousButtons, RiderInputButton.BasicAttack);
                jumpPressed = input.Buttons.WasPressed(
                    PreviousButtons, RiderInputButton.Jump);
                bool s0 = input.Buttons.WasPressed(PreviousButtons, RiderInputButton.Skill0);
                bool s1 = input.Buttons.WasPressed(PreviousButtons, RiderInputButton.Skill1);
                bool s2 = input.Buttons.WasPressed(PreviousButtons, RiderInputButton.Skill2);
                if (input.Buttons.WasPressed(PreviousButtons, RiderInputButton.DodgeForward))
                    TryBeginDodge(0);
                else if (input.Buttons.WasPressed(PreviousButtons, RiderInputButton.DodgeLeft))
                    TryBeginDodge(1);
                else if (input.Buttons.WasPressed(PreviousButtons, RiderInputButton.DodgeBack))
                    TryBeginDodge(2);
                else if (input.Buttons.WasPressed(PreviousButtons, RiderInputButton.DodgeRight))
                    TryBeginDodge(3);
                PreviousButtons = input.Buttons;

                if (FormTransitionPhase == 0 &&
                    CombatTime >= DodgeEndCombatTime &&
                    CombatTime >= HitReactionEndCombatTime)
                {
                    if (s0) TryFormSkill(0, input.Aim);
                    else if (s1) TryFormSkill(1, input.Aim);
                    else if (s2) TryFormSkill(2, input.Aim);
                    if (attackPressed) TryBasicAttack(input.Aim);
                }
                ProcessBufferedCombo(input.Aim);
                ProcessBufferedSkillCombo(input.Aim);
                bool skillHeld = SkillIndex == 0
                    ? input.Buttons.IsSet(RiderInputButton.Skill0Held)
                    : SkillIndex == 1
                        ? input.Buttons.IsSet(RiderInputButton.Skill1Held)
                        : input.Buttons.IsSet(RiderInputButton.Skill2Held);
                ProcessCharge(skillHeld, input.Aim);
            }

            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            if (simpleKCC != null && actor.TryGetForm(CurrentFormIndex, out var currentForm))
            {
                bool sprintHeld = input.Buttons.IsSet(RiderInputButton.Sprint);
                // Both locomotion and airborne gravity follow the rider's
                // CombatTime. Input sampling, network tick and UI stay unscaled.
                // If no TimeScale manager exists this safely defaults to 1x.
                // The scene manager is only read on State Authority. Clients
                // use the replicated world clock, never a local scene value.
                float locomotionTimeScale = GetRiderTimeScale();
                float speed = actor.MoveSpeed * currentForm.MovementMultiplier *
                    RiderLocomotionRules.MovementSpeedMultiplier(sprintHeld) *
                    locomotionTimeScale;
                Vector3 direction = new Vector3(move.x, 0f, move.y);
                int tick = Runner.Tick.Raw;
                bool comboActive = CombatTime < ComboLockUntilCombatTime;
                bool skillActive = ChargeActive || CombatTime < SkillLockUntilCombatTime;
                bool actionLocked = comboActive || skillActive ||
                    FormTransitionPhase != 0 || CombatTime < DodgeEndCombatTime ||
                    CombatTime < HitReactionEndCombatTime;
                // All movement branches share exactly one CombatTime airborne
                // integration per tick; SimpleKCC provides collision resolution.
                Vector3 verticalVelocity = AdvanceAirborne(
                    jumpPressed && !actionLocked && Object.HasStateAuthority,
                    input.Buttons.IsSet(RiderInputButton.JumpHeld) && !actionLocked,
                    input.Buttons.IsSet(RiderInputButton.FlyDescendHeld) && !actionLocked);
                // Animation and gameplay cannot use locomotion while attacking.
                localAnimatorSpeedNormalized = actionLocked ? 0f :
                    Mathf.Clamp(move.magnitude *
                        RiderLocomotionRules.MovementSpeedMultiplier(sprintHeld), 0f, 1.5f);
                if (Object.HasStateAuthority)
                    ReplicatedAnimatorSpeedNormalized = localAnimatorSpeedNormalized;

                // KCC handles collision/grounding for every authored local X/Z
                // displacement. Multiple windows may overlap and add together.
                Vector3 beforeMove = simpleKCC.Position;
                Vector3 externalPull = GetPullVelocityFromOtherRiders();
                if (FormTransitionPhase != 0)
                    simpleKCC.Move(externalPull + verticalVelocity);
                else if (CombatTime < HitReactionEndCombatTime)
                    simpleKCC.Move(GetKnockbackVelocity() + externalPull +
                        verticalVelocity);
                else if (CombatTime < DodgeEndCombatTime)
                {
                    float duration = Mathf.Max(0.001f,
                        DodgeEndCombatTime - DodgeStartCombatTime);
                    float dodgeSpeed = dodgeDistanceMetres / duration * GetRiderTimeScale();
                    Vector2 v = DodgeDirection == 0 ? Vector2.up :
                        DodgeDirection == 1 ? Vector2.left :
                        DodgeDirection == 2 ? Vector2.down : Vector2.right;
                    simpleKCC.Move(LocalActionVelocity(v * dodgeSpeed *
                        Runner.DeltaTime) + externalPull + verticalVelocity);
                }
                else if (comboActive)
                {
                    Vector2 delta = Vector2.zero;
                    if (actor.TryGetForm(CurrentFormIndex, out var comboForm) &&
                        comboForm.TryGetComboAttack(ComboIndex, out var comboData))
                        delta = ActionMovementDelta(comboData.MovementSteps,
                            ComboStartCombatTime, ComboLockUntilCombatTime,
                            comboData.ForwardStepDistance, comboData.StepStartNormalized,
                            comboData.StepEndNormalized);
                    simpleKCC.Move(LocalActionVelocity(delta) + externalPull +
                        verticalVelocity);
                }
                else if (ChargeActive)
                    simpleKCC.Move(externalPull + verticalVelocity);
                else if (skillActive)
                {
                    Vector2 delta = Vector2.zero;
                    if (actor.TryGetForm(CurrentFormIndex, out var skillForm) &&
                        skillForm.Abilities != null && SkillIndex >= 0 &&
                        SkillIndex < skillForm.Abilities.Length &&
                        skillForm.Abilities[SkillIndex] != null)
                    {
                        AbilityDefinition ability = skillForm.Abilities[SkillIndex];
                        FormComboAttack followUp = GetCurrentSkillComboFollowUp();
                        AbilityChargeThreshold charged = GetCurrentChargeReleaseThreshold();
                        delta = charged != null
                            ? ActionMovementDelta(charged.MovementSteps,
                                SkillStartCombatTime, SkillLockUntilCombatTime,
                                0f, 0f, 0f)
                            : followUp != null
                            ? ActionMovementDelta(followUp.MovementSteps,
                                SkillStartCombatTime, SkillLockUntilCombatTime,
                                followUp.ForwardStepDistance, followUp.StepStartNormalized,
                                followUp.StepEndNormalized)
                            : ActionMovementDelta(ability.MovementSteps,
                                SkillStartCombatTime, SkillLockUntilCombatTime,
                                ability.ForwardStepDistance, ability.StepStartNormalized,
                                ability.StepEndNormalized);
                    }
                    simpleKCC.Move(LocalActionVelocity(delta) + externalPull +
                        verticalVelocity);
                }
                else
                    simpleKCC.Move(direction * speed + externalPull +
                        verticalVelocity);

                // Diagnostic every ~1 second at 60Hz when movement is requested.
                // Helps isolate input-delivery from KCC collision/movement failure.
                if (!actionLocked && direction.sqrMagnitude > 0.01f &&
                    Runner.Tick.Raw >= nextKCCDiagnosticTick &&
                    Runner.IsForward)
                {
                    nextKCCDiagnosticTick = Runner.Tick.Raw + 60;
                    Debug.Log("[MuOnBaAr][2H KCC] player=" + Object.InputAuthority +
                        " stateAuthority=" + Object.HasStateAuthority +
                        " inputAuthority=" + Object.HasInputAuthority +
                        " input=" + move.ToString("F2") +
                        " active=" + simpleKCC.IsActive +
                        " grounded=" + simpleKCC.IsGrounded +
                        " before=" + beforeMove.ToString("F3") +
                        " after=" + simpleKCC.Position.ToString("F3") +
                        " realSpeed=" + simpleKCC.RealSpeed.ToString("F3"), this);
                }

                if (!actionLocked && direction.sqrMagnitude > 0.0001f)
                {
                    Quaternion facing = Quaternion.LookRotation(direction);
                    Quaternion smoothFacing = Quaternion.RotateTowards(
                        simpleKCC.TransformRotation, facing,
                        turnSpeed * Runner.DeltaTime * locomotionTimeScale);
                    simpleKCC.SetLookRotation(smoothFacing);
                }
            }

            // Only the server can change Forms; attacks/skills ran before KCC.
            if (!Object.HasStateAuthority)
                return;

            if ((!pressed && !confirmedForm) || actor.FormCount < 2)
                return;
            // Reject additional Form selection until both animation phases end.
            if (FormTransitionPhase != 0)
                return;

            // A Form cannot be changed while the current attack is playing.
            // Host tick-based lock is shared by all clients; animation/render
            // frame times are intentionally NOT used for gameplay authority.
            // When form-specific combo chains are added, extend the lock at each
            // accepted combo step and clear only when that chain is finished.
            if (CombatTime < ComboLockUntilCombatTime ||
                CombatTime < SkillLockUntilCombatTime || ChargeActive)
            {
                LogCombat("FORM_CHANGE_REJECTED",
                    "reason=ATTACK_ACTIVE lockedUntilTick=" +
                    FormChangeLockedUntilTick + " currentTick=" + Runner.Tick.Raw);
                return;
            }

            int target = confirmedForm
                ? input.RequestedFormIndex
                : (CurrentFormIndex + 1) % actor.FormCount;
            if (target < 0 || target >= actor.FormCount || target == CurrentFormIndex)
                return;
            if (!IsFormLevelUnlocked(target))
            {
                LogCombat("FORM_CHANGE_REJECTED", "reason=LEVEL_REQUIRED target=" +
                    target + " required=" + GetFormRequiredLevel(target) +
                    " playerLevel=" + Level);
                return;
            }

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

            // Apply cost/cooldown once at accepted input, but delay the
            // authoritative FormIndex and prefab swap until Start completes.
            Energy = state.Energy;
            NextChangeTick = state.NextChangeTick;
            FormTransitionTargetIndex = state.CurrentFormIndex;
            FormTransitionPhase = 1;
            FormTransitionSequence++;
            FormTransitionPhaseStartTime = CombatTime;
            actor.TryGetForm(CurrentFormIndex, out var outgoingForm);
            actor.TryGetForm(FormTransitionTargetIndex, out var incomingForm);
            float startDuration = outgoingForm != null
                ? outgoingForm.FormChangeStartSeconds : 1.216667f;
            float endDuration = incomingForm != null
                ? incomingForm.FormChangeEndSeconds : 2.483333f;
            FormTransitionPhaseDuration = Mathf.Max(0.05f, startDuration);
            FormChangeLockedUntilTick = Runner.Tick.Raw +
                SecondsToTicks(startDuration + endDuration);
            LogCombat("FORM_CHANGE_START", "from=" + CurrentFormIndex +
                " to=" + FormTransitionTargetIndex + " energy=" + Energy +
                " startSeconds=" + startDuration.ToString("F3") +
                " endSeconds=" + endDuration.ToString("F3"));
        }

        private void ProcessFormTransition()
        {
            if (!Object.HasStateAuthority || FormTransitionPhase == 0)
                return;
            if (Health <= 0)
            {
                CancelFormTransition();
                return;
            }
            if (FormTransitionPhase == 1 &&
                CombatTime - FormTransitionPhaseStartTime >=
                    FormTransitionPhaseDuration)
            {
                int previous = CurrentFormIndex;
                CurrentFormIndex = FormTransitionTargetIndex;
                FormTransitionPhase = 2;
                FormTransitionPhaseStartTime = CombatTime;
                if (actor.TryGetForm(CurrentFormIndex, out var destinationForm))
                    FormTransitionPhaseDuration = destinationForm.FormChangeEndSeconds;
                else
                    FormTransitionPhaseDuration = 2.483333f;
                LogCombat("FORM_CHANGED", "from=" + previous +
                    " to=" + CurrentFormIndex + " energy=" + Energy);
            }
            else if (FormTransitionPhase == 2 &&
                CombatTime - FormTransitionPhaseStartTime >=
                    FormTransitionPhaseDuration)
            {
                FormTransitionPhase = 0;
                FormTransitionTargetIndex = -1;
                FormTransitionPhaseDuration = 0f;
                FormChangeLockedUntilTick = 0;
                LogCombat("FORM_CHANGE_END", "form=" + CurrentFormIndex);
            }
        }

        private void CancelFormTransition()
        {
            if (FormTransitionPhase == 0) return;
            LogCombat("FORM_CHANGE_CANCEL", "phase=" + FormTransitionPhase);
            FormTransitionPhase = 0;
            FormTransitionTargetIndex = -1;
            FormTransitionPhaseDuration = 0f;
            FormChangeLockedUntilTick = 0;
        }

        private void UpdateFormTransitionAnimation()
        {
            if (spawnedFormAnimator == null) return;
            int phase = FormTransitionPhase;
            if (phase == 0)
            {
                // The next Form's Animator may have an authored
                // FormChangeEnd -> Locomotion transition. Respect it when it
                // has already fired; otherwise finish explicitly.
                if (observedFormTransitionPhase == 2 && Health > 0 &&
                    spawnedFormAnimator.HasState(0, LocomotionIdleStateId))
                {
                    AnimatorStateInfo current = spawnedFormAnimator.GetCurrentAnimatorStateInfo(0);
                    if (current.fullPathHash == FormEndStateId)
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            LocomotionIdleStateId, attackAnimationCrossFade, 0, 0f);
                }
                observedFormTransitionSequence = FormTransitionSequence;
                observedFormTransitionPhase = 0;
                return;
            }
            if (observedFormTransitionSequence == FormTransitionSequence &&
                observedFormTransitionPhase == phase)
                return;
            observedFormTransitionSequence = FormTransitionSequence;
            observedFormTransitionPhase = phase;
            skillAnimationActive = false;
            basicAttackAnimationActive = false;
            int state = phase == 1 ? FormStartStateId : FormEndStateId;
            if (spawnedFormAnimator.HasState(0, state))
            {
                spawnedFormAnimator.CrossFadeInFixedTime(
                    state, attackAnimationCrossFade, 0, 0f);
            }
            else
                Debug.LogWarning("[MuOnBaAr][FormChange] Missing " +
                    (phase == 1 ? "FormChangeStart" : "FormChangeEnd") +
                    " on current Form Animator.", this);
        }

        // Each HOST rider reads active Clock Up users of the same Fusion Runner.
        // The strongest active slowdown wins for non-casters.
        private float ComputeClockUpWorldScale()
        {
            float scale = MuOnBaArTimeScale.GetScale();
            foreach (NetworkRiderController other in
                FindObjectsOfType<NetworkRiderController>())
            {
                if (other == null || other == this || other.Runner != Runner ||
                    other.Object == null || other.Health <= 0 ||
                    other.ClockUpEndTick <= Runner.Tick.Raw)
                    continue;
                scale = Mathf.Min(scale, Mathf.Clamp01(other.ClockUpOtherScale));
            }
            return Mathf.Clamp(scale, 0f, 3f);
        }

        private void ProcessPendingClockUp()
        {
            if (!Object.HasStateAuthority || !PendingClockUpArmed ||
                CombatTime < PendingClockUpAtCombatTime)
                return;
            PendingClockUpArmed = false;
            if (Health <= 0) return;
            StartClockUp(PendingClockUpDurationSeconds,
                PendingClockUpCasterScale, PendingClockUpOtherScale,
                PendingClockUpSourceForm, PendingClockUpSourceSlot);
        }

        private void StartClockUp(float duration, float casterScale,
            float otherScale, int sourceForm, int sourceSlot)
        {
            // Start the full duration only when the animation reaches release.
            ClockUpSourceForm = sourceForm;
            ClockUpSourceSlot = sourceSlot;
            ClockUpSequence++;
            ClockUpEndTick = Runner.Tick.Raw + SecondsToTicks(duration);
            ClockUpCasterScale = Mathf.Clamp(casterScale, 0f, 3f);
            ClockUpOtherScale = Mathf.Clamp01(otherScale);
            LogCombat("CLOCK_UP_START", "endTick=" + ClockUpEndTick +
                " casterScale=" + ClockUpCasterScale.ToString("F2") +
                " otherScale=" + ClockUpOtherScale.ToString("F2"));
        }

        private bool IsClockUpActive()
        {
            return Runner != null && Object != null && Health > 0 &&
                ClockUpEndTick > Runner.Tick.Raw;
        }

        private void ProcessPendingPull()
        {
            if (!Object.HasStateAuthority || !PullPending ||
                CombatTime < PullPendingAtCombatTime) return;
            PullPending = false;
            if (Health <= 0 ||
                !actor.TryGetForm(PullPendingFormIndex, out var form) ||
                form.Abilities == null ||
                PullPendingAbilitySlot < 0 ||
                PullPendingAbilitySlot >= form.Abilities.Length)
                return;
            AbilityDefinition ability = form.Abilities[PullPendingAbilitySlot];
            if (ability == null) return;

            PullStartCombatTime = CombatTime;
            HookEndCombatTime = CombatTime;
            GravityEndCombatTime = CombatTime;
            HookTargetId = default(NetworkId);
            if (ability.HookEnabled)
            {
                NetworkRiderController target = FindHookTarget(ability.HookRange);
                if (target != null)
                {
                    HookTargetId = target.Object.Id;
                    HookPullSpeed = ability.HookPullSpeed;
                    HookStopDistance = ability.HookStopDistance;
                    HookEndCombatTime = CombatTime + ability.HookDurationSeconds;
                    LogCombat("HOOK_START", "target=" + target.Object.Id);
                }
                else
                    LogCombat("HOOK_MISS", "no eligible target");
            }
            if (ability.GravityPullEnabled)
            {
                GravityPullRadius = ability.GravityPullRadius;
                GravityPullSpeed = ability.GravityPullSpeed;
                GravityPullStopDistance = ability.GravityPullStopDistance;
                GravityPullCenterOffset = ability.GravityPullCenterOffset;
                GravityEndCombatTime = CombatTime + ability.GravityPullDurationSeconds;
                LogCombat("GRAVITY_PULL_START", "radius=" +
                    GravityPullRadius.ToString("F2"));
            }
        }

        private NetworkRiderController FindHookTarget(float maxRange)
        {
            NetworkRiderController selected = null;
            float closestSq = maxRange * maxRange;
            Vector2 forward = RiderCombatRules.PlanarForward(transform.rotation);
            foreach (NetworkRiderController candidate in
                FindObjectsOfType<NetworkRiderController>())
            {
                if (!IsLiveOpponent(candidate) ||
                    !candidate.Object.HasStateAuthority ||
                    !RiderCombatRules.IsInMeleeArc(transform.position,
                        candidate.transform.position, forward, maxRange,
                        attackHalfAngle) || !HasLineOfSight(candidate))
                    continue;
                Vector3 delta = candidate.transform.position - transform.position;
                delta.y = 0f;
                float sq = delta.sqrMagnitude;
                if (sq >= closestSq) continue;
                closestSq = sq;
                selected = candidate;
            }
            return selected;
        }

        // This runs on the TARGET's HOST-side network movement, so pulling
        // respects KCC collision and never teleports or moves target transforms.
        private Vector3 GetPullVelocityFromOtherRiders()
        {
            if (Object == null || !Object.HasStateAuthority || Health <= 0 ||
                Runner == null) return Vector3.zero;
            Vector3 total = Vector3.zero;
            Vector3 targetPosition = simpleKCC != null
                ? simpleKCC.Position : transform.position;
            foreach (NetworkRiderController caster in
                FindObjectsOfType<NetworkRiderController>())
            {
                if (caster == null || caster == this ||
                    caster.Object == null || caster.Runner != Runner ||
                    !caster.Object.HasStateAuthority || caster.Health <= 0)
                    continue;

                if (caster.HookEndCombatTime > caster.PullStartCombatTime &&
                    caster.CombatTime >= caster.PullStartCombatTime &&
                    caster.CombatTime < caster.HookEndCombatTime &&
                    caster.HookTargetId == Object.Id)
                {
                    total += PullVelocityToward(
                        caster.transform.position, targetPosition,
                        caster.HookPullSpeed, caster.HookStopDistance);
                }
                if (caster.GravityEndCombatTime > caster.PullStartCombatTime &&
                    caster.CombatTime >= caster.PullStartCombatTime &&
                    caster.CombatTime < caster.GravityEndCombatTime)
                {
                    Vector3 center = caster.transform.TransformPoint(
                        caster.GravityPullCenterOffset);
                    Vector3 planar = center - targetPosition;
                    planar.y = 0f;
                    if (planar.sqrMagnitude <=
                        caster.GravityPullRadius * caster.GravityPullRadius)
                        total += PullVelocityToward(center, targetPosition,
                            caster.GravityPullSpeed,
                            caster.GravityPullStopDistance);
                }
            }
            // Subject's own time scale controls displacement (a frozen target
            // stays frozen); 20m/s cap prevents stacked casters tunnelling.
            return Vector3.ClampMagnitude(total, 20f) * GetRiderTimeScale();
        }

        private Vector3 PullVelocityToward(Vector3 center,
            Vector3 targetPosition, float speed, float stopDistance)
        {
            Vector3 delta = center - targetPosition;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= stopDistance || distance < 0.0001f)
                return Vector3.zero;
            float maxPerTick = Mathf.Max(0f, distance - stopDistance) /
                Mathf.Max(0.001f, Runner.DeltaTime);
            return delta / distance * Mathf.Min(speed, maxPerTick);
        }

        // HOST-replicated gameplay clock. Fusion ticks and Unity Time stay normal.
        private float GetRiderTimeScale()
        {
            MuOnBaArTimeParticipant participant =
                GetComponentInParent<MuOnBaArTimeParticipant>();
            bool active = IsClockUpActive();
            float world = active
                ? Mathf.Clamp(ClockUpCasterScale, 0f, 3f)
                : Mathf.Clamp(ReplicatedWorldScale, 0f, 3f);
            float rate = !active && participant != null &&
                participant.IgnoreWorldScale ? 1f : world;
            return rate * (participant != null ? participant.LocalMultiplier : 1f);
        }

        // Local presentation follows the replicated KO state. Death always
        // has priority over locomotion, stun, dodge, attacks and form change.
        private bool deathAnimationActive;
        private bool missingDeathAnimationReported;

        // KCC receives a world-space velocity. The persistent Y speed is
        // expressed in gameplay seconds, so slowing time scales both gravity
        // integration and displacement (including 0x freeze) consistently.
        private Vector3 AdvanceAirborne(bool jumpPressed, bool jumpHeld, bool flyDescend)
        {
            if (simpleKCC == null || Runner == null) return Vector3.zero;
            float rate = Mathf.Max(0f, GetRiderTimeScale());
            bool grounded = simpleKCC.IsGrounded;
            FormDefinition form = null;
            if (actor != null) actor.TryGetForm(CurrentFormIndex, out form);
            if (Object.HasStateAuthority)
            {
                bool eligible = form != null && form.CanFly && Health > 0 &&
                    Energy > 0 && FormTransitionPhase == 0 &&
                    CombatTime >= HitReactionEndCombatTime &&
                    CombatTime >= StunEndCombatTime;
                // A Form with CanFly is not forced into flight on spawn.
                if (!eligible)
                {
                    FlightActive = false;
                    FlyHoldPending = false;
                    FlyEnergyFraction = 0f;
                }
                if (grounded && AirVerticalSpeed <= 0f)
                {
                    AirVerticalSpeed = 0f;
                    AirMotionState = 0;
                    // Landing switches back to grounded movement.
                    if (FlightActive)
                    {
                        FlightActive = false;
                        FlyHoldPending = false;
                        FlyEnergyFraction = 0f;
                    }
                }

                // Short Space press jumps normally even when CanFly is true.
                if (jumpPressed && Health > 0 && rate > 0f)
                {
                    if (eligible && !FlightActive)
                    {
                        FlyHoldPending = true;
                        FlyHoldStartCombatTime = CombatTime;
                    }
                    if (!FlightActive && grounded && form != null &&
                        form.JumpHeight > 0f)
                    {
                        AirVerticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) *
                            form.JumpHeight);
                        AirMotionState = 1;
                        LogCombat("JUMP", "form=" + CurrentFormIndex +
                            " height=" + form.JumpHeight.ToString("F2"));
                    }
                }

                // Only an uninterrupted hold enters flight.
                if (!jumpHeld)
                    FlyHoldPending = false;
                if (eligible && !FlightActive && FlyHoldPending && jumpHeld &&
                    CombatTime - FlyHoldStartCombatTime >= form.FlyHoldSeconds)
                {
                    FlightActive = true;
                    FlyHoldPending = false;
                    FlyEnergyFraction = 0f;
                    LogCombat("FLIGHT_START", "form=" + CurrentFormIndex);
                }

                if (FlightActive && eligible)
                {
                    // Consume Energy by the scaled gameplay clock, including
                    // when hovering. Keep fractional points between ticks.
                    FlyEnergyFraction += Runner.DeltaTime * rate *
                        form.FlyEnergyPerSecond;
                    int cost = Mathf.FloorToInt(FlyEnergyFraction);
                    if (cost > 0)
                    {
                        Energy = Mathf.Max(0, Energy - cost);
                        FlyEnergyFraction -= cost;
                    }
                    if (Energy <= 0)
                    {
                        FlightActive = false;
                        FlyHoldPending = false;
                        FlyEnergyFraction = 0f;
                        LogCombat("FLIGHT_END", "reason=ENERGY_EMPTY");
                    }
                    else
                    {
                        // Left Alt wins when Space and Alt are both held.
                        AirVerticalSpeed = flyDescend ? -form.FlySpeed :
                            jumpHeld ? form.FlySpeed : 0f;
                        AirMotionState = 3;
                    }
                }
                if (!FlightActive)
                {
                    // KO, stun, Form transition or exhausted Energy all fall
                    // through to the standard collision-aware gravity path.
                    if (!grounded || AirVerticalSpeed > 0f)
                    {
                        AirVerticalSpeed = RiderAirborneRules.IntegrateVertical(
                            AirVerticalSpeed, gravity, terminalFallSpeed,
                            Runner.DeltaTime, rate);
                        AirMotionState = AirVerticalSpeed > 0f ? 1 : 2;
                    }
                    else
                    {
                        AirVerticalSpeed = 0f;
                        AirMotionState = 0;
                    }
                }
            }
            float ySpeed = grounded && AirVerticalSpeed <= 0f && !FlightActive
                ? -2f : AirVerticalSpeed;
            float worldYSpeed = RiderAirborneRules.WorldVerticalSpeed(ySpeed, rate);
            // Absolute powered-flight ceiling measured from world Y=0.
            // Takeoff elevation never increases the ceiling. Normal jumps
            // remain independently governed by FormDefinition.JumpHeight.
            if (FlightActive && form != null)
                worldYSpeed = RiderAirborneRules.LimitFlyingAscent(
                    worldYSpeed, simpleKCC.Position.y,
                    form.MaxFlyHeight, Runner.DeltaTime);
            return Vector3.up * worldYSpeed;
        }

        // Resolve actual nested Animator paths. The imported ACL00 Controller
        // uses "Airborn", not "Locomotion" for aerial animation states.
        private int FindAirborneState(int motion)
        {
            string[] names = motion == 1 ? JumpStateNames :
                motion == 3 ? FlyStateNames : FallStateNames;
            // ACL00 has a moving Flyforward and a hovering Float state.
            // Take the moving flight state only when there is movement input.
            if (motion == 3)
            {
                float movement = Object != null && Object.HasInputAuthority
                    ? localAnimatorSpeedNormalized
                    : ReplicatedAnimatorSpeedNormalized;
                if (movement > 0.1f || Mathf.Abs(AirVerticalSpeed) > 0.05f)
                {
                    for (int i = 0; i < AirborneStatePrefixes.Length; ++i)
                    {
                        int forward = Animator.StringToHash(
                            AirborneStatePrefixes[i] + "Flyforward");
                        if (spawnedFormAnimator.HasState(0, forward))
                            return forward;
                    }
                }
            }
            for (int i = 0; i < AirborneStatePrefixes.Length; ++i)
            {
                for (int j = 0; j < names.Length; ++j)
                {
                    int state = Animator.StringToHash(
                        AirborneStatePrefixes[i] + names[j]);
                    if (spawnedFormAnimator.HasState(0, state))
                        return state;
                }
            }
            return 0;
        }

        // Set only parameters authored on the actual Animator Controller.
        // This supports user-authored transitions without requiring
        // unrelated Forms to contain new parameters.
        private void SyncAirborneAnimatorParameters(int desired, int previous)
        {
            bool grounded = desired == 0;
            bool jumping = desired == 1;
            bool falling = desired == 2;
            bool flying = desired == 3;
            // ACL00's CanFly drives Jump/Float -> flight transitions.
            // This means ACTIVE powered flight, not only Form capability:
            // a short Space jump must never become flight automatically.
            bool poweredFlight = FlightActive && Health > 0;
            // Float means stationary, level hover ONLY while powered flight
            // is active. Ascending, descending, moving and falling are not Float.
            float horizontal = Object != null && Object.HasInputAuthority
                ? localAnimatorSpeedNormalized
                : ReplicatedAnimatorSpeedNormalized;
            bool hovering = poweredFlight && grounded == false &&
                Mathf.Abs(AirVerticalSpeed) <= 0.05f &&
                horizontal <= 0.1f;
            for (int i = 0; i < cachedAirborneAnimatorParameters.Length; ++i)
            {
                AnimatorControllerParameter parameter =
                    cachedAirborneAnimatorParameters[i];
                string name = parameter.name;
                int hash = parameter.nameHash;
                if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    if (name == "Grounded" || name == "IsGrounded" ||
                        name == "OnGround")
                        spawnedFormAnimator.SetBool(hash, grounded);
                    else if (name == "CanFly")
                        spawnedFormAnimator.SetBool(hash, poweredFlight);
                    else if (name == "IsHovering" || name == "Hovering")
                        spawnedFormAnimator.SetBool(hash, hovering);
                    else if (name == "Airborn" || name == "Airborne" ||
                        name == "IsAirborne" || name == "InAir")
                        spawnedFormAnimator.SetBool(hash, !grounded);
                    else if (name == "Jump" || name == "Jumping" ||
                        name == "IsJumping")
                        spawnedFormAnimator.SetBool(hash, jumping);
                    else if (name == "Fall" || name == "Falling" ||
                        name == "IsFalling")
                        spawnedFormAnimator.SetBool(hash, falling);
                    else if (name == "Fly" || name == "Flying" ||
                        name == "IsFlying" || name == "Float" ||
                        name == "IsFloating")
                        spawnedFormAnimator.SetBool(hash, flying);
                }
                else if (parameter.type == AnimatorControllerParameterType.Float)
                {
                    if (name == "VerticalSpeed" || name == "YVelocity" ||
                        name == "VerticalVelocity" || name == "AirSpeed")
                        spawnedFormAnimator.SetFloat(hash, AirVerticalSpeed);
                }
                else if (parameter.type == AnimatorControllerParameterType.Int)
                {
                    if (name == "AirMotionState" || name == "AirborneState")
                        spawnedFormAnimator.SetInteger(hash, desired);
                }
                else if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    (name == "Jump" || name == "JumpStart") &&
                    jumping && previous != 1)
                    spawnedFormAnimator.SetTrigger(hash);
            }
        }

        private void UpdateAirborneAnimation()
        {
            if (spawnedFormAnimator == null || Health <= 0) return;
            bool actionLocked = FormTransitionPhase != 0 || ChargeActive ||
                CombatTime < HitReactionEndCombatTime ||
                CombatTime < DodgeEndCombatTime ||
                CombatTime < ComboLockUntilCombatTime ||
                CombatTime < SkillLockUntilCombatTime;
            if (actionLocked)
            {
                // Respect attack, dodge, form change and hit reaction priority.
                observedAirMotionState = -1;
                lastPlayedAirborneStateHash = 0;
                return;
            }

            int desired = AirMotionState;
            int previous = observedAirMotionState;
            // Parameters must keep updating even when the motion state has
            // not changed, to support authored Animator transitions.
            SyncAirborneAnimatorParameters(desired, previous);
            if (desired == observedAirMotionState) return;
            observedAirMotionState = desired;

            if (desired == 0)
            {
                // On ACL00, play JumpLand first; the authored transition
                // JumpLand -> Locomotion is repaired by the Editor utility.
                // Other Forms without a landing state keep the Idle fallback.
                if (lastPlayedAirborneStateHash != 0)
                {
                    int landing = Animator.StringToHash(
                        "Base Layer.Airborn.JumpLand");
                    if (!spawnedFormAnimator.HasState(0, landing))
                        landing = Animator.StringToHash(
                            "Base Layer.Airborne.JumpLand");
                    if (spawnedFormAnimator.HasState(0, landing))
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            landing, attackAnimationCrossFade, 0, 0f);
                    else if (spawnedFormAnimator.HasState(
                        0, LocomotionIdleStateId))
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            LocomotionIdleStateId, attackAnimationCrossFade, 0, 0f);
                }
                lastPlayedAirborneStateHash = 0;
                return;
            }

            int stateHash = FindAirborneState(desired);
            if (desired == 2 && stateHash == 0)
            {
                // ACL00 has no dedicated Fall state. Do NOT substitute Float:
                // while descending from a normal jump, retain the authored
                // Jump pose until Grounded triggers JumpLand. If powered
                // flight just ended, leave Float/Flyforward for Jump instead.
                if (previous != 3 && previous != -1) return;
                for (int i = 0; i < AirborneStatePrefixes.Length; ++i)
                {
                    int fallback = Animator.StringToHash(
                        AirborneStatePrefixes[i] + "Jump");
                    if (!spawnedFormAnimator.HasState(0, fallback)) continue;
                    stateHash = fallback;
                    break;
                }
            }
            if (stateHash != 0)
            {
                lastPlayedAirborneStateHash = stateHash;
                spawnedFormAnimator.CrossFadeInFixedTime(
                    stateHash, attackAnimationCrossFade, 0, 0f);
            }
            else if ((missingAirborneStateMask & (1 << desired)) == 0)
            {
                missingAirborneStateMask |= 1 << desired;
                string motionName = desired == 1 ? "Jump" :
                    desired == 3 ? "Fly/Float" : "Fall";
                string controllerName =
                    spawnedFormAnimator.runtimeAnimatorController != null
                        ? spawnedFormAnimator.runtimeAnimatorController.name
                        : "(none)";
                Debug.LogWarning(
                    "[MuOnBaAr][Airborn Animator] Missing " + motionName +
                    " state. Controller=" + controllerName +
                    ". Checked Base Layer.Airborn.* and fallback paths. " +
                    "Select the controller asset to inspect its exact state names.",
                    this);
            }
        }

        private void UpdateDeathAnimation()
        {
            if (spawnedFormAnimator == null) return;
            if (Health <= 0)
            {
                if (deathAnimationActive) return;
                deathAnimationActive = true;
                SyncAirborneAnimatorParameters(0, observedAirMotionState);
                observedAirMotionState = -1;
                lastPlayedAirborneStateHash = 0;
                basicAttackAnimationActive = false;
                skillAnimationActive = false;
                dodgeAnimationActive = false;
                hitReactionAnimationPlaying = false;
                if (HasAnimatorStuntBool())
                    spawnedFormAnimator.SetBool(StuntBoolId, false);
                // Restore meshes hidden during an interrupted attack.
                for (int i = 0; i < formBodyObjects.Length; ++i)
                    if (formBodyObjects[i] != null && i < formBodyDefaultStates.Length)
                        formBodyObjects[i].SetActive(formBodyDefaultStates[i]);
                string[] choices = { "Base Layer.Death", "Base Layer.Status.Death",
                    "Base Layer.Status.Die", "Base Layer.Status.Dead" };
                foreach (string name in choices)
                {
                    int state = Animator.StringToHash(name);
                    if (!spawnedFormAnimator.HasState(0, state)) continue;
                    spawnedFormAnimator.CrossFadeInFixedTime(
                        state, attackAnimationCrossFade, 0, 0f);
                    return;
                }
                if (!missingDeathAnimationReported)
                {
                    missingDeathAnimationReported = true;
                    Debug.LogWarning("[MuOnBaAr][Death] Missing Death animation state. " +
                        "Author Base Layer.Death or Status.Death in the Form Controller.",
                        this);
                }
                return;
            }
            if (!deathAnimationActive) return;
            deathAnimationActive = false;
            observedAnimationAttackSequence = AttackSequence;
            observedSkillSequence = SkillSequence;
            observedDodgeSequence = DodgeSequence;
            observedHitReactionSequence = HitReactionSequence;
            if (HasAnimatorStuntBool())
                spawnedFormAnimator.SetBool(StuntBoolId, false);
            if (spawnedFormAnimator.HasState(0, LocomotionIdleStateId))
                spawnedFormAnimator.CrossFadeInFixedTime(
                    LocomotionIdleStateId, attackAnimationCrossFade, 0, 0f);
        }

        public override void Render()
        {
            if (actor == null)
                return;

            if (CurrentFormIndex != displayedFormIndex)
            {
                RefreshFormVisual();
                deathAnimationActive = false;
                missingDeathAnimationReported = false;
            }

            if (spawnedFormAnimator != null)
                spawnedFormAnimator.speed = GetRiderTimeScale();
            UpdateDeathAnimation();
            if (Health > 0)
            {
                UpdateLocomotionAnimation();
                UpdateDodgeAnimation();
                UpdateHitReactionAnimation();
                if (FormTransitionPhase != 0)
                    UpdateFormTransitionAnimation();
                else
                {
                    UpdateFormTransitionAnimation();
                    UpdateBasicAttackAnimation();
                    UpdateFormSkillAnimation();
                }
            }
            if (Health > 0) UpdateAirborneAnimation();
            UpdateProjectileVisual();
            UpdateClockUpVfx();
            if (Health > 0)
            {
                UpdateActionVfxTimelines();
                UpdateActionAudioTimelines();
                UpdateBodyPartVisibility();
            }

            if (enableCombatFeedback)
            {
                ObserveCombatFeedback();
                ApplyPlaceholderFeedback();
            }

            // Update this rider's local-only uGUI from replicated Health on
            // every client; no additional networked properties or RPCs.
            if (worldHealthBar == null)
                worldHealthBar = GetComponentInChildren<RiderWorldHealthBar>(true);
            if (worldHealthBar != null)
            {
                int tick = Runner != null ? Runner.Tick.Raw : 0;
                bool shield = RiderCombatFeedbackRules.IsProtectionActive(
                    Health, tick, InvulnerableUntilTick);
                worldHealthBar.SetState(
                    Object != null ? Object.InputAuthority.ToString() : "Rider",
                    Health, actor.MaxHealth, Health <= 0, shield,
                    enableCombatFeedback, lastHitFeedbackTime,
                    lastAttackFeedbackTime, flashDurationSeconds,
                    lastDamagePopupAmount, damagePopupDurationSeconds);
            }

            if (Object != null && Object.HasInputAuthority && localCamera == null)
                BindLocalCamera();
        }

        private void ObserveCombatFeedback()
        {
            if (!feedbackInitialized)
            {
                feedbackInitialized = true;
                lastObservedHealth = Health;
                lastObservedAttackSequence = AttackSequence;
                lastObservedFeedbackForm = CurrentFormIndex;
                return;
            }

            int damage = RiderCombatFeedbackRules.DamageTaken(lastObservedHealth, Health);
            if (damage > 0)
            {
                lastDamagePopupAmount = damage;
                lastHitFeedbackTime = Time.unscaledTime;
            }

            if (RiderCombatFeedbackRules.IsNewAttack(
                    lastObservedAttackSequence, AttackSequence))
                lastAttackFeedbackTime = Time.unscaledTime;

            lastObservedHealth = Health;
            lastObservedAttackSequence = AttackSequence;
            lastObservedFeedbackForm = CurrentFormIndex;
        }

        private void ApplyPlaceholderFeedback()
        {
            // Only tint the prototype placeholder. Real imported models keep
            // their materials unchanged; their overhead flash remains visible.
            if (placeholderRenderer == null || !placeholderRenderer.enabled)
                return;

            float now = Time.unscaledTime;
            bool hitActive = now - lastHitFeedbackTime < flashDurationSeconds;
            bool attackActive = now - lastAttackFeedbackTime < flashDurationSeconds;
            int desiredTint = hitActive ? 2 : attackActive ? 1 : 0;
            if (desiredTint == lastFeedbackTint)
                return;

            if (feedbackMaterialBlock == null)
                feedbackMaterialBlock = new MaterialPropertyBlock();

            placeholderRenderer.GetPropertyBlock(feedbackMaterialBlock);
            Color tint = Color.HSVToRGB(
                (CurrentFormIndex * 0.37f) % 1f, 0.75f, 0.95f);
            if (desiredTint == 1)
                tint = new Color(1f, 0.9f, 0.2f);
            else if (desiredTint == 2)
                tint = new Color(1f, 0.22f, 0.22f);

            feedbackMaterialBlock.SetColor("_Color", tint);
            feedbackMaterialBlock.SetColor("_BaseColor", tint);
            placeholderRenderer.SetPropertyBlock(feedbackMaterialBlock);
            lastFeedbackTint = desiredTint;
        }

        private void BindLocalCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;

            localCamera = camera.GetComponent<RiderThirdPersonCamera>();
            if (localCamera == null)
                localCamera = camera.gameObject.AddComponent<RiderThirdPersonCamera>();

            localCamera.Bind(transform);
        }

        private void OnDestroy()
        {
            if (projectilePool != null)
            {
                for (int i = 0; i < ProjectileCapacity; ++i)
                    if (shotVisuals[i] != null)
                        projectilePool.Release(shotVisuals[i]);
                projectilePool.Unregister(this);
            }
            if (lastVisualLockedTarget != null)
                lastVisualLockedTarget.SetTargetLockMarkerVisible(false);
            lastVisualLockedTarget = null;
            if (localCamera != null)
                localCamera.Release(transform);
        }

        private void Update()
        {
            if (Object == null || !Object.HasInputAuthority)
                return;

            if (Health <= 0)
            {
                localLockedTarget = null;
                RefreshLocalTargetLockMarker();
                if (localCamera != null)
                    localCamera.SetLockTarget(null);
                return;
            }

            if (localLockedTarget != null &&
                (localLockedTarget.Object == null || localLockedTarget.Health <= 0 ||
                 (localLockedTarget.transform.position - transform.position).sqrMagnitude >
                 lockRange * lockRange * 2.25f ||
                 !HasCameraLineOfSight(localLockedTarget, Camera.main)))
                localLockedTarget = null;

            if (Input.GetKeyDown(KeyCode.Q))
                ToggleLocalTargetLock();

            RefreshLocalTargetLockMarker();
            if (localCamera != null)
                localCamera.SetLockTarget(localLockedTarget != null
                    ? localLockedTarget.transform : null);
        }

        private void ToggleLocalTargetLock()
        {
            if (localLockedTarget != null)
            {
                localLockedTarget = null;
                return;
            }

            Camera camera = Camera.main;
            Vector3 view = camera != null ? camera.transform.forward : transform.forward;
            Vector2 forward = new Vector2(view.x, view.z).normalized;
            if (forward.sqrMagnitude < 0.0001f)
                forward = new Vector2(transform.forward.x, transform.forward.z).normalized;

            float bestDistanceSquared = lockRange * lockRange;
            NetworkRiderController[] riders = FindObjectsOfType<NetworkRiderController>();
            for (int i = 0; i < riders.Length; i++)
            {
                NetworkRiderController candidate = riders[i];
                if (!IsLiveOpponent(candidate))
                    continue;

                Vector3 delta = candidate.transform.position - transform.position;
                Vector2 planar = new Vector2(delta.x, delta.z);
                float squareDistance = planar.sqrMagnitude;
                if (squareDistance < 0.001f || squareDistance > bestDistanceSquared ||
                    Vector2.Angle(forward, planar) > 90f ||
                    !HasCameraLineOfSight(candidate, camera))
                    continue;

                bestDistanceSquared = squareDistance;
                localLockedTarget = candidate;
            }
        }

        public Vector2 GetLocalCombatAim(float viewYaw)
        {
            if (localLockedTarget != null && IsLiveOpponent(localLockedTarget))
            {
                Vector3 delta = localLockedTarget.transform.position - transform.position;
                Vector2 planar = new Vector2(delta.x, delta.z);
                if (planar.sqrMagnitude > 0.001f)
                    return planar.normalized;
            }

            float radians = viewYaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        private bool IsLiveOpponent(NetworkRiderController candidate)
        {
            return candidate != null && candidate != this && candidate.Object != null &&
                   candidate.Runner == Runner && candidate.Health > 0;
        }

        // The current FormDefinition.Abilities[] determines available skill slots.
        // At present, only slots 0..2 have key bindings and matching ACL00 states.
        private void TryFormSkill(int slot, Vector2 aim)
        {
            int now = Runner.Tick.Raw;
            if (!Object.HasStateAuthority || Health <= 0 ||
                slot < 0 || slot > 2 ||
                CombatTime < ComboLockUntilCombatTime || ChargeActive)
                return;
            // A second press of the SAME skill while it is active may buffer
            // only the next authored follow-up, never restart another skill.
            if (CombatTime < SkillLockUntilCombatTime)
            {
                TryBufferSkillCombo(slot);
                return;
            }

            if (!actor.TryGetForm(CurrentFormIndex, out var form) ||
                form.Abilities == null || slot >= form.Abilities.Length ||
                form.Abilities[slot] == null)
                return;
            AbilityDefinition ability = form.Abilities[slot];
            float next = slot == 0 ? NextSkill0CombatTime : slot == 1
                ? NextSkill1CombatTime : NextSkill2CombatTime;
            if (CombatTime < next || Energy < ability.EnergyCost) return;
            if (ability.IsChargeAbility)
            {
                BeginCharge(slot, ability, aim);
                return;
            }
            ExecuteSkill(slot, ability, aim, 1f, false);
        }

        private void ExecuteSkill(int slot, AbilityDefinition ability, Vector2 aim,
            float multiplier, bool prepaid)
        {
            int now = Runner.Tick.Raw;
            float cooldownEnd = CombatTime +
                (ability.HasComboFollowUps || prepaid ? ability.DurationSeconds : 0f) +
                Mathf.Max(0f, ability.CooldownSeconds);
            if (slot == 0) NextSkill0CombatTime = cooldownEnd;
            else if (slot == 1) NextSkill1CombatTime = cooldownEnd;
            else NextSkill2CombatTime = cooldownEnd;
            if (!prepaid) Energy -= ability.EnergyCost;
            ApplyActionFacing(ability.LookTarget, aim);

            if (ability.HookEnabled || ability.GravityPullEnabled)
            {
                PullPending = true;
                PullPendingAtCombatTime = CombatTime + ability.StartupSeconds;
                PullPendingFormIndex = CurrentFormIndex;
                PullPendingAbilitySlot = slot;
                // Release occurs in ProcessPendingPull on a subsequent host tick.
                LogCombat("PULL_SCHEDULED", "slot=" + slot +
                    " at=" + PullPendingAtCombatTime.ToString("F3"));
            }

            if (ability.ClockUpEnabled)
            {
                if (ability.ClockUpActivationDelaySeconds <= 0f)
                    StartClockUp(ability.ClockUpDurationSeconds,
                        ability.ClockUpCasterScale, ability.ClockUpOtherRiderScale,
                        CurrentFormIndex, slot);
                else
                {
                    PendingClockUpArmed = true;
                    PendingClockUpSourceForm = CurrentFormIndex;
                    PendingClockUpSourceSlot = slot;
                    PendingClockUpAtCombatTime = CombatTime +
                        ability.ClockUpActivationDelaySeconds;
                    PendingClockUpDurationSeconds = ability.ClockUpDurationSeconds;
                    PendingClockUpCasterScale = ability.ClockUpCasterScale;
                    PendingClockUpOtherScale = ability.ClockUpOtherRiderScale;
                    LogCombat("CLOCK_UP_SCHEDULED", "releaseCombatTime=" +
                        PendingClockUpAtCombatTime.ToString("F3"));
                }
            }

            float duration = ability.DurationSeconds;
            SkillIndex = slot;
            SkillComboStage = 0;
            SkillComboNextBuffered = false;
            SkillWasCharged = prepaid;
            CurrentSkillDuration = duration;
            SkillStartTick = now;
            SkillSequence++;
            SkillLockedUntilTick = now + SecondsToTicks(duration);
            SkillStartCombatTime = CombatTime;
            SkillLockUntilCombatTime = CombatTime + Mathf.Max(0.001f, duration);
            SkillStepDistance = ability.ForwardStepDistance;
            SkillStepStart = ability.StepStartNormalized;
            SkillStepEnd = ability.StepEndNormalized;
            FormChangeLockedUntilTick = SkillLockedUntilTick;
            // Pure Clock Up is a buff, not an invisible melee attack.
            // A Clock Up ability with a projectile still schedules that projectile.
            // Charged release always uses its OWN threshold attack. Never
            // reuse the Ability's normal Projectile or normal melee settings.
            if (prepaid)
            {
                AbilityChargeThreshold threshold =
                    ability.GetChargeThreshold(ReleasedChargeThresholdIndex);
                int damage = threshold != null
                    ? threshold.CalculateDamage(ability.BaseDamage)
                    : ability.BaseDamage;
                float meleeRange = threshold != null ? threshold.MeleeRange : ability.HitRange;
                ScheduleImpact(ability.StartupSeconds, damage, meleeRange,
                    threshold != null && threshold.ReleaseType == ChargeReleaseType.Projectiles,
                    true, slot);
                PendingChargeThresholdIndex = ReleasedChargeThresholdIndex;
            }
            else if (ability.NormalReleaseImplemented &&
                     ((!ability.ClockUpEnabled &&
                        !ability.HookEnabled && !ability.GravityPullEnabled) ||
                       ability.UsesNormalProjectile ||
                       ability.NormalReleaseType != NormalAbilityReleaseType.AutoLegacy))
                ScheduleImpact(ability.StartupSeconds,
                    Mathf.Max(0, Mathf.RoundToInt(ability.BaseDamage * multiplier)),
                    ability.HitRange, ability.UsesNormalProjectile, true, slot);
            LogCombat("SKILL", "slot=" + slot + " ability=" + ability.AbilityId +
                " energy=" + Energy + " hitTick=" + PendingImpactTick);
        }

        // Host-only charge state; duration is computed from authoritative CombatTime.
        private void BeginCharge(int slot, AbilityDefinition ability, Vector2 aim)
        {
            Energy -= ability.EnergyCost; // Cost exactly once at the start.
            SkillIndex = slot;
            SkillComboStage = 0;
            SkillComboNextBuffered = false;
            SkillWasCharged = false;
            ReleasedChargeThresholdIndex = -1;
            ChargeActive = true;
            ChargeLevel = 1;
            ChargeStartCombatTime = CombatTime;
            ChargeSequence++;
            ApplyActionFacing(ability.LookTarget, aim);
            LogCombat("CHARGE_START", "slot=" + slot);
        }

        private void ProcessCharge(bool held, Vector2 aim)
        {
            if (!Object.HasStateAuthority || !ChargeActive) return;
            AbilityDefinition ability = GetCurrentSkillAbility();
            if (ability == null || !ability.IsChargeAbility || Health <= 0)
            {
                ChargeActive = false;
                ChargeLevel = 0;
                return;
            }
            float elapsed = Mathf.Clamp(CombatTime - ChargeStartCombatTime,
                0f, ability.MaximumChargeSeconds);
            ChargeLevel = ability.CalculateChargeLevel(elapsed);
            if (held && (!ability.AutoReleaseAtMax ||
                         elapsed < ability.MaximumChargeSeconds)) return;
            int chosen = ability.SelectChargeThreshold(elapsed);
            int level = ChargeLevel;
            ChargeActive = false;
            ReleasedChargeThresholdIndex = chosen;
            ExecuteSkill(SkillIndex, ability, aim, 1f, true);
            ChargeLevel = 0;
            LogCombat("CHARGE_RELEASE", "slot=" + SkillIndex +
                " elapsed=" + elapsed.ToString("F2") + " level=" + level +
                " thresholdIndex=" + chosen);
        }

        private AbilityDefinition GetCurrentSkillAbility()
        {
            if (actor == null || !actor.TryGetForm(CurrentFormIndex, out var form) ||
                form.Abilities == null || SkillIndex < 0 ||
                SkillIndex >= form.Abilities.Length)
                return null;
            return form.Abilities[SkillIndex];
        }

        // Charge release visuals and movement use HOST-selected threshold.
        // This index is replicated; all clients resolve the same authored data.
        private AbilityChargeThreshold GetCurrentChargeReleaseThreshold()
        {
            if (!SkillWasCharged || ReleasedChargeThresholdIndex < 0)
                return null;
            AbilityDefinition ability = GetCurrentSkillAbility();
            return ability != null && ability.IsChargeAbility
                ? ability.GetChargeThreshold(ReleasedChargeThresholdIndex) : null;
        }

        // Stage 0 is the original Ability. Stage 1+ uses follow-up entries.
        private FormComboAttack GetCurrentSkillComboFollowUp()
        {
            AbilityDefinition ability = GetCurrentSkillAbility();
            if (ability == null || SkillComboStage <= 0) return null;
            return ability.TryGetComboFollowUp(SkillComboStage - 1, out var stage)
                ? stage : null;
        }

        private void TryBufferSkillCombo(int slot)
        {
            if (slot != SkillIndex || SkillComboNextBuffered || Health <= 0)
                return;
            AbilityDefinition ability = GetCurrentSkillAbility();
            if (ability == null || !ability.HasComboFollowUps ||
                !ability.TryGetComboFollowUp(SkillComboStage, out var next))
                return;

            FormComboAttack current = GetCurrentSkillComboFollowUp();
            float start = current != null
                ? current.ChainInputStartNormalized : ability.ChainInputStartNormalized;
            float end = current != null
                ? current.ChainInputEndNormalized : ability.ChainInputEndNormalized;
            float elapsed = Mathf.Max(0.001f,
                SkillLockUntilCombatTime - SkillStartCombatTime);
            float progress = Mathf.Clamp01((CombatTime - SkillStartCombatTime) / elapsed);
            if (progress < start || progress > end || Energy < next.EnergyCost)
                return;
            SkillComboNextBuffered = true;
            LogCombat("SKILL_COMBO_BUFFER", "slot=" + slot +
                " nextStage=" + (SkillComboStage + 1));
        }

        private void ProcessBufferedSkillCombo(Vector2 aim)
        {
            if (!Object.HasStateAuthority || !SkillComboNextBuffered || Health <= 0)
                return;
            AbilityDefinition ability = GetCurrentSkillAbility();
            if (ability == null ||
                !ability.TryGetComboFollowUp(SkillComboStage, out var next))
            {
                SkillComboNextBuffered = false;
                return;
            }
            FormComboAttack current = GetCurrentSkillComboFollowUp();
            float transition = current != null
                ? current.ChainTransitionNormalized
                : ability.ChainTransitionNormalized;
            float length = Mathf.Max(0.001f,
                SkillLockUntilCombatTime - SkillStartCombatTime);
            float progress = Mathf.Clamp01((CombatTime - SkillStartCombatTime) / length);
            if (progress < transition) return;
            // Do not overwrite an unresolved hit/projectile from the previous stage.
            if (PendingImpactArmed) return;
            if (CombatTime >= SkillLockUntilCombatTime || Energy < next.EnergyCost)
            {
                SkillComboNextBuffered = false;
                return;
            }
            BeginSkillComboFollowUp(next, ability, aim);
        }

        private void BeginSkillComboFollowUp(FormComboAttack stage,
            AbilityDefinition ability, Vector2 aim)
        {
            Energy -= stage.EnergyCost;
            ApplyActionFacing(stage.LookTarget, aim);
            SkillComboStage++;
            SkillComboNextBuffered = false;
            float duration = stage.DurationSeconds;
            CurrentSkillDuration = duration;
            SkillStartTick = Runner.Tick.Raw;
            SkillSequence++;
            SkillLockedUntilTick = Runner.Tick.Raw + SecondsToTicks(duration);
            SkillStartCombatTime = CombatTime;
            SkillLockUntilCombatTime = CombatTime + Mathf.Max(0.001f, duration);
            SkillStepDistance = stage.ForwardStepDistance;
            SkillStepStart = stage.StepStartNormalized;
            SkillStepEnd = stage.StepEndNormalized;
            FormChangeLockedUntilTick = SkillLockedUntilTick;

            // Skill cooldown starts after the final performed stage.
            float cooldownEnd = SkillLockUntilCombatTime +
                Mathf.Max(0f, ability.CooldownSeconds);
            if (SkillIndex == 0) NextSkill0CombatTime = cooldownEnd;
            else if (SkillIndex == 1) NextSkill1CombatTime = cooldownEnd;
            else NextSkill2CombatTime = cooldownEnd;

            int damage = stage.UseCustomDamage ? stage.BaseDamage : ability.BaseDamage;
            bool projectile = stage.HasEnabledProjectiles;
            ScheduleImpact(stage.StartupSeconds, damage, stage.HitRange,
                projectile, true, SkillIndex);
            LogCombat("SKILL_COMBO_CHAIN", "slot=" + SkillIndex +
                " stage=" + SkillComboStage + " energy=" + Energy);
        }

        private void UpdateFormSkillAnimation()
        {
            if (spawnedFormAnimator == null || Object == null)
                return;

            if (ChargeActive)
            {
                skillAnimationActive = false;
                string name = "Base Layer.Skill" + SkillIndex;
                if (observedChargeSequence != ChargeSequence)
                {
                    observedChargeSequence = ChargeSequence;
                    chargeLoopPlaying = false;
                    int start = Animator.StringToHash(name + "_ChargeStart");
                    int loop = Animator.StringToHash(name + "_ChargeLoop");
                    int use = spawnedFormAnimator.HasState(0, start) ? start : loop;
                    if (spawnedFormAnimator.HasState(0, use))
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            use, attackAnimationCrossFade, 0, 0f);
                }
                AbilityDefinition ability = GetCurrentSkillAbility();
                if (!chargeLoopPlaying && ability != null &&
                    CombatTime - ChargeStartCombatTime >=
                    ability.ChargeStartAnimationSeconds)
                {
                    chargeLoopPlaying = true;
                    int loop = Animator.StringToHash(name + "_ChargeLoop");
                    if (spawnedFormAnimator.HasState(0, loop))
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            loop, attackAnimationCrossFade, 0, 0f);
                }
                return;
            }

            if (observedSkillSequence != SkillSequence)
            {
                observedSkillSequence = SkillSequence;
                if (Health > 0)
                {
                    string baseStateName = "Base Layer.Skill" + SkillIndex;
                    string stateName = SkillWasCharged
                        ? baseStateName + "_ChargeRelease"
                        : SkillComboStage > 0
                            ? baseStateName + "_Combo" + SkillComboStage : baseStateName;
                    int stateHash = Animator.StringToHash(stateName);
                    if (spawnedFormAnimator.HasState(0, stateHash))
                    {
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            stateHash, attackAnimationCrossFade, 0, 0f);
                        skillAnimationRemainingTime =
                            Mathf.Max(0.1f, CurrentSkillDuration);
                        skillAnimationActive = true;
                    }
                    else
                    {
                        // A missing authored follow-up state falls back to the
                        // original skill animation without dropping gameplay.
                        Debug.LogWarning("[MuOnBaAr][Skill] Missing " + stateName, this);
                        int fallbackHash = Animator.StringToHash(baseStateName);
                        if ((SkillComboStage > 0 || SkillWasCharged) &&
                            spawnedFormAnimator.HasState(0, fallbackHash))
                        {
                            spawnedFormAnimator.CrossFadeInFixedTime(
                                fallbackHash, attackAnimationCrossFade, 0, 0f);
                            skillAnimationRemainingTime = Mathf.Max(0.1f, CurrentSkillDuration);
                            skillAnimationActive = true;
                        }
                    }
                }
            }

            if (skillAnimationActive)
                skillAnimationRemainingTime -= Time.deltaTime * GetRiderTimeScale();
            if (skillAnimationActive &&
                (Health <= 0 || skillAnimationRemainingTime <= 0f))
            {
                skillAnimationActive = false;
                if (spawnedFormAnimator.HasState(0, LocomotionIdleStateId))
                    spawnedFormAnimator.CrossFadeInFixedTime(
                        LocomotionIdleStateId, attackAnimationCrossFade, 0, 0f);
            }
        }

        // A step specifies total local X/Z metres over its normalized
        // action window. Integrating normalized progress gives displacement
        // independent of frame rate or sudden WorldScale changes.
        private Vector2 ActionMovementDelta(CombatMovementStep[] steps,
            float actionStart, float actionEnd, float legacyForward,
            float legacyStart, float legacyEnd)
        {
            if (steps == null || steps.Length == 0)
                return Vector2.up * CombatStepDelta(actionStart, actionEnd,
                    legacyForward, legacyStart, legacyEnd);
            Vector2 total = Vector2.zero;
            float duration = actionEnd - actionStart;
            if (duration <= 0.0001f) return total;
            float previous = CombatTime - Runner.DeltaTime * GetRiderTimeScale();
            float lastNormalized = (previous - actionStart) / duration;
            float currentNormalized = (CombatTime - actionStart) / duration;
            foreach (CombatMovementStep step in steps)
            {
                if (step == null || step.EndNormalized <= step.StartNormalized)
                    continue;
                float range = step.EndNormalized - step.StartNormalized;
                float before = Mathf.Clamp01(
                    (lastNormalized - step.StartNormalized) / range);
                float after = Mathf.Clamp01(
                    (currentNormalized - step.StartNormalized) / range);
                total += step.Movement * Mathf.Max(0f, after - before);
            }
            return total;
        }

        private Vector3 LocalActionVelocity(Vector2 movementDelta)
        {
            // Fixed X=right/Z=forward relative to the rider, never camera yaw.
            Vector3 local = new Vector3(movementDelta.x, 0f, movementDelta.y);
            Vector3 world = simpleKCC.TransformRotation * local;
            world.y = 0f;
            return world / Mathf.Max(0.001f, Runner.DeltaTime);
        }

        // Legacy movement is retained for existing authored asset values.
        private float CombatStepDelta(float start, float end,
            float distance, float fromNormalized, float toNormalized)
        {
            if (distance <= 0f || end <= start ||
                toNormalized <= fromNormalized) return 0f;

            float previousCombatTime = CombatTime -
                Runner.DeltaTime * GetRiderTimeScale();
            float length = end - start;
            float previous = Mathf.Clamp01(
                ((previousCombatTime - start) / length - fromNormalized) /
                (toNormalized - fromNormalized));
            float next = Mathf.Clamp01(
                ((CombatTime - start) / length - fromNormalized) /
                (toNormalized - fromNormalized));
            return Mathf.Max(0f, next - previous) * distance;
        }

        // Still used for real-world KO/respawn, invulnerability and projectile
        // lifetime. These systems intentionally remain on Fusion ticks.
        private int SecondsToTicks(float seconds)
        {
            return Mathf.Max(1, Mathf.CeilToInt(
                Mathf.Max(0f, seconds) / Mathf.Max(0.001f, Runner.DeltaTime)));
        }

        private void TryBeginDodge(int direction)
        {
            if (!Object.HasStateAuthority || Health <= 0 ||
                CombatTime < HitReactionEndCombatTime ||
                FormTransitionPhase != 0 || ChargeActive ||
                CombatTime < ComboLockUntilCombatTime ||
                CombatTime < SkillLockUntilCombatTime ||
                CombatTime < DodgeEndCombatTime ||
                CombatTime < NextDodgeCombatTime) return;
            DodgeDirection = direction;
            DodgeStartCombatTime = CombatTime;
            DodgeEndCombatTime = CombatTime +
                Mathf.Max(0.05f, dodgeDurationSeconds);
            NextDodgeCombatTime = DodgeEndCombatTime +
                Mathf.Max(0f, dodgeCooldownSeconds);
            DodgeSequence++;
            LogCombat("DODGE", "direction=" + direction);
        }

        private void UpdateDodgeAnimation()
        {
            if (spawnedFormAnimator == null) return;
            if (DodgeSequence != observedDodgeSequence)
            {
                observedDodgeSequence = DodgeSequence;
                string name = DodgeDirection == 0 ? "RollForward" :
                    DodgeDirection == 1 ? "RollLeft" :
                    DodgeDirection == 2 ? "StepBack" : "RollRight";
                int hash = Animator.StringToHash("Base Layer.Locomotion." + name);
                if (spawnedFormAnimator.HasState(0, hash))
                {
                    spawnedFormAnimator.CrossFadeInFixedTime(hash,
                        attackAnimationCrossFade, 0, 0f);
                    dodgeAnimationActive = true;
                }
                else Debug.LogWarning("[MuOnBaAr][Dodge] Missing " + name, this);
            }
            if (dodgeAnimationActive && CombatTime >= DodgeEndCombatTime)
            {
                dodgeAnimationActive = false;
                if (spawnedFormAnimator.HasState(0, LocomotionIdleStateId))
                    spawnedFormAnimator.CrossFadeInFixedTime(
                        LocomotionIdleStateId, attackAnimationCrossFade, 0, 0f);
            }
        }

        private void TryBasicAttack(Vector2 aim)
        {
            if (!Object.HasStateAuthority || Health <= 0 || ChargeActive ||
                CombatTime < HitReactionEndCombatTime ||
                CombatTime < SkillLockUntilCombatTime) return;

            if (CombatTime < ComboLockUntilCombatTime)
            {
                // During the current attack, queue ONLY the next existing stage.
                if (!actor.TryGetForm(CurrentFormIndex, out var form) ||
                    !form.TryGetComboAttack(ComboIndex, out var current) ||
                    !form.TryGetComboAttack(ComboIndex + 1, out var next))
                    return;
                float length = Mathf.Max(0.001f,
                    ComboLockUntilCombatTime - ComboStartCombatTime);
                float progress = Mathf.Clamp01(
                    (CombatTime - ComboStartCombatTime) / length);
                if (progress < current.ChainInputStartNormalized ||
                    progress > current.ChainInputEndNormalized ||
                    Energy < next.EnergyCost) return;
                ComboNextBuffered = true;
                LogCombat("COMBO_BUFFER", "stage=" + (ComboIndex + 1));
                return;
            }

            if (CombatTime < NextComboCombatTime) return;
            FormComboAttack first = null;
            if (actor.TryGetForm(CurrentFormIndex, out var startForm))
                startForm.TryGetComboAttack(0, out first);
            BeginComboStage(0, first, false, aim);
        }

        private void ProcessBufferedCombo(Vector2 aim)
        {
            if (!Object.HasStateAuthority || !ComboNextBuffered || Health <= 0)
                return;
            if (!actor.TryGetForm(CurrentFormIndex, out var form) ||
                !form.TryGetComboAttack(ComboIndex, out var current) ||
                !form.TryGetComboAttack(ComboIndex + 1, out var next))
            {
                ComboNextBuffered = false;
                return;
            }
            float length = Mathf.Max(0.001f,
                ComboLockUntilCombatTime - ComboStartCombatTime);
            float progress = Mathf.Clamp01(
                (CombatTime - ComboStartCombatTime) / length);
            if (progress < current.ChainTransitionNormalized)
                return;
            // Do not overwrite an unresolved hit or pending projectile.
            if (PendingImpactArmed && !PendingImpactSkill) return;
            if (CombatTime > ComboLockUntilCombatTime + 0.0001f ||
                Energy < next.EnergyCost)
            {
                ComboNextBuffered = false;
                return;
            }
            BeginComboStage(ComboIndex + 1, next, true, aim);
        }

        // Facing is decided once at the authoritative action start. RiderNetworkInput.Aim
        // is already camera-facing without target lock, or opponent-facing with lock.
        // The existing action locomotion lock holds rotation for the whole move.
        private void ApplyActionFacing(bool lookTarget, Vector2 aim)
        {
            if (!lookTarget || simpleKCC == null || aim.sqrMagnitude < 0.0001f)
                return;
            Vector3 direction = new Vector3(aim.x, 0f, aim.y).normalized;
            simpleKCC.SetLookRotation(Quaternion.LookRotation(direction, Vector3.up));
        }

        private void BeginComboStage(int stageIndex, FormComboAttack combo,
            bool chained, Vector2 aim)
        {
            int cost = combo != null ? combo.EnergyCost : 0;
            if (Energy < cost) return;
            Energy -= cost;
            ApplyActionFacing(combo != null && combo.LookTarget, aim);
            float duration = combo != null ? combo.DurationSeconds :
                basicAttackAnimationLength;
            float cooldown = combo != null && combo.CooldownSeconds > 0f
                ? combo.CooldownSeconds : attackCooldownSeconds;
            NextComboCombatTime = CombatTime + Mathf.Max(0f, cooldown);
            ComboIndex = stageIndex;
            ComboNextBuffered = false;
            AttackStartTick = Runner.Tick.Raw;
            AttackActiveUntilTick = Runner.Tick.Raw + SecondsToTicks(duration);
            ComboStartCombatTime = CombatTime;
            ComboLockUntilCombatTime = CombatTime + Mathf.Max(0.001f, duration);
            AttackStepDistance = combo != null ? combo.ForwardStepDistance : 0f;
            AttackStepStart = combo != null ? combo.StepStartNormalized : 0f;
            AttackStepEnd = combo != null ? combo.StepEndNormalized : 0f;
            FormChangeLockedUntilTick = AttackActiveUntilTick;
            AttackSequence++;

            int damage = combo != null && combo.UseCustomDamage
                ? combo.BaseDamage : basicAttackDamage;
            float range = combo != null ? combo.HitRange : attackRange;
            bool projectile = combo != null && combo.HasEnabledProjectiles;
            float startup = combo != null ? combo.StartupSeconds : 0f;
            ScheduleImpact(startup, damage, range, projectile, false, stageIndex);
            LogCombat(chained ? "COMBO_CHAIN" : "ATTACK",
                "stage=" + stageIndex + " sequence=" + AttackSequence +
                " energy=" + Energy);
        }

        private void ScheduleImpact(float startupSeconds, int damage, float range,
            bool projectile, bool skill, int slot)
        {
            // Both projectile and melee release follow the same CombatTime
            // used by their animations, including slow motion and 0x freeze.
            PendingImpactCombatTime = CombatTime + Mathf.Max(0f, startupSeconds);
            // Never resolve within the input tick itself.
            PendingImpactTick = Runner.Tick.Raw + 1;
            PendingImpactArmed = true;
            PendingImpactDamage = Mathf.Max(0, damage);
            PendingImpactHitReaction = (int)ResolveCurrentAttackHitReaction(skill, slot);
            // Snapshot authoring at action start, not when the hit resolves:
            // combo buffering can otherwise change which hit cone is applied.
            PendingImpactHitAreaMode = (int)ComboHitAreaMode.FrontArc;
            PendingImpactHitHalfAngle = attackHalfAngle;
            if (!skill && actor.TryGetForm(CurrentFormIndex, out var hitForm) &&
                hitForm.TryGetComboAttack(slot, out var hitCombo))
            {
                PendingImpactHitAreaMode = (int)hitCombo.HitAreaMode;
                PendingImpactHitHalfAngle = hitCombo.HitHalfAngle;
            }
            PendingImpactStunSeconds = ResolveCurrentAttackStunSeconds(skill, slot);
            ResolveCurrentAttackKnockback(skill, slot, -1,
                out float knockbackDistance, out float knockbackSeconds);
            PendingImpactKnockbackDistance = knockbackDistance;
            PendingImpactKnockbackSeconds = knockbackSeconds;
            PendingImpactRange = Mathf.Max(0.1f, range);
            PendingImpactProjectile = projectile;
            PendingImpactSkill = skill;
            PendingImpactSlot = slot;
            PendingChargeThresholdIndex = -1;
        }

        private void ProcessPendingImpact()
        {
            if (!PendingImpactArmed || Runner.Tick.Raw < PendingImpactTick ||
                CombatTime < PendingImpactCombatTime)
                return;
            PendingImpactArmed = false;
            PendingImpactTick = 0;
            if (Health <= 0) return;

            if (PendingChargeThresholdIndex >= 0 || 
                (PendingImpactSkill && SkillWasCharged))
            {
                // Charge owns its hit data, independent of Normal Ability Projectile.
                AbilityDefinition chargedAbility = GetCurrentSkillAbility();
                AbilityChargeThreshold threshold = chargedAbility != null
                    ? chargedAbility.GetChargeThreshold(PendingChargeThresholdIndex) : null;
                if (threshold == null)
                {
                    // No authored thresholds: safe minimal melee fallback.
                    DealMeleeDamage(PendingImpactDamage, PendingImpactRange,
                        (HitReactionType)PendingImpactHitReaction,
                        PendingImpactKnockbackDistance, PendingImpactKnockbackSeconds,
                        PendingImpactStunSeconds);
                }
                else if (threshold.ReleaseType == ChargeReleaseType.Melee)
                    DealMeleeDamage(PendingImpactDamage, threshold.MeleeRange,
                        threshold.HitReaction, threshold.HitKnockbackDistance,
                        threshold.HitKnockbackSeconds,
                        threshold.StunDurationSeconds);
                else if (threshold.ReleaseType == ChargeReleaseType.Projectiles)
                {
                    ReleaseProjectileBurst(PendingChargeThresholdIndex);
                }
                else
                    LogCombat("CHARGE_UNSUPPORTED",
                        "Beam/Laser not implemented; no damage applied.");
                PendingChargeThresholdIndex = -1;
            }
            else if (PendingImpactProjectile)
            {
                ReleaseProjectileBurst(-1);
            }
            else
                DealMeleeDamage(PendingImpactDamage, PendingImpactRange,
                    (HitReactionType)PendingImpactHitReaction,
                    PendingImpactKnockbackDistance, PendingImpactKnockbackSeconds,
                    PendingImpactStunSeconds,
                    !PendingImpactSkill && PendingImpactHitAreaMode ==
                        (int)ComboHitAreaMode.FullCircle360,
                    !PendingImpactSkill ? PendingImpactHitHalfAngle : attackHalfAngle);
        }

        private void DealMeleeDamage(int baseDamage, float range,
            HitReactionType hitReaction, float knockbackDistance,
            float knockbackSeconds, float stunSeconds,
            bool fullCircle = false, float customHalfAngle = -1f)
        {
            Vector2 aim = RiderCombatRules.PlanarForward(transform.rotation);
            NetworkRiderController best = null;
            float nearest = range * range;
            float effectiveHalfAngle = customHalfAngle > 0f
                ? customHalfAngle : attackHalfAngle;
            NetworkRiderController[] riders = FindObjectsOfType<NetworkRiderController>();
            foreach (NetworkRiderController other in riders)
            {
                if (!IsLiveOpponent(other) || !other.Object.HasStateAuthority ||
                    !RiderRespawnRules.CanTakeDamage(other.Health, Runner.Tick.Raw,
                        other.InvulnerableUntilTick) || !HasLineOfSight(other))
                    continue;
                Vector3 d = other.transform.position - transform.position;
                float sq = new Vector2(d.x, d.z).sqrMagnitude;
                if (fullCircle)
                {
                    if (sq > range * range) continue;
                    // Spinning melee is radial AOE: each valid rider takes one hit.
                    ApplyDamage(other, baseDamage, hitReaction,
                        knockbackDistance, knockbackSeconds, stunSeconds);
                    continue;
                }
                if (!RiderCombatRules.IsInMeleeArc(transform.position,
                        other.transform.position, aim, range, effectiveHalfAngle) ||
                    sq >= nearest)
                    continue;
                nearest = sq;
                best = other;
            }
            if (!fullCircle && best != null)
                ApplyDamage(best, baseDamage, hitReaction,
                    knockbackDistance, knockbackSeconds, stunSeconds);
        }

        // XP is awarded only by the authoritative HOST on an actual KO.
        private void AwardExperience(int xp)
        {
            if (!Object.HasStateAuthority || xp <= 0 || Level >= MaxLevel) return;
            Experience += xp;
            while (Level < MaxLevel)
            {
                int required = ExperienceToNextLevel;
                if (required <= 0 || Experience < required) break;
                Experience -= required;
                Level++;
                LogCombat("LEVEL_UP", "level=" + Level +
                    " remainingXp=" + Experience);
            }
            if (Level >= MaxLevel) Experience = 0;
        }

        private void ApplyDamage(NetworkRiderController target, int baseDamage,
            HitReactionType hitReaction, float knockbackDistance,
            float knockbackSeconds, float stunSeconds)
        {
            if (!Object.HasStateAuthority || baseDamage <= 0 || !IsLiveOpponent(target) ||
                !target.Object.HasStateAuthority ||
                !RiderRespawnRules.CanTakeDamage(target.Health, Runner.Tick.Raw,
                    target.InvulnerableUntilTick)) return;
            float multiplier = actor.TryGetForm(CurrentFormIndex, out var form)
                ? form.DamageMultiplier : 1f;
            int damage = RiderCombatRules.CalculateDamage(baseDamage, multiplier);
            int before = target.Health;
            target.Health = Mathf.Max(0, before - damage);
            // State Authority grants Energy to the attacker only after confirmed
            // HP loss. One ApplyDamage = one target hit (including multi-target
            // 360 melee and each projectile). Blocked, missed and zero-damage
            // attacks cannot generate Energy.
            int actualDamage = before - target.Health;
            int energyBefore = Energy;
            Energy = RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                Energy, actor.MaxEnergy, actor.EnergyGainOnHit, actualDamage);
            if (Energy > energyBefore)
                LogCombat("ENERGY_ON_HIT", "target=" + target.Object.Id +
                    " gained=" + (Energy - energyBefore) + " energy=" + Energy +
                    "/" + actor.MaxEnergy);
            LogCombat("HIT", "target=" + target.Object.Id + " damage=" +
                (before - target.Health) + " hpAfter=" + target.Health);
            if (target.Health > 0 && before > target.Health &&
                hitReaction != HitReactionType.None)
            {
                // HOST owns reaction choice; replicated on the VICTIM so
                // local player, opponent and proxies all render the same hit.
                bool targetAlreadyStunned = target.CombatTime < target.StunEndCombatTime;
                if (!targetAlreadyStunned || hitReaction == HitReactionType.Stun)
                {
                    target.HitReactionIndex = (int)hitReaction;
                    target.HitReactionSequence++;
                }
                if (hitReaction == HitReactionType.Stun)
                    target.StunEndCombatTime = Mathf.Max(
                        target.StunEndCombatTime,
                        target.CombatTime + Mathf.Max(0.05f, stunSeconds));
                target.HitReactionEndCombatTime = Mathf.Max(
                    target.CombatTime + Mathf.Max(0.05f, target.hitReactionSeconds),
                    target.StunEndCombatTime);
                target.BeginHitKnockback(transform.position,
                    knockbackDistance, knockbackSeconds);
                target.LogCombat("HIT_REACTION", "reaction=" + hitReaction +
                    " sequence=" + target.HitReactionSequence);
            }
            if (target.Health == 0)
            {
                AwardExperience(actor.XpAwardForKnockout);
                target.BeginKnockout(Runner.Tick.Raw);
            }
        }

        private void BeginHitKnockback(Vector3 attackerPosition,
            float distance, float duration)
        {
            // Called by attacking HOST, but movement and rotation belong to
            // the victim's authority. No transform teleport; KCC respects walls.
            if (!Object.HasStateAuthority || Health <= 0) return;
            Vector3 toward = attackerPosition - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.0001f && simpleKCC != null)
                simpleKCC.SetLookRotation(
                    Quaternion.LookRotation(toward.normalized, Vector3.up));
            Vector3 away = -toward;
            if (away.sqrMagnitude < 0.0001f)
                away = -(simpleKCC != null
                    ? simpleKCC.TransformRotation * Vector3.forward
                    : transform.forward);
            away.y = 0f;
            KnockbackAwayDirection = away.sqrMagnitude > 0.0001f
                ? away.normalized : Vector3.back;
            KnockbackDistance = Mathf.Max(0f, distance);
            KnockbackStartCombatTime = CombatTime;
            KnockbackEndCombatTime = CombatTime + Mathf.Max(0.05f, duration);
            LogCombat("HIT_KNOCKBACK", "distance=" +
                KnockbackDistance.ToString("F2") + " seconds=" +
                duration.ToString("F2"));
        }

        private Vector3 GetKnockbackVelocity()
        {
            if (CombatTime >= KnockbackEndCombatTime ||
                KnockbackDistance <= 0f || Runner == null)
                return Vector3.zero;
            float duration = Mathf.Max(0.001f,
                KnockbackEndCombatTime - KnockbackStartCombatTime);
            float lastTime = Mathf.Max(KnockbackStartCombatTime,
                CombatTime - Runner.DeltaTime * GetRiderTimeScale());
            float nextTime = Mathf.Min(CombatTime, KnockbackEndCombatTime);
            float moved = Mathf.Clamp01(
                (nextTime - lastTime) / duration) * KnockbackDistance;
            return KnockbackAwayDirection *
                (moved / Mathf.Max(0.001f, Runner.DeltaTime));
        }

        private float ResolveCurrentAttackStunSeconds(bool fromSkill,
            int slot, int thresholdIndex = -1)
        {
            const float fallback = 2f;
            if (actor == null || !actor.TryGetForm(CurrentFormIndex, out var form))
                return fallback;
            if (!fromSkill)
                return form.TryGetComboAttack(slot, out var combo)
                    ? combo.StunDurationSeconds : fallback;
            if (form.Abilities == null || slot < 0 || slot >= form.Abilities.Length)
                return fallback;
            AbilityDefinition ability = form.Abilities[slot];
            if (ability == null) return fallback;
            int chosenThreshold = thresholdIndex >= 0
                ? thresholdIndex
                : SkillWasCharged ? ReleasedChargeThresholdIndex : -1;
            if (chosenThreshold >= 0)
            {
                AbilityChargeThreshold threshold = ability.GetChargeThreshold(chosenThreshold);
                if (threshold != null) return threshold.StunDurationSeconds;
            }
            if (SkillComboStage > 0 &&
                ability.TryGetComboFollowUp(SkillComboStage - 1, out var followUp))
                return followUp.StunDurationSeconds;
            return ability.StunDurationSeconds;
        }

        private void ResolveCurrentAttackKnockback(bool fromSkill, int slot,
            int thresholdIndex, out float distance, out float duration)
        {
            distance = 0f;
            duration = 0.25f;
            if (actor == null || !actor.TryGetForm(CurrentFormIndex, out var form))
                return;
            if (!fromSkill)
            {
                if (form.TryGetComboAttack(slot, out var combo))
                {
                    distance = combo.HitKnockbackDistance;
                    duration = combo.HitKnockbackSeconds;
                }
                return;
            }
            if (form.Abilities == null || slot < 0 ||
                slot >= form.Abilities.Length) return;
            AbilityDefinition ability = form.Abilities[slot];
            if (ability == null) return;
            if (thresholdIndex >= 0)
            {
                AbilityChargeThreshold threshold =
                    ability.GetChargeThreshold(thresholdIndex);
                if (threshold != null)
                {
                    distance = threshold.HitKnockbackDistance;
                    duration = threshold.HitKnockbackSeconds;
                    return;
                }
            }
            if (SkillComboStage > 0 &&
                ability.TryGetComboFollowUp(SkillComboStage - 1, out var followUp))
            {
                distance = followUp.HitKnockbackDistance;
                duration = followUp.HitKnockbackSeconds;
                return;
            }
            distance = ability.HitKnockbackDistance;
            duration = ability.HitKnockbackSeconds;
        }

        private HitReactionType ResolveCurrentAttackHitReaction(bool fromSkill,
            int slot, int chargeThresholdIndex = -1)
        {
            if (actor == null || !actor.TryGetForm(CurrentFormIndex, out var form))
                return HitReactionType.None;
            if (!fromSkill)
                return form.TryGetComboAttack(slot, out var combo)
                    ? combo.HitReaction : HitReactionType.Body;
            if (form.Abilities == null || slot < 0 ||
                slot >= form.Abilities.Length || form.Abilities[slot] == null)
                return HitReactionType.None;
            AbilityDefinition ability = form.Abilities[slot];
            int selectedThreshold = chargeThresholdIndex >= 0
                ? chargeThresholdIndex
                : SkillWasCharged ? ReleasedChargeThresholdIndex : -1;
            if (selectedThreshold >= 0)
            {
                AbilityChargeThreshold threshold =
                    ability.GetChargeThreshold(selectedThreshold);
                if (threshold != null) return threshold.HitReaction;
            }
            if (SkillComboStage > 0 && slot == SkillIndex &&
                ability.TryGetComboFollowUp(SkillComboStage - 1, out var followUp))
                return followUp.HitReaction;
            return ability.HitReaction;
        }

        private CombatProjectileSettings GetProjectileSettings(bool fromSkill, int slot,
            int sourceFormIndex = -1, int chargeThresholdIndex = -1,
            int projectileIndex = -1, int skillStageIndex = -1)
        {
            if (actor == null || !actor.TryGetForm(sourceFormIndex >= 0
                ? sourceFormIndex : CurrentFormIndex, out var form))
                return null;
            if (fromSkill)
            {
                if (form.Abilities == null || slot < 0 ||
                    slot >= form.Abilities.Length || form.Abilities[slot] == null)
                    return null;
                AbilityDefinition ability = form.Abilities[slot];
                if (chargeThresholdIndex >= 0)
                {
                    AbilityChargeThreshold threshold =
                        ability.GetChargeThreshold(chargeThresholdIndex);
                    return threshold != null
                        ? threshold.GetProjectile(projectileIndex) : null;
                }
                int stageIndex = skillStageIndex >= 0 ? skillStageIndex : SkillComboStage;
                if (stageIndex > 0 && ability.TryGetComboFollowUp(
                    stageIndex - 1, out var stage))
                    return stage.GetProjectile(projectileIndex);
                return ability.GetProjectile(projectileIndex);
            }
            return form.TryGetComboAttack(slot, out var combo)
                ? combo.GetProjectile(projectileIndex) : null;
        }

        private Transform FindProjectileSpawnSocket(string socketPath)
        {
            if (spawnedVisual == null || string.IsNullOrWhiteSpace(socketPath))
                return null;
            // An explicit slash-separated path takes precedence and is stable
            // even if the rig contains duplicate bone names.
            Transform exact = spawnedVisual.transform.Find(socketPath);
            if (exact != null) return exact;
            // A bare name is convenient for simple author-created socket objects.
            // Also accept a path rooted at spawnedVisual's own name.
            string prefix = spawnedVisual.transform.name + "/";
            if (socketPath.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                exact = spawnedVisual.transform.Find(socketPath.Substring(prefix.Length));
                if (exact != null) return exact;
            }
            foreach (Transform child in spawnedVisual.GetComponentsInChildren<Transform>(true))
                if (child.name == socketPath) return child;
            return null;
        }

        // Select the full authored burst. A nonempty array overrides legacy Projectile.
        private void ReleaseProjectileBurst(int chargeThresholdIndex)
        {
            if (actor == null || !actor.TryGetForm(CurrentFormIndex, out var form))
                return;
            CombatProjectileSettings[] entries = null;
            CombatProjectileSettings legacy = null;
            int slot = PendingImpactSlot;
            if (chargeThresholdIndex >= 0)
            {
                AbilityDefinition ability = GetCurrentSkillAbility();
                AbilityChargeThreshold threshold = ability != null
                    ? ability.GetChargeThreshold(chargeThresholdIndex) : null;
                if (threshold == null) return;
                entries = threshold.Projectiles;
            }
            else if (PendingImpactSkill)
            {
                if (form.Abilities == null || slot < 0 ||
                    slot >= form.Abilities.Length || form.Abilities[slot] == null)
                    return;
                AbilityDefinition ability = form.Abilities[slot];
                if (SkillComboStage > 0 && slot == SkillIndex &&
                    ability.TryGetComboFollowUp(SkillComboStage - 1, out var stage))
                {
                    entries = stage.Projectiles;
                    legacy = stage.Projectile;
                }
                else
                {
                    entries = ability.Projectiles;
                    legacy = ability.Projectile;
                }
            }
            else if (form.TryGetComboAttack(slot, out var combo))
            {
                entries = combo.Projectiles;
                legacy = combo.Projectile;
            }

            if (entries != null && entries.Length > 0)
            {
                for (int i = 0; i < entries.Length; ++i)
                    QueueProjectileShot(entries[i], chargeThresholdIndex, i);
            }
            else if (chargeThresholdIndex < 0)
                QueueProjectileShot(legacy, -1, -1);
        }

        private void QueueProjectileShot(CombatProjectileSettings settings,
            int chargeThresholdIndex, int projectileIndex)
        {
            if (settings == null || !settings.Enabled) return;
            RiderScheduledProjectileState request = new RiderScheduledProjectileState
            {
                Active = true,
                ReleaseCombatTime = CombatTime + settings.ReleaseDelaySeconds,
                FormIndex = CurrentFormIndex,
                FromSkill = PendingImpactSkill,
                SourceSlot = PendingImpactSlot,
                SkillStageIndex = PendingImpactSkill && chargeThresholdIndex < 0
                    ? SkillComboStage : 0,
                ChargeThresholdIndex = chargeThresholdIndex,
                ProjectileIndex = projectileIndex,
                Damage = PendingImpactDamage,
                HitReaction = PendingImpactHitReaction,
                StunDurationSeconds = PendingImpactStunSeconds,
                KnockbackDistance = PendingImpactKnockbackDistance,
                KnockbackSeconds = PendingImpactKnockbackSeconds
            };
            if (chargeThresholdIndex >= 0)
            {
                AbilityDefinition ability = GetCurrentSkillAbility();
                AbilityChargeThreshold threshold = ability != null
                    ? ability.GetChargeThreshold(chargeThresholdIndex) : null;
                if (threshold != null)
                {
                    request.HitReaction = (int)threshold.HitReaction;
                    request.StunDurationSeconds = threshold.StunDurationSeconds;
                    request.KnockbackDistance = threshold.HitKnockbackDistance;
                    request.KnockbackSeconds = threshold.HitKnockbackSeconds;
                }
            }
            if (settings.ReleaseDelaySeconds <= 0f)
            {
                LaunchProjectile(settings, request);
                return;
            }
            for (int i = 0; i < ScheduledProjectileCapacity; ++i)
            {
                if (ScheduledShots.Get(i).Active) continue;
                ScheduledShots.Set(i, request);
                return;
            }
            LogCombat("PROJECTILE_QUEUE_FULL", "max=" + ScheduledProjectileCapacity);
        }

        private void ProcessScheduledProjectiles()
        {
            if (!Object.HasStateAuthority || Health <= 0) return;
            for (int i = 0; i < ScheduledProjectileCapacity; ++i)
            {
                RiderScheduledProjectileState request = ScheduledShots.Get(i);
                if (!request.Active || CombatTime < request.ReleaseCombatTime)
                    continue;
                request.Active = false;
                ScheduledShots.Set(i, request);
                CombatProjectileSettings settings = GetProjectileSettings(
                    request.FromSkill, request.SourceSlot, request.FormIndex,
                    request.ChargeThresholdIndex, request.ProjectileIndex,
                    request.SkillStageIndex);
                if (settings != null && settings.Enabled)
                    LaunchProjectile(settings, request);
            }
        }

        private void LaunchProjectile(CombatProjectileSettings settings,
            RiderScheduledProjectileState request)
        {
            if (!Object.HasStateAuthority || settings == null || !settings.Enabled)
                return;
            int free = -1;
            for (int i = 0; i < ProjectileCapacity; ++i)
                if (!Shots.Get(i).Active) { free = i; break; }
            if (free < 0)
            {
                LogCombat("PROJECTILE_POOL_FULL", "max=" + ProjectileCapacity);
                return;
            }

            // Socket is sampled at each actual release, not when the attack starts.
            Quaternion facing = simpleKCC != null
                ? simpleKCC.TransformRotation : transform.rotation;
            Vector3 direction = facing * Vector3.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();
            Vector3 spawn = transform.position + settings.SpawnOffset +
                direction * settings.SpawnForward;
            if (request.FormIndex == CurrentFormIndex &&
                !string.IsNullOrWhiteSpace(settings.SpawnSocketPath))
            {
                Transform socket = FindProjectileSpawnSocket(settings.SpawnSocketPath);
                if (socket != null)
                {
                    if (settings.UseSocketRotationForDirection)
                    {
                        direction = socket.forward;
                        if (direction.sqrMagnitude > 0.0001f)
                            direction.Normalize();
                        else
                            direction = facing * Vector3.forward;
                    }
                    spawn = socket.TransformPoint(settings.SpawnOffset) +
                        direction * settings.SpawnForward;
                }
                else if (missingSpawnSockets.Add(settings.SpawnSocketPath))
                    Debug.LogWarning("[MuOnBaAr][Projectile] Socket not found on " +
                        "current Form: " + settings.SpawnSocketPath +
                        ". Using rider-root spawn fallback.", this);
            }
            RiderProjectileState shot = new RiderProjectileState
            {
                Active = true,
                Sequence = ++ProjectileSequence,
                FormIndex = request.FormIndex,
                FromSkill = request.FromSkill,
                SourceSlot = request.SourceSlot,
                ChargeThresholdIndex = request.ChargeThresholdIndex,
                ChargeProjectileIndex = request.ProjectileIndex,
                SkillStageIndex = request.SkillStageIndex,
                Position = spawn,
                Velocity = direction * settings.Speed,
                HitRadius = settings.HitRadius,
                Damage = request.Damage,
                HitReaction = request.HitReaction,
                KnockbackDistance = request.KnockbackDistance,
                KnockbackSeconds = request.KnockbackSeconds,
                StunDurationSeconds = request.StunDurationSeconds,
                ExpiryCombatTime = CombatTime + settings.LifetimeSeconds
            };
            Shots.Set(free, shot);
            LogCombat("PROJECTILE_LAUNCHED", "sequence=" + shot.Sequence +
                " slot=" + free + " position=" + spawn.ToString("F2"));
        }

        private void SimulateProjectile()
        {
            if (!Object.HasStateAuthority) return;
            float scaledDelta = Runner.DeltaTime * GetRiderTimeScale();
            for (int i = 0; i < ProjectileCapacity; ++i)
            {
                RiderProjectileState shot = Shots.Get(i);
                if (!shot.Active) continue;
                if (CombatTime >= shot.ExpiryCombatTime)
                {
                    shot.Active = false;
                    Shots.Set(i, shot);
                    continue;
                }
                if (scaledDelta <= 0f) continue;
                Vector3 from = shot.Position;
                Vector3 segment = shot.Velocity * scaledDelta;
                float distance = segment.magnitude;
                if (distance <= 0f) continue;
                Vector3 to = from + segment;

                float nearestWall = float.PositiveInfinity;
                Vector3 wallPoint = to;
                foreach (RaycastHit hit in Physics.RaycastAll(from,
                    segment.normalized, distance, meleeLineOfSightLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null ||
                        hit.collider.GetComponentInParent<NetworkRiderController>() != null)
                        continue;
                    if (hit.distance < nearestWall)
                    {
                        nearestWall = hit.distance;
                        wallPoint = hit.point;
                    }
                }

                NetworkRiderController target = null;
                float nearestT = float.PositiveInfinity;
                foreach (NetworkRiderController other in
                    FindObjectsOfType<NetworkRiderController>())
                {
                    if (!IsLiveOpponent(other) || !other.Object.HasStateAuthority ||
                        !RiderRespawnRules.CanTakeDamage(other.Health, Runner.Tick.Raw,
                            other.InvulnerableUntilTick)) continue;
                    Vector3 torso = other.transform.position + Vector3.up;
                    float t = Mathf.Clamp01(Vector3.Dot(torso - from, segment) /
                        Mathf.Max(0.0001f, segment.sqrMagnitude));
                    if ((torso - (from + t * segment)).sqrMagnitude >
                        shot.HitRadius * shot.HitRadius || t >= nearestT ||
                        t * distance >= nearestWall) continue;
                    nearestT = t;
                    target = other;
                }

                if (target != null)
                {
                    shot.Position = from + segment * nearestT;
                    ApplyDamage(target, shot.Damage,
                        (HitReactionType)shot.HitReaction,
                        shot.KnockbackDistance, shot.KnockbackSeconds,
                        shot.StunDurationSeconds);
                    shot.Active = false;
                }
                else if (nearestWall < float.PositiveInfinity)
                {
                    shot.Position = wallPoint;
                    shot.Active = false;
                }
                else shot.Position = to;
                Shots.Set(i, shot);
            }
        }

        private void ClearScheduledProjectiles()
        {
            if (!Object.HasStateAuthority) return;
            for (int i = 0; i < ScheduledProjectileCapacity; ++i)
                ScheduledShots.Set(i, default(RiderScheduledProjectileState));
        }

        private void ClearProjectiles()
        {
            if (!Object.HasStateAuthority) return;
            for (int i = 0; i < ProjectileCapacity; ++i)
            {
                RiderProjectileState shot = Shots.Get(i);
                if (!shot.Active) continue;
                shot.Active = false;
                Shots.Set(i, shot);
            }
        }

        private AbilityDefinition GetClockUpSourceAbility(int formIndex, int slot)
        {
            if (actor == null || !actor.TryGetForm(formIndex, out var form) ||
                form.Abilities == null || slot < 0 || slot >= form.Abilities.Length)
                return null;
            AbilityDefinition ability = form.Abilities[slot];
            return ability != null && ability.ClockUpEnabled ? ability : null;
        }

        // Visual state is derived from HOST-replicated sequence/end tick.
        // No extra network objects or client-side game-rule modifications.
        // Called on each peer's Render; only HOST accepts the combat action.
        // CombatTime and action sequences are replicated by Fusion.
        private void CacheBodyPartObjects(FormDefinition form)
        {
            FormBodyPart[] parts = form.CollectedBodyParts;
            int count = parts == null ? 0 : parts.Length;
            formBodyObjects = new GameObject[count];
            formBodyDefaultStates = new bool[count];
            formBodyDesiredStates = new bool[count];
            warnedBodyPartIndex = -1;
            if (spawnedVisual == null) return;
            for (int i = 0; i < count; ++i)
            {
                if (parts[i] == null) continue;
                Transform found = spawnedVisual.transform.Find(parts[i].HierarchyPath);
                if (found == null)
                {
                    Debug.LogWarning("[MuOnBaAr][BodyParts] Missing mesh path: " +
                        parts[i].HierarchyPath + ". Recollect body parts.", this);
                    continue;
                }
                formBodyObjects[i] = found.gameObject;
                formBodyDefaultStates[i] = found.gameObject.activeSelf;
                formBodyDesiredStates[i] = found.gameObject.activeSelf;
            }
        }

        private void UpdateBodyPartVisibility()
        {
            if (formBodyObjects.Length == 0 || actor == null) return;
            for (int i = 0; i < formBodyObjects.Length; ++i)
                formBodyDesiredStates[i] = formBodyDefaultStates[i];
            if (Health > 0 && actor.TryGetForm(CurrentFormIndex, out var form))
            {
                // The old Form owns Start visibility. The new Form owns End visibility.
                // These cues override older skill/attack cues while transforming.
                if (FormTransitionPhase == 1 || FormTransitionPhase == 2)
                {
                    BodyPartVisibilityCue[] timeline = FormTransitionPhase == 1
                        ? form.FormChangeStartBodyPartTimeline
                        : form.FormChangeEndBodyPartTimeline;
                    ApplyBodyPartCues(timeline,
                        Mathf.Max(0f, CombatTime - FormTransitionPhaseStartTime));
                }
                else if (SkillSequence > 0 && CombatTime >= SkillStartCombatTime &&
                    CombatTime < SkillLockUntilCombatTime &&
                    form.Abilities != null && SkillIndex >= 0 &&
                    SkillIndex < form.Abilities.Length &&
                    form.Abilities[SkillIndex] != null)
                    ApplyBodyPartCues(GetCurrentSkillComboFollowUp() != null
                            ? GetCurrentSkillComboFollowUp().BodyPartTimeline
                            : form.Abilities[SkillIndex].BodyPartTimeline,
                        CombatTime - SkillStartCombatTime);
                else if (AttackSequence > 0 && CombatTime >= ComboStartCombatTime &&
                    CombatTime < ComboLockUntilCombatTime &&
                    form.TryGetComboAttack(ComboIndex, out var combo))
                    ApplyBodyPartCues(combo.BodyPartTimeline,
                        CombatTime - ComboStartCombatTime);
            }
            // Only change activeSelf when needed: do not toggle Animator bones.
            for (int i = 0; i < formBodyObjects.Length; ++i)
            {
                GameObject go = formBodyObjects[i];
                if (go != null && go.activeSelf != formBodyDesiredStates[i])
                    go.SetActive(formBodyDesiredStates[i]);
            }
        }

        private void ApplyBodyPartCues(BodyPartVisibilityCue[] timeline, float elapsed)
        {
            if (timeline == null) return;
            // Highest TimeSeconds <= current action progress wins, independent
            // of Inspector array ordering (tie: later array element wins).
            float[] latestTimes = new float[formBodyDesiredStates.Length];
            for (int i = 0; i < latestTimes.Length; ++i)
                latestTimes[i] = -1f;
            foreach (BodyPartVisibilityCue cue in timeline)
            {
                if (cue == null || cue.TimeSeconds > elapsed) continue;
                int idx = cue.BodyPartIndex;
                if (idx < 0 || idx >= formBodyDesiredStates.Length)
                {
                    if (warnedBodyPartIndex != idx)
                    {
                        warnedBodyPartIndex = idx;
                        Debug.LogWarning("[MuOnBaAr][BodyParts] Invalid body part index " +
                            idx + ". Collect body parts on this FormDefinition.", this);
                    }
                    continue;
                }
                if (cue.TimeSeconds >= latestTimes[idx])
                {
                    latestTimes[idx] = cue.TimeSeconds;
                    formBodyDesiredStates[idx] = cue.Active;
                }
            }
        }

        // Local cosmetic AudioSources are driven by HOST-replicated action sequence
        // and CombatTime. Overlapping cues use distinct pooled AudioSources.
        private void UpdateActionAudioTimelines()
        {
            if (combatAudioPool == null)
                combatAudioPool = CombatAudioPoolManager.FindOrCreate();
            for (int i = activeCombatAudio.Count - 1; i >= 0; --i)
            {
                AudioSource src = activeCombatAudio[i];
                if (src == null || Health <= 0)
                {
                    if (src != null) combatAudioPool.Release(src);
                    activeCombatAudio.RemoveAt(i);
                }
                else if (!combatAudioPool.UpdateSource(src, GetRiderTimeScale()))
                    activeCombatAudio.RemoveAt(i);
            }
            if (Health <= 0)
            {
                firedComboAudioCues.Clear();
                firedSkillAudioCues.Clear();
                observedComboAudioSequence = AttackSequence;
                observedSkillAudioSequence = SkillSequence;
                return;
            }
            if (observedComboAudioSequence != AttackSequence)
            {
                observedComboAudioSequence = AttackSequence;
                firedComboAudioCues.Clear();
            }
            if (observedSkillAudioSequence != SkillSequence)
            {
                observedSkillAudioSequence = SkillSequence;
                firedSkillAudioCues.Clear();
            }
            if (!actor.TryGetForm(CurrentFormIndex, out var form)) return;
            if (AttackSequence > 0 && CombatTime >= ComboStartCombatTime &&
                CombatTime < ComboLockUntilCombatTime &&
                form.TryGetComboAttack(ComboIndex, out var combo))
                PlayActionAudioCues(combo.AudioTimeline,
                    CombatTime - ComboStartCombatTime, firedComboAudioCues);
            if (SkillSequence > 0 && CombatTime >= SkillStartCombatTime &&
                CombatTime < SkillLockUntilCombatTime &&
                form.Abilities != null && SkillIndex >= 0 &&
                SkillIndex < form.Abilities.Length &&
                form.Abilities[SkillIndex] != null)
                PlayActionAudioCues(GetCurrentChargeReleaseThreshold() != null
                        ? GetCurrentChargeReleaseThreshold().AudioTimeline
                        : GetCurrentSkillComboFollowUp() != null
                            ? GetCurrentSkillComboFollowUp().AudioTimeline
                            : form.Abilities[SkillIndex].AudioTimeline,
                    CombatTime - SkillStartCombatTime, firedSkillAudioCues);
        }

        private void PlayActionAudioCues(CombatAudioCue[] cues,
            float elapsed, System.Collections.Generic.HashSet<int> fired)
        {
            if (cues == null) return;
            for (int i = 0; i < cues.Length; i++)
            {
                CombatAudioCue cue = cues[i];
                if (cue == null || fired.Contains(i) || elapsed < cue.StartSeconds)
                    continue;
                fired.Add(i);
                if (cue.Clip == null) continue;
                float missed = elapsed - cue.StartSeconds;
                if (cue.ScalePitchWithCombatTime &&
                    missed * cue.Pitch >= cue.Clip.length)
                    continue; // Late proxy: skip a clip that already ended.
                Transform anchor = transform;
                if (!string.IsNullOrWhiteSpace(cue.SocketPath))
                {
                    Transform found = FindProjectileSpawnSocket(cue.SocketPath);
                    if (found != null) anchor = found;
                }
                AudioSource src = combatAudioPool.Rent(cue, anchor, GetRiderTimeScale());
                if (src == null) continue;
                if (cue.ScalePitchWithCombatTime && missed > 0f)
                    src.time = Mathf.Clamp(missed * cue.Pitch, 0f,
                        Mathf.Max(0f, cue.Clip.length - 0.01f));
                activeCombatAudio.Add(src);
            }
        }

        private void UpdateActionVfxTimelines()
        {
            if (projectilePool == null)
                projectilePool = ProjectilePoolManager.FindOrCreate();
            for (int i = timedActionVisuals.Count - 1; i >= 0; --i)
            {
                if (Health <= 0 || CombatTime >= timedActionVisualEnds[i])
                {
                    projectilePool.Release(timedActionVisuals[i]);
                    timedActionVisuals.RemoveAt(i);
                    timedActionVisualEnds.RemoveAt(i);
                }
            }
            if (Health <= 0)
            {
                firedComboCues.Clear();
                firedSkillCues.Clear();
                observedComboVfxSequence = AttackSequence;
                observedSkillVfxSequence = SkillSequence;
                return;
            }
            if (observedComboVfxSequence != AttackSequence)
            {
                observedComboVfxSequence = AttackSequence;
                firedComboCues.Clear();
            }
            if (observedSkillVfxSequence != SkillSequence)
            {
                observedSkillVfxSequence = SkillSequence;
                firedSkillCues.Clear();
            }
            if (!actor.TryGetForm(CurrentFormIndex, out var form))
                return;
            if (AttackSequence > 0 && CombatTime >= ComboStartCombatTime &&
                CombatTime < ComboLockUntilCombatTime &&
                form.TryGetComboAttack(ComboIndex, out var combo))
                PlayActionVfxCues(combo.VfxTimeline,
                    CombatTime - ComboStartCombatTime, firedComboCues);
            if (SkillSequence > 0 && CombatTime >= SkillStartCombatTime &&
                CombatTime < SkillLockUntilCombatTime &&
                form.Abilities != null && SkillIndex >= 0 &&
                SkillIndex < form.Abilities.Length && form.Abilities[SkillIndex] != null)
                PlayActionVfxCues(GetCurrentChargeReleaseThreshold() != null
                        ? GetCurrentChargeReleaseThreshold().VfxTimeline
                        : GetCurrentSkillComboFollowUp() != null
                            ? GetCurrentSkillComboFollowUp().VfxTimeline
                            : form.Abilities[SkillIndex].VfxTimeline,
                    CombatTime - SkillStartCombatTime, firedSkillCues);
        }

        private void PlayActionVfxCues(CombatVfxCue[] timeline,
            float elapsed, System.Collections.Generic.HashSet<int> fired)
        {
            if (timeline == null) return;
            for (int i = 0; i < timeline.Length; ++i)
            {
                CombatVfxCue cue = timeline[i];
                if (cue == null || fired.Contains(i) || elapsed < cue.StartSeconds)
                    continue;
                fired.Add(i);
                // A late-joining proxy does not replay VFX that already ended.
                float remaining = cue.DurationSeconds - (elapsed - cue.StartSeconds);
                if (remaining <= 0f || cue.Prefab == null) continue;
                Transform anchor = transform;
                if (!string.IsNullOrWhiteSpace(cue.SocketPath))
                {
                    Transform found = FindProjectileSpawnSocket(cue.SocketPath);
                    if (found != null) anchor = found;
                }
                GameObject visual = projectilePool.RentVfx(
                    cue.Prefab, anchor, cue.LocalOffset, cue.FollowSocket);
                if (visual == null) continue;
                timedActionVisuals.Add(visual);
                timedActionVisualEnds.Add(CombatTime + remaining);
            }
        }

        private void UpdateClockUpVfx()
        {
            if (Object == null || Runner == null) return;
            if (projectilePool == null)
                projectilePool = ProjectilePoolManager.FindOrCreate();
            bool active = IsClockUpActive();
            bool newActivation = active && ClockUpSequence != observedClockUpSequence;
            if (newActivation)
            {
                // Close prior VFX cleanly if a second Clock Up replaces it.
                ReleaseClockUpVfx(false);
                observedClockUpSequence = ClockUpSequence;
                observedClockUpSourceForm = ClockUpSourceForm;
                observedClockUpSourceSlot = ClockUpSourceSlot;
                AbilityDefinition source = GetClockUpSourceAbility(
                    ClockUpSourceForm, ClockUpSourceSlot);
                if (source != null)
                {
                    clockUpStartVisual = projectilePool.RentVfx(
                        source.ClockUpStartVfxPrefab, transform, source.ClockUpVfxOffset);
                    clockUpStartVisualTime = source.ClockUpStartVfxSeconds;
                    clockUpActiveVisual = projectilePool.RentVfx(
                        source.ClockUpActiveVfxPrefab, transform, source.ClockUpVfxOffset);
                }
            }
            if (!active && observedClockUpActive)
            {
                AbilityDefinition source = GetClockUpSourceAbility(
                    observedClockUpSourceForm, observedClockUpSourceSlot);
                ReleaseClockUpVfx(false);
                if (source != null)
                {
                    clockUpEndVisual = projectilePool.RentVfx(
                        source.ClockUpEndVfxPrefab, transform, source.ClockUpVfxOffset);
                    clockUpEndVisualTime = source.ClockUpEndVfxSeconds;
                }
            }
            observedClockUpActive = active;
            float dt = Time.unscaledDeltaTime;
            if (clockUpStartVisual != null)
            {
                clockUpStartVisualTime -= dt;
                if (clockUpStartVisualTime <= 0f)
                {
                    projectilePool.Release(clockUpStartVisual);
                    clockUpStartVisual = null;
                }
            }
            if (clockUpEndVisual != null)
            {
                clockUpEndVisualTime -= dt;
                if (clockUpEndVisualTime <= 0f)
                {
                    projectilePool.Release(clockUpEndVisual);
                    clockUpEndVisual = null;
                }
            }
        }

        private void ReleaseClockUpVfx(bool includeEnd)
        {
            if (projectilePool == null) return;
            if (clockUpStartVisual != null) projectilePool.Release(clockUpStartVisual);
            if (clockUpActiveVisual != null) projectilePool.Release(clockUpActiveVisual);
            if (includeEnd && clockUpEndVisual != null)
                projectilePool.Release(clockUpEndVisual);
            clockUpStartVisual = null;
            clockUpActiveVisual = null;
            if (includeEnd) clockUpEndVisual = null;
        }

        private void UpdateProjectileVisual()
        {
            if (projectilePool == null)
                projectilePool = ProjectilePoolManager.FindOrCreate();
            for (int i = 0; i < ProjectileCapacity; ++i)
            {
                RiderProjectileState shot = Shots.Get(i);
                GameObject visual = shotVisuals[i];
                if (!shot.Active)
                {
                    if (visual != null) projectilePool.Release(visual);
                    shotVisuals[i] = null;
                    shotVisualSequences[i] = 0;
                    continue;
                }
                if (visual == null || shotVisualSequences[i] != shot.Sequence)
                {
                    if (visual != null) projectilePool.Release(visual);
                    shotVisualSequences[i] = shot.Sequence;
                    CombatProjectileSettings settings = GetProjectileSettings(
                        shot.FromSkill, shot.SourceSlot, shot.FormIndex,
                        shot.ChargeThresholdIndex, shot.ChargeProjectileIndex,
                        shot.SkillStageIndex);
                    GameObject prefab = settings != null ? settings.VisualPrefab : null;
                    Quaternion rotation = shot.Velocity.sqrMagnitude > 0.0001f ?
                        Quaternion.LookRotation(shot.Velocity) : Quaternion.identity;
                    shotVisuals[i] = visual = projectilePool.Rent(prefab,
                        shot.Position, rotation, GetRiderTimeScale());
                }
                if (visual == null) continue;
                visual.transform.position = shot.Position;
                if (shot.Velocity.sqrMagnitude > 0.0001f)
                    visual.transform.rotation = Quaternion.LookRotation(shot.Velocity);
                projectilePool.UpdatePlayback(visual, GetRiderTimeScale());
            }
        }

        private void BeginKnockout(int currentTick)
        {
            if (!Object.HasStateAuthority || RespawnTick > 0)
                return;

            int delayTicks = Mathf.Max(1, RiderRespawnRules.DurationTicks(
                respawnDelaySeconds, Runner.DeltaTime));
            RespawnTick = currentTick + delayTicks;
            CancelFormTransition();
            ClockUpEndTick = 0;
            PendingClockUpArmed = false;
            PullPending = false;
            HookEndCombatTime = CombatTime;
            GravityEndCombatTime = CombatTime;
            FormChangeLockedUntilTick = 0;
            PendingImpactTick = 0;
            PendingImpactArmed = false;
            ClearScheduledProjectiles();
            ClearProjectiles();
            AttackActiveUntilTick = 0;
            ComboLockUntilCombatTime = CombatTime;
            ComboIndex = 0;
            ComboNextBuffered = false;
            SkillComboStage = 0;
            SkillComboNextBuffered = false;
            ChargeActive = false;
            ChargeLevel = 0;
            SkillWasCharged = false;
            ReleasedChargeThresholdIndex = -1;
            PendingChargeThresholdIndex = -1;
            SkillLockUntilCombatTime = CombatTime;
            DodgeEndCombatTime = CombatTime;
            AttackStepDistance = 0f;
            SkillLockedUntilTick = 0;
            InvulnerableUntilTick = 0;
            StunEndCombatTime = CombatTime;
            HitReactionEndCombatTime = CombatTime;
            // Do not keep moving upward after KO; body can still fall.
            AirVerticalSpeed = Mathf.Min(0f, AirVerticalSpeed);
            FlightActive = false;
            FlyHoldPending = false;
            FlyEnergyFraction = 0f;
            LogCombat("KO", "hp=" + Health + " respawnTick=" + RespawnTick +
                " delayTicks=" + delayTicks);
        }

        private void RespawnAtOriginalSpawn()
        {
            if (!Object.HasStateAuthority || Health > 0)
                return;

            // KCC teleport resets the movement interpolation across respawn.
            if (simpleKCC != null)
            {
                simpleKCC.SetPosition(originalSpawnPosition, true);
                simpleKCC.SetLookRotation(originalSpawnRotation);
                simpleKCC.ResetVelocity();
            }
            else
            {
                transform.SetPositionAndRotation(originalSpawnPosition, originalSpawnRotation);
            }
            CurrentFormIndex = actor.StartingFormIndex;
            CancelFormTransition();
            ClockUpEndTick = 0;
            PendingClockUpArmed = false;
            PullPending = false;
            HookEndCombatTime = CombatTime;
            GravityEndCombatTime = CombatTime;
            PendingImpactTick = 0;
            PendingImpactArmed = false;
            ClearScheduledProjectiles();
            ClearProjectiles();
            AttackActiveUntilTick = 0;
            SkillLockedUntilTick = 0;
            ComboLockUntilCombatTime = CombatTime;
            ComboIndex = 0;
            ComboNextBuffered = false;
            SkillComboStage = 0;
            SkillComboNextBuffered = false;
            ChargeActive = false;
            ChargeLevel = 0;
            SkillWasCharged = false;
            ReleasedChargeThresholdIndex = -1;
            PendingChargeThresholdIndex = -1;
            SkillLockUntilCombatTime = CombatTime;
            Health = actor.MaxHealth;
            AirVerticalSpeed = 0f;
            AirMotionState = 0;
            FlightActive = false;
            FlyHoldPending = false;
            FlyEnergyFraction = 0f;
            StunEndCombatTime = CombatTime;
            HitReactionEndCombatTime = CombatTime;
            Energy = actor.MaxEnergy;

            int now = Runner.Tick.Raw;
            NextChangeTick = now;
            NextAttackTick = now;
            NextComboCombatTime = CombatTime;
            NextSkill0CombatTime = NextSkill1CombatTime = NextSkill2CombatTime = CombatTime;
            RespawnTick = 0;
            InvulnerableUntilTick = now + RiderRespawnRules.DurationTicks(
                respawnProtectionSeconds, Runner.DeltaTime);
            LogCombat("RESPAWN", "hp=" + Health + " energy=" + Energy +
                " form=" + CurrentFormIndex + " position=" +
                originalSpawnPosition.ToString("F2") +
                " protectedUntilTick=" + InvulnerableUntilTick);
        }

        private void LogCombat(string eventName, string details)
        {
            // Only the State Authority emits these diagnostic events.
            if (!enableCombatDebugLogs || Runner == null || Object == null ||
                !Object.HasStateAuthority)
                return;

            Debug.Log(RiderCombatTelemetry.Format(
                eventName, Runner.Tick.Raw, Object.InputAuthority.ToString(),
                Object.Id.ToString(), details), this);
        }

        private bool HasLineOfSight(NetworkRiderController target)
        {
            // Server-authoritative melee originates at the rider's torso,
            // never from the client camera. Ignore the full attacker hierarchy
            // (including VisualRoot/Cube) while retaining solid walls.
            Vector3 origin = transform.position + Vector3.up;
            Vector3 destination = target.transform.position + Vector3.up;
            return RiderLineOfSightRules.HasClearPath(
                origin, destination, transform, target.transform,
                meleeLineOfSightLayers);
        }

        private bool HasCameraLineOfSight(
            NetworkRiderController target, Camera camera)
        {
            // MOBA-style LOCAL target selection only; this does not authorize
            // a hit or alter the server-side melee facing (+Z).
            if (!requireTargetLockLineOfSight || camera == null)
                return true;

            CharacterController targetBody =
                target.GetComponent<CharacterController>();
            Vector3 destination = targetBody != null && targetBody.enabled
                ? targetBody.bounds.center
                : target.transform.position + Vector3.up;
            return RiderLineOfSightRules.HasClearPath(
                camera.transform.position, destination,
                transform, target.transform, targetLockVisibilityLayers);
        }

        private void UpdateLocomotionAnimation()
        {
            if (!supportsLocomotion || spawnedFormAnimator == null)
                return;

            // Input Authority drives its own animation from the last Fusion input.
            // Proxies use the server-replicated value. The value is reset on release,
            // KO and missing input, avoiding persistent Run from transform jitter.
            float normalizedSpeed = Object != null && Object.HasInputAuthority
                ? localAnimatorSpeedNormalized
                : ReplicatedAnimatorSpeedNormalized;
            if (Health <= 0 || FormTransitionPhase != 0 ||
                CombatTime < HitReactionEndCombatTime ||
                CombatTime < DodgeEndCombatTime ||
                CombatTime < ComboLockUntilCombatTime ||
                CombatTime < SkillLockUntilCombatTime || ChargeActive)
                normalizedSpeed = 0f;

            float blend = RiderLocomotionRules.BlendValueForSpeed(normalizedSpeed);
            float dt = Mathf.Max(0f, Time.deltaTime * GetRiderTimeScale());
            // Do not let locomotion blend back in during combo/skill.
            bool locked = FormTransitionPhase != 0 ||
                CombatTime < HitReactionEndCombatTime ||
                CombatTime < DodgeEndCombatTime ||
                CombatTime < ComboLockUntilCombatTime ||
                CombatTime < SkillLockUntilCombatTime || ChargeActive;
            if (locked)
                spawnedFormAnimator.SetFloat(LocomotionId, 0f);
            else
                spawnedFormAnimator.SetFloat(LocomotionId, blend, 0.08f, dt);
        }

        private bool HasAnimatorStuntBool()
        {
            if (spawnedFormAnimator == null) return false;
            foreach (AnimatorControllerParameter p in spawnedFormAnimator.parameters)
                if (p.nameHash == StuntBoolId &&
                    p.type == AnimatorControllerParameterType.Bool)
                    return true;
            return false;
        }

        private void UpdateHitReactionAnimation()
        {
            if (spawnedFormAnimator == null || Object == null) return;
            bool stuntActive = Health > 0 && CombatTime < StunEndCombatTime;
            // Parameter drives the authored Stunt(loop) -> Locomotion transition.
            // Do not auto-crossfade to Idle when stun ends.
            if (HasAnimatorStuntBool())
                spawnedFormAnimator.SetBool(StuntBoolId, stuntActive);
            bool active = Health > 0 && CombatTime < HitReactionEndCombatTime;
            if (HitReactionSequence != observedHitReactionSequence)
            {
                observedHitReactionSequence = HitReactionSequence;
                hitReactionAnimationPlaying = false;
                if (!active || HitReactionIndex == (int)HitReactionType.None) return;
                string state;
                switch ((HitReactionType)HitReactionIndex)
                {
                    case HitReactionType.Head: state = "HitHead"; break;
                    case HitReactionType.Body: state = "HItBody"; break;
                    case HitReactionType.Foot: state = "HitFoot"; break;
                    case HitReactionType.Left: state = "Hit2Left"; break;
                    case HitReactionType.Right: state = "Hit2Right"; break;
                    case HitReactionType.Pull: state = "GotPull"; break;
                    case HitReactionType.Stun: state = "Stunt"; break;
                    default: return;
                }
                int hash = Animator.StringToHash("Base Layer.Hit Reaction." + state);
                if (!spawnedFormAnimator.HasState(0, hash))
                    hash = Animator.StringToHash("Base Layer.Status." + state);
                if (spawnedFormAnimator.HasState(0, hash))
                {
                    spawnedFormAnimator.CrossFadeInFixedTime(hash,
                        attackAnimationCrossFade, 0, 0f);
                    hitReactionAnimationPlaying = true;
                }
                else
                    Debug.LogWarning("[MuOnBaAr][HitReaction] Missing state " +
                        state + " in Animator on " + gameObject.name, this);
            }
            // Do NOT force a return to Locomotion when the gameplay hit lock
            // expires. Each Animator Controller owns Hit Reaction -> Locomotion
            // transitions (Exit Time, duration and conditions) per individual state.
            // The HOST reaction clock only gates gameplay, not clip playback.
            if (hitReactionAnimationPlaying && !active)
                hitReactionAnimationPlaying = false;
        }

        private void UpdateBasicAttackAnimation()
        {
            if (spawnedFormAnimator == null || Object == null)
                return;
            if (CombatTime < HitReactionEndCombatTime) return;

            if (!attackAnimationInitialized)
            {
                observedAnimationAttackSequence = AttackSequence;
                attackAnimationInitialized = true;
            }

            // Each accepted server attack increments the already-replicated sequence.
            // This makes the attack visible to HOST, local CLIENT and remote proxies
            // without new RPCs or additional networked fields.
            if (AttackSequence != observedAnimationAttackSequence)
            {
                observedAnimationAttackSequence = AttackSequence;
                if (Health > 0)
                {
                    string stateName = "Base Layer.Attack Combo.Combo" + ComboIndex;
                    int stateHash = Animator.StringToHash(stateName);
                    if (spawnedFormAnimator.HasState(0, stateHash))
                    {
                        spawnedFormAnimator.CrossFadeInFixedTime(
                            stateHash, attackAnimationCrossFade, 0, 0f);
                        float duration = basicAttackAnimationLength;
                        if (actor.TryGetForm(CurrentFormIndex, out var form) &&
                            form.TryGetComboAttack(ComboIndex, out var comboStage))
                            duration = comboStage.DurationSeconds;
                        basicAttackAnimationRemainingTime = duration;
                        basicAttackAnimationActive = true;
                    }
                    else
                    {
                        basicAttackAnimationActive = false;
                        Debug.LogWarning("[MuOnBaAr][Attack Animator] Missing " +
                            stateName + "; gameplay continues.", this);
                    }
                }
            }

            if (!basicAttackAnimationActive)
                return;

            basicAttackAnimationRemainingTime -=
                Time.deltaTime * GetRiderTimeScale();
            if (Health <= 0 || basicAttackAnimationRemainingTime <= 0f)
            {
                basicAttackAnimationActive = false;
                // Exit to the user-authored Locomotion sub-state machine.
                // The Float Locomotion parameter immediately selects Run/Sprint
                // when input is held and Idle otherwise.
                if (spawnedFormAnimator.HasState(0, LocomotionIdleStateId))
                    spawnedFormAnimator.CrossFadeInFixedTime(
                        LocomotionIdleStateId, attackAnimationCrossFade, 0, 0f);
            }
        }

        private void RefreshFormVisual()
        {
            if (!actor.TryGetForm(CurrentFormIndex, out var form))
                return;

            displayedFormIndex = CurrentFormIndex;
            // Prevent old Form's socket-anchored VFX from following destroyed bones.
            for (int i = timedActionVisuals.Count - 1; i >= 0; --i)
                if (projectilePool != null) projectilePool.Release(timedActionVisuals[i]);
            timedActionVisuals.Clear();
            timedActionVisualEnds.Clear();
            if (combatAudioPool != null)
                foreach (AudioSource src in activeCombatAudio)
                    combatAudioPool.Release(src);
            activeCombatAudio.Clear();
            firedComboAudioCues.Clear();
            firedSkillAudioCues.Clear();
            observedComboAudioSequence = AttackSequence;
            observedSkillAudioSequence = SkillSequence;
            firedComboCues.Clear();
            firedSkillCues.Clear();
            observedComboVfxSequence = AttackSequence;
            observedSkillVfxSequence = SkillSequence;
            if (projectilePool != null)
                projectilePool.Prepare(this, form);
            observedAnimationAttackSequence = AttackSequence;
            attackAnimationInitialized = true;
            basicAttackAnimationActive = false;
            skillAnimationActive = false;
            missingComboStateLogged = false;
            observedAirMotionState = -1;
            lastPlayedAirborneStateHash = 0;
            missingAirborneStateMask = 0;
            cachedAirborneAnimatorParameters =
                System.Array.Empty<AnimatorControllerParameter>();
            lastFeedbackTint = -1;
            spawnedFormAnimator = null;
            supportsLocomotion = false;
            previousRenderPosition = transform.position;
            hasRenderPosition = false;

            if (spawnedVisual != null)
                Destroy(spawnedVisual);

            spawnedVisual = null;
            formBodyObjects = System.Array.Empty<GameObject>();
            formBodyDefaultStates = System.Array.Empty<bool>();
            formBodyDesiredStates = System.Array.Empty<bool>();
            if (form.VisualPrefab != null)
            {
                Transform parent = visualRoot != null ? visualRoot : transform;
                spawnedVisual = Instantiate(form.VisualPrefab, parent);
                // Align the form mesh to its animation-space ground (ACL00 Y=0)
                // while preserving the authoritative rider root at spawn Y=1.
                // This affects the instantiated form visual only, not collision.
                spawnedVisual.transform.localPosition = spawnedVisualLocalOffset;
                spawnedVisual.transform.localRotation = Quaternion.identity;
                spawnedFormAnimator = spawnedVisual.GetComponentInChildren<Animator>();
                if (spawnedFormAnimator != null)
                {
                    // Prevent imported root motion from moving the visual
                    // away from the fixed local Y=-1 alignment.
                    spawnedFormAnimator.applyRootMotion = false;
                    if (form.AnimatorController != null)
                        spawnedFormAnimator.runtimeAnimatorController =
                            form.AnimatorController;

                    AnimatorControllerParameter[] parameters =
                        spawnedFormAnimator.parameters;
                    cachedAirborneAnimatorParameters = parameters;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].nameHash == LocomotionId &&
                            parameters[i].type == AnimatorControllerParameterType.Float)
                        {
                            supportsLocomotion = true;
                            break;
                        }
                    }
                    if (supportsLocomotion)
                        spawnedFormAnimator.SetFloat(LocomotionId, 0f);
                }
            }

            CacheBodyPartObjects(form);

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
            if (Object == null || actor == null)
                return;

            // Legacy screen-space OnGUI is only an emergency HUD fallback;
            // the overhead health UI is now authored in the Rider prefab.
            if (!Object.HasInputAuthority || MuOnBaArCanvasHud.IsReady)
                return;

            string formName = actor.TryGetForm(CurrentFormIndex, out var form)
                ? form.DisplayName : "Unknown";
            GUI.Label(new Rect(20f, 178f, 520f, 28f),
                "Local Rider: " + formName + " | HP " + Health +
                " | Energy " + Energy + " | TAB: Change Form");

            string lockText = localLockedTarget == null ? "None" :
                ("Rider HP " + localLockedTarget.Health);
            GUI.Label(new Rect(20f, 208f, 520f, 28f),
                "Q: Target Lock [" + lockText + "] | LMB: Basic Attack");
        }
    }
}
