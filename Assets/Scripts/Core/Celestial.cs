using UnityEngine;
using UniverseSimAR.Visuals;

namespace UniverseSimAR.Core {
    public enum CelestialType { Star, Planet, Debris }

    public class Celestial : MonoBehaviour {
        [Header("Identities")]
        public string id;
        public string celestialName;
        public CelestialType bodyType;

        [Header("Physics Stats")]
        public float mass = 10f;
        public float radius = 20f;
        public Vector3 velocity;

        [HideInInspector]
        public bool isDead = false;
        public bool isPending = false;

        public float Volume => (4f / 3f) * Mathf.PI * Mathf.Pow(radius, 3);

        private void Awake() {
            if (string.IsNullOrEmpty(id))
                id = System.Guid.NewGuid().ToString();
        }

        private void OnDisable() {
            if (UniverseSystem.Instance != null)
                UniverseSystem.Instance.UnregisterBody(this);
        }

        public void ApplyForce(Vector3 force, float dt) {
            velocity += (force / mass) * dt;
        }

        public void UpdatePosition(float dt) {
            transform.position += velocity * dt;
        }

#if UNITY_EDITOR
        private void OnValidate() {
            if (Application.isPlaying) return;

            transform.localScale = Vector3.one * (radius * 2f);
        }
#endif
    }
}