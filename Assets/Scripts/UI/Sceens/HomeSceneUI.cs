using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using TMPro;

namespace UniverseSimAR.UI.Screens {
    public class HomeSceneUI : MonoBehaviour {
        [Header("UI References")]
        public Button startBtn;
        public Button downloadBtn;

        [Header("Model Settings")]
        public string modelUrl = "https://huggingface.co/mohdnahidmondol/qwen-0.5b-gguf/resolve/main/qwen-0.5b-q4_k_m.gguf";
        public string modelFileName = "qwen_model.gguf";

        private TMP_Text downloadTxt;

        private string GetModelPath() {
            return Path.Combine(Application.persistentDataPath, modelFileName);
        }

        void Start() {
            startBtn.onClick.AddListener(() => {
                SceneManager.LoadScene("PrepareScene");
            });

            if (downloadTxt == null) {
                downloadTxt = downloadBtn.GetComponentInChildren<TMP_Text>();
            }

            CheckAndSetupModelButton();
        }

        private void CheckAndSetupModelButton() {
            if (HasLLMModel()) {
                downloadBtn.interactable = false;
                if (downloadTxt != null) downloadTxt.text = "Đã tải model";
            } else {
                downloadBtn.interactable = true;
                if (downloadTxt != null) downloadTxt.text = "Tải mô hình LLM";

                downloadBtn.onClick.RemoveAllListeners();
                downloadBtn.onClick.AddListener(StartDownloadModel);
            }
        }

        private bool HasLLMModel() {
            return File.Exists(GetModelPath());
        }

        private void StartDownloadModel() {
            downloadBtn.interactable = false;
            StartCoroutine(DownloadModelRoutine());
        }

        private IEnumerator DownloadModelRoutine() {
            string savePath = GetModelPath();

            using (UnityWebRequest www = UnityWebRequest.Get(modelUrl)) {
                www.downloadHandler = new DownloadHandlerFile(savePath);

                UnityWebRequestAsyncOperation operation = www.SendWebRequest();

                while (!operation.isDone) {
                    float progress = www.downloadProgress * 100f;
                    if (downloadTxt != null) {
                        downloadTxt.text = $"Đang tải... ({progress:F1}%)";
                    }
                    yield return null;
                }

                if (www.result == UnityWebRequest.Result.Success) {
                    Debug.Log("Model downloaded successfully to: " + savePath);
                    if (downloadTxt != null) downloadTxt.text = "Đã tải mô hình";
                    downloadBtn.interactable = false;
                } else {
                    Debug.LogError("Failed to download model: " + www.error);
                    if (downloadTxt != null) downloadTxt.text = "Tải mô hình LLM";

                    if (File.Exists(savePath)) {
                        File.Delete(savePath);
                    }

                    downloadBtn.interactable = true;
                }
            }
        }
    }
}