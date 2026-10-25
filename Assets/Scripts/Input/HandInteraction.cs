using UnityEngine;
using UnityEngine.Events;
using UniverseSimAR.Utils;

namespace UniverseSimAR.Input {
    public enum GestureState { UNKNOWN, PINCH, PINCH2, RELEASE }
    public enum MouseButtonState { UP, DOWN }

    [System.Serializable]
    public class HandInteractionEvent : UnityEvent<string, Vector2> { }

    [RequireComponent(typeof(HandTracker))]
    public class HandInteraction : MonoBehaviour {
        [Header("Pinch Settings")]
        public float pinchMoveDistance = 0.4f;
        public float pinchCloseDistance = 0.15f;
        public float pinchOpenDistance = 0.4f;

        [Header("Middle Pinch Settings")]
        public float middlePinchCloseDistance = 0.15f;

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
        private float smoothedMiddlePinchRatio = 1.0f;

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

        private void HandTracker_HandDetected(System.Collections.Generic.List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> obj) {
            if (obj == null || obj.Count < 21) return;

            Vector3 wrist = new Vector3(obj[0].x, obj[0].y, obj[0].z);
            Vector3 thumbTip = new Vector3(obj[4].x, obj[4].y, obj[4].z);

            Vector3 indexMcp = new Vector3(obj[5].x, obj[5].y, obj[5].z);
            Vector3 middleMcp = new Vector3(obj[9].x, obj[9].y, obj[9].z);
            Vector3 pinkyMcp = new Vector3(obj[17].x, obj[17].y, obj[17].z);

            Vector3 indexTip = new Vector3(obj[8].x, obj[8].y, obj[8].z);
            Vector3 middleTip = new Vector3(obj[12].x, obj[12].y, obj[12].z);

            ProcessHandData(wrist, indexMcp, middleMcp, pinkyMcp, thumbTip, indexTip, middleTip);
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

        private void ProcessHandData(Vector3 wrist, Vector3 indexMcp, Vector3 middleMcp, Vector3 pinkyMcp, Vector3 thumbTip, Vector3 indexTip, Vector3 middleTip) {
            lostHandFrames = 0;

            float referenceLength = GetRobustPalmSize(wrist, indexMcp, middleMcp, pinkyMcp);

            float rawPinchRatio = Distance2D(thumbTip, indexTip) / referenceLength;
            smoothedPinchRatio = Mathf.Lerp(smoothedPinchRatio, rawPinchRatio, 0.6f);

            float rawMiddlePinchRatio = Distance2D(thumbTip, middleTip) / referenceLength;
            smoothedMiddlePinchRatio = Mathf.Lerp(smoothedMiddlePinchRatio, rawMiddlePinchRatio, 0.6f);

            Vector3 cursorTarget = Vector3.Lerp(indexTip, thumbTip, 0.3f);
            if (smoothedPinchRatio <= pinchMoveDistance) {
                UpdateCursor(cursorTarget, Time.time * 1000f, Screen.width, Screen.height);
            }

            GestureState gesture = DetectGesture(smoothedPinchRatio, smoothedMiddlePinchRatio);

            Debug.Log($"[Hand] IndexPinch = {smoothedPinchRatio:F2} | MiddlePinch = {smoothedMiddlePinchRatio:F2} | state = {gesture.ToString()}");
            UpdateGesture(gesture);
        }

        private GestureState DetectGesture(float indexPinchDist, float middlePinchDist) {
            if (middlePinchDist <= middlePinchCloseDistance) {
                return GestureState.PINCH2;
            }

            if (gestureState == GestureState.PINCH) {
                return indexPinchDist >= pinchOpenDistance ? GestureState.RELEASE : GestureState.PINCH;
            }
            return indexPinchDist <= pinchCloseDistance ? GestureState.PINCH : GestureState.RELEASE;
        }

        private void UpdateCursor(Vector3 indexTip, float timestampMs, int width, int height) {
            float x = Mathf.Clamp(1f - indexTip.x, 0f, 1f);
            float y = Mathf.Clamp(indexTip.y, 0f, 1f);

            float mouseX = filterX.Filter(1f - x, timestampMs) * width;
            float mouseY = filterY.Filter(1f - y, timestampMs) * height;

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

            if (gesture == GestureState.PINCH2 && previous != GestureState.PINCH2) {
                onEvent.Invoke("pinch2", cursorP ?? Vector2.zero);
            } else if (gesture == GestureState.PINCH && previous != GestureState.PINCH) {
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