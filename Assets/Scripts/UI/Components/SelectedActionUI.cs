using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Core;
using UniverseSimAR.Managers;

namespace UniverseSimAR.UI.Components {
    public class SelectedActionUI : MonoBehaviour {
        public static SelectedActionUI Instance;

        [Header("UI References")]
        public Button btnEdit;
        public Button btnDelete;
        public Button btnFlingMode;

        [Header("Settings")]
        [Tooltip("Khoảng cách UI nằm phía trên hành tinh, tính theo hướng 'lên' của màn hình người nhìn")]
        public float heightOffset = 0.03f;
        [Tooltip("Kéo UI lại gần camera một chút để không bị hành tinh che khuất (0 = tắt)")]
        public float towardCameraOffset = 0.02f;

        [Header("Mode Texture")]
        public Sprite flingModeTexture;
        public Sprite keepModeTexture;

        private Celestial targetCelestial;
        private Camera arCamera;
        private bool isFlingMode = false;
        private MainGameManager gameManager;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
            } else {
                Instance = this;
            }
        }

        private void Start() {
            arCamera = Camera.main;
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace) {
                canvas.worldCamera = arCamera;
            }

            gameManager = MainGameManager.Instance;

            btnEdit.onClick.AddListener(() => {
                EditorActionUI.Instance.Attach(targetCelestial);
                Hide();
            });

            btnDelete.onClick.AddListener(() => {
                gameManager.DeleteSelectedCelestial();
                Hide();
            });

            btnFlingMode.onClick.AddListener(() => {
                isFlingMode = !isFlingMode;
                if (isFlingMode) {
                    btnFlingMode.image.sprite = flingModeTexture;
                } else {
                    btnFlingMode.image.sprite = keepModeTexture;
                }

                gameManager.setFlingMode(isFlingMode);
            });

            gameObject.SetActive(false);
        }

        private void LateUpdate() {
            if (targetCelestial == null || arCamera == null) return;

            Transform cam = arCamera.transform;
            Vector3 anchor = targetCelestial.transform.position;

            // Nằm phía trên hành tinh theo hướng "lên" của camera (màn hình), không phải trục Y của thế giới.
            Vector3 pos = anchor + cam.up * heightOffset;

            // Kéo nhẹ về phía camera để không bị chính hành tinh che.
            if (towardCameraOffset > 0f) {
                Vector3 toCam = (cam.position - anchor).normalized;
                pos += toCam * towardCameraOffset;
            }

            transform.position = pos;

            // Quay mặt về camera và giữ đúng chiều "lên" của camera nên UI luôn thẳng đứng theo người nhìn.
            Vector3 lookDirection = pos - cam.position;
            if (lookDirection.sqrMagnitude > 1e-8f) {
                transform.rotation = Quaternion.LookRotation(lookDirection, cam.up);
            }
        }

        public void Show(Celestial celestial) {
            targetCelestial = celestial;
            gameObject.SetActive(true);
        }

        public void Hide() {
            targetCelestial = null;
            gameObject.SetActive(false);
        }
    }
}