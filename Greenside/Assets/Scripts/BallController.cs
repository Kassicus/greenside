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

        public enum BallState { Teed, InPlay, Resting }
        public BallState State { get; private set; } = BallState.Teed;

        /// <summary>Fired when a shot is launched.</summary>
        public event Action OnLaunched;
        /// <summary>Fired when the ball comes to rest, with total distance (carry + roll) in yards.</summary>
        public event Action<float> OnRest;

        /// <summary>Live horizontal distance from the launch point, in yards (0 at the tee).</summary>
        public float DistanceYards =>
            (tuning != null ? tuning.yardsPerMeter : 1f) * HorizontalDistance(_launchPosition, transform.position);

        /// <summary>Carry (distance to first landing) in yards, or -1 if not recorded (e.g. a putt that never left the ground).</summary>
        public float CarryYards => _carryYards;

        private Rigidbody _rb;
        private Vector3 _teePosition;
        private Vector3 _launchPosition;
        private float _restTimer;
        private float _launchTime;
        private bool _carryRecorded;
        private float _carryYards = -1f;
        private bool _grounded;

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
        }

        private void Start()
        {
            _teePosition = transform.position;
        }

        /// <summary>
        /// Launch the ball with the selected club. headingFlat is a horizontal aim
        /// direction; the club supplies loft, launch-speed range and spin factor;
        /// power01 (0..1) scales speed; signedCurve (-1..+1) sets sidespin
        /// (+ = slice/right, - = hook/left).
        /// </summary>
        public void Launch(float power01, float signedCurve, Vector3 headingFlat, Club club)
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

            float speed = Mathf.Lerp(club.minLaunchSpeed, club.maxLaunchSpeed, Mathf.Clamp01(power01));

            // Wake the ball into dynamic, anti-tunnel (Continuous) physics for the shot.
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
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
            State = BallState.InPlay;
            OnLaunched?.Invoke();
        }

        private void FixedUpdate()
        {
            if (State != BallState.InPlay) return;

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
                {
                    State = BallState.Resting;
                    float carryYards = HorizontalDistance(_launchPosition, transform.position) * tuning.yardsPerMeter;
                    OnRest?.Invoke(carryYards);
                }
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

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
