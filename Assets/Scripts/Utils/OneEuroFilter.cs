using UnityEngine;

namespace UniverseSimAR.Utils {
    public class OneEuroFilter {
        private float? x = null;
        private float dx = 0f;
        private float? lastTimestamp = null;

        private readonly float minCutoff;
        private readonly float beta;
        private readonly float derivativeCutoff;

        public OneEuroFilter(float minCutoff = 1.5f, float beta = 0.04f, float derivativeCutoff = 1.0f) {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.derivativeCutoff = derivativeCutoff;
        }

        public void Reset(float? value = null) {
            x = value;
            dx = 0f;
            lastTimestamp = null;
        }

        public float Filter(float value, float timestampMs) {
            if (x == null) {
                x = value;
                lastTimestamp = timestampMs;
                return value;
            }

            float dt = 1f / 60f;
            if (lastTimestamp != null) {
                dt = (timestampMs - lastTimestamp.Value) / 1000f;
                // Giới hạn dt để tránh nội suy sai khi FPS rớt
                if (float.IsNaN(dt) || dt <= 0f || dt > 0.2f) {
                    dt = 1f / 60f;
                }
            }

            lastTimestamp = timestampMs;

            float rawDx = (value - x.Value) / dt;
            float derivativeAlpha = Alpha(derivativeCutoff, dt);
            dx = dx + derivativeAlpha * (rawDx - dx);

            float cutoff = minCutoff + beta * Mathf.Abs(dx);
            float alpha = Alpha(cutoff, dt);
            x = x.Value + alpha * (value - x.Value);

            return x.Value;
        }

        private float Alpha(float cutoff, float dt) {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }
    }
}