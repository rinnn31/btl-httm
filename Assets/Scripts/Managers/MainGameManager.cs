using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Mediapipe.Tasks.Components.Containers;
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

        [Header("Space Background (nen den vu tru, AR van track mat phang)")]
        public bool startInSpaceMode = false;
        [Tooltip("Anh nen vu tru (tot nhat la anh equirectangular 2:1). De trong = nen den + sao tu sinh.")]
        public Texture2D spaceSkyboxImage;
        public Color spaceBackgroundColor = Color.black;
        public int starCount = 1500;

        [Header("Virtual Hand 3D (hien khi Space Mode, toi da 2 tay)")]
        [Tooltip("TAY 1: object goc cua mo hinh ban tay 3D (dat san trong scene, se tu dong an luc dau).")]
        public Transform handModelRoot;
        [Tooltip("TAY 1: 21 khop theo thu tu MediaPipe: 0 Wrist | 1-4 Thumb CMC,MCP,IP,Tip | 5-8 Index MCP,PIP,DIP,Tip | 9-12 Middle | 13-16 Ring | 17-20 Pinky. Bat buoc: 0,5,9,17. Tip (4,8,12,16,20) nen gan bone 'end' cua rig.")]
        public Transform[] handModelBones = new Transform[21];
        [Tooltip("TAY 2 (tuy chon): de trong thi chi render 1 tay. Co the dung prefab tay thu 2 (nen la ban lat guong neu can).")]
        public Transform handModelRootABC;
        [Tooltip("TAY 2: 21 khop, cung thu tu nhu TAY 1.")]
        public Transform[] handModelBonesABC = new Transform[21];
        [Tooltip("Khoang cach (m) tu camera toi ban tay.")]
        public float handDepth = 0.6f;
        [Tooltip("He so landmark.z -> do sau (m). Am = gan camera hon.")]
        public float handZScale = 0.5f;
        public float handSmoothing = 20f;
        public int handLostFramesToHide = 6;
        [Tooltip("Neu chi thay 1 tay: tay do duoc gan cho mo hinh dang theo doi no neu cach co tay lan truoc <= gia tri nay (m, trong khong gian camera).")]
        public float handMatchDistance = 0.25f;

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

        private bool isSpaceMode = false;
        private ARCameraBackground arCameraBackground;
        private CameraClearFlags savedClearFlags;
        private Color savedBackgroundColor;
        private Material savedSkybox;
        private bool savedBgEnabled;
        private Material spaceSkyboxMat;
        private Transform starRoot;
        private Texture2D starTex;
        private Material starMat;

        private HandTracker handTracker;
        private class HandState {
            public Transform root;
            public Transform[] bones;
            public bool ready;
            public bool visible;
            public bool hasPose;
            public int lostFrames;
            public float restPalmLen = 1f;
            public readonly Vector3[] target = new Vector3[21];   // khong gian camera
            public readonly Vector3[] current = new Vector3[21];
            public readonly Vector3[] world = new Vector3[21];
        }
        private readonly HandState[] handStates = { new HandState(), new HandState() };
        private readonly Vector3[][] handTemp = { new Vector3[21], new Vector3[21] };
        private readonly int[] handDetForSlot = { -1, -1 };
        private static readonly int[] HandSolveOrder = { 1, 2, 3, 5, 6, 7, 9, 10, 11, 13, 14, 15, 17, 18, 19 };

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

            handTracker = GetComponent<HandTracker>();
            if (handTracker != null) {
                handTracker.HandsDetected += OnVirtualHandsDetected;
            }
            SetupHandModel();

            if (startInSpaceMode) {
                SetSpaceMode(true);
            }
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


        // ===================== SPACE BACKGROUND =====================
        // Chi tat phan VE video camera (ARCameraBackground). ARSession / ARPlaneManager /
        // ARCameraManager van chay nen mat phang van duoc track va universeGroup van bam ARAnchor.
        public bool IsSpaceMode => isSpaceMode;

        public void ToggleSpaceMode() {
            SetSpaceMode(!isSpaceMode);
        }

        public void SetSpaceMode(bool on) {
            EnsureMainCamera();
            if (mainCamera == null || on == isSpaceMode) {
                return;
            }

            if (on) {
                if (arCameraBackground == null) {
                    arCameraBackground = FindAnyObjectByType<ARCameraBackground>();
                }

                savedClearFlags = mainCamera.clearFlags;
                savedBackgroundColor = mainCamera.backgroundColor;
                savedSkybox = RenderSettings.skybox;
                savedBgEnabled = arCameraBackground != null && arCameraBackground.enabled;

                if (arCameraBackground != null) {
                    arCameraBackground.enabled = false;
                }

                if (spaceSkyboxImage != null && spaceSkyboxMat == null) {
                    Shader sh = Shader.Find("Skybox/Panoramic");
                    if (sh != null) {
                        spaceSkyboxMat = new Material(sh);
                        spaceSkyboxMat.SetTexture("_MainTex", spaceSkyboxImage);
                    }
                }

                if (spaceSkyboxMat != null) {
                    RenderSettings.skybox = spaceSkyboxMat;
                    mainCamera.clearFlags = CameraClearFlags.Skybox;
                } else {
                    mainCamera.clearFlags = CameraClearFlags.SolidColor;
                    mainCamera.backgroundColor = spaceBackgroundColor;
                    if (starRoot == null) {
                        BuildStarField();
                    }
                    starRoot.gameObject.SetActive(true);
                }
            } else {
                mainCamera.clearFlags = savedClearFlags;
                mainCamera.backgroundColor = savedBackgroundColor;
                RenderSettings.skybox = savedSkybox;
                HideVirtualHand();
                if (arCameraBackground != null) {
                    arCameraBackground.enabled = savedBgEnabled;
                }
                if (starRoot != null) {
                    starRoot.gameObject.SetActive(false);
                }
            }

            isSpaceMode = on;
        }

        private void BuildStarField() {
            float farClip = mainCamera != null ? mainCamera.farClipPlane : 100f;
            float radius = Mathf.Clamp(farClip * 0.8f, 5f, 50f);

            var go = new GameObject("StarField");
            starRoot = go.transform;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = starCount;
            main.startSpeed = 0f;
            main.startLifetime = 1e9f;

            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;

            starTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            starTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 32; y++) {
                for (int x = 0; x < 32; x++) {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                    float a = Mathf.Clamp01(1f - d);
                    starTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            starTex.Apply();

            Shader sh = Shader.Find("Sprites/Default");
            starMat = new Material(sh != null ? sh : Shader.Find("Hidden/InternalErrorShader"));
            starMat.mainTexture = starTex;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = starMat;
            psr.minParticleSize = 0f;
            psr.maxParticleSize = 0.05f;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < starCount; i++) {
                ep.position = Random.onUnitSphere * radius;
                ep.velocity = Vector3.zero;
                ep.startLifetime = 1e9f;
                ep.startSize = radius * Random.Range(0.006f, 0.02f);
                float b = Random.Range(0.4f, 1f);
                ep.startColor = new Color(b, b, b, 1f);
                ps.Emit(ep, 1);
            }
            ps.Pause(true);

            go.SetActive(false);
        }

        // ===================== VIRTUAL HAND 3D (toi da 2 tay) =====================
        // Mo hinh co xuong (rig) duoc dieu khien bang landmark MediaPipe:
        // moi xuong duoc xoay sao cho huong "khop -> khop ke tiep" trung voi huong landmark.
        private void SetupHandModel() {
            SetupHandState(handStates[0], handModelRoot, handModelBones);
            SetupHandState(handStates[1], handModelRootABC, handModelBonesABC);
        }

        private void SetupHandState(HandState st, Transform root, Transform[] bones) {
            st.ready = false;
            st.root = root;
            st.bones = bones;
            if (root == null || bones == null || bones.Length < 21) {
                return;
            }

            // Wrist(0), Index MCP(5), Middle MCP(9), Pinky MCP(17) la bat buoc
            if (bones[0] == null || bones[5] == null || bones[9] == null || bones[17] == null) {
                Debug.LogWarning($"[MainGameManager] Hand model '{root.name}' thieu bone bat buoc (0, 5, 9, 17).");
                return;
            }

            // Khong de collider cua tay chan raycast PickBody; tat Animator de khong ghi de xuong
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) {
                c.enabled = false;
            }
            foreach (var a in root.GetComponentsInChildren<Animator>(true)) {
                a.enabled = false;
            }

            float palm = Vector3.Distance(bones[0].position, bones[9].position);
            st.restPalmLen = Mathf.Max(0.0001f, palm / Mathf.Max(0.0001f, root.lossyScale.x));

            root.gameObject.SetActive(false);
            st.ready = true;
        }

        private void LandmarksToCameraLocal(List<NormalizedLandmark> lm, Vector3[] outLocal) {
            Transform cam = mainCamera.transform;
            for (int i = 0; i < 21; i++) {
                // Cung cach map voi HandInteraction.ToScreen: x -> x, y -> 1 - y
                float d = Mathf.Max(0.15f, handDepth + lm[i].z * handZScale);
                Vector3 world = mainCamera.ViewportToWorldPoint(
                    new Vector3(Mathf.Clamp01(lm[i].x), Mathf.Clamp01(1f - lm[i].y), d));
                outLocal[i] = cam.InverseTransformPoint(world); // luu theo khong gian camera
            }
        }

        // Khoang cach co tay (khong gian camera) giua tay phat hien `det` va mo hinh `slot`; vo cuc neu mo hinh chua co pose
        private float HandSlotDist(int slot, int det) {
            var st = handStates[slot];
            if (!st.hasPose) {
                return float.PositiveInfinity;
            }
            return Vector3.Distance(handTemp[det][0], st.target[0]);
        }

        private int NearestDetForSlot(int slot, int n) {
            if (n <= 1) {
                return 0;
            }
            float d0 = HandSlotDist(slot, 0);
            float d1 = HandSlotDist(slot, 1);
            return d1 < d0 ? 1 : 0;
        }

        // Gan cac tay phat hien (n <= 2) cho 2 mo hinh sao cho moi mo hinh bam on dinh vao 1 ban tay that
        private void AssignDetectedHands(int n) {
            handDetForSlot[0] = -1;
            handDetForSlot[1] = -1;

            bool r0 = handStates[0].ready;
            bool r1 = handStates[1].ready;
            if (n == 0 || (!r0 && !r1)) {
                return;
            }

            if (!r1) { handDetForSlot[0] = NearestDetForSlot(0, n); return; }
            if (!r0) { handDetForSlot[1] = NearestDetForSlot(1, n); return; }

            if (n == 1) {
                float d0 = HandSlotDist(0, 0);
                float d1 = HandSlotDist(1, 0);
                int slot;
                if (float.IsInfinity(d0) && float.IsInfinity(d1)) {
                    slot = 0;
                } else {
                    slot = d0 <= d1 ? 0 : 1;
                    float best = Mathf.Min(d0, d1);
                    int other = 1 - slot;
                    // tay moi xuat hien xa tay dang theo doi va mo hinh con lai dang trong -> dung mo hinh con lai
                    if (best > handMatchDistance && !handStates[other].hasPose) {
                        slot = other;
                    }
                }
                handDetForSlot[slot] = 0;
                return;
            }

            // n == 2, ca 2 mo hinh san sang
            bool p0 = handStates[0].hasPose;
            bool p1 = handStates[1].hasPose;
            if (p0 && p1) {
                float straight = HandSlotDist(0, 0) + HandSlotDist(1, 1);
                float crossed = HandSlotDist(0, 1) + HandSlotDist(1, 0);
                bool swap = crossed < straight;
                handDetForSlot[0] = swap ? 1 : 0;
                handDetForSlot[1] = swap ? 0 : 1;
            } else if (p0) {
                int d = NearestDetForSlot(0, 2);
                handDetForSlot[0] = d;
                handDetForSlot[1] = 1 - d;
            } else if (p1) {
                int d = NearestDetForSlot(1, 2);
                handDetForSlot[1] = d;
                handDetForSlot[0] = 1 - d;
            } else {
                handDetForSlot[0] = 0;
                handDetForSlot[1] = 1;
            }
        }

        private void OnVirtualHandsDetected(List<List<NormalizedLandmark>> hands) {
            if (!isSpaceMode || mainCamera == null) {
                return;
            }

            int n = Mathf.Min(hands != null ? hands.Count : 0, 2);
            for (int h = 0; h < n; h++) {
                LandmarksToCameraLocal(hands[h], handTemp[h]);
            }

            AssignDetectedHands(n);

            for (int s = 0; s < 2; s++) {
                var st = handStates[s];
                if (!st.ready) {
                    continue;
                }

                int det = handDetForSlot[s];
                if (det >= 0) {
                    st.lostFrames = 0;
                    System.Array.Copy(handTemp[det], st.target, 21);

                    if (!st.hasPose) {
                        System.Array.Copy(st.target, st.current, 21);
                        st.hasPose = true;
                    }
                    if (!st.visible) {
                        st.visible = true;
                        st.root.gameObject.SetActive(true);
                    }
                } else if (st.visible) {
                    st.lostFrames++;
                    if (st.lostFrames >= handLostFramesToHide) {
                        HideHandState(st);
                    }
                }
            }
        }

        private void HideHandState(HandState st) {
            st.visible = false;
            st.hasPose = false;
            st.lostFrames = 0;
            if (st.root != null) {
                st.root.gameObject.SetActive(false);
            }
        }

        private void HideVirtualHand() {
            HideHandState(handStates[0]);
            HideHandState(handStates[1]);
        }

        private void UpdateVirtualHand() {
            if (!isSpaceMode || mainCamera == null) {
                return;
            }

            for (int s = 0; s < 2; s++) {
                var st = handStates[s];
                if (st.ready && st.visible) {
                    UpdateHandState(st);
                }
            }
        }

        private void UpdateHandState(HandState st) {
            Transform cam = mainCamera.transform;
            float k = 1f - Mathf.Exp(-handSmoothing * Time.deltaTime);
            for (int i = 0; i < 21; i++) {
                st.current[i] = Vector3.Lerp(st.current[i], st.target[i], k);
                st.world[i] = cam.TransformPoint(st.current[i]);
            }

            Transform[] bones = st.bones;
            Vector3[] w = st.world;
            Transform wrist = bones[0];

            // 1) Scale mo hinh cho vua kich thuoc long ban tay, dat co tay dung vi tri
            float palm = Vector3.Distance(w[0], w[9]);
            st.root.localScale = Vector3.one * Mathf.Max(0.0001f, palm / st.restPalmLen);
            wrist.position = w[0];

            // 2) Xoay co tay: huong co tay -> ngon giua, roi xoay quanh truc do cho khop (ngon tro -> ngon ut)
            Vector3 curFwd = bones[9].position - wrist.position;
            Vector3 wantFwd = w[9] - w[0];
            if (curFwd.sqrMagnitude > 1e-10f && wantFwd.sqrMagnitude > 1e-10f) {
                wrist.rotation = Quaternion.FromToRotation(curFwd, wantFwd) * wrist.rotation;

                Vector3 axis = wantFwd.normalized;
                Vector3 curSide = Vector3.ProjectOnPlane(bones[17].position - bones[5].position, axis);
                Vector3 wantSide = Vector3.ProjectOnPlane(w[17] - w[5], axis);
                if (curSide.sqrMagnitude > 1e-10f && wantSide.sqrMagnitude > 1e-10f) {
                    float angle = Vector3.SignedAngle(curSide, wantSide, axis);
                    wrist.rotation = Quaternion.AngleAxis(angle, axis) * wrist.rotation;
                }
            }

            // 3) Tung ngon: xoay cha truoc, con sau (HandSolveOrder da theo thu tu cha -> con)
            foreach (int i in HandSolveOrder) {
                Transform bone = bones[i];
                Transform child = bones[i + 1];
                if (bone == null || child == null) {
                    continue;
                }

                Vector3 cur = child.position - bone.position;
                Vector3 want = w[i + 1] - w[i];
                if (cur.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) {
                    continue;
                }

                bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
            }
        }

        private void LateUpdate() {
            UpdateVirtualHand();

            // Sao di theo vi tri camera (khong theo huong) -> luc nao cung o "vo cuc"
            if (isSpaceMode && starRoot != null && starRoot.gameObject.activeSelf && mainCamera != null) {
                starRoot.position = mainCamera.transform.position;
            }

#if UNITY_EDITOR
            if (UnityEngine.Input.GetKeyDown(KeyCode.B)) {
                ToggleSpaceMode();
            }
#endif
        }

        private void OnDestroy() {
            if (handTracker != null) {
                handTracker.HandsDetected -= OnVirtualHandsDetected;
            }
            if (spaceSkyboxMat != null) Destroy(spaceSkyboxMat);
            if (starTex != null) Destroy(starTex);
            if (starMat != null) Destroy(starMat);
            if (starRoot != null) Destroy(starRoot.gameObject);
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

        public void AddCelestial(Celestial celestialTemplate, Vector3? pos = null, Vector3? velocity = null, float? radius = null, float? mass = null) {
            DeselectCelestial();

            if (pendingCelestial != null) {
                CancelPending();
            }

            var celestial = Instantiate(celestialTemplate, universeGroup);
            celestial.id = System.Guid.NewGuid().ToString();
            celestial.isPending = true;
            celestial.velocity = velocity == null ? Vector3.zero : velocity.Value;
            if (radius != null) {
                celestial.radius = radius.Value;
            }
            if (mass != null) {
                celestial.mass = mass.Value;
            }
            UpdateActiveDragPlane(universeGroup != null ? universeGroup.position : Vector3.zero);

            Vector2 screenCenter = mainCamera != null
    ? new Vector2(mainCamera.pixelWidth * 0.5f, mainCamera.pixelHeight * 0.5f)
    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Vector3 baseSpawnPos;
            if (!ScreenToPlane(screenCenter, out baseSpawnPos) && mainCamera != null) {
                baseSpawnPos = mainCamera.transform.position + mainCamera.transform.forward * 1.2f;
            }

            Vector3 finalSpawnPos = FindEmptyPosition(baseSpawnPos, celestial.radius);
            celestial.transform.position = pos == null ? finalSpawnPos : pos.Value;
            ClampToUniversePlane(celestial.transform);

            SetPendingCelestial(celestial);

            if (PendingActionUI.Instance != null) {
                PendingActionUI.Instance.Show(celestial);
            }

            allowFirstTouchToPlace = true;
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

        public void TogglePause(bool? pause = null) {
            if (pause == null) UniverseSystem.Instance.pause = !UniverseSystem.Instance.pause;
            else UniverseSystem.Instance.pause = pause.Value;
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
                    ApplyTwoBodyOrbit(pendingCelestial, lockedPrimaryBody);
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

        private bool allowFirstTouchToPlace = false;

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
                    if (allowFirstTouchToPlace) {
                        if (ScreenToPlane(pos, out Vector3 worldHit)) {
                            pendingCelestial.transform.position = worldHit;
                            ClampToUniversePlane(pendingCelestial.transform);
                        }
                    } else {
                        allowFirstTouchToPlace = false;
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

        public void ResetGame() {
            CancelPending();

            // 2. Bỏ chọn thiên thể hiện tại và ẩn bảng thông tin
            DeselectCelestial();

            // 3. Lấy tất cả các thiên thể hiện có trong scene và xóa chúng
            Celestial[] allBodies = FindObjectsByType<Celestial>(FindObjectsSortMode.None);
            foreach (var body in allBodies) {
                if (body != null) {
                    if (UniverseSystem.Instance != null) {
                        UniverseSystem.Instance.UnregisterBody(body);
                    }
                    Destroy(body.gameObject);
                }
            }

            // 4. Dọn dẹp toàn bộ nhãn tên (TextMesh) của hành tinh
            foreach (var kvp in celestialLabels) {
                if (kvp.Value != null) {
                    Destroy(kvp.Value.gameObject);
                }
            }
            celestialLabels.Clear();

            // 5. Xóa bộ nhớ đệm vết quỹ đạo
            activeTrails.Clear();

            // 6. Đặt lại các tham chiếu và biến trạng thái
            lockedPrimaryBody = null;
            dragBody = null;
            pressedBody = null;
            targetOrbitBody = null;
            isPointerDown = false;
            isStillPressDownSinceSelect = false;

            // 7. Ẩn đường kẻ tâm (Center Link Line)
            if (centerLinkLine != null) {
                centerLinkLine.positionCount = 0;
            }

            // 8. Đảm bảo trạng thái mô phỏng không bị tạm dừng (Unpause)
            if (UniverseSystem.Instance != null) {
                UniverseSystem.Instance.pause = false;
            }
        }

        /// <summary>
        /// Quy dao tron cua bai toan 2 vat:
        ///  - toc do tuong doi v_rel = sqrt(G (M + m) / r)
        ///  - vat con nhan v_rel * M/(M+m), vat me nhan nguoc chieu v_rel * m/(M+m)
        ///  => tong dong luong giu nguyen (cap khong bi troi), dung ca khi 2 khoi luong bang nhau.
        /// Khi M >> m thi ket qua gan nhu giong cong thuc cu.
        /// </summary>
        private void ApplyTwoBodyOrbit(Celestial child, Celestial primary) {
            Vector3 rVec = child.transform.position - primary.transform.position;

            // Dam bao khong bat dau ben trong vung va cham (UniverseSystem bo qua luc hap dan khi distance < sumR)
            float minDist = (child.radius + primary.radius) * 1.1f;
            if (rVec.magnitude < minDist) {
                Vector3 dir = rVec.sqrMagnitude > 1e-10f
                    ? rVec.normalized
                    : (universeGroup != null ? universeGroup.right : Vector3.right);
                rVec = dir * minDist;
                child.transform.position = primary.transform.position + rVec;
                ClampToUniversePlane(child.transform);
                rVec = child.transform.position - primary.transform.position;
            }

            float r = rVec.magnitude;
            if (r < 1e-5f || UniverseSystem.Instance == null) {
                child.velocity = Vector3.zero;
                return;
            }

            float G = UniverseSystem.Instance.gravitationalConstant;
            float M = primary.mass;
            float m = child.mass;
            float total = M + m;

            float vRel = Mathf.Sqrt(G * total / r);

            Vector3 planeNormal = universeGroup != null ? universeGroup.up : Vector3.up;
            Vector3 vDir = Vector3.Cross(planeNormal, rVec).normalized;

            Vector3 baseVel = primary.velocity; // van toc nen cua he (tam khoi)
            child.velocity = baseVel + vDir * (vRel * M / total);
            primary.velocity = baseVel - vDir * (vRel * m / total);
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
            SetSpaceMode(false);

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