using UnityEngine;
using UnityEngine.Events;
using UniverseSimAR.Utils;

namespace UniverseSimAR.Input {
    public enum GestureState { UNKNOWN, PINCH, FIST, RELEASE }
    public enum MouseButtonState { UP, DOWN }

    [System.Serializable]
    public class HandInteractionEvent : UnityEvent<string, Vector2> { }

    [RequireComponent(typeof(HandTracker))]
    public class HandInteraction : MonoBehaviour {
        [Header("Pinch Settings")]
        public float pinchMoveDistance = 0.4f;
        public float pinchCloseDistance = 0.15f;
        public float pinchOpenDistance = 0.4f;

        [Header("Fist -> Open (select) Settings")]
        [Tooltip("Ti le (dau ngon - co tay) / (khop ngon - co tay) trung binh 4 ngon. Nho hon gia tri nay = dang nam tay.")]
        public float fistCloseRatio = 1.0f;
        [Tooltip("Lon hon gia tri nay = da mo tay (phai > fistCloseRatio de co hysteresis).")]
        public float fistOpenRatio = 1.4f;
        [Tooltip("Nam tay it nhat bao lau (giay) thi moi tinh la select. Loc nhieu.")]
        public float minFistSeconds = 0.1f;
        [Tooltip("Nam tay qua lau (giay) thi bo qua, khong ban select. 0 = khong gioi han.")]
        public float maxFistSeconds = 3f;

        [Header("System Settings")]
        public int gestureConfirmFrames = 2;
        public int lostHandFramesThreshold = 8;
        public float moveDeadZone = 1f;

        [Header("Filter Settings")]
        public float oneEuroMinCutoff = 1.5f;
        public float oneEuroBeta = 0.04f;
        public float oneEuroDerivativeCutoff = 1.0f;

        public HandInteractionEvent onEvent = new HandInteractionEvent();

        private MouseButtonState mouseState = MouseButtonState.UP;
        private GestureState gestureState = GestureState.UNKNOWN;
        private GestureState pendingGesture = GestureState.UNKNOWN;

        private int pendingGestureFrames = 0;
        private int lostHandFrames = 0;

        private OneEuroFilter filterX;
        private OneEuroFilter filterY;

        private Vector2? cursorP = null;
        private Vector2? lastEmittedP = null;

        private HandTracker handTracker;

        private float smoothedPinchRatio = 1.0f;
        private float smoothedCurlRatio = 2.0f;

        private bool isFistRaw = false;
        private float fistStartTime = 0f;
        private Vector2 fistPos = Vector2.zero; 

        private void Awake() {
            handTracker = GetComponent<HandTracker>();
            filterX = new OneEuroFilter(oneEuroMinCutoff, oneEuroBeta, oneEuroDerivativeCutoff);
            filterY = new OneEuroFilter(oneEuroMinCutoff, oneEuroBeta, oneEuroDerivativeCutoff);
        }

        private void OnEnable() {
            handTracker.HandDetected += HandTracker_HandDetected;
            handTracker.HandLost += HandleHandLost;
        }

        private void OnDisable() {
            handTracker.HandDetected -= HandTracker_HandDetected;
            handTracker.HandLost -= HandleHandLost;
            if (mouseState == MouseButtonState.DOWN) MouseUp();
        }

        private static Vector3 P(System.Collections.Generic.List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> l, int i) {
            return new Vector3(l[i].x, l[i].y, l[i].z);
        }

        private void HandTracker_HandDetected(System.Collections.Generic.List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> obj) {
            if (obj == null || obj.Count < 21) return;
            ProcessHandData(obj);
        }

        private float Distance2D(Vector3 p1, Vector3 p2) {
            float aspect = Screen.width / (float)Screen.height;
            float dx = (p1.x - p2.x) * aspect;
            float dy = p1.y - p2.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private float GetRobustPalmSize(Vector3 wrist, Vector3 indexMcp, Vector3 middleMcp, Vector3 pinkyMcp) {
            float dMiddle = Distance2D(wrist, middleMcp);
            float dIndex = Distance2D(wrist, indexMcp);
            float palmWidth = Distance2D(indexMcp, pinkyMcp) * 1.25f;
            return Mathf.Max(0.0001f, Mathf.Max(dMiddle, Mathf.Max(dIndex, palmWidth)));
        }

        private float FingerCurl(Vector3 wrist, Vector3 mcp, Vector3 tip) {
            return Distance2D(wrist, tip) / Mathf.Max(0.0001f, Distance2D(wrist, mcp));
        }

        private Vector2 ToScreen(Vector3 lm) {
            return new Vector2(
                Mathf.Clamp01(lm.x) * Screen.width,
                Mathf.Clamp01(1f - lm.y) * Screen.height);
        }

        private void ProcessHandData(System.Collections.Generic.List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> lm) {
            lostHandFrames = 0;

            Vector3 wrist = P(lm, 0);
            Vector3 thumbTip = P(lm, 4);
            Vector3 indexMcp = P(lm, 5);
            Vector3 indexTip = P(lm, 8);
            Vector3 middleMcp = P(lm, 9);
            Vector3 middleTip = P(lm, 12);
            Vector3 ringMcp = P(lm, 13);
            Vector3 ringTip = P(lm, 16);
            Vector3 pinkyMcp = P(lm, 17);
            Vector3 pinkyTip = P(lm, 20);

            float referenceLength = GetRobustPalmSize(wrist, indexMcp, middleMcp, pinkyMcp);
            float rawPinchRatio = Distance2D(thumbTip, indexTip) / referenceLength;
            smoothedPinchRatio = Mathf.Lerp(smoothedPinchRatio, rawPinchRatio, 0.6f);

            float rawCurl = (FingerCurl(wrist, indexMcp, indexTip)
                           + FingerCurl(wrist, middleMcp, middleTip)
                           + FingerCurl(wrist, ringMcp, ringTip)
                           + FingerCurl(wrist, pinkyMcp, pinkyTip)) * 0.25f;
            smoothedCurlRatio = Mathf.Lerp(smoothedCurlRatio, rawCurl, 0.6f);

            if (isFistRaw) {
                if (smoothedCurlRatio >= fistOpenRatio) isFistRaw = false;
            } else {
                if (smoothedCurlRatio <= fistCloseRatio) isFistRaw = true;
            }

            if (isFistRaw) {
                // luu vi tri long ban tay de ban "select" khi mo tay
                fistPos = ToScreen(Vector3.Lerp(wrist, middleMcp, 0.5f));
            } else if (smoothedPinchRatio <= pinchMoveDistance) {
                Vector3 cursorTarget = Vector3.Lerp(indexTip, thumbTip, 0.3f);
                UpdateCursor(cursorTarget, Time.time * 1000f, Screen.width, Screen.height);
            }

            GestureState gesture = DetectGesture(smoothedPinchRatio);

            Debug.Log($"[Hand] Pinch = {smoothedPinchRatio:F2} | Curl = {smoothedCurlRatio:F2} | state = {gesture}");
            UpdateGesture(gesture);
        }

        private GestureState DetectGesture(float indexPinchDist) {
            // Nam tay co uu tien cao nhat (khi nam, ngon cai thuong cham ngon tro -> tranh nhan nham thanh pinch)
            if (isFistRaw) {
                return GestureState.FIST;
            }

            if (gestureState == GestureState.PINCH) {
                return indexPinchDist >= pinchOpenDistance ? GestureState.RELEASE : GestureState.PINCH;
            }
            return indexPinchDist <= pinchCloseDistance ? GestureState.PINCH : GestureState.RELEASE;
        }

        private void UpdateCursor(Vector3 indexTip, float timestampMs, int width, int height) {
            float x = Mathf.Clamp01(indexTip.x);
            float y = Mathf.Clamp01(1f - indexTip.y);

            float mouseX = filterX.Filter(x, timestampMs) * width;
            float mouseY = filterY.Filter(y, timestampMs) * height;

            cursorP = new Vector2(mouseX, mouseY);

            if (lastEmittedP != null) {
                float dist = Vector2.Distance(cursorP.Value, lastEmittedP.Value);
                if (dist < moveDeadZone) return;
            }

            lastEmittedP = cursorP;
            onEvent.Invoke("move", cursorP.Value);
        }

        private void UpdateGesture(GestureState gesture) {
            if (gesture == GestureState.UNKNOWN) return;

            if (gesture == gestureState) {
                pendingGesture = GestureState.UNKNOWN;
                pendingGestureFrames = 0;
                return;
            }

            if (gesture != pendingGesture) {
                pendingGesture = gesture;
                pendingGestureFrames = 1;
                return;
            }

            pendingGestureFrames++;
            if (pendingGestureFrames < gestureConfirmFrames) return;

            GestureState previous = gestureState;
            gestureState = gesture;
            pendingGesture = GestureState.UNKNOWN;
            pendingGestureFrames = 0;

            if (gesture == GestureState.FIST && previous != GestureState.FIST) {
                fistStartTime = Time.time;
            }

            if (previous == GestureState.FIST && gesture != GestureState.FIST) {
                float held = Time.time - fistStartTime;
                bool longEnough = held >= minFistSeconds;
                bool notTooLong = maxFistSeconds <= 0f || held <= maxFistSeconds;
                if (longEnough && notTooLong) {
                    onEvent.Invoke("select", fistPos);
                }
            }

            if (gesture == GestureState.PINCH && previous != GestureState.PINCH) {
                MouseDown();
            } else if (gesture != GestureState.PINCH && mouseState == MouseButtonState.DOWN) {
                MouseUp();
            }
        }

        private void HandleHandLost() {
            lostHandFrames++;
            if (lostHandFrames < lostHandFramesThreshold) return;

            if (mouseState == MouseButtonState.DOWN) MouseUp();
            gestureState = GestureState.RELEASE;
            pendingGesture = GestureState.UNKNOWN;
            pendingGestureFrames = 0;
            isFistRaw = false; // mat tay thi huy, khong ban select

            filterX.Reset(); filterY.Reset();
            cursorP = null; lastEmittedP = null;
        }

        private void MouseDown() {
            if (mouseState == MouseButtonState.DOWN) return;
            mouseState = MouseButtonState.DOWN;
            onEvent.Invoke("down", cursorP ?? Vector2.zero);
        }

        private void MouseUp() {
            if (mouseState == MouseButtonState.UP) return;
            mouseState = MouseButtonState.UP;
            onEvent.Invoke("up", cursorP ?? Vector2.zero);
        }
    }
}