using System;
using UnityEngine;
using UnityEngine.UI;

namespace UniverseSimAR.UI.Components {
    public class NumPadUI : MonoBehaviour {
        [Header("UI References")]
        public GameObject menuPanel;

        public static NumPadUI Instance;

        private Action<int> currentNumInputEvent = null;
        private Action currentDelInputEvent = null;
        private Action currentConfirmInputEvent = null;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
            } else {
                Instance = this;
            }
        }

        private void Start() {
            for (int i =0; i<=9; i++) {
                int num = i;
                Button button = GameObject.Find("Btn_" + i).GetComponent<Button>();
                button.onClick.AddListener(() => {
                    if (currentNumInputEvent != null) {
                        currentNumInputEvent.Invoke(num);
                    }
                });
            }

            Button confirmBtn = GameObject.Find("Btn_OK").GetComponent<Button>();
            confirmBtn.onClick.AddListener(() => {
                if (currentConfirmInputEvent != null) {
                    currentConfirmInputEvent.Invoke();
                }
            });
            Button delBtn = GameObject.Find("Btn_Del").GetComponent<Button>();
            delBtn.onClick.AddListener(() => {
                if (currentDelInputEvent != null) {
                    currentDelInputEvent.Invoke();
                }
            });


            menuPanel.SetActive(false);
        }

        public void Show(Action<int> onNumInput, Action onDelInput, Action onConfirmInput) {
            currentNumInputEvent = onNumInput;
            currentConfirmInputEvent = onConfirmInput;
            currentDelInputEvent = onDelInput;

            menuPanel.SetActive(true);
        }

        public void Hide() {
            currentNumInputEvent = null;
            currentConfirmInputEvent = null;
            currentDelInputEvent = null;

            menuPanel.SetActive(false);
        }
    }
}