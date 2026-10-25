using System;
using System.Collections;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Components.Containers;
using System.Collections.Generic;

namespace UniverseSimAR.Input {
    public class HandTracker : MonoBehaviour {
        public event Action<List<NormalizedLandmark>> HandDetected;

        public event Action HandLost;

        private ARCameraManager cameraManager;

        [Header("Model")]
        [Tooltip("Model Filename")]
        public string modelFileName = "hand_landmarker.bytes";
        [Tooltip("Use GPU")]
        public bool useGpu = false;

        [Header("Performance")]
        [Tooltip("Input image long side size")]
        public int targetLongSide = 320;
        [Tooltip("Frame counts for next detection")]
        public int detectEveryNFrames = 2;

        [Header("Detection")]
        [Range(0.1f, 1f)] public float minHandDetectionConfidence = 0.5f;
        [Range(0.1f, 1f)] public float minHandPresenceConfidence = 0.5f;
        [Range(0.1f, 1f)] public float minTrackingConfidence = 0.5f;

        private HandLandmarker landmarker;
        private HandLandmarkerResult result;
        private bool ready;

        private NativeArray<byte> rawBuffer;
        private NativeArray<byte> rotatedBuffer;
        private int rotatedWidth, rotatedHeight;

        private int frameCounter;
        private long lastTimestampMs = -1;

        private void Awake() {
            cameraManager = Camera.main.GetComponent<ARCameraManager>();   
           
        }

        private IEnumerator Start() {
            string modelPath = Path.Combine(Application.persistentDataPath, modelFileName);
            if (!File.Exists(modelPath)) {
                string srcPath = Application.streamingAssetsPath + "/" + modelFileName;
                using (var req = UnityWebRequest.Get(srcPath)) {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) {
                        Debug.LogError($"[HandTracker] Không đọc được model: {srcPath} ({req.error})");
                        yield break;
                    }
                    File.WriteAllBytes(modelPath, req.downloadHandler.data);
                }
            }

            var baseOptions = new BaseOptions(
                useGpu ? BaseOptions.Delegate.GPU : BaseOptions.Delegate.CPU,
                modelAssetPath: modelPath);

            var options = new HandLandmarkerOptions(
                baseOptions: baseOptions,
                runningMode: RunningMode.VIDEO,
                numHands: 1,
                minHandDetectionConfidence: minHandDetectionConfidence,
                minHandPresenceConfidence: minHandPresenceConfidence,
                minTrackingConfidence: minTrackingConfidence);

            landmarker = HandLandmarker.CreateFromOptions(options);
            result = HandLandmarkerResult.Alloc(options.numHands);
            ready = true;
        }

        private void OnEnable() {
            if (cameraManager != null) cameraManager.frameReceived += OnCameraFrame;
        }

        private void OnDisable() {
            if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrame;
        }

        private void OnDestroy() {
            ready = false;
            landmarker?.Close();
            if (rawBuffer.IsCreated) rawBuffer.Dispose();
            if (rotatedBuffer.IsCreated) rotatedBuffer.Dispose();
        }

        private void OnCameraFrame(ARCameraFrameEventArgs args) {
            if (!ready) return;
            if (++frameCounter % Mathf.Max(1, detectEveryNFrames) != 0) return;
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage)) return;

            using (cpuImage) {
                if (!PrepareImage(cpuImage)) return;
            }

            using var image = new Mediapipe.Image(
                Mediapipe.ImageFormat.Types.Format.Srgba,
                rotatedWidth, rotatedHeight, rotatedWidth * 4, rotatedBuffer);

            long ts = (long)(Time.realtimeSinceStartup * 1000f);
            if (ts <= lastTimestampMs) ts = lastTimestampMs + 1;
            lastTimestampMs = ts;

            bool ok = landmarker.TryDetectForVideo(image, ts, null, ref result);

            if (!ok || result.handLandmarks == null || result.handLandmarks.Count == 0) {
                HandLost?.Invoke();
                return;
            }

            var landmarks = result.handLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < 21) {
                HandLost?.Invoke();
                return;
            }
            
            HandDetected?.Invoke(landmarks);
        }

        private bool PrepareImage(XRCpuImage cpuImage) {
            float scale = (float)targetLongSide / Mathf.Max(cpuImage.width, cpuImage.height);
            var outSize = new Vector2Int(
                Mathf.Max(2, Mathf.RoundToInt(cpuImage.width * scale)),
                Mathf.Max(2, Mathf.RoundToInt(cpuImage.height * scale)));

            var conversion = new XRCpuImage.ConversionParams {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = outSize,
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None // top-down
            };

            int size = cpuImage.GetConvertedDataSize(conversion);
            if (!rawBuffer.IsCreated || rawBuffer.Length != size) {
                if (rawBuffer.IsCreated) rawBuffer.Dispose();
                rawBuffer = new NativeArray<byte>(size, Allocator.Persistent);
            }

            try {
                cpuImage.Convert(conversion, rawBuffer);
            }
            catch (Exception e) {
                Debug.LogWarning($"[HandTracker] Convert failed: {e.Message}");
                return false;
            }

            int w = outSize.x, h = outSize.y;
            int rotation = GetRotationDegrees();
            bool swap = rotation == 90 || rotation == 270;
            rotatedWidth = swap ? h : w;
            rotatedHeight = swap ? w : h;

            if (!rotatedBuffer.IsCreated || rotatedBuffer.Length != size) {
                if (rotatedBuffer.IsCreated) rotatedBuffer.Dispose();
                rotatedBuffer = new NativeArray<byte>(size, Allocator.Persistent);
            }

            var src = rawBuffer.Reinterpret<uint>(1);
            var dst = rotatedBuffer.Reinterpret<uint>(1);

            for (int y = 0; y < h; y++) {
                int row = y * w;
                for (int x = 0; x < w; x++) {
                    int dx, dy;
                    switch (rotation) {
                        case 90: dx = h - 1 - y; dy = x; break;           // xoay 90° cùng chiều kim đồng hồ
                        case 180: dx = w - 1 - x; dy = h - 1 - y; break;
                        case 270: dx = y; dy = w - 1 - x; break;
                        default: dx = x; dy = y; break;
                    }
                    dst[dy * rotatedWidth + dx] = src[row + x];
                }
            }
            return true;
        }

        private static int GetRotationDegrees() {
            switch (Screen.orientation) {
                case ScreenOrientation.Portrait: return 90;
                case ScreenOrientation.LandscapeLeft: return 0;
                case ScreenOrientation.PortraitUpsideDown: return 270;
                case ScreenOrientation.LandscapeRight: return 180;
                default: return 90;
            }
        }
    }
}