
using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Managers;
using TMPro;

namespace UniverseSimAR.UI.Screens {
    public class UIManager : MonoBehaviour {
        [Header("UI References")]
        [SerializeField] private Button confirmBtn;
        [SerializeField] private Button resetBtn;
        [SerializeField] private TMP_Text helperText;

        private PrepareGameManager gameManager;
        private bool isSubscribed;

        private void Awake() {
            if (confirmBtn == null) {
                GameObject obj = GameObject.Find("Btn_Confirm");
                if (obj != null) confirmBtn = obj.GetComponent<Button>();
            }

            if (resetBtn == null) {
                GameObject obj = GameObject.Find("Btn_Reset");
                if (obj != null) resetBtn = obj.GetComponent<Button>();
            }

            if (helperText == null) {
                GameObject obj = GameObject.Find("Txt_Helper");
                if (obj != null) helperText = obj.GetComponent<TMP_Text>();
            }
        }

        private void Start() {
            gameManager = PrepareGameManager.Instance;

            gameManager.OnHitChanged.AddListener(OnHitChanged);
            gameManager.OnAnchorChanged.AddListener(OnAnchorChanged);

            isSubscribed = true;

            resetBtn.onClick.AddListener(HandleReset);

            confirmBtn.onClick.AddListener(HandleConfirm);

            OnHitChanged(gameManager.HasHit);
            OnAnchorChanged(gameManager.HasAnchor);
        }

        private void HandleReset() {
            gameManager?.ResetState();
        }

        private void HandleConfirm() {
            gameManager?.ConfirmAnchor();
        }

        private void OnHitChanged(bool hit) {
            if (gameManager != null && gameManager.HasAnchor) return;

            helperText.enabled = true;
            helperText.text = hit
                ? "Đã phát hiện mặt phẳng\nNhấn vào màn hình để đặt Grid"
                : "Di chuyển điện thoại\nđến mặt phẳng muốn đặt Grid";
        }

        private void OnAnchorChanged(bool placed) {
            confirmBtn.gameObject.SetActive(placed);
            resetBtn.gameObject.SetActive(placed);

            if (helperText != null) {
                helperText.enabled = !placed;

                if (!placed) {
                    helperText.text =
                        gameManager != null && gameManager.HasHit
                        ? "Đã phát hiện mặt phẳng\nNhấn vào màn hình để đặt Grid"
                        : "Di chuyển điện thoại\nđến mặt phẳng muốn đặt Grid";
                }
            }
        }

        private void OnDestroy() {
            if (gameManager != null && isSubscribed) {
                gameManager.OnHitChanged.RemoveListener(OnHitChanged);
                gameManager.OnAnchorChanged.RemoveListener(OnAnchorChanged);
            }

            resetBtn.onClick.RemoveListener(HandleReset);
            confirmBtn.onClick.RemoveListener(HandleConfirm);
        }
    }
}