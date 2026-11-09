using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System;

namespace UniverseSimAR.Utils {
    public class GeminiJsonGenerator {
        private static string apiKey = "AQ.Ab8RN6LVZ8iVeqlrIDxjBVPRsb_sm5aI67sHabpGq9X2w1yNJQ";
        private static string model = "gemini-3.1-flash-lite";

        public static IEnumerator SendGenerateRequest(string systemInstruction, string userPrompt, string jsonSchema, Action<string> callback) {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

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