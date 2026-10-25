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
        public float heightOffset = 0.03f;

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

        private void Update() {
            if (targetCelestial == null || arCamera == null) return;

            transform.position = targetCelestial.transform.position + Vector3.up * heightOffset;

            Vector3 lookDirection = transform.position - arCamera.transform.position;
            if (lookDirection != Vector3.zero) {
                transform.rotation = Quaternion.LookRotation(lookDirection);
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