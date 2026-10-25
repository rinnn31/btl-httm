using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace UniverseSimAR.UI.Components {
    public class WorldUIHandler : MonoBehaviour {
        public static WorldUIHandler Instance { get; private set; }

        private GameObject currentPressedUI;
        private PointerEventData uiPointerData;
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private Camera arCamera;

        private void Awake() {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        private void Start() {
            EnsureCanvasCameras();
        }

        public void EnsureCanvasCameras() {
            if (arCamera == null) {
                arCamera = Camera.main;
                if (arCamera == null) {
                    var arCamBg = FindAnyObjectByType<ARCameraBackground>();
                    if (arCamBg != null) arCamera = arCamBg.GetComponent<Camera>();
                }
            }
            if (arCamera == null) return;

            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            foreach (Canvas c in canvases) {
                if (c.renderMode == RenderMode.WorldSpace && c.worldCamera == null) {
                    c.worldCamera = arCamera;
                }
            }
        }

        public bool HandlePointerDown(Vector2 screenPosition) {

            Debug.Log($"Down pos: {screenPosition.x} {screenPosition.y}");
            if (EventSystem.current == null) return false;
            EnsureCanvasCameras();

            uiPointerData ??= new PointerEventData(EventSystem.current);
            uiPointerData.Reset();
            uiPointerData.position = screenPosition;
            uiPointerData.pressPosition = screenPosition;
            uiPointerData.button = PointerEventData.InputButton.Left;

            raycastResults.Clear();
            EventSystem.current.RaycastAll(uiPointerData, raycastResults);

            foreach (RaycastResult result in raycastResults) {
                if (result.gameObject.transform.IsChildOf(transform)) {
                    GameObject hitObj = result.gameObject;
                    uiPointerData.pointerCurrentRaycast = result;
                    uiPointerData.pointerPressRaycast = result;

                    GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitObj);
                    currentPressedUI = clickHandler != null ? clickHandler : hitObj;

                    ExecuteEvents.ExecuteHierarchy(hitObj, uiPointerData, ExecuteEvents.pointerEnterHandler);
                    ExecuteEvents.ExecuteHierarchy(hitObj, uiPointerData, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.ExecuteHierarchy(hitObj, uiPointerData, ExecuteEvents.selectHandler);
                    return true;
                }
            }

            GameObject fallbackButton = FindClosestButton(screenPosition, 60f);
            if (fallbackButton != null) {
                currentPressedUI = fallbackButton;
                ExecuteEvents.ExecuteHierarchy(currentPressedUI, uiPointerData, ExecuteEvents.pointerDownHandler);
                return true;
            }

            return false;
        }

        private GameObject FindClosestButton(Vector2 screenPos, float maxDist) {
            Button[] buttons = GetComponentsInChildren<Button>(false);
            Button closest = null;
            float minDist = maxDist;

            foreach (var btn in buttons) {
                if (!btn.gameObject.activeInHierarchy) continue;
                Vector2 btnScreenPos = RectTransformUtility.WorldToScreenPoint(arCamera, btn.transform.position);
                float dist = Vector2.Distance(screenPos, btnScreenPos);
                if (dist < minDist) {
                    minDist = dist;
                    closest = btn;
                }
            }
            return closest != null ? closest.gameObject : null;
        }

        public void HandlePointerUp(Vector2 screenPosition) {
            if (currentPressedUI == null || uiPointerData == null) return;

            uiPointerData.position = screenPosition;
            ExecuteEvents.ExecuteHierarchy(currentPressedUI, uiPointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(currentPressedUI, uiPointerData, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(currentPressedUI, uiPointerData, ExecuteEvents.pointerExitHandler);

            currentPressedUI = null;
        }
    }
}