using System;
using UnityEngine;

namespace MuOnBaAr.Runtime
{
    // 0=None, remaining values map to Animator state names.
    public enum HitReactionType
    {
        None = 0, Head = 1, Body = 2, Foot = 3,
        Left = 4, Right = 5, Pull = 6, Stun = 7
    }

    [Serializable]
    public sealed class CombatProjectileSettings
    {
        [SerializeField] private bool enabled;
        [Tooltip("Extra scaled CombatTime seconds after attack startup; zero fires on impact.")]
        [SerializeField, Min(0f)] private float releaseDelaySeconds;
        [Tooltip("Visual-only prefab (no NetworkObject required); each client instantiates it locally.")]
        [SerializeField] private GameObject visualPrefab;
        [SerializeField, Min(0.1f)] private float speed = 12f;
        [SerializeField, Min(0.1f)] private float lifetimeSeconds = 3f;
        [SerializeField, Min(0.05f)] private float hitRadius = 0.35f;
        [SerializeField, Min(0f)] private float spawnForward = 0.8f;
        [SerializeField] private Vector3 spawnOffset = Vector3.up;
        [Header("Optional Animated Spawn Socket")]
        [Tooltip("Name of a child Transform on the spawned Form visual, or a relative path " +
            "(for example Armature/RightHand/ProjectileSocket). Leave empty for root spawn.")]
        [SerializeField] private string spawnSocketPath = "";
        [Tooltip("If socket is found, Spawn Offset is relative to its orientation. " +
            "Without a socket, legacy world-space Spawn Offset is preserved.")]
        [SerializeField] private bool useSocketRotationForDirection;

        public bool Enabled => enabled;
        public float ReleaseDelaySeconds => Mathf.Max(0f, releaseDelaySeconds);
        public static bool HasEnabledEntries(CombatProjectileSettings[] entries,
            CombatProjectileSettings legacy)
        {
            if (entries != null && entries.Length > 0)
            {
                foreach (CombatProjectileSettings entry in entries)
                    if (entry != null && entry.Enabled) return true;
                return false;
            }
            return legacy != null && legacy.Enabled;
        }
        public GameObject VisualPrefab => visualPrefab;
        public float Speed => Mathf.Max(0.1f, speed);
        public float LifetimeSeconds => Mathf.Max(0.1f, lifetimeSeconds);
        public float HitRadius => Mathf.Max(0.05f, hitRadius);
        public float SpawnForward => Mathf.Max(0f, spawnForward);
        public Vector3 SpawnOffset => spawnOffset;
        public string SpawnSocketPath => spawnSocketPath;
        public bool UseSocketRotationForDirection => useSocketRotationForDirection;
    }

    [Serializable]
    public sealed class CombatVfxCue
    {
        [Tooltip("Local-only prefab. ParticleSystem prefabs are supported.")]
        [SerializeField] private GameObject prefab;
        [Tooltip("When this VFX appears, in scaled animation/CombatTime seconds from action start.")]
        [SerializeField, Min(0f)] private float startSeconds;
        [Tooltip("How long the spawned effect remains alive, in the action's CombatTime seconds.")]
        [SerializeField, Min(0.05f)] private float durationSeconds = 0.5f;
        [Tooltip("Optional path or unique Transform name on the current Form visual. Blank = rider root.")]
        [SerializeField] private string socketPath;
        [SerializeField] private Vector3 localOffset;
        [Tooltip("If disabled, stay at the world position where the VFX started.")]
        [SerializeField] private bool followSocket = true;

        public GameObject Prefab => prefab;
        public float StartSeconds => Mathf.Max(0f, startSeconds);
        public float DurationSeconds => Mathf.Max(0.05f, durationSeconds);
        public string SocketPath => socketPath;
        public Vector3 LocalOffset => localOffset;
        public bool FollowSocket => followSocket;
    }

    [Serializable]
    public sealed class CombatAudioCue
    {
        [SerializeField] private AudioClip clip;
        [Tooltip("Start offset in scaled CombatTime seconds from Combo/Skill start.")]
        [SerializeField, Min(0f)] private float startSeconds;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
        [Tooltip("0 = 2D audio, 1 = fully spatial 3D audio.")]
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
        [SerializeField] private string socketPath;
        [SerializeField] private Vector3 localOffset;
        [SerializeField] private bool followSocket = true;
        [Tooltip("Play/pause/pitch follows Clock Up / WorldScale of this rider.")]
        [SerializeField] private bool scalePitchWithCombatTime = true;
        public AudioClip Clip => clip;
        public float StartSeconds => Mathf.Max(0f, startSeconds);
        public float Volume => Mathf.Clamp01(volume);
        public float Pitch => Mathf.Clamp(pitch, 0.1f, 3f);
        public float SpatialBlend => Mathf.Clamp01(spatialBlend);
        public string SocketPath => socketPath;
        public Vector3 LocalOffset => localOffset;
        public bool FollowSocket => followSocket;
        public bool ScalePitchWithCombatTime => scalePitchWithCombatTime;
    }

    [Serializable]
    public sealed class FormBodyPart
    {
        [SerializeField] private string hierarchyPath;
        [SerializeField] private string meshName;
        [SerializeField] private bool defaultActive;

        public string HierarchyPath => hierarchyPath;
        public string MeshName => meshName;
        public bool DefaultActive => defaultActive;

        public FormBodyPart(string path, string name, bool isActive)
        {
            hierarchyPath = path;
            meshName = name;
            defaultActive = isActive;
        }
    }

    [Serializable]
    public sealed class BodyPartVisibilityCue
    {
        [Tooltip("Index into FormDefinition > Collected Body Parts. Refresh the list from the prefab first.")]
        [SerializeField, Min(0)] private int bodyPartIndex;
        [Tooltip("Elapsed scaled CombatTime seconds since this Combo or Ability began.")]
        [SerializeField, Min(0f)] private float timeSeconds;
        [Tooltip("True=SetActive(true), False=SetActive(false) on the selected mesh GameObject.")]
        [SerializeField] private bool active = true;

        public int BodyPartIndex => Mathf.Max(0, bodyPartIndex);
        public float TimeSeconds => Mathf.Max(0f, timeSeconds);
        public bool Active => active;
    }

    [Serializable]
    public sealed class CombatMovementStep
    {
        [Tooltip("Local displacement in metres: X=left(-)/right(+), Y=back(-)/forward(+).")]
        [SerializeField] private Vector2 movement = Vector2.zero;
        [Tooltip("Normalized action time when this movement starts (0..1).")]
        [SerializeField, Range(0f, 1f)] private float startNormalized;
        [Tooltip("Normalized action time when this movement finishes (0..1).")]
        [SerializeField, Range(0f, 1f)] private float endNormalized = 0.5f;

        public Vector2 Movement => movement;
        public float StartNormalized => Mathf.Clamp01(startNormalized);
        public float EndNormalized => Mathf.Clamp01(endNormalized);
    }

    // Per-combo melee detection area. 360 degrees affects all enemies in radius.
    public enum ComboHitAreaMode
    {
        FrontArc = 0,
        FullCircle360 = 1
    }

    [Serializable]
    public sealed class FormComboAttack
    {
        [SerializeField, Min(0.1f)] private float durationSeconds = 0.8666667f;
        [Header("Facing Direction")]
        [Tooltip("If enabled, face the locked opponent (or camera direction without a lock) " +
            "once when this combo stage begins. If disabled, keep current facing.")]
        [SerializeField] private bool lookTarget;
        [Header("Combo Chain Window")]
        [Tooltip("Earliest point in this animation that accepts the next Attack press.")]
        [SerializeField, Range(0f, 1f)] private float chainInputStartNormalized = 0.45f;
        [Tooltip("When buffered Combo advances to the next animation.")]
        [SerializeField, Range(0f, 1f)] private float chainTransitionNormalized = 0.85f;
        [Tooltip("Latest point in this animation that accepts the next Attack press.")]
        [SerializeField, Range(0f, 1f)] private float chainInputEndNormalized = 1f;
        [Header("Movement Steps (local X/Z, multiple entries)")]
        [SerializeField] private CombatMovementStep[] movementSteps =
            Array.Empty<CombatMovementStep>();
        // Keep old serialized fields for existing Form assets. These are used
        // only when Movement Steps has no authored entries.
        [HideInInspector, SerializeField, Min(0f)] private float forwardStepDistance;
        [HideInInspector, SerializeField, Range(0f, 1f)] private float stepStartNormalized;
        [HideInInspector, SerializeField, Range(0f, 1f)] private float stepEndNormalized = 0.5f;
        [Header("Timing (optional phase override)")]
        [SerializeField] private bool usePhaseDurations;
        [SerializeField, Min(0f)] private float startupSeconds;
        [SerializeField, Min(0f)] private float activeSeconds = 0.1f;
        [SerializeField, Min(0f)] private float recoverySeconds = 0.7666667f;
        [Header("Combat")]
        [SerializeField] private HitReactionType hitReaction = HitReactionType.Body;
        [Tooltip("Used only for Stun: lock victim and keep Animator Stunt=true for this many CombatTime seconds.")]
        [SerializeField, Min(0.05f)] private float stunDurationSeconds = 2f;
        [Tooltip("Metres pushed AWAY from the attacker when this hit lands. 0 disables knockback.")]
        [SerializeField, Min(0f)] private float hitKnockbackDistance = 0f;
        [Tooltip("Scaled CombatTime seconds over which the hit knockback is applied.")]
        [SerializeField, Min(0.05f)] private float hitKnockbackSeconds = 0.25f;
        [SerializeField, Min(0)] private int energyCost;
        [Header("Energy Gain On Successful Hit (per Combo Stage)")]
        [Tooltip("Energy awarded to the attacker for each enemy that actually loses HP. " +
            "Set 0 to disable. Examples: Light=2, Medium=5, Heavy=10.")]
        [SerializeField, Min(0)] private int energyGainOnHit = 5;
        [SerializeField, Min(0f)] private float cooldownSeconds;
        [SerializeField] private bool useCustomDamage;
        [SerializeField, Min(0)] private int baseDamage = 10;
        [SerializeField, Min(0.1f)] private float hitRange = 2.75f;
        [Header("Melee Hit Area")]
        [SerializeField] private ComboHitAreaMode hitAreaMode = ComboHitAreaMode.FrontArc;
        [Tooltip("Half-width of the frontal hit cone. Total angle = 2 x Half Angle. Ignored in 360 mode.")]
        [SerializeField, Range(1f, 180f)] private float hitHalfAngle = 75f;
        [SerializeField] private CombatProjectileSettings projectile = new CombatProjectileSettings();
        [Tooltip("Optional burst. A nonempty array overrides legacy Projectile.")]
        [SerializeField] private CombatProjectileSettings[] projectiles =
            Array.Empty<CombatProjectileSettings>();
        [Header("Combat VFX Timeline (multiple entries)")]
        [SerializeField] private CombatVfxCue[] vfxTimeline = Array.Empty<CombatVfxCue>();
        [Header("Body Part Visibility Timeline")]
        [SerializeField] private BodyPartVisibilityCue[] bodyPartTimeline =
            Array.Empty<BodyPartVisibilityCue>();
        [Header("Combat Audio Timeline (multiple clips)")]
        [SerializeField] private CombatAudioCue[] audioTimeline =
            Array.Empty<CombatAudioCue>();

        public HitReactionType HitReaction => hitReaction;
        public float StunDurationSeconds => Mathf.Max(0.05f, stunDurationSeconds);
        public float HitKnockbackDistance => Mathf.Max(0f, hitKnockbackDistance);
        public float HitKnockbackSeconds => Mathf.Max(0.05f, hitKnockbackSeconds);
        public bool LookTarget => lookTarget;
        public float ChainInputStartNormalized => Mathf.Clamp01(chainInputStartNormalized);
        public float ChainTransitionNormalized =>
            Mathf.Clamp(chainTransitionNormalized, ChainInputStartNormalized, 1f);
        public float ChainInputEndNormalized =>
            Mathf.Clamp(chainInputEndNormalized, ChainInputStartNormalized, 1f);
        public CombatAudioCue[] AudioTimeline => audioTimeline;
        public CombatVfxCue[] VfxTimeline => vfxTimeline;
        public BodyPartVisibilityCue[] BodyPartTimeline => bodyPartTimeline;
        public float DurationSeconds => usePhaseDurations
            ? Mathf.Max(0.1f, StartupSeconds + ActiveSeconds + RecoverySeconds)
            : Mathf.Max(0.1f, durationSeconds);
        public float StartupSeconds => usePhaseDurations ? Mathf.Max(0f, startupSeconds) : 0f;
        public float ActiveSeconds => usePhaseDurations ? Mathf.Max(0f, activeSeconds) : DurationSeconds;
        public float RecoverySeconds => usePhaseDurations ? Mathf.Max(0f, recoverySeconds) : 0f;
        public int EnergyCost => Mathf.Max(0, energyCost);
        public int EnergyGainOnHit => Mathf.Max(0, energyGainOnHit);
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public bool UseCustomDamage => useCustomDamage;
        public int BaseDamage => Mathf.Max(0, baseDamage);
        public float HitRange => Mathf.Max(0.1f, hitRange);
        public ComboHitAreaMode HitAreaMode => hitAreaMode;
        public float HitHalfAngle => Mathf.Clamp(hitHalfAngle, 1f, 180f);
        public CombatProjectileSettings Projectile => projectile;
        public CombatProjectileSettings[] Projectiles => projectiles;
        public bool HasEnabledProjectiles =>
            CombatProjectileSettings.HasEnabledEntries(projectiles, projectile);
        public CombatProjectileSettings GetProjectile(int index) =>
            index >= 0 ? (projectiles != null && index < projectiles.Length
                ? projectiles[index] : null) : projectile;
        public CombatMovementStep[] MovementSteps => movementSteps;
        public float ForwardStepDistance => Mathf.Max(0f, forwardStepDistance);
        public float StepStartNormalized => Mathf.Clamp01(stepStartNormalized);
        public float StepEndNormalized => Mathf.Max(StepStartNormalized,
            Mathf.Clamp01(stepEndNormalized));
    }

    [CreateAssetMenu(fileName = "FormDefinition", menuName = "MuOnBaAr/Definitions/Form")]
    public sealed class FormDefinition : ScriptableObject
    {
        [SerializeField] private string formId = "form";
        [Header("Level Unlock")]
        [Tooltip("Minimum player level to select this Form.")]
        [SerializeField, Min(1)] private int requiredLevel = 1;
        [SerializeField] private string displayName = "Form";
        [SerializeField, Min(0.1f)] private float movementMultiplier = 1f;
        [SerializeField, Min(0.1f)] private float damageMultiplier = 1f;
        [Header("Jump / Flight (Per Form)")]
        [Tooltip("Height of ordinary jump in metres (0 disables jumping).")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.5f;
        [Tooltip("Tap Space to jump; hold Space to fly after Fly Hold Seconds. Release to hover, Left Alt to descend.")]
        [SerializeField] private bool canFly;
        [Tooltip("Flying climb and descent speed, metres per CombatTime second.")]
        [SerializeField, Min(0.1f)] private float flySpeed = 4f;
        [Tooltip("Horizontal flight acceleration in metres per second squared. Applies to WASD movement only during powered flight; does not change vertical climb or descent.")]
        [SerializeField, Min(0.1f)] private float flyHorizontalAcceleration = 12f;
        [Tooltip("Horizontal flight deceleration in metres per second squared when WASD is released, reversed, or speed is reduced. Vertical movement remains unchanged.")]
        [SerializeField, Min(0.1f)] private float flyHorizontalDeceleration = 8f;
        [Tooltip("Absolute world-space Y ceiling for powered flight, measured from Unity Y=0 (not from takeoff ground). 10 means maximum flight Y=10 regardless of launch point.")]
        [SerializeField, Min(0f)] private float maxFlyHeight = 10f;
        [Tooltip("Hold Space for this many scaled CombatTime seconds to switch from normal jump to flight.")]
        [SerializeField, Min(0.05f)] private float flyHoldSeconds = 0.35f;
        [Tooltip("Energy points consumed per scaled CombatTime second while flying, including hovering and descending.")]
        [SerializeField, Min(0.1f)] private float flyEnergyPerSecond = 5f;
        [SerializeField, Min(0)] private int changeEnergyCost = 0;
        [SerializeField, Min(0f)] private float changeLockSeconds = 1f;
        [Header("Form Change Animation Timing (Per Form)")]
        [Tooltip("Duration of FormChangeStart when transforming OUT of this Form." +
            " Scaled CombatTime seconds. Default matches ACL00 clip.")]
        [SerializeField, Min(0.05f)] private float formChangeStartSeconds = 1.216667f;
        [Tooltip("Duration of FormChangeEnd when transforming INTO this Form." +
            " Scaled CombatTime seconds. Default matches ACL00 clip.")]
        [SerializeField, Min(0.05f)] private float formChangeEndSeconds = 2.483333f;
        [Header("Form Change VFX Timeline (Per Form)")]
        [Tooltip("OUTGOING Form: local cosmetic VFX during FormChangeStart. " +
            "Start Seconds are relative to the beginning of the Start animation.")]
        [SerializeField] private CombatVfxCue[] formChangeStartVfxTimeline =
            Array.Empty<CombatVfxCue>();
        [Tooltip("INCOMING Form: local cosmetic VFX during FormChangeEnd, " +
            "after the new Form visual is spawned. Time is relative to End.")]
        [SerializeField] private CombatVfxCue[] formChangeEndVfxTimeline =
            Array.Empty<CombatVfxCue>();
        [Header("Form Change Body Part Visibility (Per Form)")]
        [Tooltip("OUTGOING Form: timed mesh hide/show events during FormChangeStart." +
            " TimeSeconds is measured from Start animation beginning.")]
        [SerializeField] private BodyPartVisibilityCue[] formChangeStartBodyPartTimeline =
            Array.Empty<BodyPartVisibilityCue>();
        [Tooltip("INCOMING Form: timed mesh hide/show events during FormChangeEnd." +
            " TimeSeconds is measured from End animation beginning.")]
        [SerializeField] private BodyPartVisibilityCue[] formChangeEndBodyPartTimeline =
            Array.Empty<BodyPartVisibilityCue>();
        [SerializeField] private bool restrictSourceForms;
        [SerializeField] private FormDefinition[] acceptedSourceForms = Array.Empty<FormDefinition>();
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private AbilityDefinition[] abilities = Array.Empty<AbilityDefinition>();
        [Header("Basic Attack Combo (per Form)")]
        [SerializeField] private FormComboAttack[] comboAttacks = Array.Empty<FormComboAttack>();
        [Header("Body Parts (collected from Visual Prefab)")]
        [Tooltip("Use Collect Body Parts From Prefab in this asset's Inspector " +
            "to populate mesh GameObjects. Indexes are used by Combo/Ability timelines.")]
        [SerializeField] private FormBodyPart[] collectedBodyParts =
            Array.Empty<FormBodyPart>();

        public string FormId => formId;
        public int RequiredLevel => Mathf.Max(1, requiredLevel);
        public string DisplayName => displayName;
        public float MovementMultiplier => movementMultiplier;
        public float DamageMultiplier => damageMultiplier;
        public float JumpHeight => Mathf.Max(0f, jumpHeight);
        public bool CanFly => canFly;
        public float FlySpeed => Mathf.Max(0.1f, flySpeed);
        public float FlyHorizontalAcceleration => Mathf.Max(0.1f, flyHorizontalAcceleration);
        public float FlyHorizontalDeceleration => Mathf.Max(0.1f, flyHorizontalDeceleration);
        public float MaxFlyHeight => Mathf.Max(0f, maxFlyHeight);
        public float FlyHoldSeconds => Mathf.Max(0.05f, flyHoldSeconds);
        public float FlyEnergyPerSecond => Mathf.Max(0.1f, flyEnergyPerSecond);
        public int ChangeEnergyCost => Mathf.Max(0, changeEnergyCost);
        public float ChangeLockSeconds => Mathf.Max(0f, changeLockSeconds);
        public float FormChangeStartSeconds => Mathf.Max(0.05f, formChangeStartSeconds);
        public float FormChangeEndSeconds => Mathf.Max(0.05f, formChangeEndSeconds);
        public CombatVfxCue[] FormChangeStartVfxTimeline =>
            formChangeStartVfxTimeline;
        public CombatVfxCue[] FormChangeEndVfxTimeline =>
            formChangeEndVfxTimeline;
        public BodyPartVisibilityCue[] FormChangeStartBodyPartTimeline =>
            formChangeStartBodyPartTimeline;
        public BodyPartVisibilityCue[] FormChangeEndBodyPartTimeline =>
            formChangeEndBodyPartTimeline;
        public GameObject VisualPrefab => visualPrefab;
        public RuntimeAnimatorController AnimatorController => animatorController;
        public FormBodyPart[] CollectedBodyParts => collectedBodyParts;
        public AbilityDefinition[] Abilities => abilities;
        public int ComboAttackCount => comboAttacks == null ? 0 : comboAttacks.Length;
        public bool TryGetComboAttack(int index, out FormComboAttack combo)
        {
            combo = null;
            if (comboAttacks == null || index < 0 || index >= comboAttacks.Length)
                return false;
            combo = comboAttacks[index];
            return combo != null;
        }

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

        // Editor asset collection only. Runtime never changes prefab assets.
        // Reuse existing indexes when possible so authored timeline cues remain valid.
        [ContextMenu("Collect Body Parts From Prefab")]
        public void CollectBodyPartsFromPrefab()
        {
            if (visualPrefab == null)
            {
                Debug.LogWarning("[MuOnBaAr] Assign Visual Prefab before collecting body parts.", this);
                return;
            }
            var found = new System.Collections.Generic.List<FormBodyPart>();
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (Renderer renderer in visualPrefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;
                Transform t = renderer.transform;
                // Disabling the visual root would remove every bone/Animator.
                if (t == visualPrefab.transform) continue;
                var names = new System.Collections.Generic.List<string>();
                Transform parent = t;
                while (parent != null && parent != visualPrefab.transform)
                {
                    names.Insert(0, parent.name);
                    parent = parent.parent;
                }
                if (parent == null) continue;
                string path = string.Join("/", names.ToArray());
                if (!seen.Add(path)) continue;
                found.Add(new FormBodyPart(path, t.name, t.gameObject.activeSelf));
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Collect Form Body Parts");
#endif
            // Keep paths in their prior positions, and append newly found mesh
            // paths. This avoids silently changing timeline indices on refresh.
            var result = new System.Collections.Generic.List<FormBodyPart>();
            if (collectedBodyParts != null)
                foreach (FormBodyPart old in collectedBodyParts)
                {
                    if (old == null) continue;
                    FormBodyPart match = found.Find(x =>
                        x.HierarchyPath == old.HierarchyPath);
                    if (match == null) continue;
                    result.Add(match);
                    found.Remove(match);
                }
            result.AddRange(found);
            collectedBodyParts = result.ToArray();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log("[MuOnBaAr] Collected " + collectedBodyParts.Length +
                " body part mesh GameObjects from " + visualPrefab.name, this);
        }

        private void OnValidate()
        {
            movementMultiplier = Mathf.Max(0.1f, movementMultiplier);
            damageMultiplier = Mathf.Max(0.1f, damageMultiplier);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            flySpeed = Mathf.Max(0.1f, flySpeed);
            flyHorizontalAcceleration = Mathf.Max(0.1f, flyHorizontalAcceleration);
            flyHorizontalDeceleration = Mathf.Max(0.1f, flyHorizontalDeceleration);
            maxFlyHeight = Mathf.Max(0f, maxFlyHeight);
            flyHoldSeconds = Mathf.Max(0.05f, flyHoldSeconds);
            flyEnergyPerSecond = Mathf.Max(0.1f, flyEnergyPerSecond);
            changeEnergyCost = Mathf.Max(0, changeEnergyCost);
            changeLockSeconds = Mathf.Max(0f, changeLockSeconds);
            formChangeStartSeconds = Mathf.Max(0.05f, formChangeStartSeconds);
            formChangeEndSeconds = Mathf.Max(0.05f, formChangeEndSeconds);
            requiredLevel = Mathf.Max(1, requiredLevel);
        }
    }
}
