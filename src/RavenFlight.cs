using Jotunn.Managers;
using UnityEngine;

namespace RavenScout
{
    /// <summary>
    /// Purely visual: a bird model takes off from the roost, or lands on the roost.
    /// Built from a vanilla bird prefab with all game logic stripped, so it never creates world data.
    /// </summary>
    internal class RavenFlight : MonoBehaviour
    {
        public const float LandingSeconds = 6f;

        public const float TakeOffSeconds = 12f;
        private const float MinTakeOffSeconds = 3f;
        private const float CruiseSpeed = 5f;
        private const float MaxCruiseSpeed = 30f;
        private const float ClimbSpeed = 1.6f;
        private const float CircleSeconds = 3f;
        private const float FadeSeconds = 1.5f;
        private const float LandingDistance = 30f;
        private const float LandingHeight = 14f;

        // First one found is used. Names are checked at load and logged.
        private static readonly string[] s_candidatePrefabs = { "Crow", "Raven", "Seagal" };

        private static GameObject s_template;       // flying pose
        private static GameObject s_perchedTemplate; // landed pose
        private static bool s_warned;

        /// <summary>Stripped, inactive bird in its flying pose; null if no bird prefab exists.</summary>
        public static GameObject Template
        {
            get
            {
                Prepare();
                return s_template;
            }
        }

        /// <summary>Stripped, inactive bird in its landed (sitting) pose; null if no bird prefab exists.</summary>
        public static GameObject PerchedTemplate
        {
            get
            {
                Prepare();
                return s_perchedTemplate;
            }
        }

        private enum Mode { TakeOff, Land }

        private Mode _mode;
        private Vector3 _origin;
        private Vector3 _target;
        private Vector3 _heading;
        private float _duration;
        private float _circleSeconds = CircleSeconds;
        private float _cruiseSpeed = CruiseSpeed;
        private float _age;
        private Vector3 _baseScale;

        public static void Prepare()
        {
            if (s_template)
            {
                return;
            }

            GameObject source = null;
            foreach (var name in s_candidatePrefabs)
            {
                source = PrefabManager.Instance.GetPrefab(name);
                if (source)
                {
                    Plugin.Log.LogInfo($"Raven visual uses prefab '{name}'");
                    break;
                }
            }
            if (!source)
            {
                if (!s_warned)
                {
                    s_warned = true;
                    Plugin.Log.LogWarning("No bird prefab found; ravens will be invisible.");
                }
                return;
            }

            // Instantiate under an inactive parent so none of the prefab's Awake() methods run.
            var holder = new GameObject("RavenScout_Templates");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            s_template = BuildTemplate(source, holder.transform, landed: false);
            s_perchedTemplate = BuildTemplate(source, holder.transform, landed: true);
        }

        /// <summary>
        /// Copies the bird without its game logic. Vanilla birds (RandomFlyingBird) either have separate
        /// flying/landed models, or one model with a "flying" animator flag; pick the requested pose.
        /// </summary>
        private static GameObject BuildTemplate(GameObject source, Transform holder, bool landed)
        {
            var copy = Object.Instantiate(source, holder);
            copy.name = landed ? "RavenScout_RavenPerched" : "RavenScout_Raven";

            var bird = copy.GetComponent<RandomFlyingBird>();
            bool singleModel = !bird || bird.m_singleModel || !bird.m_flyingModel || !bird.m_landedModel;
            if (!singleModel)
            {
                bird.m_flyingModel.SetActive(!landed);
                bird.m_landedModel.SetActive(landed);
            }

            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(behaviour);
            }
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
            foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }

            var pose = copy.AddComponent<BirdPose>();
            pose.Flying = !landed;
            return copy;
        }

        private static RavenFlight Spawn(Vector3 position, Vector3 facing)
        {
            if (!s_template)
            {
                Prepare();
                if (!s_template)
                {
                    return null;
                }
            }
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f)
            {
                facing = Vector3.forward;
            }
            var bird = Object.Instantiate(s_template, position, Quaternion.LookRotation(facing.normalized));
            bird.SetActive(true);
            var flight = bird.AddComponent<RavenFlight>();
            flight._baseScale = bird.transform.localScale;
            return flight;
        }

        /// <summary>
        /// Circles up above the roost, then glides off toward the target and fades out. It never lasts longer
        /// than the real flight, and faster ravens (better food) visibly glide faster. Returns how long the bird is shown, so the sitting raven can return.
        /// </summary>
        public static float Launch(Vector3 from, Vector3 to, float flightSeconds, float speedMultiplier)
        {
            float duration = Mathf.Clamp(flightSeconds * 0.8f, MinTakeOffSeconds, TakeOffSeconds);
            var heading = to - from;
            heading.y = 0f;
            heading = heading.sqrMagnitude < 0.01f ? Vector3.forward : heading.normalized;

            var flight = Spawn(from, heading);
            if (!flight)
            {
                return 0f;
            }
            flight._mode = Mode.TakeOff;
            flight._origin = from;
            flight._heading = heading;
            flight._duration = duration;
            flight._circleSeconds = Mathf.Min(CircleSeconds, duration * 0.3f);
            flight._cruiseSpeed = Mathf.Min(MaxCruiseSpeed, CruiseSpeed * Mathf.Max(1f, speedMultiplier));
            return duration;
        }

        /// <summary>Glides in from the direction it scouted and touches down on the roost after <paramref name="seconds"/>.</summary>
        public static void Land(Vector3 roost, Vector3 fromPos, float seconds)
        {
            var fromDir = fromPos - roost;
            fromDir.y = 0f;
            if (fromPos == Vector3.zero || fromDir.sqrMagnitude < 0.01f)
            {
                fromDir = Random.insideUnitSphere;
                fromDir.y = 0f;
            }
            fromDir = fromDir.sqrMagnitude < 0.01f ? Vector3.back : fromDir.normalized;

            var start = roost + fromDir * LandingDistance + Vector3.up * LandingHeight;
            var flight = Spawn(start, -fromDir);
            if (!flight)
            {
                return;
            }
            flight._mode = Mode.Land;
            flight._origin = start;
            flight._target = roost;
            flight._heading = -fromDir;
            flight._duration = Mathf.Max(1f, seconds);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_mode == Mode.TakeOff)
            {
                UpdateTakeOff();
            }
            else
            {
                UpdateLanding();
            }
        }

        private void UpdateTakeOff()
        {
            Vector3 pos;
            Vector3 facing;
            if (_age < _circleSeconds)
            {
                // Spiral up over the roost once.
                float t = _age / _circleSeconds;
                float angle = t * Mathf.PI * 2f;
                float radius = Mathf.Lerp(0.5f, 3f, t);
                var offset = Quaternion.LookRotation(_heading) * new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * radius;
                pos = _origin + offset + Vector3.up * (_age * ClimbSpeed * 1.5f);
                facing = Quaternion.LookRotation(_heading) * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            else
            {
                // Then glide away toward the destination, still climbing gently.
                float cruise = _age - _circleSeconds;
                var circleEnd = _origin + Quaternion.LookRotation(_heading) * new Vector3(0f, 0f, -3f)
                                + Vector3.up * (_circleSeconds * ClimbSpeed * 1.5f);
                pos = circleEnd + _heading * (cruise * _cruiseSpeed) + Vector3.up * (cruise * ClimbSpeed);
                facing = _heading;
            }

            transform.position = pos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), Time.deltaTime * 6f);
            FadeAndExpire(_duration);
        }

        private void UpdateLanding()
        {
            float t = Mathf.Clamp01(_age / _duration);
            // Ease out: fast at first, gentle as it reaches the roost.
            float eased = 1f - (1f - t) * (1f - t);
            var pos = Vector3.Lerp(_origin, _target, eased);
            pos.y += Mathf.Sin(t * Mathf.PI) * 2f; // slight arc
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(_heading);

            // On touchdown the flying bird is swapped for the sitting one on the post.
            if (_age >= _duration)
            {
                Destroy(gameObject);
            }
        }

        private void FadeAndExpire(float lifetime)
        {
            float left = lifetime - _age;
            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (left < FadeSeconds)
            {
                transform.localScale = _baseScale * (left / FadeSeconds);
            }
        }
    }

    /// <summary>Holds the bird's animator in a flying or landed pose.</summary>
    internal class BirdPose : MonoBehaviour
    {
        private static readonly int s_flyingHash = Animator.StringToHash("flying");
        private static readonly int s_flappingHash = Animator.StringToHash("flapping");

        public bool Flying;

        private Animator _animator;
        private bool _hasFlying;
        private bool _hasFlapping;

        private void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            if (!_animator)
            {
                return;
            }
            foreach (var p in _animator.parameters)
            {
                _hasFlying |= p.nameHash == s_flyingHash;
                _hasFlapping |= p.nameHash == s_flappingHash;
            }
            Apply();
        }

        private void Update() => Apply();

        private void Apply()
        {
            if (!_animator || !_animator.isActiveAndEnabled)
            {
                return;
            }
            if (_hasFlying)
            {
                _animator.SetBool(s_flyingHash, Flying);
            }
            if (_hasFlapping)
            {
                _animator.SetBool(s_flappingHash, Flying);
            }
        }
    }
}
