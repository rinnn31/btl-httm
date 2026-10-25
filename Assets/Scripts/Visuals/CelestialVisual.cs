using UnityEngine;
using UniverseSimAR.Core;

namespace UniverseSimAR.Visuals {
    [RequireComponent(typeof(Celestial))]
    public class CelestialVisuals : MonoBehaviour {
        [Header("References")]
        public Renderer targetRenderer;
        public TrailRenderer trailRenderer;

        private Celestial bodyData;
        private Material materialInstance;
        private float baseRadius;

        // Wobble state
        private float wobbleAmp = 0f;
        private float wobbleT = 0f;

        private void Awake() {
            bodyData = GetComponent<Celestial>();
            baseRadius = bodyData.radius;

            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
            if (trailRenderer == null) trailRenderer = GetComponentInChildren<TrailRenderer>();

            if (targetRenderer != null) {
                materialInstance = targetRenderer.material;
            }
        }

        private void Update() {
            UpdateWobbleAndScale();
            UpdateTrail();
        }

        private void UpdateWobbleAndScale() {
            float s = bodyData.radius / baseRadius;

            if (wobbleAmp > 0.002f) {
                wobbleT += Time.deltaTime;
                wobbleAmp *= Mathf.Exp(-3f * Time.deltaTime);

                float w = wobbleAmp * Mathf.Sin(wobbleT * 18f);

                transform.localScale = new Vector3(
                    s * (1f + w),
                    s * (1f - w),
                    s * (1f + w * 0.3f)
                ) * (baseRadius * 2f);
            } else {
                wobbleAmp = 0f;
                transform.localScale = Vector3.one * (bodyData.radius * 2f);
            }
        }

        private void UpdateTrail() {
            if (trailRenderer == null) return;

            bool isDragged = (bodyData.velocity.sqrMagnitude < 0.001f);
            trailRenderer.emitting = !isDragged;
        }

        public void TriggerImpact(float intensity) {
            wobbleAmp = intensity;
            wobbleT = 0f;
        }

        private void OnDestroy() {
            if (materialInstance != null) Destroy(materialInstance);
        }
    }
}