using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UniverseSimAR.Core;
using UniverseSimAR.Managers;
using UniverseSimAR.Utils;

namespace UniverseSimAR.UI.Components {
    [System.Serializable]
    public class CelestialItem {
        public Sprite icon;
        public Celestial template;
    }

    [System.Serializable]
    public class CelestialResponseList {
        public CelestialBodyData[] celestialBodies;
    }

    [System.Serializable]
    public class CelestialBodyData {
        public string celestialName;
        public Vector3Data position;
        public float radius;
        public float mass;
        public Vector3Data velocity;

        // Các hàm tiện ích giúp chuyển đổi nhanh sang kiểu Vector3 của Unity
        public Vector3 ToPositionVector() => new Vector3(position.x, position.y, position.z);
        public Vector3 ToVelocityVector() => new Vector3(velocity.x, velocity.y, velocity.z);
    }

    [System.Serializable]
    public class Vector3Data {
        public float x;
        public float y;
        public float z;
    }

    public class MenuActionUI : MonoBehaviour, ISpeechToTextListener {
        public static MenuActionUI Instance { get; private set; }

        private static string SYSTEM_PROMPT = @"Bạn là bộ xử lý ngôn ngữ tự nhiên thông minh cho ứng dụng mô phỏng không gian Unity. Nhiệm vụ của bạn là lắng nghe và phân tích câu lệnh giọng nói của người dùng để tạo ra danh sách các thiên thể theo đúng yêu cầu.
Quy tắc giới hạn tham số:
1. Khối lượng (mass): từ 0.00004 (Mặt Trăng) đến 0.5 (5 lần Mặt Trời).
2. Bán kính (radius): từ 0.01 (Mặt Trăng) đến 0.15 (2.5 lần Mặt Trời).
3. Vị trí (position - x, y, z): Tọa độ 3D hợp lý xung quanh tâm mô phỏng.
4. Vận tốc (velocity - x, y, z): Phù hợp điều kiện G = 100 (dao động trong khoảng -15 đến 15).
5. Các thiên thể cho phép: Trái Đất, Mặt Trăng, Mặt Trời, Sao Hỏa, Sao Kim, Sao Thủy, Sao Mộc, Sao Thổ, Sao Thiên Vương, Sao Hải Vương, Sao Diêm Vương.
6. Yêu cầu các thiên thể nằm trên một mặt phẳng (z = 0) và không chồng lấn nhau.
Trả về kết quả hoàn toàn bằng cấu trúc JSON chuẩn.";

        private static string JSON_SCHEMA = @"{
    ""type"": ""OBJECT"",
    ""properties"": {
        ""celestialBodies"": {
            ""type"": ""ARRAY"",
            ""description"": ""Danh sách các thiên thể được tạo ra"",
            ""items"": {
                ""type"": ""OBJECT"",
                ""properties"": {
                    ""celestialName"": {
                        ""type"": ""STRING"",
                        ""description"": ""Tên thiên thể""
                    },
                    ""position"": {
                        ""type"": ""OBJECT"",
                        ""properties"": {
                            ""x"": { ""type"": ""NUMBER"" },
                            ""y"": { ""type"": ""NUMBER"" },
                            ""z"": { ""type"": ""NUMBER"" }
                        },
                        ""required"": [""x"", ""y"", ""z""]
                    },
                    ""radius"": { 
                        ""type"": ""NUMBER"", 
                        ""description"": ""Bán kính (0.01 đến 0.15)"" 
                    },
                    ""mass"": { 
                        ""type"": ""NUMBER"", 
                        ""description"": ""Khối lượng (0.00004 đến 0.5)"" 
                    },
                    ""velocity"": {
                        ""type"": ""OBJECT"",
                        ""properties"": {
                            ""x"": { ""type"": ""NUMBER"" },
                            ""y"": { ""type"": ""NUMBER"" },
                            ""z"": { ""type"": ""NUMBER"" }
                        },
                        ""required"": [""x"", ""y"", ""z""]
                    }
                },
                ""required"": [""celestialName"", ""position"", ""radius"", ""mass"", ""velocity""]
            }
        }
    },
    ""required"": [""celestialBodies""]
}";

        [Header("References")]
        public GameObject menuPanel;
        public Button pauseBtn;
        public Button addBtn;
        public Button prevBtn;
        public Button nextBtn;
        public Button micBtn;
        public Button resetBtn;
        public Image celestialImage;
        public Button toggleViewBtn;
        public TMP_Text debugMicTxt;

        [Header("Settings")]
        public float distanceFromCamera = 1.5f;
        public float followSpeed = 5.0f;
        public CelestialItem[] celestialTemplates;

        private int currentIdx = 0;
        private Camera arCamera;
        private bool isLocked;
        private MainGameManager gameManager;
        private bool isSpeech = false;
        private string text = "";

        private void Awake() {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;

            celestialTemplates ??= new CelestialItem[0];

            var result = SpeechToText.IsLanguageSupported("vi-VN");
            if (result == SpeechToText.LanguageSupport.Supported) {
                SpeechToText.Initialize("vi-VN");
            } else {
                SpeechToText.Initialize();
            }

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
            resetBtn.onClick.AddListener(() => {
                gameManager.ResetGame();
            });
            micBtn.onClick.AddListener(() => {
                if (isSpeech) {
                    SpeechToText.Cancel();
                    isSpeech = false;

                    Image img = micBtn.GetComponent<Image>();
                    if (img) {
                        img.color = new Color(150, 255, 222);
                    }

                    debugMicTxt.gameObject.SetActive(false);

                } else {
                    SpeechToText.RequestPermissionAsync((permission) =>
                    {
                        if (permission == SpeechToText.Permission.Granted) {
                            if (SpeechToText.Start(this, preferOfflineRecognition: false)) {
                                text = "";
                                debugMicTxt.gameObject.SetActive(true);
                            } else isSpeech = false;
                        } else {
                            isSpeech = false;
                        }
                    });
                }
            });
            toggleViewBtn.onClick.AddListener(() => {
                gameManager.ToggleSpaceMode();
            });

            debugMicTxt.gameObject.SetActive(false);
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

        public void OnReadyForSpeech() {
            Image img = micBtn.GetComponent<Image>();
            if (img) {
                img.color = Color.green;
            }
            Debug.Log("UniverseAR Speech Ready");
            isSpeech = true;
        }

        public void OnBeginningOfSpeech() {
            Debug.Log("Speech");
        }

        public void OnVoiceLevelChanged(float normalizedVoiceLevel) {
            micBtn.transform.localScale = Vector3.one * (1 + 0.5f * normalizedVoiceLevel);
        }

        public void OnPartialResultReceived(string spokenText) {
            debugMicTxt.text = spokenText;
        }

        public void OnResultReceived(string spokenText, int? errorCode) {

            micBtn.gameObject.transform.localScale = Vector3.one;
            debugMicTxt.text = $"[{errorCode}] {spokenText}";
            Debug.Log($"Code = {errorCode} | Text = {spokenText}");
            if (errorCode == 0) {
                text = spokenText;

                if (text != null) {
                    StartCoroutine(GeminiJsonGenerator.SendGenerateRequest(SYSTEM_PROMPT, text, JSON_SCHEMA, (res) => {
                        Debug.Log("Got response: " + res);
                        try {
                            CelestialResponseList celestialResponse = JsonUtility.FromJson<CelestialResponseList>(res);

                            gameManager.ResetGame();
                            gameManager.TogglePause(true);
                            gameObject.SetActive(false);

                            foreach (CelestialBodyData ce in celestialResponse.celestialBodies) {
                                Celestial celestial = celestialTemplates.Where(x => x.template.celestialName.ToLower() == ce.celestialName.ToLower()).FirstOrDefault()?.template;
                                if (celestial !=  null) {
                                    gameManager.AddCelestial(
                                        celestial,
                                        new Vector3(ce.position.x, ce.position.y, ce.position.z),
                                        new Vector3(ce.velocity.x, ce.velocity.y, ce.velocity.z),
                                        ce.radius,
                                        ce.mass);
                                }
                            }
                        } catch (Exception e) {
                            Debug.LogException(e);
                        }

                    }));
                }
            }
        }
    }
}