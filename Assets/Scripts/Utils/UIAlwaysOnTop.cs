using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using TMPro;

namespace UniverseSimAR.UI {
    public class UIAlwaysOnTop : MonoBehaviour {
        private static readonly int GuiZTestProp = Shader.PropertyToID("unity_GUIZTestMode");
        private static readonly int ZTestProp = Shader.PropertyToID("_ZTest");

        private int targetRenderQueue = 4000;

        private void Awake() {
            ApplyAlwaysOnTop();
        }

        private void OnEnable() {
            ApplyAlwaysOnTop();
        }

        public void ApplyAlwaysOnTop() {
            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);

            foreach (Graphic g in graphics) {
                if (g is TextMeshProUGUI tmp) {
                    if (tmp.fontSharedMaterial != null) {
                        Material mat = new Material(tmp.fontSharedMaterial);
                        mat.SetInt(GuiZTestProp, (int)CompareFunction.Always);
                        mat.SetInt(ZTestProp, (int)CompareFunction.Always);
                        mat.renderQueue = targetRenderQueue;
                        tmp.fontMaterial = mat;
                    }
                } else {
                    Material baseMat = g.material != null ? g.material : Canvas.GetDefaultCanvasMaterial();
                    Material mat = new Material(baseMat);

                    mat.SetInt(GuiZTestProp, (int)CompareFunction.Always);
                    mat.SetInt(ZTestProp, (int)CompareFunction.Always);
                    mat.renderQueue = targetRenderQueue;

                    g.material = mat;
                }
            }
        }
    }
}