using UnityEngine;

namespace MuOnBaAr.Runtime
{
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
        [SerializeField] private string animatorTrigger;

        public string AbilityId => abilityId;
        public int EnergyCost => Mathf.Max(0, energyCost);
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public float StartupSeconds => Mathf.Max(0f, startupSeconds);
        public float ActiveSeconds => Mathf.Max(0f, activeSeconds);
        public float RecoverySeconds => Mathf.Max(0f, recoverySeconds);
        public int BaseDamage => Mathf.Max(0, baseDamage);
        public string AnimatorTrigger => animatorTrigger;
    }
}
