using UnityEngine;
using System.Collections.Generic;

namespace UniverseSimAR.Visuals {
    [RequireComponent(typeof(MeshFilter))]
    public class DebrisMeshModifier : MonoBehaviour {
        private void Start() {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Mesh mesh = Instantiate(mf.sharedMesh);
            Vector3[] vertices = mesh.vertices;

            float radius = transform.localScale.x / 2f;
            float jitter = radius * 0.25f;

            Dictionary<string, Vector3> offsetMap = new Dictionary<string, Vector3>();

            for (int i = 0; i < vertices.Length; i++) {
                Vector3 v = vertices[i];
                string key = $"{v.x:F3}_{v.y:F3}_{v.z:F3}";

                if (!offsetMap.TryGetValue(key, out Vector3 offset)) {
                    offset = new Vector3(
                        Random.Range(-0.5f, 0.5f) * jitter,
                        Random.Range(-0.5f, 0.5f) * jitter,
                        Random.Range(-0.5f, 0.5f) * jitter
                    );
                    offsetMap[key] = offset;
                }
                vertices[i] = v + offset;
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            transform.localScale = new Vector3(
                Random.Range(0.6f, 1.4f),
                Random.Range(0.3f, 0.8f),
                Random.Range(0.5f, 1.5f)
            );

            transform.rotation = Random.rotation;
        }
    }
}