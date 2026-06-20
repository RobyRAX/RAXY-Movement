using System;
using Sirenix.OdinInspector;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RAXY.Movement
{
    [RequireComponent(typeof(CharacterController), typeof(GroundChecker))]
    public class MovementController : MonoBehaviour
    {
        [TitleGroup("Base")]
        [InfoBox("Set this via Game Dependency Prefab")]
        [ShowInInspector]
        [ReadOnly]
        protected float gravity = -75;

        [TitleGroup("Base")]
        [Tooltip("How quickly the character reaches target velocity (higher = more responsive)")]
        [SerializeField]
        protected float acceleration = 100;

        [TitleGroup("Base")]
        [SerializeField]
        protected float rotationSpeed = 15f;

        [TitleGroup("Base")]
        [Tooltip("Default decay speed for impulses when not specified")]
        public const float DEFAULT_IMPULSE_DECAY_SPEED = 5f;

        [TitleGroup("Debug")]
        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        protected Vector3 rawHorizontalVelocityInput;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        public Vector3 gravityVelocity;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        public Vector3 currentHorizontalVelocity;

        [BoxGroup("Debug/Debug/Impulse")]
        [ShowInInspector]
        [HideReferenceObjectPicker]
        [HideLabel]
        protected Impulse currentImpulse;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        protected Vector3 impulseDisplacement;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        [ReadOnly]
        public ResolveMethod CurrentResolveMethod { get; private set; }

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        protected Vector3 surfaceHorizontalVelocity;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        public Vector3 finalVelocity;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        protected float gravityModifier = 1;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        public float accelerationModifier = 1;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        protected float slopeSpeedMultiplier;

        [FoldoutGroup("Debug/Debug")]
        [ShowInInspector]
        public bool enableRotation;

#if UNITY_EDITOR
        [FoldoutGroup("Debug/Gizmos")]
        public bool showVelocityGizmos = true;

        [FoldoutGroup("Debug/Gizmos")]
        public float gizmoLineThickness = 3f;
#endif

        public float Gravity
        {
            get => gravity * gravityModifier;
            set => gravity = value;
        }

        public float Acceleration
        {
            get => acceleration * accelerationModifier;
            set => acceleration = value;
        }

        public GroundChecker GroundChecker { get; protected set; }
        public CharacterController CharCon { get; protected set; }
        public const float TERMINAL_VELOCITY = -53f; // adjust to taste
        protected const float GRAVITY_ON_FLAT = -2.5f;
        protected virtual float AdditionalSpeedMultiplier => 1;

        protected virtual void Awake()
        {
            GroundChecker = GetComponent<GroundChecker>();
            CharCon = GetComponent<CharacterController>();

            EnableRotate();
        }

        protected virtual void Update()
        {
            SmoothHorizontalVelocity();

            if (GroundChecker.IsGrounded)
            {
                if (GroundChecker.GroundType == GroundType.Steep)
                {
                    if (finalVelocity.y <= 0)
                    {
                        CurrentResolveMethod = ResolveMethod.SteepGround;
                        Resolve_SteepGround();
                    }
                    else
                    {
                        CurrentResolveMethod = ResolveMethod.Airborne_SteepWithPositiveY;
                        Resolve_Airborne();
                    }
                }
                else if (GroundChecker.GroundType == GroundType.Slope)
                {
                    if (GroundChecker.isUseRaycast)
                    {
                        if (GroundChecker.RaycastHit)
                        {
                            CurrentResolveMethod = ResolveMethod.SlopeGround;
                            Resolve_SlopeGround();
                        }
                        else
                        {
                            // Raycast miss but SphereCast has data - trust SphereCast
                            // This happens at ledge edges where center is past edge but bottom still touching
                            CurrentResolveMethod = ResolveMethod.SlopeGround_RaycastMiss;
                            Resolve_SlopeGround();
                        }
                    }
                    else
                    {
                        CurrentResolveMethod = ResolveMethod.SlopeGround;
                        Resolve_SlopeGround();
                    }
                }
                else
                {
                    CurrentResolveMethod = ResolveMethod.FlatGround;
                    Resolve_FlatGround();
                }
            }
            else
            {
                CurrentResolveMethod = ResolveMethod.Airborne;
                Resolve_Airborne();
            }

            ApplyMovement();
        }

        [TitleGroup("Debug Function")]
        [Button]
        public void SetAccelerationModifier(float mod = 1)
        {
            accelerationModifier = mod;
        }

        void SmoothHorizontalVelocity()
        {
            // Horizontal intent (velocity)
            currentHorizontalVelocity = Vector3.MoveTowards(
                currentHorizontalVelocity,
                rawHorizontalVelocityInput,
                Acceleration * Time.deltaTime
            );
        }

        Vector3 GetHorizontalVelocity()
        {
            return currentHorizontalVelocity * AdditionalSpeedMultiplier;
        }

        void Resolve_FlatGround()
        {
            gravityVelocity += Vector3.up * (Gravity * 0.33f) * Time.deltaTime;
            gravityVelocity = Vector3.ClampMagnitude(gravityVelocity, -GRAVITY_ON_FLAT);

            finalVelocity = GetHorizontalVelocity() + gravityVelocity;
        }

        void Resolve_SlopeGround()
        {
            gravityVelocity += Vector3.up * (Gravity * 0.33f) * Time.deltaTime;
            gravityVelocity = Vector3.ClampMagnitude(gravityVelocity, -GRAVITY_ON_FLAT);

            Quaternion rotation = Quaternion.AngleAxis(GroundChecker.SurfaceAngle, transform.right);

            float currentSpeed = GetHorizontalVelocity().magnitude;
            Vector3 rotatedVelocity = rotation * currentHorizontalVelocity.normalized * currentSpeed;

            // Apply slope-based speed multiplier
            slopeSpeedMultiplier = 1 - (GroundChecker.SurfaceAngle / 270);
            finalVelocity = rotatedVelocity * slopeSpeedMultiplier;
        }

        private void Resolve_SteepGround()
        {
            Vector3 surfDir = GroundChecker.SurfaceDirection;
            Vector3 rotatedGravity = surfDir * -Gravity;

            gravityVelocity += rotatedGravity * Time.deltaTime;
            gravityVelocity = Vector3.ClampMagnitude(gravityVelocity, -TERMINAL_VELOCITY / 2);
            surfaceHorizontalVelocity = Vector3.ProjectOnPlane(GetHorizontalVelocity(), GroundChecker.SurfaceNormal);

            finalVelocity = surfaceHorizontalVelocity + gravityVelocity;
        }

        private void Resolve_Airborne()
        {
            gravityVelocity += Vector3.up * Gravity * Time.deltaTime;
            gravityVelocity = Vector3.ClampMagnitude(gravityVelocity, -TERMINAL_VELOCITY);
            finalVelocity = GetHorizontalVelocity() + gravityVelocity;
        }

        protected virtual void ApplyMovement()
        {
            UpdateImpulse();

            Vector3 displacement = finalVelocity * Time.deltaTime;
            displacement += impulseDisplacement;

            if (CharCon.enabled)
                CharCon.Move(displacement);
        }

        #region Toggle
        public void EnableRotate()
        {
            enableRotation = true;
        }

        public void DisableRotate()
        {
            enableRotation = false;
        }
        #endregion

        #region Rotate
        public void RotateTowardsMovement()
        {
            Vector3 lookDir = currentHorizontalVelocity;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude < 0.001f)
                return;

            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                rotationSpeed * Time.deltaTime
            );
        }

        public void LookAt(Transform target, float angleOffset = 0f, bool instant = false, float? customSpeed = null)
        {
            if (target == null)
                return;

            LookAt(target.position, angleOffset, instant, customSpeed);
        }

        public void LookAt(Vector3 targetPos, float angleOffset = 0f, bool instant = false, float? customSpeed = null)
        {
            Vector3 dir = targetPos - transform.position; // get direction from self to target
            LookAtDirection_AxisY(dir, angleOffset, instant, customSpeed);
        }

        public void LookAtDirection_AxisY(Vector3 dir, float angleOffset = 0f, bool instant = false, float? customSpeed = null)
        {
            if (!enableRotation)
                return;

            // Calculate target rotation
            Quaternion targetRotation;
            if (!TryGetTargetRotation(dir, angleOffset, out targetRotation))
                return;

            // Apply rotation
            if (instant)
            {
                transform.rotation = targetRotation;
            }
            else
            {
                float speed = customSpeed ?? rotationSpeed;
                transform.rotation = Quaternion.Slerp(
                                        transform.rotation,
                                        targetRotation,
                                        speed * Time.deltaTime);
            }
        }

        private bool TryGetTargetRotation(Vector3 dir, float angleOffset, out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            // Flatten direction
            dir.y = 0;
            if (dir.sqrMagnitude < 0.001f)
                return false;

            dir.Normalize();

            // Apply offset and build rotation
            Quaternion offset = Quaternion.Euler(0, angleOffset, 0);
            Vector3 finalDir = offset * dir;
            rotation = Quaternion.LookRotation(finalDir);

            return true;
        }
        #endregion

        [TitleGroup("Debug Function")]
        [Button]
        public void Set_HorizontalVelocity(Vector3 velocity)
        {
            velocity.y = 0f;
            rawHorizontalVelocityInput = velocity;
        }

        void UpdateImpulse()
        {
            impulseDisplacement = Vector3.zero;

            if (currentImpulse != null && !currentImpulse.IsExpired())
            {
                if (currentImpulse.RemoveOnGrounded &&
                    GroundChecker.IsGrounded)
                {
                    if (GroundChecker.ConfirmedGroundType != GroundType.Steep)
                    {
                        ClearImpulse();
                        return;
                    }
                    else
                    {
                        currentImpulse.HorizontalDecaySpeed = 10f;
                        currentImpulse.VerticalDecaySpeed = 10f;
                    }
                }

                impulseDisplacement = currentImpulse.GetDisplacementAndUpdate(Time.deltaTime);
            }
            else
            {
                ClearImpulse();
            }
        }

        public void AddImpulse(ImpulseRequest req)
        {
            AddImpulse(req.impulseVelocity,
                        req.horizontalDecaySpeed,
                        req.verticalDecaySpeed,
                        req.forceUnground,
                        req.resetGravity,
                        req.removeOnGrounded);
        }

        [TitleGroup("Debug Function")]
        [Button]
        public void AddImpulse(Vector3 impulseVelocity,
                                float horizontalDecay = DEFAULT_IMPULSE_DECAY_SPEED,
                                float verticalDecay = DEFAULT_IMPULSE_DECAY_SPEED,
                                bool forceUnground = false,
                                bool resetGravity = true,
                                bool removeOnGrounded = false)
        {
            currentImpulse = new Impulse(impulseVelocity, horizontalDecay, verticalDecay, removeOnGrounded);

            if (resetGravity && impulseVelocity.y > 0.1f)
            {
                gravityVelocity = Vector3.zero;
            }

            if (forceUnground)
            {
                gravityVelocity = Vector3.zero;
                GroundChecker.ForceUngrounded().Forget();
            }
        }

        public void ClearImpulse()
        {
            currentImpulse = null; ;
        }

        public void SetGravityModifier(float mod = 1)
        {
            gravityModifier = mod;
        }

        public void TeleportToPosition(Vector3 position)
        {
            CharCon.enabled = false;
            transform.position = position;
            CharCon.enabled = true;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!showVelocityGizmos || !Application.isPlaying)
                return;

            Vector3 origin = transform.position;
            float lineLength = 2f;

            // Final Velocity - CYAN
            if (finalVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 dir = finalVelocity.normalized;
                UnityEditor.Handles.color = Color.cyan;
                UnityEditor.Handles.DrawLine(origin, origin + dir * lineLength, gizmoLineThickness);
                UnityEditor.Handles.Label(origin + dir * lineLength,
                    $"Final: {finalVelocity.magnitude:F1}\n{dir:F2}");
            }

            // Gravity Velocity - RED
            if (gravityVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 dir = gravityVelocity.normalized;
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.DrawLine(origin, origin + dir * lineLength, gizmoLineThickness);
                UnityEditor.Handles.Label(origin + dir * lineLength + Vector3.down * 0.3f,
                    $"Gravity: {gravityVelocity.magnitude:F1}");
            }

            // Current Horizontal - GREEN
            if (currentHorizontalVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 dir = currentHorizontalVelocity.normalized;
                UnityEditor.Handles.color = Color.green;
                UnityEditor.Handles.DrawLine(origin, origin + dir * lineLength, gizmoLineThickness);
                UnityEditor.Handles.Label(origin + dir * lineLength + Vector3.up * 0.3f,
                    $"Horizontal: {currentHorizontalVelocity.magnitude:F1}");
            }

            // Current Horizontal - GREEN
            if (surfaceHorizontalVelocity.sqrMagnitude > 0.01f && GroundChecker.OnSteep)
            {
                Vector3 dir = surfaceHorizontalVelocity.normalized;
                UnityEditor.Handles.color = Color.green;
                UnityEditor.Handles.DrawLine(origin, origin + dir * lineLength, gizmoLineThickness);
                UnityEditor.Handles.Label(origin + dir * lineLength + Vector3.up * 0.3f,
                    $"Surface: {surfaceHorizontalVelocity.magnitude:F1}");
            }

            // Surface Normal - YELLOW (jika grounded)
            if (GroundChecker != null && GroundChecker.IsGrounded)
            {
                Vector3 normalStart = origin + Vector3.down * 0.5f;
                UnityEditor.Handles.color = Color.yellow;
                UnityEditor.Handles.DrawLine(normalStart, normalStart + GroundChecker.SurfaceNormal * lineLength, gizmoLineThickness);
                UnityEditor.Handles.Label(normalStart + GroundChecker.SurfaceNormal * lineLength,
                    $"Normal\n{GroundChecker.GroundType}");
            }
        }
#endif
    }

    [Serializable]
    public class ImpulseRequest
    {
        public Vector3 impulseVelocity;
        public float horizontalDecaySpeed = 5;
        public float verticalDecaySpeed = 5;
        public bool forceUnground = true;
        public bool resetGravity = true;
        public bool removeOnGrounded = true;
    }

    [Serializable]
    public class Impulse
    {
        [ShowInInspector]
        public Vector3 Velocity { get; set; }

        [ShowInInspector]
        public float HorizontalDecaySpeed { get; set; } = 5f;

        [ShowInInspector]
        public float VerticalDecaySpeed { get; set; } = 5f;

        [ShowInInspector]
        public bool RemoveOnGrounded { get; private set; }

        public Impulse() { }

        public Impulse(Vector3 velocity, float horizontalDecay = 5f,
                        float verticalDecay = 5f, bool removeOnGrounded = false)
        {
            Velocity = velocity;
            HorizontalDecaySpeed = horizontalDecay;
            VerticalDecaySpeed = verticalDecay;
            RemoveOnGrounded = removeOnGrounded;
        }

        public void Update(float deltaTime)
        {
            float hDecay = Mathf.Exp(-HorizontalDecaySpeed * deltaTime);
            float vDecay = Mathf.Exp(-VerticalDecaySpeed * deltaTime);

            Velocity = new Vector3(Velocity.x * hDecay,
                                    Velocity.y * vDecay,
                                    Velocity.z * hDecay
            );
        }


        public Vector3 GetDisplacementAndUpdate(float deltaTime)
        {
            float hDecay = Mathf.Exp(-HorizontalDecaySpeed * deltaTime);
            float vDecay = Mathf.Exp(-VerticalDecaySpeed * deltaTime);

            Vector3 displacement = Vector3.zero;

            // Horizontal (XZ)
            if (HorizontalDecaySpeed < 0.001f)
            {
                displacement.x = Velocity.x * deltaTime;
                displacement.z = Velocity.z * deltaTime;
            }
            else
            {
                displacement.x = Velocity.x * (1f - hDecay) / HorizontalDecaySpeed;
                displacement.z = Velocity.z * (1f - hDecay) / HorizontalDecaySpeed;
            }

            // Vertical (Y)
            if (VerticalDecaySpeed < 0.001f)
            {
                displacement.y = Velocity.y * deltaTime;
            }
            else
            {
                displacement.y = Velocity.y * (1f - vDecay) / VerticalDecaySpeed;
            }

            // Update velocity AFTER displacement
            Velocity = new Vector3(
                Velocity.x * hDecay,
                Velocity.y * vDecay,
                Velocity.z * hDecay
            );

            return displacement;
        }


        public bool IsExpired(float horizontalThreshold = 0.01f,
                                float verticalThreshold = 0.01f)
        {
            Vector2 horizontal = new Vector2(Velocity.x, Velocity.z);

            return horizontal.sqrMagnitude < horizontalThreshold * horizontalThreshold &&
                    Mathf.Abs(Velocity.y) < verticalThreshold;
        }

    }

    public enum ResolveMethod
    {
        FlatGround,
        SlopeGround,
        SlopeGround_RaycastMiss,
        SteepGround,
        Airborne,
        Airborne_SteepWithPositiveY
    }
}