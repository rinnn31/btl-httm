using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Core;
using UniverseSimAR.Managers;

namespace UniverseSimAR.UI.Components {

    public class EditorActionUI : MonoBehaviour {
        public static EditorActionUI Instance { get; private set; }

        [Header("References")]
        public GameObject menuPanel;
        public TMP_Text celestialNameTxt;
        public TMP_Text massValueTxt;
        public TMP_Text radiusValueTxt;
        public Button editMassBtn;
        public Button editRadiusBtn;
        public Button cancelBtn;
        public Button confirmBtn;

        [Header("Layout Settings")]
        public float spacing = 0.25f;

        [Header("Settings")]
        public int maxMassValue = 1000000;
        public int minMassValue = 1;
        public int maxRadiusValue = 10000;
        public int minRadiusValue = 1;
        public float distanceFromCamera = 1.5f;
        public float followSpeed = 5.0f;

        private Celestial editingCelestial;
        private Camera arCamera;

        private bool isEditingMass = false;
        private int currentMass;
        private int currentRadius;

        private void Awake() {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        private void Start() {
            arCamera = Camera.main;

            if (menuPanel != null) {
                Canvas parentCanvas = menuPanel.GetComponentInParent<Canvas>();
                if (parentCanvas != null && parentCanvas.renderMode == RenderMode.WorldSpace) {
                    parentCanvas.worldCamera = arCamera;
                }
                menuPanel.SetActive(false);
            }

            editMassBtn.onClick.AddListener(() => {
                isEditingMass = true;
                if (NumPadUI.Instance != null) {
                    NumPadUI.Instance.Show(OnNumInput, OnDelInput, OnConfirmInput);
                    Canvas.ForceUpdateCanvases();
                    UpdateLayoutPosition();
                }
            });

            editRadiusBtn.onClick.AddListener(() => {
                isEditingMass = false;
                if (NumPadUI.Instance != null) {
                    NumPadUI.Instance.Show(OnNumInput, OnDelInput, OnConfirmInput);
                    Canvas.ForceUpdateCanvases();
                    UpdateLayoutPosition();
                }
            });

            if (cancelBtn != null) {
                cancelBtn.onClick.AddListener(() => {
                    if (NumPadUI.Instance != null) NumPadUI.Instance.Hide();
                    menuPanel.SetActive(false);
                });
            }

            if (confirmBtn != null) {
                confirmBtn.onClick.AddListener(() => {
                    if (editingCelestial != null) {
                        editingCelestial.mass = currentMass / 10000f;
                        editingCelestial.radius = currentRadius / 100000f;
                    }
                    if (NumPadUI.Instance != null) NumPadUI.Instance.Hide();
                    menuPanel.SetActive(false);
                });
            }
        }

        private void OnNumInput(int digit) {
            if (isEditingMass) {
                currentMass = Mathf.Clamp(currentMass * 10 + digit, minMassValue, maxMassValue);
                SetMass(currentMass);
            } else {
                currentRadius = Mathf.Clamp(currentRadius * 10 + digit, minRadiusValue, maxRadiusValue);
                SetRadius(currentRadius);
            }
        }

        private void OnDelInput() {
            if (isEditingMass) {
                currentMass = currentMass / 10;
                SetMass(currentMass);
            } else {
                currentRadius = currentRadius / 10;
                SetRadius(currentRadius);
            }
        }

        private void OnConfirmInput() {
            if (NumPadUI.Instance != null) {
                NumPadUI.Instance.Hide();
            }
            Canvas.ForceUpdateCanvases();
            UpdateLayoutPosition();
        }

        private void SetMass(int newMass) {
            massValueTxt.text = newMass.ToString() + " kg";
        }

        private void SetRadius(int newRadius) {
            radiusValueTxt.text = newRadius.ToString() + " m";
        }

        public void Attach(Celestial celestial) {
            if (menuPanel == null) return;

            editingCelestial = celestial;
            menuPanel.SetActive(true);

            currentMass = (int)(celestial.mass * 10000);
            currentRadius = (int)(celestial.radius * 100000);

            celestialNameTxt.text = "Tên hành tinh: " + celestial.celestialName;
            SetMass(currentMass);
            SetRadius(currentRadius);

            if (NumPadUI.Instance != null) {
                NumPadUI.Instance.Hide();
            }

            Canvas.ForceUpdateCanvases();
            UpdateLayoutPosition();
        }

        private float GetUIWidth(GameObject ui) {
            if (ui == null) return 0f;
            RectTransform rect = ui.GetComponent<RectTransform>();
            if (rect == null) return 0f;
            return rect.rect.width * rect.lossyScale.x;
        }

        private float GetUIHeight(GameObject ui) {
            if (ui == null) return 0f;
            RectTransform rect = ui.GetComponent<RectTransform>();
            if (rect == null) return 0f;
            return rect.rect.height * rect.lossyScale.y;
        }

        private void UpdateLayoutPosition() {
            if (arCamera == null)
                arCamera = Camera.main;

            if (arCamera == null || menuPanel == null)
                return;

            bool isLandscape = Screen.width > Screen.height;
            bool isNumPadActive = NumPadUI.Instance != null && NumPadUI.Instance.gameObject.activeSelf;

            Vector3 cameraPos = arCamera.transform.position;
            Vector3 forward = arCamera.transform.forward;
            Vector3 separationDir = arCamera.transform.up; // Trục chuẩn theo hướng màn hình

            Vector3 centerTargetPos = cameraPos + forward * distanceFromCamera;

            if (isNumPadActive) {
                float sizeA = isLandscape ? GetUIWidth(menuPanel) : GetUIHeight(menuPanel);
                float sizeB = isLandscape ? GetUIWidth(NumPadUI.Instance.gameObject) : GetUIHeight(NumPadUI.Instance.gameObject);

                float offset = (sizeA + sizeB) * 0.5f + spacing;

                transform.position = centerTargetPos - separationDir * (offset * 0.5f);

                Vector3 numPadPos = centerTargetPos + separationDir * (offset * 0.5f);
                NumPadUI.Instance.transform.position = numPadPos;
                NumPadUI.Instance.transform.rotation = Quaternion.LookRotation(centerTargetPos - cameraPos);
            } else {
                transform.position = centerTargetPos;
            }

            Vector3 lookDir = transform.position - cameraPos;
            if (lookDir.sqrMagnitude > 0.001f) {
                transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }

        private void Update() {
            if (menuPanel == null || !menuPanel.activeSelf || arCamera == null) return;

            bool isLandscape = Screen.width > Screen.height;
            bool isNumPadActive = NumPadUI.Instance != null && NumPadUI.Instance.gameObject.activeSelf;

            Vector3 cameraPos = arCamera.transform.position;
            Vector3 forward = arCamera.transform.forward;
            Vector3 separationDir = arCamera.transform.up;

            Vector3 centerTargetPos = cameraPos + forward * distanceFromCamera;

            Vector3 targetPos;

            if (isNumPadActive) {
                float sizeA = isLandscape ? GetUIWidth(menuPanel) : GetUIHeight(menuPanel);
                float sizeB = isLandscape ? GetUIWidth(NumPadUI.Instance.gameObject) : GetUIHeight(NumPadUI.Instance.gameObject);

                float offset = (sizeA + sizeB) * 0.5f + spacing;

                targetPos = centerTargetPos - separationDir * (offset * 0.5f);
                Vector3 numPadTargetPos = centerTargetPos + separationDir * (offset * 0.5f);

                Transform numPadTransform = NumPadUI.Instance.transform;

                numPadTransform.position = Vector3.Lerp(
                    numPadTransform.position,
                    numPadTargetPos,
                    Time.deltaTime * followSpeed
                );

                Vector3 numPadLookDir = centerTargetPos - cameraPos;
                if (numPadLookDir.sqrMagnitude > 0.001f) {
                    Quaternion numPadTargetRot = Quaternion.LookRotation(numPadLookDir);
                    numPadTransform.rotation = Quaternion.Slerp(
                        numPadTransform.rotation,
                        numPadTargetRot,
                        Time.deltaTime * followSpeed
                    );
                }
            } else {
                targetPos = centerTargetPos;
            }

            transform.position = Vector3.Lerp(
                transform.position,
                targetPos,
                Time.deltaTime * followSpeed
            );

            Vector3 lookDir = centerTargetPos - cameraPos;
            if (lookDir.sqrMagnitude > 0.001f) {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    Time.deltaTime * followSpeed
                );
            }
        }
    }
}