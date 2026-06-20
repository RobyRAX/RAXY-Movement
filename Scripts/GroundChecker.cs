using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using RAXY.Utility.Gizmo;
using Sirenix.OdinInspector;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RAXY.Movement
{
    public class GroundChecker : MonoBehaviour
    {
        [TitleGroup("Events")]
        [FoldoutGroup("Events/Events")]
        public UnityEvent OnGrounded;

        [FoldoutGroup("Events/Events")]
        public UnityEvent OnUngrounded;

        public event Action<bool> IsGroundedChange;
        public event Action<bool> OnSlopeChange;
        public event Action<bool> OnSteepChange;
        public event Action<bool> OnRaycastHitChange;

        [TitleGroup("Setting")]
        public bool isUseRaycast = false;

        [TitleGroup("Setting")]
        public bool isUseGroundConfirmation = false;

        public void SetUseRaycast(bool useRaycast)
        {
            isUseRaycast = useRaycast;
        }

        [TitleGroup("Setting")]
        public float RaycastCheckDistance = 2f;

        [TitleGroup("Setting")]
        public float Grounded_SphereCastCheckDistance = 0.05f;
        [TitleGroup("Setting")]
        public float Airborne_SphereCastCheckDistance = 0.1f;
        [TitleGroup("Setting")]
        public LayerMask GroundCheckLayers;
        [TitleGroup("Setting")]
        public LayerMask ForceSteepLayers;
        [TitleGroup("Setting")]
        public float ungroundedDelay = 0.1f;
        [TitleGroup("Setting")]
        public float groundTypeChangeDelay = 0.2f;

        [TitleGroup("Status")]
        [ShowInInspector]
        public GroundType GroundType { get; private set; }
        [TitleGroup("Status")]
        [ShowInInspector]
        float _currentGroundTypeChangeDelay;

        [TitleGroup("Status")]
        [ShowInInspector]
        bool _isGrounded;
        public bool IsGrounded
        {
            get => _isGrounded;
            set
            {
                if (_isGrounded == value)
                    return;
                _isGrounded = value;
                IsGroundedChange?.Invoke(_isGrounded);

                if (_isGrounded)
                {
                    //CustomDebug.Log($"Grounded + {OnSteep}");
                    OnGrounded?.Invoke();
                }
                else
                {
                    OnUngrounded?.Invoke();
                }
            }
        }

        [TitleGroup("Status")]
        [ShowInInspector]
        bool _onSlope;
        public bool OnSlope
        {
            get => _onSlope;
            set
            {
                if (_onSlope == value)
                    return;
                _onSlope = value;
                OnSlopeChange?.Invoke(_onSlope);
            }
        }

        [TitleGroup("Status")]
        [ShowInInspector]
        bool _onSteep;
        public bool OnSteep
        {
            get => _onSteep;
            set
            {
                if (_onSteep == value)
                    return;
                _onSteep = value;
                OnSteepChange?.Invoke(_onSteep);
            }
        }

        [TitleGroup("Status")]
        [ShowInInspector]
        [ShowIf("@isUseRaycast")]
        bool _raycastHit;
        public bool RaycastHit
        {
            get => _raycastHit;
            set
            {
                if (_raycastHit == value)
                    return;
                _raycastHit = value;
                OnRaycastHitChange?.Invoke(_raycastHit);
            }
        }

        [TitleGroup("Status")]
        [ShowInInspector, ReadOnly]
        public Vector3 SurfaceNormal { get; private set; }

        [TitleGroup("Status")]
        [ShowInInspector, ReadOnly]
        public Vector3 SurfaceDirection
        {
            get
            {
                return Vector3.Cross(SurfaceNormal, Vector3.Cross(SurfaceNormal, Vector3.up)).normalized;
            }
        }

        [TitleGroup("Status")]
        [ShowInInspector, ReadOnly]
        public float SurfaceAngle { get; private set; }

        [TitleGroup("Status")]
        [ShowInInspector, ReadOnly]
        public bool IsForceUngrounded { get; private set; }

        CharacterController _charCon;
        const float MIN_SLOPE_ANGLE = 5f;

        [TitleGroup("Debug")]
        [ShowInInspector]
        bool _useGroundTypeDelay = true;

        [TitleGroup("Debug")]
        [ShowInInspector]
        public LayerMask CombinedGroundMask { get; private set; }

        [TitleGroup("Debug")]
        [ShowInInspector]
        private float _cachedSlopeLimit;

        [TitleGroup("Debug")]
        [ShowInInspector]
        private float _cachedRadius;

        float _ungroundedTimer = 0f;
        bool _waitingForUnground = false;

#if UNITY_EDITOR
        Vector3 _debugDrawOrigin;
        bool _debugDrawValid;
#endif

        public void SetMask(LayerMask groundMask, LayerMask forceSteepMask)
        {
            GroundCheckLayers = groundMask;
            ForceSteepLayers = forceSteepMask;
            CombinedGroundMask = GroundCheckLayers | ForceSteepLayers;
        }

        void Awake()
        {
            CombinedGroundMask = GroundCheckLayers | ForceSteepLayers;
        }

        void Start()
        {
            _charCon = GetComponent<CharacterController>();
            Cache_SlopeLimit();
            Cache_GroundCheckerRadius();

            SetUseGroundTypeDelay(true);
        }

        public void Cache_SlopeLimit()
        {
            _cachedSlopeLimit = _charCon.slopeLimit;
        }

        public void Cache_GroundCheckerRadius()
        {
            _cachedRadius = _charCon.radius; //+ _charCon.skinWidth;
        }

        private GroundType DetermineTargetGroundType()
        {
            if (!IsGrounded)
                return GroundType.None;

            if (OnSteep)
                return GroundType.Steep;

            if (OnSlope)
                return GroundType.Slope;

            return GroundType.Flat;
        }

        public void SetUseGroundTypeDelay(bool useDelay)
        {
            _useGroundTypeDelay = useDelay;

            if (!useDelay)
            {
                // Reset delay timer when disabled
                _currentGroundTypeChangeDelay = 0f;
            }
        }

        RaycastHit sphereHit;
        RaycastHit rayHit;
        void Update()
        {
            if (IsForceUngrounded)
                return;

            float checkDistance = IsGrounded ?
                Grounded_SphereCastCheckDistance :
                Airborne_SphereCastCheckDistance;

            Vector3 sphereOrigin = transform.position + (Vector3.up * _cachedRadius);

            bool groundedNow = Physics.SphereCast(
                sphereOrigin,
                _cachedRadius,
                Vector3.down,
                out sphereHit,
                checkDistance,
                CombinedGroundMask,
                QueryTriggerInteraction.Ignore
            );

            if (groundedNow)
            {
                _ungroundedTimer = 0f;
                _waitingForUnground = false;
                IsGrounded = true;

                SurfaceNormal = sphereHit.normal;
                SurfaceAngle = Vector3.Angle(Vector3.up, SurfaceNormal);

                bool isForceSteep = ((1 << sphereHit.collider.gameObject.layer) & ForceSteepLayers) != 0;
                bool raycastValid = false;

                // Extra Raycast check for better steep detection
                if (isUseRaycast)
                {
                    RaycastHit = Physics.Raycast(sphereOrigin,
                                                    Vector3.down,
                                                    out rayHit,
                                                    RaycastCheckDistance,
                                                    CombinedGroundMask,
                                                    QueryTriggerInteraction.Ignore);
                    if (RaycastHit)
                    {
                        raycastValid = true;
                        float rayAngle = Vector3.Angle(Vector3.up, rayHit.normal);

                        // Replace with steeper result
                        if (rayAngle > SurfaceAngle)
                        {
                            SurfaceAngle = rayAngle;
                            SurfaceNormal = rayHit.normal;
                        }
                    }
                }

                OnSlope = !isForceSteep && SurfaceAngle <= _cachedSlopeLimit && SurfaceAngle > MIN_SLOPE_ANGLE;
                OnSteep = isForceSteep || SurfaceAngle > _cachedSlopeLimit;

#if UNITY_EDITOR
                _debugDrawOrigin = sphereHit.point; // SphereCast hit point
                if (raycastValid)
                    _debugDrawOrigin = rayHit.point; // Use Raycast hit point if available
                _debugDrawValid = true;
#endif
            }
            else
            {
                SurfaceNormal = Vector3.up;
                SurfaceAngle = 0f;

                OnSlope = false;
                OnSteep = false;

#if UNITY_EDITOR
                _debugDrawValid = false;
#endif

                if (IsGrounded)
                {
                    if (!_waitingForUnground)
                    {
                        _waitingForUnground = true;
                        _ungroundedTimer = 0f;
                    }

                    _ungroundedTimer += Time.deltaTime;

                    if (_ungroundedTimer >= ungroundedDelay)
                    {
                        IsGrounded = false;
                        _waitingForUnground = false;
                    }
                }
            }

            GroundType targetGroundType = DetermineTargetGroundType();

            if (targetGroundType != GroundType)
            {
                bool fromSteepToFlat = GroundType == GroundType.Steep && targetGroundType == GroundType.Flat;
                bool fromSlopeToSteep = GroundType == GroundType.Slope && targetGroundType == GroundType.Steep;
                bool fromFlatToSteep = GroundType == GroundType.Flat && targetGroundType == GroundType.Steep;

                bool shouldUseDelay = _useGroundTypeDelay &&
                                        (fromSteepToFlat || fromSlopeToSteep);

                if (shouldUseDelay)
                {
                    _currentGroundTypeChangeDelay += Time.deltaTime;

                    if (_currentGroundTypeChangeDelay >= groundTypeChangeDelay)
                    {
                        GroundType = targetGroundType;
                        _currentGroundTypeChangeDelay = 0f;
                    }
                }
                else
                {
                    GroundType = targetGroundType;
                    _currentGroundTypeChangeDelay = 0f;
                }
            }
            else
            {
                _currentGroundTypeChangeDelay = 0f;
            }
        }

        bool RaycastFromHitPointDown(Vector3 hitPoint, out RaycastHit hit,
                                    float rayHeight = 0.3f, float rayDistance = 0.6f)
        {
            Vector3 origin = hitPoint + Vector3.up * rayHeight;

            bool hasHit = Physics.Raycast(
                origin,
                Vector3.down,
                out hit,
                rayDistance,
                CombinedGroundMask,
                QueryTriggerInteraction.Ignore
            );

            return hasHit;
        }

        [TitleGroup("Status")]
        [ShowInInspector]
        public GroundType ConfirmedGroundType { get; private set; }
        public GroundType ConfirmGroundType()
        {
            if (IsGrounded == false)
                return GroundType.None;

            if (sphereHit.point == Vector3.zero)
                return ConfirmedGroundType;

            Vector3 origin = transform.position;
            Vector3 dir = (sphereHit.point - origin).normalized;
            float distance = Vector3.Distance(origin, sphereHit.point) + 0.5f;
            Vector3 end = origin + dir * distance;

            bool rayValid = Physics.Raycast(
                origin,
                dir,
                out RaycastHit hit,
                distance,
                CombinedGroundMask,
                QueryTriggerInteraction.Ignore
            );

#if UNITY_EDITOR
            float lineDuration = 5;
            // Debug raycast (from hit point downward)
            Debug.DrawLine(
                origin,
                end,
                rayValid ? Color.yellow : Color.red,
                lineDuration
            );
#endif

            if (rayValid)
            {
                SurfaceNormal = hit.normal;
                SurfaceAngle = Vector3.Angle(Vector3.up, SurfaceNormal);

                OnSlope = SurfaceAngle <= _cachedSlopeLimit && SurfaceAngle > MIN_SLOPE_ANGLE;
                OnSteep = SurfaceAngle > _cachedSlopeLimit;

#if UNITY_EDITOR
                Debug.DrawLine(
                    hit.point,
                    hit.point + SurfaceNormal * 0.5f,
                    Color.green,
                    lineDuration
                );
#endif
            }

            ConfirmedGroundType = DetermineTargetGroundType();
            return ConfirmedGroundType;
        }


        public async UniTask ForceUngrounded(float duration = 0.1f)
        {
            IsForceUngrounded = true;

            IsGrounded = false;
            SurfaceNormal = Vector3.up;

            await UniTask.WaitForSeconds(duration);
            IsForceUngrounded = false;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;

            if (_charCon == null)
                _charCon = GetComponent<CharacterController>();

            float checkDistance = IsGrounded ?
                Grounded_SphereCastCheckDistance :
                Airborne_SphereCastCheckDistance;

            Vector3 sphereOrigin = transform.position + (Vector3.up * _cachedRadius);
            CustomGizmos.DrawSphereCastGizmos(sphereOrigin, Vector3.down, checkDistance, _cachedRadius);

            if (isUseRaycast)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(sphereOrigin, sphereOrigin + Vector3.down * RaycastCheckDistance);
            }

            // Draw thick lines for surface normal and direction
            // if (_debugDrawValid)
            // {
            //     Handles.color = Color.green;
            //     Handles.DrawLine(_debugDrawOrigin, _debugDrawOrigin + SurfaceNormal * 1f, 10f);
            //     Handles.color = Color.blue;
            //     Handles.DrawLine(_debugDrawOrigin, _debugDrawOrigin + SurfaceDirection * 1f, 10f);
            // }
        }
#endif
    }

    public enum GroundType
    {
        None, Flat, Slope, Steep
    }
}
