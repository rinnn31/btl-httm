using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Core;
using UniverseSimAR.Managers;

namespace UniverseSimAR.UI.Components {
    public class PendingActionUI : MonoBehaviour {
        public static PendingActionUI Instance;

        [Header("UI References")]
        public Button btnConfirm;
        public Button btnCancel;
        public Button btnPlacementMode;
        
        [Header("Settings")]
        public float heightOffset = 0.03f;

        [Header("Placement Mode Texture")]
        public Sprite orbitModeTexture;
        public Sprite stillModeTexture;

        private Celestial targetCelestial;
        private Camera arCamera;
        private bool isOrbitMode = true;
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

            btnConfirm.onClick.AddListener(() => {
                gameManager.ConfirmPlacePending();
                Hide();
            });

            btnCancel.onClick.AddListener(() => {
                gameManager.CancelPending();
                Hide();
            });

            btnPlacementMode.onClick.AddListener(() => {
                isOrbitMode = !isOrbitMode;
                if (isOrbitMode) {
                    btnPlacementMode.image.sprite = orbitModeTexture;
                } else {
                    btnPlacementMode.image.sprite = stillModeTexture;
                }

                gameManager.setPlaceAsOrbitMode(isOrbitMode);
            });

            gameObject.SetActive(false);
        }

        private void Update() {
            if (targetCelestial == null || arCamera == null) return;

            transform.position = targetCelestial.transform.position + Vector3.up * heightOffset;

            // Cho UI luôn quay về phía camera
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