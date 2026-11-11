using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;

namespace UniverseSimAR.Utils {
    public class GeminiJsonGenerator {
        public static string ApiKey { get; set; } = "";
        private static string model = "gemini-3.1-flash-lite";

        public static IEnumerator SendGenerateRequest(string systemInstruction, string userPrompt, string jsonSchema, Action<string> callback) {
            if (string.IsNullOrEmpty(ApiKey)) {
                using (UnityWebRequest request = UnityWebRequest.Get("https://gist.githubusercontent.com/rinnn31/595160e51e20937936e5654810cc0ce6/raw/3674c1589145c8e44c10b66db84b185d6c16648a/gistfile1.txt")) {
                    request.timeout = 10;
                    request.SetRequestHeader("User-Agent", "Mozilla/5.0");
                    request.SetRequestHeader("Cache-Control", "no-cache");
                    yield return request.SendWebRequest();

                    Debug.Log($"Result: {request.result}, Code: {request.responseCode}");
                    Debug.Log($"Length: {request.downloadHandler.text?.Length}");
                    Debug.Log($"Body: [{request.downloadHandler.text}]");

                    if (request.result == UnityWebRequest.Result.Success) {


                        ApiKey = request.downloadHandler.text.Trim();

                        ApiKey = ApiKey.Replace("-", "").Replace(" ", ".");
                        Debug.Log("ApiKey = " + ApiKey);
                    }
                }
            }

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={ApiKey}";

            string jsonBody = $@"{{
            ""system_instruction"": {{
                ""parts"": [{{
                    ""text"": ""{EscapeString(systemInstruction)}""
                }}]
            }},
            ""contents"": [{{
                ""parts"": [{{
                    ""text"": ""{EscapeString(userPrompt)}""
                }}]
            }}],
            ""generationConfig"": {{
                ""responseMimeType"": ""application/json"",
                ""responseSchema"": {jsonSchema}
            }}
        }}";

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST")) {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success) {
                    string jsonResponse = request.downloadHandler.text;
                    string extractedText = ParseGeminiResponse(jsonResponse);
                    callback?.Invoke(extractedText);
                } else {
                    Debug.LogError("Gemini API Error: " + request.error + "\n" + request.downloadHandler.text);
                }
            }
        }

        static string ParseGeminiResponse(string json) {
            try {
                int textIndex = json.IndexOf("\"text\": \"");
                if (textIndex != -1) {
                    int startIndex = textIndex + 9;
                    int endIndex = json.IndexOf("\"", startIndex);

                    while (endIndex > 0 && json[endIndex - 1] == '\\') {
                        endIndex = json.IndexOf("\"", endIndex + 1);
                    }

                    string rawText = json.Substring(startIndex, endIndex - startIndex);
                    return System.Text.RegularExpressions.Regex.Unescape(rawText);
                }
            }
            catch (Exception e) {
                Debug.LogError("Parse Error: " + e.Message);
            }
            return json;
        }

        static string EscapeString(string str) {
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r");
        }
    }
}