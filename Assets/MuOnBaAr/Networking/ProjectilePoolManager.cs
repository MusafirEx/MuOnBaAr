using System.Collections.Generic;
using UnityEngine;
using MuOnBaAr.Runtime;

namespace MuOnBaAr.Networking
{
    // Visual-only pool. Each peer prewarms only its riders' current Form.
    // The HOST's NetworkRiderController controls projectile gameplay state.
    [DisallowMultipleComponent]
    public sealed class ProjectilePoolManager : MonoBehaviour
    {
        [SerializeField, Min(0)] private int warmCount = 2;
        [SerializeField, Min(1)] private int maxPerPrefab = 32;
        private readonly Dictionary<GameObject, List<GameObject>> items =
            new Dictionary<GameObject, List<GameObject>>();
        private readonly HashSet<GameObject> rented = new HashSet<GameObject>();
        private readonly Dictionary<NetworkRiderController, FormDefinition> forms =
            new Dictionary<NetworkRiderController, FormDefinition>();

        public static ProjectilePoolManager FindOrCreate()
        {
            ProjectilePoolManager result = FindObjectOfType<ProjectilePoolManager>();
            if (result != null) return result;
            GameObject go = GameObject.Find("PoolManager");
            if (go == null) go = new GameObject("PoolManager");
            return go.GetComponent<ProjectilePoolManager>() ??
                go.AddComponent<ProjectilePoolManager>();
        }

        private List<GameObject> Bucket(GameObject prefab)
        {
            if (!items.TryGetValue(prefab, out var list))
                items[prefab] = list = new List<GameObject>();
            return list;
        }

        private GameObject Make(GameObject prefab, List<GameObject> bucket)
        {
            GameObject go = Instantiate(prefab, transform);
            go.name = prefab.name + " (Pooled)";
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
            go.SetActive(false);
            bucket.Add(go);
            return go;
        }

        public void Prepare(NetworkRiderController rider, FormDefinition form)
        {
            if (rider == null) return;
            if (form == null) forms.Remove(rider);
            else forms[rider] = form;
            if (form == null)
            {
                PruneUnused();
                return;
            }
            if (form.Abilities != null)
                foreach (AbilityDefinition a in form.Abilities)
                    if (a != null)
                    {
                        Warm(a.Projectile);
                        WarmProjectiles(a.Projectiles);
                        WarmTimeline(a.VfxTimeline);
                        // Phase C: prewarm the actual Form-specific skill follow-ups.
                        if (a.HasComboFollowUps)
                            for (int i = 0; a.TryGetComboFollowUp(i, out var stage); ++i)
                            {
                                Warm(stage.Projectile);
                                WarmProjectiles(stage.Projectiles);
                                WarmTimeline(stage.VfxTimeline);
                            }
                        // A charge threshold may own multiple projectile
                        // prefabs and an independent release VFX timeline.
                        for (int i = 0; i < a.ChargeThresholdCount; ++i)
                        {
                            AbilityChargeThreshold threshold = a.GetChargeThreshold(i);
                            if (threshold == null) continue;
                            CombatProjectileSettings[] shots = threshold.Projectiles;
                            if (shots != null)
                                foreach (CombatProjectileSettings shot in shots)
                                    Warm(shot);
                            WarmTimeline(threshold.VfxTimeline);
                        }
                    }
            for (int i = 0; i < form.ComboAttackCount; ++i)
                if (form.TryGetComboAttack(i, out var combo))
                {
                    Warm(combo.Projectile);
                    WarmProjectiles(combo.Projectiles);
                    WarmTimeline(combo.VfxTimeline);
                }
            if (form.Abilities != null)
                foreach (AbilityDefinition a in form.Abilities)
                    if (a != null && a.ClockUpEnabled)
                    {
                        WarmVfx(a.ClockUpStartVfxPrefab);
                        WarmVfx(a.ClockUpActiveVfxPrefab);
                        WarmVfx(a.ClockUpEndVfxPrefab);
                    }
            // Changing Form must not keep old inactive prefab visuals alive.
            // Preserve rented visuals until their authoritative shots finish.
            PruneUnused();
        }

        private void WarmProjectiles(CombatProjectileSettings[] entries)
        {
            if (entries == null) return;
            foreach (CombatProjectileSettings entry in entries) Warm(entry);
        }

        private void Warm(CombatProjectileSettings config)
        {
            if (config == null || !config.Enabled || config.VisualPrefab == null)
                return;
            var list = Bucket(config.VisualPrefab);
            while (list.Count < Mathf.Min(warmCount, maxPerPrefab))
                Make(config.VisualPrefab, list);
        }

        private void WarmTimeline(CombatVfxCue[] cues)
        {
            if (cues == null) return;
            foreach (CombatVfxCue cue in cues)
                if (cue != null) WarmVfx(cue.Prefab);
        }

        private void WarmVfx(GameObject prefab)
        {
            if (prefab == null) return;
            var list = Bucket(prefab);
            // VFX is usually triggered once per rider: only one prewarmed
            // instance is needed; more instances are created on demand.
            if (list.Count == 0) Make(prefab, list);
        }

        public GameObject RentVfx(GameObject prefab, Transform follow, Vector3 offset,
            bool keepFollowing = true)
        {
            if (prefab == null || follow == null) return null;
            GameObject go = Rent(prefab, follow.position, follow.rotation, 1f);
            if (go == null) return null;
            go.transform.SetParent(follow, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.identity;
            if (!keepFollowing)
                go.transform.SetParent(transform, true);
            // Returning pooled ParticleSystems may retain their old particles.
            foreach (ParticleSystem particle in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.simulationSpeed = 1f; // Caster VFX follows real time.
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
            return go;
        }

        public GameObject Rent(GameObject prefab, Vector3 pos, Quaternion rot, float scale)
        {
            if (prefab == null) return null;
            var bucket = Bucket(prefab);
            GameObject go = null;
            foreach (GameObject item in bucket)
                if (item != null && !rented.Contains(item)) { go = item; break; }
            if (go == null && bucket.Count < maxPerPrefab)
                go = Make(prefab, bucket);
            if (go == null)
            {
                Debug.LogWarning("[MuOnBaAr][Pool] Capacity reached: " + prefab.name);
                return null;
            }
            go.transform.SetPositionAndRotation(pos, rot);
            rented.Add(go);
            var visual = go.GetComponent<ProjectileSampleVisual>();
            if (visual != null) visual.SetPlaybackScale(scale);
            go.SetActive(true);
            return go;
        }

        public void UpdatePlayback(GameObject go, float scale)
        {
            if (go == null) return;
            var visual = go.GetComponent<ProjectileSampleVisual>();
            if (visual != null) visual.SetPlaybackScale(scale);
        }

        public void Release(GameObject go)
        {
            if (go == null) return;
            rented.Remove(go);
            foreach (ParticleSystem particle in go.GetComponentsInChildren<ParticleSystem>(true))
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            // Returning a visual from a previous Form must free it rather
            // than retain an unnecessary pool entry forever.
            PruneUnused();
        }

        public void Unregister(NetworkRiderController rider)
        {
            if (rider == null) return;
            forms.Remove(rider);
            PruneUnused();
        }

        // Do not prune rented visuals: a projectile from an earlier Form may
        // still be flying. Delete only inactive instances whose prefab is no
        // longer referenced by ANY live rider's selected Form.
        private void PruneUnused()
        {
            List<GameObject> emptyPrefabs = new List<GameObject>();
            foreach (var entry in items)
            {
                if (UsedByCurrentForm(entry.Key)) continue;
                List<GameObject> bucket = entry.Value;
                for (int i = bucket.Count - 1; i >= 0; --i)
                {
                    GameObject go = bucket[i];
                    if (go != null && rented.Contains(go)) continue;
                    bucket.RemoveAt(i);
                    if (go != null) Destroy(go);
                }
                if (bucket.Count == 0) emptyPrefabs.Add(entry.Key);
            }
            foreach (GameObject prefab in emptyPrefabs)
                items.Remove(prefab);
        }

        private static bool TimelineUsesPrefab(CombatVfxCue[] timeline, GameObject prefab)
        {
            if (timeline == null) return false;
            foreach (CombatVfxCue cue in timeline)
                if (cue != null && cue.Prefab == prefab) return true;
            return false;
        }

        private static bool ChargeUsesPrefab(AbilityDefinition ability, GameObject prefab)
        {
            if (ability == null) return false;
            for (int i = 0; i < ability.ChargeThresholdCount; ++i)
            {
                AbilityChargeThreshold threshold = ability.GetChargeThreshold(i);
                if (threshold == null) continue;
                if (TimelineUsesPrefab(threshold.VfxTimeline, prefab)) return true;
                CombatProjectileSettings[] shots = threshold.Projectiles;
                if (shots == null) continue;
                foreach (CombatProjectileSettings shot in shots)
                    if (shot != null && shot.Enabled && shot.VisualPrefab == prefab)
                        return true;
            }
            return false;
        }

        private static bool UsesProjectile(CombatProjectileSettings legacy,
            CombatProjectileSettings[] entries, GameObject prefab)
        {
            if (entries != null && entries.Length > 0)
            {
                foreach (CombatProjectileSettings entry in entries)
                    if (entry != null && entry.Enabled && entry.VisualPrefab == prefab)
                        return true;
                return false;
            }
            return legacy != null && legacy.Enabled && legacy.VisualPrefab == prefab;
        }

        private static bool SkillComboUsesPrefab(AbilityDefinition ability, GameObject prefab)
        {
            if (ability == null || !ability.HasComboFollowUps) return false;
            for (int i = 0; ability.TryGetComboFollowUp(i, out var followUp); ++i)
                if (UsesProjectile(followUp.Projectile, followUp.Projectiles, prefab) ||
                    TimelineUsesPrefab(followUp.VfxTimeline, prefab))
                    return true;
            return false;
        }

        private bool UsedByCurrentForm(GameObject prefab)
        {
            foreach (FormDefinition form in forms.Values)
            {
                if (form == null) continue;
                if (form.Abilities != null)
                    foreach (AbilityDefinition a in form.Abilities)
                        if (a != null && UsesProjectile(a.Projectile, a.Projectiles, prefab))
                            return true;
                if (form.Abilities != null)
                    foreach (AbilityDefinition a in form.Abilities)
                        if (a != null &&
                            (TimelineUsesPrefab(a.VfxTimeline, prefab) ||
                             ChargeUsesPrefab(a, prefab) ||
                             SkillComboUsesPrefab(a, prefab)))
                            return true;
                if (form.Abilities != null)
                    foreach (AbilityDefinition ability in form.Abilities)
                        if (ability != null && ability.ClockUpEnabled &&
                            (ability.ClockUpStartVfxPrefab == prefab ||
                             ability.ClockUpActiveVfxPrefab == prefab ||
                             ability.ClockUpEndVfxPrefab == prefab))
                            return true;
                for (int i = 0; i < form.ComboAttackCount; ++i)
                    if (form.TryGetComboAttack(i, out var combo) &&
                        (UsesProjectile(combo.Projectile, combo.Projectiles, prefab) ||
                          TimelineUsesPrefab(combo.VfxTimeline, prefab))) return true;
            }
            return false;
        }
    }
}
