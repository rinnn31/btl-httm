using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UniverseSimAR.Managers {
    public class PrepareGameManager : MonoBehaviour {
        [Header("AR Prefabs")]
        public GameObject gridPrefab;
        public GameObject reticlePrefab;

        [Header("AR Components")]
        public ARRaycastManager raycastManager;
        public ARAnchorManager anchorManager;
        public ARPlaneManager planeManager;
        public GameObject arContainer;

        [Header("Events")]
        public UnityEvent<bool> OnHitChanged = new UnityEvent<bool>();
        public UnityEvent<bool> OnAnchorChanged = new UnityEvent<bool>();

        private GameObject reticleInstance;
        private ARAnchor currentAnchor;
        private readonly List<ARRaycastHit> hits = new();

        private bool isPlacing;
        private bool lastHitState;
        private bool isHit;

        public static PrepareGameManager Instance { get; private set; }

        public bool HasAnchor => currentAnchor != null;
        public bool HasHit => isHit;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (reticlePrefab != null) {
                reticleInstance = Instantiate(reticlePrefab);
                reticleInstance.SetActive(false);
            }
        }

        private void Start() {
            isHit = false;
            lastHitState = false;
            OnHitChanged.Invoke(false);
            OnAnchorChanged.Invoke(false);
        }

        private void Update() {
            if (currentAnchor != null || isPlacing) {
                return;
            }

            if (raycastManager == null || reticleInstance == null) {
                return;
            }

            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

            isHit = raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon);

            if (isHit && hits.Count > 0) {
                Pose hitPose = hits[0].pose;

                reticleInstance.transform.SetPositionAndRotation(
                    hitPose.position,
                    hitPose.rotation
                );

                if (!reticleInstance.activeSelf) {
                    reticleInstance.SetActive(true);
                }
            } else {
                if (reticleInstance.activeSelf) {
                    reticleInstance.SetActive(false);
                }
            }

            if (isHit != lastHitState) {
                lastHitState = isHit;
                OnHitChanged.Invoke(isHit);
            }

            if (isHit &&
                reticleInstance.activeSelf && UnityEngine.Input.touchCount > 0) {
                Touch touch = UnityEngine.Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began) {
                    Pose pose = new Pose(
                        reticleInstance.transform.position,
                        reticleInstance.transform.rotation
                    );

                    _ = PlaceGrid(pose);
                }
            }
        }

        private async Task PlaceGrid(Pose pose) {
            if (isPlacing || currentAnchor != null) {
                return;
            }

            if (anchorManager == null || gridPrefab == null) {
                Debug.LogError(
                    "Chưa gán AnchorManager hoặc Grid Prefab!"
                );
                return;
            }

            isPlacing = true;

            try {
                var result = await anchorManager.TryAddAnchorAsync(pose);

                if (this == null) {
                    return;
                }

                if (!result.status.IsSuccess() || result.value == null) {
                    Debug.LogWarning("Không thể tạo AR Anchor.");
                    return;
                }

                currentAnchor = result.value;

                GameObject grid = Instantiate(
                    gridPrefab,
                    currentAnchor.transform
                );

                grid.transform.localPosition = Vector3.zero;
                grid.transform.localRotation = Quaternion.identity;

                if (reticleInstance != null) {
                    reticleInstance.SetActive(false);
                }

                isHit = false;
                lastHitState = false;
                OnHitChanged.Invoke(false);

                if (planeManager != null) {
                    planeManager.enabled = false;

                    foreach (var plane in planeManager.trackables) {
                        plane.gameObject.SetActive(false);
                    }
                }

                OnAnchorChanged.Invoke(true);
            }
            catch (System.Exception e) {
                Debug.LogException(e);
            }
            finally {
                if (this != null) {
                    isPlacing = false;
                }
            }
        }

        public void ResetState() {
            if (isPlacing) {
                return;
            }

            if (currentAnchor != null) {
                Destroy(currentAnchor.gameObject);
                currentAnchor = null;
            }

            if (planeManager != null) {
                foreach (var plane in planeManager.trackables) {
                    plane.gameObject.SetActive(true);
                }

                planeManager.enabled = true;
            }

            if (reticleInstance != null) {
                reticleInstance.SetActive(false);
            }

            isHit = false;
            lastHitState = false;

            OnHitChanged.Invoke(false);
            OnAnchorChanged.Invoke(false);
        }

        public void ConfirmAnchor() {
            DontDestroyOnLoad(arContainer);
            SceneManager.LoadScene("MainScene");
        }

        private void OnDestroy() {
            if (Instance == this) {
                Instance = null;
            }
        }
    }
}