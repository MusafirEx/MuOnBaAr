using System.Collections.Generic;
using MuOnBaAr.Runtime;
using UnityEngine;

namespace MuOnBaAr.Networking
{
    // Reusable local-only AudioSource pool. No NetworkObjects and no Time.timeScale.
    [DisallowMultipleComponent]
    public sealed class CombatAudioPoolManager : MonoBehaviour
    {
        private readonly List<AudioSource> free = new List<AudioSource>();
        private readonly Dictionary<AudioSource, float> basePitch =
            new Dictionary<AudioSource, float>();
        private readonly HashSet<AudioSource> scaled = new HashSet<AudioSource>();
        private readonly HashSet<AudioSource> paused = new HashSet<AudioSource>();
        private readonly HashSet<AudioSource> started = new HashSet<AudioSource>();

        public static CombatAudioPoolManager FindOrCreate()
        {
            CombatAudioPoolManager pool = FindObjectOfType<CombatAudioPoolManager>();
            if (pool != null) return pool;
            GameObject go = GameObject.Find("PoolManager");
            if (go == null) go = new GameObject("PoolManager");
            return go.GetComponent<CombatAudioPoolManager>() ??
                go.AddComponent<CombatAudioPoolManager>();
        }

        public AudioSource Rent(CombatAudioCue cue, Transform anchor, float rate)
        {
            if (cue == null || cue.Clip == null || anchor == null) return null;
            AudioSource src;
            if (free.Count > 0)
            {
                int last = free.Count - 1;
                src = free[last];
                free.RemoveAt(last);
            }
            else
            {
                GameObject go = new GameObject("Combat Audio (Pooled)");
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
            }
            src.gameObject.SetActive(true);
            src.transform.SetParent(anchor, false);
            src.transform.localPosition = cue.LocalOffset;
            src.transform.localRotation = Quaternion.identity;
            if (!cue.FollowSocket) src.transform.SetParent(transform, true);
            src.playOnAwake = false;
            src.loop = false;
            src.clip = cue.Clip;
            src.volume = cue.Volume;
            src.spatialBlend = cue.SpatialBlend;
            src.dopplerLevel = 0f;
            basePitch[src] = cue.Pitch;
            if (cue.ScalePitchWithCombatTime) scaled.Add(src);
            else scaled.Remove(src);
            started.Remove(src);
            paused.Remove(src);
            src.pitch = Mathf.Clamp(cue.Pitch *
                (cue.ScalePitchWithCombatTime ? rate : 1f), 0.01f, 3f);
            if (cue.ScalePitchWithCombatTime && rate <= 0.0001f)
            {
                paused.Add(src);
                return src;
            }
            src.Play();
            started.Add(src);
            return src;
        }

        public bool UpdateSource(AudioSource src, float rate)
        {
            if (src == null || !basePitch.ContainsKey(src)) return false;
            bool useScale = scaled.Contains(src);
            if (useScale && rate <= 0.0001f)
            {
                if (!paused.Contains(src))
                {
                    src.Pause();
                    paused.Add(src);
                }
                return true;
            }
            src.pitch = Mathf.Clamp(basePitch[src] *
                (useScale ? rate : 1f), 0.01f, 3f);
            if (paused.Remove(src))
            {
                if (started.Contains(src)) src.UnPause();
                else { src.Play(); started.Add(src); }
                return true;
            }
            if (!started.Contains(src))
            {
                src.Play();
                started.Add(src);
                return true;
            }
            if (!src.isPlaying)
            {
                Release(src);
                return false;
            }
            return true;
        }

        public void Release(AudioSource src)
        {
            if (src == null || free.Contains(src)) return;
            src.Stop();
            src.clip = null;
            basePitch.Remove(src);
            scaled.Remove(src);
            paused.Remove(src);
            started.Remove(src);
            src.transform.SetParent(transform, false);
            src.gameObject.SetActive(false);
            free.Add(src);
        }
    }
}
