using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Core;
using UniverseSimAR.Managers;

namespace UniverseSimAR.UI.Components {
    [System.Serializable]
    public class CelestialItem {
        public Sprite icon;
        public Celestial template;
    }

    public class MenuActionUI : MonoBehaviour {
        public static MenuActionUI Instance { get; private set; }

        [Header("References")]
        public GameObject menuPanel;
        public Button pauseBtn;
        public Button addBtn;
        public Button prevBtn;
        public Button nextBtn;
        public Image celestialImage;

        [Header("Settings")]
        public float distanceFromCamera = 1.5f;
        public float followSpeed = 5.0f;
        public CelestialItem[] celestialTemplates;

        private int currentIdx = 0;
        private Camera arCamera;
        private bool isLocked;
        private MainGameManager gameManager;

        private void Awake() {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;

            celestialTemplates ??= new CelestialItem[0];
        }

        private void Start() {
            gameManager = MainGameManager.Instance;

            arCamera = Camera.main;

            if (menuPanel != null) {
                Canvas parentCanvas = menuPanel.GetComponentInParent<Canvas>();
                if (parentCanvas != null && parentCanvas.renderMode == RenderMode.WorldSpace) {
                    parentCanvas.worldCamera = arCamera;
                }
                menuPanel.SetActive(false);
            }

            prevBtn.onClick.AddListener(() => SwitchTemplate(-1));
            nextBtn.onClick.AddListener(() => SwitchTemplate(1));
            addBtn.onClick.AddListener(() => {
                if (celestialTemplates.Length > 0) {
                    gameManager.AddCelestial(celestialTemplates[currentIdx].template);
                    ToggleVisible();
                }
            });
            pauseBtn.onClick.AddListener(() => {
                gameManager.TogglePause();
            });

            SwitchTemplate(0);
        }

        public void LockMenu(bool state) {
            isLocked = state;
            if (isLocked && menuPanel != null && menuPanel.activeSelf)
                menuPanel.SetActive(false);
        }

        private void SwitchTemplate(int delta) {
            if (celestialTemplates.Length == 0 || celestialImage == null) return;
            currentIdx = (currentIdx + delta + celestialTemplates.Length) % celestialTemplates.Length;
            celestialImage.sprite = celestialTemplates[currentIdx].icon;
        }

        public void ToggleVisible() {
            if (menuPanel == null || isLocked) return;

            bool isShowing = !menuPanel.activeSelf;
            menuPanel.SetActive(isShowing);

            if (isShowing && arCamera != null) {
                transform.position = arCamera.transform.position + arCamera.transform.forward * distanceFromCamera;
                Vector3 lookDir = transform.position - arCamera.transform.position;
                if (lookDir != Vector3.zero) {
                    transform.rotation = Quaternion.LookRotation(lookDir);
                }
            }
        }

        private void Update() {
            if (menuPanel == null || !menuPanel.activeSelf || isLocked || arCamera == null) return;

            Vector3 targetPos = arCamera.transform.position + arCamera.transform.forward * distanceFromCamera;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);

            Vector3 lookDir = transform.position - arCamera.transform.position;
            if (lookDir != Vector3.zero) {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * followSpeed);
            }
        }
    }
}