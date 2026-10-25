using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Managers;

namespace UniverseSimAR.UI.Screens {
    public class MainSceneUI : MonoBehaviour {
        [Header("UI References")]
        [SerializeField] private Button quitBtn;

        private MainGameManager gameManager;

        private void Awake() {
            if (quitBtn == null) {
                var quitBtnObj = GameObject.Find("Btn_Quit");
                if (quitBtnObj != null) {
                    quitBtn = quitBtnObj.GetComponent<Button>();
                }
            }
        }

        void Start() {
            gameManager = MainGameManager.Instance;
   
            quitBtn.onClick.AddListener(handleQuitBtnClicked);
        }

        private void handleQuitBtnClicked() {
            gameManager.BackToHomeScene();
        }
    }
}
