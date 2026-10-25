using UnityEngine;
using UniverseSimAR.Core;

namespace UniverseSimAR.Visuals {
    public class VFXManager : MonoBehaviour {
        [Header("Global Lighting")]
        public Light flashLight;
        private float flashPeak = 0f;
        private float flashT = 0f;
        private float flashDur = 1f;
        private const float FLASH_LIGHT_GAIN = 20f;

        [Header("Audio")]
        public AudioSource explosionAudioSource;
        public AudioClip explosionClip;

        [Header("Particle Prefabs")]
        public GameObject mergeVFXPrefab;
        public GameObject partialVFXPrefab;
        public GameObject hitAndRunVFXPrefab;
        public GameObject catastrophicVFXPrefab;
        public GameObject absorbVFXPrefab;

        public float effectScale = 1f;

        private void Start() {
            if (UniverseSystem.Instance != null) {
                UniverseSystem.Instance.OnCollisionOccurred.AddListener(HandleCollision);
            }
            if (flashLight != null) flashLight.intensity = 0f;
        }

        private void Update() {
            // Xử lý làm mờ đèn chớp theo thời gian giống hàm updateEffects
            if (flashPeak > 0 && flashLight != null) {
                flashT += Time.deltaTime;
                float u = flashT / flashDur;

                if (u >= 1f) {
                    flashLight.intensity = 0f;
                    flashPeak = 0f;
                } else {
                    flashLight.intensity = flashPeak * (1 - u) * (1 - u);
                }
            }
        }

        private void HandleCollision(CollisionEvent e) {
            float S = e.impactScale * effectScale;
            Vector3 P = e.point;

            switch (e.type) {
                case CollisionType.Merge: FxMerge(e, P, S); break;
                case CollisionType.PartialAccretion: FxPartial(e, P, S); break;
                case CollisionType.HitAndRun: FxHitAndRun(e, P, S); break;
                case CollisionType.Catastrophic: FxCatastrophic(e, P, S); break;
                case CollisionType.Absorb: FxAbsorb(e, P, S); break;
            }
        }

        private void FxMerge(CollisionEvent e, Vector3 P, float S) {
            PulseLight(P, S, new Color(1f, 0.69f, 0.37f), 0.9f, 0.5f); // 0xffb060

            SpawnVFX(mergeVFXPrefab, P, Quaternion.identity, S, 5f);

            if (e.survivor != null) {
                var visual = e.survivor.GetComponent<CelestialVisuals>();
                if (visual != null) visual.TriggerImpact(0.16f);
            }
            PlayExplosionSound(0.5f);
        }

        private void FxPartial(CollisionEvent e, Vector3 P, float S) {
            PulseLight(P, S, new Color(1f, 0.69f, 0.43f), 1.2f, 0.9f); // 0xffb070

            SpawnVFX(partialVFXPrefab, P, Quaternion.LookRotation(e.normal), S, 9f);

            if (e.survivor != null) {
                var visual = e.survivor.GetComponent<CelestialVisuals>();
                if (visual != null) visual.TriggerImpact(0.06f);
            }
            PlayExplosionSound(0.2f);
        }

        private void FxHitAndRun(CollisionEvent e, Vector3 P, float S) {
            PulseLight(P, S, new Color(1f, 0.65f, 0.37f), 0.6f, 0.5f); // 0xffa860

            SpawnVFX(hitAndRunVFXPrefab, P, Quaternion.LookRotation(e.travelDirection), S, 5f);

            if (e.big != null) {
                var visualB = e.big.GetComponent<CelestialVisuals>();
                if (visualB != null) visualB.TriggerImpact(0f);
            }
            if (e.small != null) {
                var visualS = e.small.GetComponent<CelestialVisuals>();
                if (visualS != null) visualS.TriggerImpact(0.05f);
            }
            PlayExplosionSound(0.1f);
        }

        private void FxCatastrophic(CollisionEvent e, Vector3 P, float S) {
            PulseLight(P, S, new Color(1f, 0.75f, 0.5f), 1.8f, 1.6f); // 0xffc080

            // Tinh vân bụi sinh ra cần tồn tại lâu (khoảng 9-10s)
            SpawnVFX(catastrophicVFXPrefab, P, Quaternion.identity, S, 10f);
            PlayExplosionSound(0.8f);
        }

        private void FxAbsorb(CollisionEvent e, Vector3 P, float S) {
            float s = Mathf.Max(e.small.radius * 3f, S * 0.4f);
            SpawnVFX(absorbVFXPrefab, P, Quaternion.identity, s, 2f);

            if (e.survivor != null) {
                var visual = e.survivor.GetComponent<CelestialVisuals>();
                if (visual != null) visual.TriggerImpact(0f);
            }
        }

        private void SpawnVFX(GameObject prefab, Vector3 pos, Quaternion rot, float scale, float destroyDelay) {
            if (prefab == null) return;
            var fx = Instantiate(prefab, pos, rot);
            fx.transform.localScale = Vector3.one * scale;
            Destroy(fx.gameObject, destroyDelay);
        }

        private void PulseLight(Vector3 P, float S, Color color, float duration, float strength) {
            if (flashLight == null) return;

            float peak = FLASH_LIGHT_GAIN * S * S * strength;
            if (peak <= flashLight.intensity) return;

            flashLight.transform.position = P;
            flashLight.color = color;
            flashLight.intensity = peak;
            flashPeak = peak;
            flashT = 0f;
            flashDur = duration;
        }

        private void PlayExplosionSound(float volume) {
            if (explosionAudioSource != null && explosionClip != null) {
                explosionAudioSource.PlayOneShot(explosionClip, volume);
            }
        }
    }
}