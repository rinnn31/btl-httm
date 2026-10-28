using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UniverseSimAR.Input;
using UniverseSimAR.UI.Components;
using UniverseSimAR.Core;

namespace UniverseSimAR.Managers {
    public class MainGameManager : MonoBehaviour {
        public static MainGameManager Instance { get; private set; }

        [Header("References")]
        public Transform universeGroup;

        [Header("Orbit Trail Settings")]
        public Material orbitLineMaterial;
        public bool showActiveOrbits = true;
        public float orbitTrailTime = 6f;
        public float orbitTrailWidth = 0.003f;
        public float minTrailVertexDistance = 0.02f;

        [Header("Label Settings")]
        public bool showPlanetLabels = true;
        public float labelHeightOffset = 0.15f;
        public int labelFontSize = 36;
        public float labelCharacterSize = 0.02f;
        public Color labelColor = Color.white;

        [Header("Fling Settings")]
        public float flingVelocityScale = 0.75f;
        private const float DEADZONE_SPEED = 0.05f;
        private const float MAX_SPEED = 300f;

        private bool isOrbitMode = true;
        private bool isFlingMode = false;

        private bool isStillPressDownSinceSelect = false;

        private Celestial pendingCelestial;
        private Celestial selectedCelestial;
        private Celestial lockedPrimaryBody;
        private Celestial dragBody;

        private Celestial pressedBody = null;
        private Vector2 pressScreenPos;
        private bool isPointerDown = false;

        private Vector3 savedVelocity;
        private Vector3 dragOffset;
        private Plane activeDragPlane;

        private Celestial targetOrbitBody = null;
        private LineRenderer centerLinkLine;
        private Material cachedLineMaterial;

        private readonly Dictionary<Celestial, TextMesh> celestialLabels = new Dictionary<Celestial, TextMesh>();
        private readonly List<Celestial> cleanupCache = new List<Celestial>();
        private readonly Dictionary<Celestial, TrailRenderer> activeTrails = new Dictionary<Celestial, TrailRenderer>();

        private bool isInteractionOnUI = false;

        private struct PointerTrail {
            public float time;
            public Vector2 pos;
        }
        private readonly List<PointerTrail> trail = new List<PointerTrail>();
        private Vector2 pointerPos;

        private Camera mainCamera;
        private HandInteraction handInteraction;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
            } else {
                Instance = this;
            }
        }

        private void Start() {
            EnsureMainCamera();

            if (universeGroup != null) {
                ARAnchor anchor = FindAnyObjectByType<ARAnchor>();
                if (anchor != null) {
                    universeGroup.SetParent(anchor.transform);
                    universeGroup.localPosition = Vector3.zero;
                    universeGroup.localRotation = Quaternion.identity;
                } else if (universeGroup.position.magnitude > 100f && mainCamera != null) {
                    universeGroup.position = mainCamera.transform.position + mainCamera.transform.forward * 1.5f;
                    universeGroup.rotation = Quaternion.identity;
                }
            }

            handInteraction = GetComponent<HandInteraction>();
            if (handInteraction != null) {
                handInteraction.onEvent.AddListener(HandleInteractionEvent);
            }

            SetupCenterLinkLine();
        }

        private void EnsureMainCamera() {
            if (mainCamera == null) {
                mainCamera = Camera.main;
                if (mainCamera == null) {
                    var arCamBg = FindAnyObjectByType<ARCameraBackground>();
                    if (arCamBg != null) {
                        mainCamera = arCamBg.GetComponent<Camera>();
                    }
                }
            }
        }

        private Material GetValidLineMaterial() {
            if (orbitLineMaterial != null) {
                return orbitLineMaterial;
            }

            if (cachedLineMaterial != null) {
                return cachedLineMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
            cachedLineMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            cachedLineMaterial.color = Color.white;
            return cachedLineMaterial;
        }

        private void SetupCenterLinkLine() {
            GameObject lineObj = new GameObject("CenterLinkLine");
            lineObj.transform.SetParent(transform);
            centerLinkLine = lineObj.AddComponent<LineRenderer>();
            centerLinkLine.startWidth = 0.005f;
            centerLinkLine.endWidth = 0.005f;
            centerLinkLine.startColor = Color.yellow;
            centerLinkLine.endColor = Color.yellow;
            centerLinkLine.positionCount = 0;
            centerLinkLine.useWorldSpace = true;
            centerLinkLine.sharedMaterial = GetValidLineMaterial();
        }

        private void Update() {
            EnsureMainCamera();

            Vector2 mp = ToCameraPixels(UnityEngine.Input.mousePosition);
            if (UnityEngine.Input.GetMouseButtonDown(0)) {
                ProcessInputDown(mp);
            } else if (UnityEngine.Input.GetMouseButton(0)) {
                ProcessInputMove(mp);
            } else if (UnityEngine.Input.GetMouseButtonUp(0)) {
                ProcessInputUp(mp);
            }

            float now = Time.time * 1000f;
            while (trail.Count > 0 && now - trail[0].time > 300f) {
                trail.RemoveAt(0);
            }

            UpdateCenterLinkLine();
            UpdateCelestialLabels();
            UpdateActiveOrbitTrails();
        }


        private void UpdateCelestialLabels() {
            Celestial[] allBodies = FindObjectsByType<Celestial>();
            HashSet<Celestial> currentSet = new HashSet<Celestial>(allBodies);

            cleanupCache.Clear();
            foreach (var kvp in celestialLabels) {
                if (kvp.Key == null || kvp.Key.isDead || !currentSet.Contains(kvp.Key)) {
                    if (kvp.Value != null) {
                        Destroy(kvp.Value.gameObject);
                    }
                    cleanupCache.Add(kvp.Key);
                }
            }
            foreach (var key in cleanupCache) {
                celestialLabels.Remove(key);
            }

            if (!showPlanetLabels || mainCamera == null) {
                foreach (var kvp in celestialLabels) {
                    if (kvp.Value != null) {
                        kvp.Value.gameObject.SetActive(false);
                    }
                }
                return;
            }

            foreach (var body in allBodies) {
                if (body == null || body.isDead) {
                    continue;
                }

                if (!celestialLabels.TryGetValue(body, out TextMesh labelMesh) || labelMesh == null) {
                    labelMesh = CreateLabelForBody(body);
                    celestialLabels[body] = labelMesh;
                }

                labelMesh.gameObject.SetActive(true);
                labelMesh.text = body.bodyType == CelestialType.Debris ? "" : body.celestialName;

                float visualRadius = Mathf.Max(body.radius * body.transform.lossyScale.x, 0.05f);

                labelMesh.transform.position = body.transform.position + Vector3.up * (visualRadius + labelHeightOffset);

                Vector3 directionToCamera = labelMesh.transform.position - mainCamera.transform.position;
                labelMesh.transform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
            }
        }

        private float GetVisualRadius(Celestial body) {
            Renderer r = body.GetComponentInChildren<Renderer>();
            if (r != null) {
                return r.bounds.extents.magnitude * 0.6f;
            }
            return Mathf.Max(body.radius * body.transform.lossyScale.x, 0.05f);
        }

        private TextMesh CreateLabelForBody(Celestial body) {
            GameObject labelObj = new GameObject($"Label_{body.name}");
            labelObj.transform.SetParent(universeGroup != null ? universeGroup : transform);

            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = body.bodyType == CelestialType.Debris ? "" : body.celestialName;
            tm.fontSize = labelFontSize;
            tm.characterSize = labelCharacterSize;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = labelColor;

            MeshRenderer mr = labelObj.GetComponent<MeshRenderer>();
            if (mr != null) {
                Font defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 14);

                if (defaultFont != null) {
                    tm.font = defaultFont;
                    mr.sharedMaterial = defaultFont.material;
                }
            }

            return tm;
        }

        private void UpdateActiveOrbitTrails() {
            Celestial[] allBodies = FindObjectsByType<Celestial>();
            bool isPaused = UniverseSystem.Instance != null && UniverseSystem.Instance.pause;

            foreach (var body in allBodies) {
                if (body == null || body.isDead) {
                    continue;
                }

                if (!activeTrails.TryGetValue(body, out TrailRenderer tr) || tr == null) {
                    tr = SetupTrailRendererForBody(body);
                    activeTrails[body] = tr;
                }

                tr.widthMultiplier = orbitTrailWidth;
                if (body.isPending || body == pendingCelestial || body == dragBody) {
                    tr.emitting = false;
                    tr.Clear();
                    continue;
                }

                tr.enabled = showActiveOrbits;
                if (!showActiveOrbits) {
                    continue;
                }

                tr.time = isPaused ? Mathf.Infinity : orbitTrailTime;
                tr.emitting = !isPaused;
            }
        }

        private TrailRenderer SetupTrailRendererForBody(Celestial body) {
            TrailRenderer tr = body.GetComponent<TrailRenderer>();
            if (tr == null) {
                tr = body.gameObject.AddComponent<TrailRenderer>();
            }

            tr.time = orbitTrailTime;
            tr.minVertexDistance = minTrailVertexDistance;
            tr.startWidth = 1f;
            tr.endWidth = 0f;
            tr.widthMultiplier = orbitTrailWidth;
            tr.autodestruct = false;
            tr.emitting = false;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;

            tr.sharedMaterial = GetValidLineMaterial();

            Color trailColor = new Color(0.52f, 0.95f, 0.84f, 0.85f);
            Renderer bodyRenderer = body.GetComponentInChildren<Renderer>();
            if (bodyRenderer != null && bodyRenderer.sharedMaterial != null && bodyRenderer.sharedMaterial.HasProperty("_Color")) {
                Color c = bodyRenderer.sharedMaterial.color;
                if (c.maxColorComponent > 0.1f) {
                    trailColor = new Color(c.r, c.g, c.b, 0.85f);
                }
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(trailColor, 0.0f), new GradientColorKey(trailColor, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            tr.colorGradient = gradient;
            tr.Clear();
            return tr;
        }

        private void UpdateCenterLinkLine() {
            if (centerLinkLine == null) {
                return;
            }

            if (isOrbitMode && pendingCelestial != null && targetOrbitBody != null && !targetOrbitBody.isDead) {
                centerLinkLine.positionCount = 2;
                centerLinkLine.SetPosition(0, pendingCelestial.transform.position);
                centerLinkLine.SetPosition(1, targetOrbitBody.transform.position);
            } else {
                if (centerLinkLine.positionCount > 0) {
                    centerLinkLine.positionCount = 0;
                }
            }
        }

        private void HandleInteractionEvent(string eventName, Vector2 pos) {
            if (eventName == "select") {
                MenuActionUI.Instance.ToggleVisible();
                return;
            }

            Vector2 mp = ToCameraPixels(UnityEngine.Input.mousePosition);

            switch (eventName) {
                case "down":
                    ProcessInputDown(pos);
                    break;
                case "move":
                    ProcessInputMove(pos);
                    break;
                case "up":
                    ProcessInputUp(pos);
                    break;
            }
        }

        private void ProcessInputDown(Vector2 pos) {
            pointerPos = pos;
            isPointerDown = true;
            isInteractionOnUI = WorldUIHandler.Instance != null && WorldUIHandler.Instance.HandlePointerDown(pos);

            if (!isInteractionOnUI) {
                OnPointerDown(pos);
            }
        }

        private void ProcessInputMove(Vector2 pos) {
            pointerPos = pos;
            if (!isPointerDown) {
                return;
            }

            if (!isInteractionOnUI) {
                OnPointerMove(pos);
            }
        }

        private void ProcessInputUp(Vector2 pos) {
            pointerPos = pos;
            isPointerDown = false;

            if (isInteractionOnUI) {
                if (WorldUIHandler.Instance != null) {
                    WorldUIHandler.Instance.HandlePointerUp(pos);
                }
                isInteractionOnUI = false;
            } else {
                OnPointerUp();
            }
        }

        public void AddCelestial(Celestial celestialTemplate) {
            DeselectCelestial();

            if (pendingCelestial != null) {
                CancelPending();
            }

            var celestial = Instantiate(celestialTemplate, universeGroup);
            celestial.id = System.Guid.NewGuid().ToString();
            celestial.isPending = true;
            celestial.velocity = Vector3.zero;

            UpdateActiveDragPlane(universeGroup != null ? universeGroup.position : Vector3.zero);

            Vector2 screenCenter = mainCamera != null
    ? new Vector2(mainCamera.pixelWidth * 0.5f, mainCamera.pixelHeight * 0.5f)
    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Vector3 baseSpawnPos;
            if (!ScreenToPlane(screenCenter, out baseSpawnPos) && mainCamera != null) {
                baseSpawnPos = mainCamera.transform.position + mainCamera.transform.forward * 1.2f;
            }

            Vector3 finalSpawnPos = FindEmptyPosition(baseSpawnPos, celestial.radius);
            celestial.transform.position = finalSpawnPos;
            ClampToUniversePlane(celestial.transform);

            SetPendingCelestial(celestial);

            if (PendingActionUI.Instance != null) {
                PendingActionUI.Instance.Show(celestial);
            }
        }

        private Vector3 FindEmptyPosition(Vector3 preferredPos, float newRadius) {
            Celestial[] existingBodies = FindObjectsByType<Celestial>();
            if (existingBodies.Length == 0) {
                return preferredPos;
            }

            Vector3 testPos = preferredPos;
            float stepRadius = 2.5f;
            int maxAttempts = 20;

            for (int i = 0; i < maxAttempts; i++) {
                bool isOverlap = false;
                foreach (var body in existingBodies) {
                    if (body == pendingCelestial || body.isDead) {
                        continue;
                    }
                    float minDist = body.radius + newRadius + 1.5f;
                    if (Vector3.Distance(testPos, body.transform.position) < minDist) {
                        isOverlap = true;
                        break;
                    }
                }

                if (!isOverlap) {
                    return testPos;
                }

                float angle = i * 1.5f;
                float r = stepRadius * (i + 1);
                testPos = preferredPos + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            }

            return preferredPos;
        }

        public void TogglePause() {
            UniverseSystem.Instance.pause = !UniverseSystem.Instance.pause;
        }

        private void SetPendingCelestial(Celestial tpl) {
            pendingCelestial = tpl;
            dragBody = null;
            pressedBody = null;
            dragOffset = Vector3.zero;
        }

        public void ConfirmPlacePending() {
            if (pendingCelestial == null) {
                return;
            }

            pendingCelestial.isPending = false;

            if (isOrbitMode) {
                if (targetOrbitBody != null) {
                    lockedPrimaryBody = targetOrbitBody;
                }

                if (lockedPrimaryBody != null && lockedPrimaryBody != pendingCelestial) {
                    pendingCelestial.velocity = GetOrbitVelocity(pendingCelestial.transform.position, lockedPrimaryBody);
                } else {
                    pendingCelestial.velocity = Vector3.zero;
                }
            } else {
                pendingCelestial.velocity = Vector3.zero;
            }

            if (UniverseSystem.Instance != null) {
                UniverseSystem.Instance.RegisterBody(pendingCelestial);
            }

            if (lockedPrimaryBody == null) {
                lockedPrimaryBody = pendingCelestial;
            }

            if (centerLinkLine != null) {
                centerLinkLine.positionCount = 0;
            }

            pendingCelestial = null;
            dragBody = null;
            pressedBody = null;
            targetOrbitBody = null;
        }

        public void CancelPending() {
            if (pendingCelestial != null) {
                Destroy(pendingCelestial.gameObject);
                pendingCelestial = null;
                dragBody = null;
                pressedBody = null;
                targetOrbitBody = null;
                if (centerLinkLine != null) {
                    centerLinkLine.positionCount = 0;
                }
            }
        }

        private void SelectCelestial(Celestial body) {
            if (pendingCelestial != null || body == null || body.isPending) {
                return;
            }

            selectedCelestial = body;
            if (SelectedActionUI.Instance != null) {
                SelectedActionUI.Instance.Show(selectedCelestial);
            }
        }

        private void DeselectCelestial() {
            selectedCelestial = null;
            if (SelectedActionUI.Instance != null) {
                SelectedActionUI.Instance.Hide();
            }
        }

        public void DeleteSelectedCelestial() {
            if (selectedCelestial == null) {
                return;
            }

            Celestial toDelete = selectedCelestial;
            DeselectCelestial();

            if (lockedPrimaryBody == toDelete) {
                lockedPrimaryBody = null;
            }
            if (dragBody == toDelete) {
                dragBody = null;
            }

            toDelete.isDead = true;
            Destroy(toDelete.gameObject);
        }

        private void OnPointerDown(Vector2 pos) {
            trail.Clear();
            trail.Add(new PointerTrail { time = Time.time * 1000f, pos = pos });

            pressScreenPos = pos;
            dragBody = null;
            pressedBody = null;

            Celestial hitBody = PickBody(pos);

            if (pendingCelestial != null) {
                if (hitBody == pendingCelestial) {
                    pressedBody = pendingCelestial;
                    UpdateActiveDragPlane(pendingCelestial.transform.position);
                    if (ScreenToPlane(pos, out Vector3 hitPoint)) {
                        dragOffset = pendingCelestial.transform.position - hitPoint;
                    }
                } else {
                    if (ScreenToPlane(pos, out Vector3 worldHit)) {
                        pendingCelestial.transform.position = worldHit;
                        ClampToUniversePlane(pendingCelestial.transform);
                    }
                }
                pendingCelestial.velocity = Vector3.zero;
                CheckCollisionWithOtherBodies();
                return;
            }

            if (hitBody != null && !hitBody.isPending) {
                pressedBody = hitBody;
                SelectCelestial(hitBody);

                isStillPressDownSinceSelect = hitBody != selectedCelestial;

                savedVelocity = hitBody.velocity;
                hitBody.velocity = Vector3.zero;
                UpdateActiveDragPlane(hitBody.transform.position);

                if (ScreenToPlane(pos, out Vector3 hitPoint)) {
                    dragOffset = hitBody.transform.position - hitPoint;
                }
            } else {
                pressedBody = null;
                dragBody = null;
                DeselectCelestial();
            }
        }

        private void OnPointerMove(Vector2 pos) {
            if (!isPointerDown || pressedBody == null || isStillPressDownSinceSelect) {
                return;
            }

            trail.Add(new PointerTrail { time = Time.time * 1000f, pos = pos });

            if (dragBody == null) {
                if (Vector2.Distance(pos, pressScreenPos) > 6f) {
                    dragBody = pressedBody;
                } else {
                    return;
                }
            }

            if (dragBody != null && ScreenToPlane(pos, out Vector3 worldHit)) {
                dragBody.transform.position = worldHit + dragOffset;
                ClampToUniversePlane(dragBody.transform);

                if (dragBody == pendingCelestial) {
                    CheckCollisionWithOtherBodies();
                }
            }
        }

        private void CheckCollisionWithOtherBodies() {
            if (!isOrbitMode || pendingCelestial == null) {
                targetOrbitBody = null;
                if (centerLinkLine != null) {
                    centerLinkLine.positionCount = 0;
                }
                return;
            }

            float pendingVisualRadius = GetVisualRadius(pendingCelestial);
            Celestial[] allBodies = FindObjectsByType<Celestial>();

            foreach (var body in allBodies) {
                if (body == pendingCelestial || body.isDead) {
                    continue;
                }

                float distance = Vector3.Distance(pendingCelestial.transform.position, body.transform.position);
                float bodyVisualRadius = GetVisualRadius(body);
                float sumRadius = Mathf.Max(pendingCelestial.radius + body.radius, pendingVisualRadius + bodyVisualRadius);

                if (distance <= sumRadius) {
                    targetOrbitBody = body;
                    break;
                }
            }

            UpdateCenterLinkLine();
        }

        private Vector2 ToCameraPixels(Vector2 screenPos) {
            if (mainCamera == null) return screenPos;
            return new Vector2(
                screenPos.x / Screen.width * mainCamera.pixelWidth,
                screenPos.y / Screen.height * mainCamera.pixelHeight);
        }

        private void OnPointerUp() {
            isPointerDown = false;
            isStillPressDownSinceSelect = false;
            pressedBody = null;

            if (dragBody != null && dragBody != pendingCelestial) {
                if (!isFlingMode) {
                    dragBody.velocity = savedVelocity;
                } else {
                    float now = Time.time * 1000f;
                    PointerTrail pastSample = GetPointerSampleAt(now - 120f);
                    float dt = Mathf.Max((now - pastSample.time) / 1000f, 0.016f);

                    if (ScreenToPlane(pastSample.pos, out Vector3 v0) && ScreenToPlane(pointerPos, out Vector3 v1)) {
                        Vector3 deltaVec = v1 - v0;
                        float rawSpeed = deltaVec.magnitude / dt;

                        if (rawSpeed < DEADZONE_SPEED) {
                            dragBody.velocity = Vector3.zero;
                        } else {
                            Vector3 flingVel = deltaVec.normalized * ((rawSpeed - DEADZONE_SPEED) * flingVelocityScale);
                            if (flingVel.magnitude > MAX_SPEED) {
                                flingVel = flingVel.normalized * MAX_SPEED;
                            }

                            if (universeGroup != null) {
                                Vector3 localVel = universeGroup.InverseTransformDirection(flingVel);
                                localVel.y = 0f;
                                dragBody.velocity = universeGroup.TransformDirection(localVel);
                            } else {
                                dragBody.velocity = new Vector3(flingVel.x, 0f, flingVel.z);
                            }
                        }
                    }
                }
            }

            dragBody = null;
        }

        private void UpdateActiveDragPlane(Vector3 planeOrigin) {
            if (universeGroup == null) {
                activeDragPlane = new Plane(Vector3.up, planeOrigin);
                return;
            }

            Vector3 normal = universeGroup.up;
            activeDragPlane = new Plane(normal, planeOrigin);
        }

        private void ClampToUniversePlane(Transform target) {
            if (universeGroup == null) {
                return;
            }

            Vector3 localPos = universeGroup.InverseTransformPoint(target.position);
            localPos.y = 0f;
            target.position = universeGroup.TransformPoint(localPos);
        }

        private bool ScreenToPlane(Vector2 screenPos, out Vector3 worldPos) {
            if (mainCamera == null) {
                worldPos = Vector3.zero;
                return false;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            Vector3 planeOrigin = dragBody != null
                ? dragBody.transform.position
                : (universeGroup != null ? universeGroup.position : Vector3.zero);
            UpdateActiveDragPlane(planeOrigin);

            if (activeDragPlane.Raycast(ray, out float enter) && enter > 0f && enter <= 500f) {
                Vector3 hitPoint = ray.GetPoint(enter);
                if (universeGroup != null) {
                    Vector3 localPos = universeGroup.InverseTransformPoint(hitPoint);
                    localPos.y = 0f;
                    worldPos = universeGroup.TransformPoint(localPos);
                } else {
                    worldPos = hitPoint;
                }
                return true;
            }

            worldPos = Vector3.zero;
            return false;
        }

        private Celestial PickBody(Vector2 screenPos) {
            if (mainCamera == null) {
                return null;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) {
                Celestial hitCelestial = hit.collider.GetComponentInParent<Celestial>();
                if (hitCelestial != null && !hitCelestial.isDead) {
                    return hitCelestial;
                }
            }

            if (Physics.SphereCast(ray, 0.05f, out RaycastHit sphereHit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) {
                Celestial hitCelestial = sphereHit.collider.GetComponentInParent<Celestial>();
                if (hitCelestial != null && !hitCelestial.isDead) {
                    return hitCelestial;
                }
            }

            Celestial[] allBodies = FindObjectsByType<Celestial>();
            Celestial closestBody = null;
            float minScreenDist = 65f;

            foreach (var body in allBodies) {
                if (body == null || body.isDead) {
                    continue;
                }

                Vector3 screenPt = mainCamera.WorldToScreenPoint(body.transform.position);
                if (screenPt.z <= 0f) {
                    continue;
                }

                float dist2D = Vector2.Distance(screenPos, new Vector2(screenPt.x, screenPt.y));
                if (dist2D < minScreenDist) {
                    minScreenDist = dist2D;
                    closestBody = body;
                }
            }

            return closestBody;
        }

        private Vector3 GetOrbitVelocity(Vector3 pos, Celestial primary) {
            Vector3 rVec = pos - primary.transform.position;
            float r = rVec.magnitude;
            if (r < 1e-5f) {
                return Vector3.zero;
            }

            float G = UniverseSystem.Instance.gravitationalConstant;

            // 1. Dùng trực tiếp vận tốc quỹ đạo tròn (Circular Orbit Velocity) mà không nhân với eccentricityFactor ngẫu nhiên
            float vMag = Mathf.Sqrt((G * primary.mass) / r);

            // 2. Lấy pháp tuyến mặt phẳng quỹ đạo
            Vector3 planeNormal = universeGroup != null ? universeGroup.up : Vector3.up;

            // 3. Tính hướng vận tốc vuông góc với rVec và planeNormal
            Vector3 vDir = Vector3.Cross(planeNormal, rVec).normalized;

            // 4. Trả về vận tốc quỹ đạo tròn cộng với vận tốc của hành tinh mẹ (nếu có)
            return (vDir * vMag) + primary.velocity;
        }


        private PointerTrail GetPointerSampleSampleAt(float tMs) {
            if (trail.Count == 0) {
                return new PointerTrail { time = Time.time * 1000f, pos = pointerPos };
            }
            PointerTrail best = trail[0];
            foreach (var s in trail) {
                if (s.time <= tMs) {
                    best = s;
                } else {
                    break;
                }
            }
            return best;
        }

        private PointerTrail GetPointerSampleAt(float tMs) {
            return GetPointerSampleSampleAt(tMs);
        }

        public void BackToHomeScene() {
            if (universeGroup != null) {
                Destroy(universeGroup.gameObject);
            }

            ARAnchor anchor = FindAnyObjectByType<ARAnchor>();
            if (anchor != null) {
                Destroy(anchor.gameObject);
            }

            var arContainer = GameObject.Find("AR_Container");
            if (arContainer != null) {
                Destroy(arContainer);
            }

            SceneManager.LoadScene("HomeScene");
        }

        public void setPlaceAsOrbitMode(bool placeAsOrbitMode) {
            isOrbitMode = placeAsOrbitMode;
            if (!isOrbitMode) {
                targetOrbitBody = null;
                if (centerLinkLine != null) {
                    centerLinkLine.positionCount = 0;
                }
            }
        }

        public void setFlingMode(bool flingMode) {
            isFlingMode = flingMode;
        }
    }
}