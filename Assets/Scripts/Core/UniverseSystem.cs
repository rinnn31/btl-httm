using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UniverseSimAR.Core {
    public enum CollisionType { Merge, PartialAccretion, HitAndRun, Catastrophic, Absorb }

    public class CollisionEvent {
        public CollisionType type;
        public Celestial big;
        public Celestial small;
        public Celestial survivor;
        public List<Celestial> destroyed = new List<Celestial>();
        public List<Celestial> spawned = new List<Celestial>();
        public Vector3 point;
        public Vector3 normal;
        public Vector3 travelDirection;
        public float relativeSpeed;
        public float speedRatio;
        public float impactParameter;
        public float massRatio;
        public Vector3 centerVelocity;
        public float impactScale;
        public Vector3 orbitAxis;
        public float orbitMass;
        public float gravitationalConstant;
    }

    [System.Serializable]
    public class CollisionThresholds {
        public float catastrophicSpeedRatio = 2.5f;
        public float catastrophicMinMassRatio = 0.1f;
        public float hitAndRunImpact = 0.6f;
        public float hitAndRunSpeedRatio = 1.2f;
        public float mergeSpeedRatio = 0.8f;
        public float mergeImpact = 0.35f;
        public float partialMassRatio = 0.35f;
        public int maxDebris = 150;
    }

    public class UniverseSystem : MonoBehaviour {
        public static UniverseSystem Instance { get; private set; }

        [Header("Simulation Settings")]
        public float gravitationalConstant = 500f;
        public float timeScale = 1f;
        public int subSteps = 10;
        public bool pause = false;

        public CollisionThresholds thresholds = new CollisionThresholds();

        [Header("Prefabs")]
        public GameObject debrisPrefab;

        public UnityEvent<CollisionEvent> OnCollisionOccurred;

        private List<Celestial> celestials = new List<Celestial>();
        private Dictionary<string, string[]> contacts = new Dictionary<string, string[]>();

        private void Awake() {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;
        }

        public void RegisterBody(Celestial body) { if (!celestials.Contains(body)) celestials.Add(body); }
        public void UnregisterBody(Celestial body) { celestials.Remove(body); }

        private void FixedUpdate() {
            if (timeScale <= 0f || celestials.Count == 0 || pause) return;

            float stepDt = (Time.fixedDeltaTime * timeScale) / subSteps;
            for (int i = 0; i < subSteps; i++) {
                UpdatePhysics(stepDt);
            }
        }

        private void UpdatePhysics(float dt) {
            RefreshContacts();

            Dictionary<string, Vector3> forces = new Dictionary<string, Vector3>();
            List<Celestial> removed = new List<Celestial>();
            List<Celestial> spawned = new List<Celestial>();
            List<CollisionEvent> events = new List<CollisionEvent>();

            foreach (var body in celestials) {
                forces[body.id] = Vector3.zero;
            }

            for (int i = 0; i < celestials.Count; i++) {
                for (int j = i + 1; j < celestials.Count; j++) {
                    Celestial bodyA = celestials[i];
                    Celestial bodyB = celestials[j];

                    if (bodyA.isDead || bodyB.isDead || bodyA.isPending || bodyB.isPending) continue;

                    Vector3 direction = bodyB.transform.position - bodyA.transform.position;
                    float distance = direction.magnitude;
                    float sumR = bodyA.radius + bodyB.radius;

                    if (distance < sumR) {
                        string pKey = PairKey(bodyA, bodyB);
                        if (!contacts.ContainsKey(pKey)) {
                            CollisionEvent ev = ResolveCollision(bodyA, bodyB, distance, removed, spawned);
                            events.Add(ev);
                        }
                        continue; // Bỏ qua lực hấp dẫn nếu đang đâm vào nhau
                    }

                    if (distance > 0f) {
                        float forceMag = (gravitationalConstant * bodyA.mass * bodyB.mass) / (distance * distance);
                        Vector3 forceOnA = (direction / distance) * forceMag;
                        Vector3 forceOnB = -forceOnA;

                        if (!forces.ContainsKey(bodyA.id)) forces[bodyA.id] = Vector3.zero;
                        if (!forces.ContainsKey(bodyB.id)) forces[bodyB.id] = Vector3.zero;

                        forces[bodyA.id] += forceOnA;
                        forces[bodyB.id] += forceOnB;
                    }
                }
            }

            foreach (var dead in removed) {
                dead.isDead = true;
                celestials.Remove(dead);
                Destroy(dead.gameObject);
            }
            foreach (var s in spawned) {
                celestials.Add(s);
            }

            foreach (var body in celestials) {
                if (body.isDead || body.isPending) continue;

                if (forces.TryGetValue(body.id, out Vector3 netForce)) {
                    body.ApplyForce(netForce, dt);
                }
                body.UpdatePosition(dt);
            }

            foreach (var ev in events) {
                OnCollisionOccurred?.Invoke(ev);
            }
        }

        private CollisionEvent ResolveCollision(Celestial a, Celestial b, float distance, List<Celestial> removed, List<Celestial> spawned) {
            CollisionThresholds t = thresholds;
            Celestial big = PickBig(a, b);
            Celestial small = (big == a) ? b : a;

            float sumR = big.radius + small.radius;
            float mTot = big.mass + small.mass;
            Vector3 n = UnitOr(small.transform.position - big.transform.position, Vector3.right);
            Vector3 vRel = small.velocity - big.velocity;
            float speed = vRel.magnitude;

            float cosT = speed > 1e-9f ? Mathf.Abs(Vector3.Dot(vRel, n)) / speed : 1f;
            float impact = Mathf.Sqrt(Mathf.Max(0f, 1f - cosT * cosT));

            float vEsc = Mathf.Sqrt((2f * gravitationalConstant * mTot) / sumR);
            float speedRatio = speed / vEsc;
            float gamma = small.mass / big.mass;

            float overlap = Mathf.Max(0f, sumR - distance);
            Vector3 point = big.transform.position + n * (big.radius - overlap / 2f);
            Vector3 cmVel = ((big.velocity * big.mass) + (small.velocity * small.mass)) / mTot;
            bool starInvolved = a.bodyType == CelestialType.Star || b.bodyType == CelestialType.Star;

            CollisionEvent ev = new CollisionEvent {
                type = CollisionType.Merge,
                big = big,
                small = small,
                point = point,
                normal = n,
                travelDirection = UnitOr(vRel, -n),
                relativeSpeed = speed,
                speedRatio = speedRatio,
                impactParameter = impact,
                massRatio = gamma,
                centerVelocity = cmVel,
                impactScale = sumR,
                gravitationalConstant = gravitationalConstant
            };
            
            if ((a.bodyType == CelestialType.Debris && b.bodyType == CelestialType.Debris) ||
                (small.bodyType == CelestialType.Debris && gamma < 0.05f)) {
                Accrete(big, small, small.mass, 0.3f);
                removed.Add(small);
                ev.type = CollisionType.Absorb;
                ev.survivor = big;
                ev.destroyed.Add(small);
                return ev;
            }

            CollisionType type;
            if (starInvolved) type = CollisionType.Merge;
            else if (speedRatio >= t.catastrophicSpeedRatio && impact < t.hitAndRunImpact && gamma >= t.catastrophicMinMassRatio) type = CollisionType.Catastrophic;
            else if (impact >= t.hitAndRunImpact && speedRatio >= t.hitAndRunSpeedRatio) type = CollisionType.HitAndRun;
            else if (gamma < t.partialMassRatio && (speedRatio >= t.mergeSpeedRatio || impact >= t.mergeImpact)) type = CollisionType.PartialAccretion;
            else type = CollisionType.Merge;

            ev.type = type;

            int Capacity() => Mathf.Max(0, t.maxDebris - DebrisCount() - spawned.FindAll(x => x.bodyType == CelestialType.Debris).Count);

            switch (type) {
                case CollisionType.Merge:
                    float debrisMass = 0f;
                    if (!starInvolved && speedRatio > 0.5f) {
                        debrisMass = small.mass * Mathf.Min(0.05f, (0.05f * (speedRatio - 0.5f)) / 0.3f);
                    }
                    float density = small.mass / small.Volume;
                    Accrete(big, small, small.mass - debrisMass, 1.0f);
                    removed.Add(small);
                    ev.survivor = big;
                    ev.destroyed.Add(small);

                    int count = Mathf.Min(Capacity(), debrisMass > 0f ? 1 + Random.Range(0, 3) : 0);
                    if (count > 0) {
                        float vEscBig = Mathf.Sqrt((2f * gravitationalConstant * big.mass) / big.radius);
                        Vector3 outward = UnitOr(point - big.transform.position, n);
                        var frags = LaunchEjecta(count, debrisMass, density, big, outward, null, vEscBig * 0.3f, vEscBig * 0.8f);
                        RegisterContacts(frags, new List<Celestial> { big });
                        spawned.AddRange(frags);
                        ev.spawned.AddRange(frags);
                    }
                    break;

                case CollisionType.PartialAccretion:
                    float debrisFrac = Mathf.Clamp(0.12f + 0.25f * impact + 0.08f * speedRatio, 0.1f, 0.45f);
                    float dm = small.mass * debrisFrac;
                    float dDens = small.mass / small.Volume;

                    Accrete(big, small, small.mass - dm, 0.6f);
                    removed.Add(small);
                    ev.survivor = big;
                    ev.destroyed.Add(small);

                    Vector3 axis = UnitOr(Vector3.Cross(n, vRel), UnitOr(Vector3.Cross(n, Random.onUnitSphere), Vector3.up));
                    ev.orbitAxis = axis;
                    ev.orbitMass = big.mass;

                    int pCount = Mathf.Min(Capacity(), 5 + Random.Range(0, 5));
                    float[] masses = SplitMass(dm, pCount);
                    List<Celestial> pFrags = new List<Celestial>();

                    for (int i = 0; i < masses.Length; i++) {
                        float theta = ((float)i / pCount) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                        Vector3 radial = n * Mathf.Cos(theta) + Vector3.Cross(axis, n) * Mathf.Sin(theta);
                        Vector3 tangent = Vector3.Cross(axis, radial);

                        Celestial f = MakeFragment(masses[i], dDens, big.transform.position, big.velocity);
                        float r = big.radius * Random.Range(1.6f, 3.0f) + f.radius;
                        f.transform.position = big.transform.position + radial * r;

                        float vc = Mathf.Sqrt((gravitationalConstant * big.mass) / r);
                        f.velocity = big.velocity + tangent * (vc * Random.Range(0.7f, 1.05f)) + radial * (vc * Random.Range(0f, 0.15f));
                        pFrags.Add(f);
                    }
                    RegisterContacts(pFrags, new List<Celestial> { big });
                    spawned.AddRange(pFrags);
                    ev.spawned.AddRange(pFrags);
                    break;

                case CollisionType.HitAndRun:
                    float hd = mTot / (big.Volume + small.Volume);
                    float vn = Vector3.Dot(vRel, n);

                    if (vn < 0) {
                        float e = 0.35f;
                        float j = (-(1f + e) * vn) / (1f / big.mass + 1f / small.mass);
                        big.velocity -= n * (j / big.mass);
                        small.velocity += n * (j / small.mass);
                    }
                    Vector3 vRel2 = small.velocity - big.velocity;
                    Vector3 vt = vRel2 - n * Vector3.Dot(vRel2, n);
                    Vector3 lost = vt * 0.08f;
                    small.velocity -= lost * (big.mass / mTot);
                    big.velocity += lost * (small.mass / mTot);

                    if (overlap > 0f) {
                        small.transform.position += n * (overlap * (big.mass / mTot));
                        big.transform.position -= n * (overlap * (small.mass / mTot));
                    }

                    float lossSmall = small.mass * Mathf.Clamp(0.04f + 0.05f * speedRatio, 0.05f, 0.25f);
                    float lossBig = Mathf.Min(lossSmall * 0.5f, big.mass * 0.02f);
                    float hrDebrisMass = lossSmall + lossBig;
                    Shrink(small, lossSmall);
                    Shrink(big, lossBig);

                    contacts[PairKey(big, small)] = new string[] { big.id, small.id };

                    int hCount = Mathf.Min(Capacity(), 3 + Random.Range(0, 5));
                    if (hCount > 0) {
                        Vector3 outward = UnitOr(point - big.transform.position, n);
                        var frags = LaunchEjecta(hCount, hrDebrisMass, hd, big, outward, ev.travelDirection, speed * 0.3f, speed * 0.9f);
                        RegisterContacts(frags, new List<Celestial> { big, small });
                        spawned.AddRange(frags);
                        ev.spawned.AddRange(frags);
                    }
                    break;

                case CollisionType.Catastrophic:
                    removed.Add(a);
                    removed.Add(b);
                    ev.destroyed.Add(a);
                    ev.destroyed.Add(b);

                    float cd = mTot / (a.Volume + b.Volume);
                    float totalDebris = mTot * 0.85f;
                    int cCount = Mathf.Min(Capacity(), 14 + Random.Range(0, 12));
                    float[] cMasses = SplitMass(totalDebris, cCount);

                    List<Celestial> cFrags = new List<Celestial>();
                    foreach (float m in cMasses) {
                        Celestial parent = Random.value < (a.mass / mTot) ? a : b;
                        Vector3 origin = parent.transform.position + Random.onUnitSphere * (parent.radius * 0.8f * Mathf.Pow(Random.value, 1f / 3f));
                        Vector3 dir = UnitOr(origin - point, Random.onUnitSphere);
                        float ejectSpeed = vEsc * Random.Range(0.9f, 1.8f) + speed * 0.1f * Random.value;
                        Vector3 inherit = (parent.velocity - cmVel) * Random.Range(0.2f, 0.5f);

                        Celestial f = MakeFragment(m, cd, origin, cmVel + dir * ejectSpeed + inherit);
                        cFrags.Add(f);
                    }
                    RegisterContacts(cFrags, new List<Celestial>());
                    spawned.AddRange(cFrags);
                    ev.spawned.AddRange(cFrags);
                    break;
            }

            return ev;
        }

        private Celestial PickBig(Celestial a, Celestial b) {
            if (a.bodyType == CelestialType.Star && b.bodyType != CelestialType.Star) return a;
            if (b.bodyType == CelestialType.Star && a.bodyType != CelestialType.Star) return b;
            return a.mass >= b.mass ? a : b;
        }

        private void Accrete(Celestial host, Celestial victim, float victimMass, float heatScale) {
            float total = host.mass + victimMass;
            float victimVolume = victimMass / (victim.mass / victim.Volume);

            Vector3 momentum = (host.velocity * host.mass) + (victim.velocity * victimMass);
            Vector3 centre = ((host.transform.position * host.mass) + (victim.transform.position * victimMass)) / total;

            host.radius = Mathf.Pow((3f * (host.Volume + victimVolume)) / (4f * Mathf.PI), 1f / 3f);
            host.velocity = momentum / total;
            host.transform.position = centre;

            float boost = host.bodyType == CelestialType.Star ? 0 : (victimMass / total) * 1000f * heatScale;
            host.mass = total;
            host.transform.localScale = Vector3.one * (host.radius * 2f);
        }

        private void Shrink(Celestial body, float dm) {
            dm = Mathf.Min(dm, body.mass * 0.9f);
            body.radius *= Mathf.Pow((body.mass - dm) / body.mass, 1f / 3f);
            body.mass -= dm;
            body.transform.localScale = Vector3.one * (body.radius * 2f);
        }

        private Celestial MakeFragment(float mass, float density, Vector3 pos, Vector3 vel) {
            float radius = Mathf.Pow((3f * mass) / (density * 4f * Mathf.PI), 1f / 3f);
            GameObject obj = Instantiate(debrisPrefab, pos, Quaternion.identity);
            Celestial f = obj.GetComponent<Celestial>();

            f.id = GUID.Generate().ToString();
            f.celestialName = "Debris";
            f.bodyType = CelestialType.Debris;
            f.mass = mass;
            f.radius = radius;
            f.velocity = vel;
            f.transform.localScale = Vector3.one * (radius * 2f);

            return f;
        }

        private List<Celestial> LaunchEjecta(int count, float totalMass, float density, Celestial big, Vector3 outward, Vector3? travel, float speedMin, float speedMax) {
            float[] masses = SplitMass(totalMass, count);
            List<Celestial> results = new List<Celestial>();

            foreach (float m in masses) {
                Vector3 tVec = travel.HasValue ? travel.Value * 0.7f : Vector3.zero;
                Vector3 dir = UnitOr(outward * 0.6f + tVec + Random.onUnitSphere * 0.35f, outward);
                Celestial f = MakeFragment(m, density, big.transform.position, big.velocity);

                Vector3 surfaceDir = UnitOr(outward + Random.onUnitSphere * 0.25f, outward);
                f.transform.position = big.transform.position + surfaceDir * (big.radius * 1.02f + f.radius * 1.3f);
                f.velocity = big.velocity + dir * Random.Range(speedMin, speedMax);

                results.Add(f);
            }
            return results;
        }

        private float[] SplitMass(float total, int n) {
            if (n <= 0) return new float[0];
            float[] w = new float[n];
            float sum = 0;
            for (int i = 0; i < n; i++) {
                w[i] = Mathf.Pow(Random.value, 2.5f) + 0.04f;
                sum += w[i];
            }
            for (int i = 0; i < n; i++) w[i] = (w[i] / sum) * total;
            return w;
        }

        private int DebrisCount() {
            int c = 0;
            foreach (var b in celestials) if (b.bodyType == CelestialType.Debris) c++;
            return c;
        }

        private string PairKey(Celestial a, Celestial b) {
            return string.Compare(a.id, b.id) < 0 ? $"{a.id}|{b.id}" : $"{b.id}|{a.id}";
        }

        private void RegisterContacts(List<Celestial> frags, List<Celestial> others) {
            for (int i = 0; i < frags.Count; i++) {
                for (int j = i + 1; j < frags.Count; j++) {
                    contacts[PairKey(frags[i], frags[j])] = new string[] { frags[i].id, frags[j].id };
                }
                foreach (var o in others) {
                    contacts[PairKey(frags[i], o)] = new string[] { frags[i].id, o.id };
                }
            }
        }

        private void RefreshContacts() {
            List<string> keysToRemove = new List<string>();
            foreach (var kvp in contacts) {
                string ia = kvp.Value[0];
                string ib = kvp.Value[1];

                Celestial a = celestials.Find(c => c.id == ia);
                Celestial b = celestials.Find(c => c.id == ib);

                if (a == null || b == null || (a.transform.position - b.transform.position).magnitude >= (a.radius + b.radius)) {
                    keysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in keysToRemove) contacts.Remove(key);
        }

        private Vector3 UnitOr(Vector3 v, Vector3 fallback) {
            float m = v.magnitude;
            return m > 1e-9f ? v / m : fallback;
        }
    }
}