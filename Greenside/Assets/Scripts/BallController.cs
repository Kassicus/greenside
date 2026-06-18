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
        /// <summary>Fired when the ball comes to rest, with carry distance in yards.</summary>
        public event Action<float> OnRest;

        private Rigidbody _rb;
        private Vector3 _teePosition;
        private Vector3 _launchPosition;
        private float _restTimer;

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
        /// Launch the ball. headingFlat is a horizontal aim direction; loftDeg
        /// tilts it upward; power01 (0..1) scales speed; signedCurve (-1..+1)
        /// sets sidespin (+ = slice/right, - = hook/left).
        /// </summary>
        public void Launch(float power01, float signedCurve, Vector3 headingFlat, float loftDeg)
        {
            headingFlat.y = 0f;
            if (headingFlat.sqrMagnitude < 1e-4f) headingFlat = Vector3.forward;
            headingFlat.Normalize();

            // Tilt the heading upward by the loft angle: horizontal component along
            // the heading plus a vertical component. (Building it from cos/sin avoids
            // any axis-handedness confusion — it always launches upward.)
            float loftRad = loftDeg * Mathf.Deg2Rad;
            Vector3 launchDir = headingFlat * Mathf.Cos(loftRad) + Vector3.up * Mathf.Sin(loftRad);

            float speed = Mathf.Lerp(tuning.minLaunchSpeed, tuning.maxLaunchSpeed, Mathf.Clamp01(power01));

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            // Impulse = m * dv, so the velocity change is exactly launchDir * speed.
            _rb.AddForce(launchDir * speed * _rb.mass, ForceMode.Impulse);

            // Sidespin about the up axis; Magnus (below) turns it into curve.
            float spin = signedCurve * tuning.maxSidespin;
            _rb.AddTorque(Vector3.up * spin, ForceMode.VelocityChange);

            _launchPosition = transform.position;
            _restTimer = 0f;
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

        /// <summary>Return the ball to the tee, stationary.</summary>
        public void ResetToTee()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.position = _teePosition;
            _restTimer = 0f;
            State = BallState.Teed;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
