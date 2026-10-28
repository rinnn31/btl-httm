using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UniverseSimAR.Visuals {
    public class SplitScreen : MonoBehaviour {
        public RawImage leftEye;
        public RawImage rightEye;

        [Header("Chỉnh cho khớp kính")]
        public float ipdMm = 63f;
        [Range(0.2f, 0.5f)] public float eyeWidth = 0.40f;
        [Range(1f, 2f)] public float zoom = 1f;
        [Range(0.3f, 1f)] public float resolutionScale = 0.8f;

        RenderTexture rt;
        Camera arCamera;

        IEnumerator Start() {
            arCamera = Camera.main;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            yield return null;
            yield return null;

            float dpi = Screen.dpi > 0 ? Screen.dpi : 400f;
            float screenWidthMm = Screen.width / dpi * 25.4f;
            float offset = (ipdMm * 0.5f) / screenWidthMm;
            float ew = Mathf.Min(eyeWidth, offset * 2f); // không cho 2 ô chồng nhau

            // RT luôn cùng tỉ lệ với màn hình
            int w = Mathf.RoundToInt(Screen.width * resolutionScale);
            int h = Mathf.RoundToInt(Screen.height * resolutionScale);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            arCamera.targetTexture = rt;

            SetEye(leftEye, 0.5f - offset, ew);
            SetEye(rightEye, 0.5f + offset, ew);
        }

        void SetEye(RawImage img, float centerX, float ew) {
            img.texture = rt;
            img.uvRect = new Rect(0, 0, 1, 1);   // không cắt: thấy trọn khung hình
            img.raycastTarget = false;           // không chặn raycast UI
            var r = img.rectTransform;
            r.anchorMin = new Vector2(centerX - ew * 0.5f, 0f);
            r.anchorMax = new Vector2(centerX + ew * 0.5f, 1f);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        void OnDestroy() {
            if (rt != null) { arCamera.targetTexture = null; rt.Release(); }
        }
    }
}