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
        [Tooltip("Khoảng hở giữa Editor và NumPad (đơn vị thế giới, mét)")]
        public float spacing = 0.25f;
        [Tooltip("Luôn xếp ngang (trái-phải) theo màn hình, kể cả khi đang ở chế độ dọc")]
        public bool forceHorizontal = false;

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
                    ApplyLayout(true);
                }
            });

            editRadiusBtn.onClick.AddListener(() => {
                isEditingMass = false;
                if (NumPadUI.Instance != null) {
                    NumPadUI.Instance.Show(OnNumInput, OnDelInput, OnConfirmInput);
                    Canvas.ForceUpdateCanvases();
                    ApplyLayout(true);
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
            ApplyLayout(true);
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
            ApplyLayout(true);
        }

        private static float GetUIWidth(RectTransform rect) {
            return rect == null ? 0f : rect.rect.width * rect.lossyScale.x;
        }

        private static float GetUIHeight(RectTransform rect) {
            return rect == null ? 0f : rect.rect.height * rect.lossyScale.y;
        }

        // Tính vị trí gốc (pivot) của `root` sao cho TÂM hình chữ nhật `rect` rơi đúng vào worldCenter
        // khi root có hướng xoay `rot`. Nhờ vậy UI luôn ở giữa dù pivot/anchor của canvas hay panel bị lệch.
        private static Vector3 RootPositionForCenter(Transform root, RectTransform rect, Vector3 worldCenter, Quaternion rot) {
            if (rect == null) return worldCenter;
            Vector3 localCenter = root.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            Vector3 worldOffset = rot * Vector3.Scale(localCenter, root.lossyScale);
            return worldCenter - worldOffset;
        }

        // snap = true: đặt ngay lập tức, false: di chuyển mượt về đích.
        private void ApplyLayout(bool snap) {
            if (arCamera == null) arCamera = Camera.main;
            if (arCamera == null || menuPanel == null) return;

            Transform cam = arCamera.transform;
            Vector3 center = cam.position + cam.forward * distanceFromCamera;

            // Giữ chiều "lên" theo camera để UI thẳng theo hướng người nhìn
            Quaternion rot = Quaternion.LookRotation(cam.forward, cam.up);

            RectTransform editorRect = menuPanel.GetComponent<RectTransform>();
            bool numPadActive = NumPadUI.Instance != null && NumPadUI.Instance.gameObject.activeSelf;

            bool horizontal = forceHorizontal || Screen.width > Screen.height;
            // Ngang: xếp theo trục phải của camera (Editor bên trái, NumPad bên phải).
            // Dọc: xếp theo trục lên của camera.
            Vector3 axis = horizontal ? cam.right : cam.up;

            Vector3 editorCenter = center;
            Vector3 numPadCenter = center;
            RectTransform numPadRect = null;

            if (numPadActive) {
                numPadRect = NumPadUI.Instance.GetComponent<RectTransform>();

                float sizeA = horizontal ? GetUIWidth(editorRect) : GetUIHeight(editorRect);
                float sizeB = horizontal ? GetUIWidth(numPadRect) : GetUIHeight(numPadRect);

                // Cả cụm (A + khoảng hở + B) nằm giữa tầm nhìn
                float total = sizeA + spacing + sizeB;
                editorCenter = center - axis * (total * 0.5f - sizeA * 0.5f);
                numPadCenter = center + axis * (total * 0.5f - sizeB * 0.5f);
            }

            // Editor
            Vector3 editorRootPos = RootPositionForCenter(transform, editorRect, editorCenter, rot);
            MoveTo(transform, editorRootPos, rot, snap);

            // NumPad
            if (numPadActive) {
                Transform npT = NumPadUI.Instance.transform;
                Vector3 numPadRootPos = RootPositionForCenter(npT, numPadRect, numPadCenter, rot);
                MoveTo(npT, numPadRootPos, rot, snap);
            }
        }

        private void MoveTo(Transform t, Vector3 pos, Quaternion rot, bool snap) {
            if (snap) {
                t.SetPositionAndRotation(pos, rot);
                return;
            }
            // lerp không phụ thuộc framerate
            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            t.SetPositionAndRotation(
                Vector3.Lerp(t.position, pos, k),
                Quaternion.Slerp(t.rotation, rot, k));
        }

        private void LateUpdate() {
            if (menuPanel == null || !menuPanel.activeSelf || arCamera == null) return;
            ApplyLayout(false);
        }
    }
}