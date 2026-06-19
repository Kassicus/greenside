using System;
using UnityEngine;

namespace Greenside
{
    /// <summary>
    /// The golf ball: a Rigidbody + SphereCollider with Continuous collision
    /// (it moves fast and would tunnel with discrete detection).
    ///
    /// On Launch it gets an impulse (heading + loft elevation) plus sidespin,
    /// then a Magnus-style lateral force each FixedUpdate makes spin curve the
    /// flight. Bounce and roll are left to PhysX.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        public SwingTuning tuning;

        public enum BallState { Teed, InPlay, Resting, Holed }
        public BallState State { get; private set; } = BallState.Teed;

        /// <summary>Fired when a shot is launched.</summary>
        public event Action OnLaunched;
        /// <summary>Fired when the ball comes to rest, with total distance (carry + roll) in yards.</summary>
        public event Action<float> OnRest;

        /// <summary>Fired when the ball is holed.</summary>
        public event Action OnHoled;

        /// <summary>Live horizontal distance from the launch point, in yards (0 at the tee).</summary>
        public float DistanceYards =>
            (tuning != null ? tuning.yardsPerMeter : 1f) * HorizontalDistance(_launchPosition, transform.position);

        /// <summary>Carry (distance to first landing) in yards, or -1 if not recorded (e.g. a putt that never left the ground).</summary>
        public float CarryYards => _carryYards;

        /// <summary>Horizontal distance from the ball to the pin in yards, or -1 if no hole is set.</summary>
        public float DistanceToPinYards => _hasHole
            ? HorizontalDistance(transform.position, _pinPos) * (tuning != null ? tuning.yardsPerMeter : 1f)
            : -1f;

        private Rigidbody _rb;
        private Vector3 _teePosition;
        private Vector3 _launchPosition;
        private float _restTimer;
        private float _launchTime;
        private bool _carryRecorded;
        private float _carryYards = -1f;
        private bool _grounded;
        private Vector3 _pinPos;
        private float _cupRadius;
        private bool _hasHole;
        private float _radius = 0.2f;
        private Collider _col;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            if (tuning == null)
            {
                Debug.LogWarning("[BallController] No SwingTuning assigned — using defaults. " +
                                 "Create one via Assets > Create > Greenside > Swing Tuning.");
                tuning = ScriptableObject.CreateInstance<SwingTuning>();
            }

            // Fast small object: never let it tunnel through terrain.
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.mass = tuning.mass;
            _rb.linearDamping = tuning.linearDamping;
            _rb.angularDamping = tuning.angularDamping;

            var sphere = GetComponent<SphereCollider>();
            _col = sphere;
            _radius = sphere != null ? sphere.radius * transform.lossyScale.x : 0.2f;
        }

        private void Start()
        {
            _teePosition = transform.position;
        }

        /// <summary>
        /// Launch the ball with the selected club. headingFlat is a horizontal aim
        /// direction; the club supplies loft, launch-speed range and spin factor;
        /// power01 (0..1) scales speed; signedCurve (-1..+1) sets sidespin
        /// (+ = slice/right, - = hook/left); powerMultiplier caps the top-end speed
        /// for the lie surface (e.g. rough) without reducing the club's minimum.
        /// </summary>
        public void Launch(float power01, float signedCurve, Vector3 headingFlat, Club club, float powerMultiplier)
        {
            if (club == null)
            {
                Debug.LogWarning("[BallController] Launch called with no club.");
                return;
            }

            headingFlat.y = 0f;
            if (headingFlat.sqrMagnitude < 1e-4f) headingFlat = Vector3.forward;
            headingFlat.Normalize();

            // Tilt the heading upward by the club's loft: horizontal component along
            // the heading plus a vertical component. (Building it from cos/sin avoids
            // any axis-handedness confusion — it always launches upward.)
            float loftRad = club.loftDegrees * Mathf.Deg2Rad;
            Vector3 launchDir = headingFlat * Mathf.Cos(loftRad) + Vector3.up * Mathf.Sin(loftRad);

            // The lie multiplier caps the top end (e.g. rough), but the club's minimum
            // power is always imparted so a struck shot never just dies on the ground.
            float maxSpeed = Mathf.Max(club.minLaunchSpeed, club.maxLaunchSpeed * powerMultiplier);
            float speed = Mathf.Lerp(club.minLaunchSpeed, maxSpeed, Mathf.Clamp01(power01));

            // Wake the ball into dynamic, anti-tunnel (Continuous) physics for the shot,
            // and lift it just clear of the ground so a resting lie can't absorb the launch.
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            transform.position += Vector3.up * 0.02f;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            // Impulse = m * dv, so the velocity change is exactly launchDir * speed.
            _rb.AddForce(launchDir * speed * _rb.mass, ForceMode.Impulse);

            // Sidespin about the up axis, scaled by the club (putter = 0 -> no curve).
            // Magnus (below) turns it into curve.
            float spin = signedCurve * tuning.maxSidespin * club.spinFactor;
            _rb.AddTorque(Vector3.up * spin, ForceMode.VelocityChange);

            _launchPosition = transform.position;
            _restTimer = 0f;
            _launchTime = Time.time;
            _carryRecorded = false;
            _carryYards = -1f;
            _grounded = false;   // airborne until we actually land; stops ground-roll
                                 // damping from killing horizontal speed mid-flight
            State = BallState.InPlay;
            OnLaunched?.Invoke();
        }

        private void FixedUpdate()
        {
            if (State != BallState.InPlay) return;

            // Hole-out: over the cup, near green height, and slow enough to drop in.
            if (_hasHole
                && HorizontalDistance(transform.position, _pinPos) <= _cupRadius
                && Mathf.Abs(transform.position.y - _pinPos.y) < 0.6f
                && _rb.linearVelocity.magnitude < tuning.holeCaptureSpeed)
            {
                HoleOut();
                return;
            }

            // Magnus-style lateral force: proportional to spin x velocity.
            Vector3 v = _rb.linearVelocity;
            Vector3 w = _rb.angularVelocity;
            if (v.sqrMagnitude > 0.01f)
            {
                Vector3 magnus = tuning.magnusCoefficient * Vector3.Cross(w, v);
                _rb.AddForce(magnus, ForceMode.Acceleration);
            }

            // Ground roll-out damping (interim until per-surface physics in Phase 6).
            // Only while touching the ground and not ascending, so it tames roll
            // without ever affecting the carry (the airborne portion of the shot).
            if (_grounded && _rb.linearVelocity.y < 0.5f)
            {
                float f = Mathf.Clamp01(1f - tuning.groundRollDrag * Time.fixedDeltaTime);
                Vector3 lv = _rb.linearVelocity;
                _rb.linearVelocity = new Vector3(lv.x * f, lv.y, lv.z * f);
                _rb.angularVelocity *= f;
            }

            // Rest detection: slow for long enough -> the shot is done.
            if (_rb.linearVelocity.magnitude < tuning.restSpeed)
            {
                _restTimer += Time.fixedDeltaTime;
                if (_restTimer >= tuning.restTime)
                    ComeToRest();
            }
            else
            {
                _restTimer = 0f;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Record carry (distance to first landing) the first time the ball
            // touches down after launch. A small grace period skips the contact
            // the ball is already resting in at launch. Putts that never leave the
            // ground fire no fresh collision, so carry stays unrecorded (-1).
            if (State != BallState.InPlay || _carryRecorded) return;
            if (Time.time - _launchTime < 0.1f) return;
            _carryRecorded = true;
            _carryYards = HorizontalDistance(_launchPosition, transform.position) * tuning.yardsPerMeter;
        }

        private void OnCollisionStay(Collision collision)
        {
            _grounded = true;
        }

        private void OnCollisionExit(Collision collision)
        {
            _grounded = false;
        }

        /// <summary>Return the ball to the tee, stationary.</summary>
        public void ResetToTee()
        {
            // Freeze the ball on the tee as a kinematic body so it can't fall through
            // colliders that haven't finished registering with the physics engine yet.
            // It goes dynamic (and Continuous) again the moment it is launched.
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.position = _teePosition;
            _rb.isKinematic = true;
            _restTimer = 0f;
            State = BallState.Teed;
        }

        /// <summary>Set a new tee position and place the ball there, stationary
        /// (used when a hole is generated).</summary>
        public void PlaceOnTee(Vector3 teePos)
        {
            _teePosition = teePos;
            ResetToTee();
        }

        /// <summary>Tell the ball where the cup is so it can detect a hole-out.</summary>
        public void SetHole(Vector3 pinPos, float cupRadius)
        {
            _pinPos = pinPos;
            _cupRadius = cupRadius;
            _hasHole = true;
        }

        /// <summary>Freeze the ball at its current lie so the next swing plays from here.</summary>
        private void ComeToRest()
        {
            // Snap cleanly onto the surface so the lie isn't embedded in the terrain.
            // Disable the ball's own collider for the cast so the ray finds the
            // terrain, not the top of the ball (which would lift it a diameter each time).
            if (_col != null) _col.enabled = false;
            bool foundGround = Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f);
            if (_col != null) _col.enabled = true;
            if (foundGround)
                transform.position = hit.point + Vector3.up * _radius;

            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            State = BallState.Resting;
            float totalYards = HorizontalDistance(_launchPosition, transform.position) * tuning.yardsPerMeter;
            OnRest?.Invoke(totalYards);
        }

        /// <summary>Drop the ball into the cup and mark the hole complete.</summary>
        private void HoleOut()
        {
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            transform.position = _pinPos;
            State = BallState.Holed;
            OnHoled?.Invoke();
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Carry (flat-ground, in yards) of a full-power, dead-straight shot with this
        /// club, computed by simulating the same flight physics the ball uses (gravity
        /// + the linear drag; no Magnus on a straight shot). This always matches the
        /// current tuning, so it reflects exactly what a full swing lands.
        /// </summary>
        public static float EstimateFlatCarryYards(Club club, SwingTuning tuning)
        {
            if (club == null || tuning == null || club.maxLaunchSpeed <= 0.01f) return 0f;

            float dt = Time.fixedDeltaTime;
            float gravityY = Physics.gravity.y;                        // ~ -9.81
            float drag = Mathf.Clamp01(1f - tuning.linearDamping * dt);
            float rad = club.loftDegrees * Mathf.Deg2Rad;

            float vx = club.maxLaunchSpeed * Mathf.Cos(rad);
            float vy = club.maxLaunchSpeed * Mathf.Sin(rad);
            float px = 0f, py = 0f;

            // Semi-implicit Euler matching Unity: gravity, then damping, then integrate.
            for (int i = 0; i < 20000; i++)
            {
                vy += gravityY * dt;
                vx *= drag;
                vy *= drag;
                px += vx * dt;
                py += vy * dt;
                if (py <= 0f && vy < 0f) break;   // returned to launch height, descending
            }
            return px * tuning.yardsPerMeter;
        }
    }
}
