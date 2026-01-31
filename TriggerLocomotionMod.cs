
using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;

[assembly: MelonInfo(typeof(TriggerLocomotion.TriggerLocomotionMod), "Stone Stick By Nano", "2.1.0", "Nano")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace TriggerLocomotion
{
    public sealed class TriggerLocomotionMod : MelonMod
    {
        private const string PrefCategory = "TriggerLocomotion";
        private const float DefaultWalkSpeed = 2.0f;
        private const float DefaultButtonThreshold = 0.5f;
        private const float DefaultDebugLogInterval = 1.0f;
        private const float DefaultSprintSpeedMultiplier = 1.25f;
        private const float DefaultSprintArmSpeedThreshold = 0.6f;
        private const float DefaultSprintArmSpeedFull = 1.5f;

        private const float PhysFollowSpeed = 9.0f;
        private const float PhysSnapDist = 1.25f;
        private const float MaxVrLead = 0.35f;
        private const float ExternalRecenterThreshold = 0.75f;
        private const float SprintPoseMinArmSpeedFactor = 0.9f;

        private MelonPreferences_Entry<float> _walkSpeed;
        private MelonPreferences_Entry<float> _buttonThreshold;
        private MelonPreferences_Entry<bool> _overrideJoystickMovement;
        private MelonPreferences_Entry<bool> _debugLogging;
        private MelonPreferences_Entry<float> _sprintMultiplier;
        private MelonPreferences_Entry<float> _sprintArmSpeedThreshold;
        private MelonPreferences_Entry<float> _sprintArmSpeedFull;
        private MelonPreferences_Entry<bool> _sprintPoseHack;

        private Transform _rigRoot;
        private Transform _locomotionRoot;
        private Transform _vrRoot;
        private Transform _physicsRoot;
        private Rigidbody _physicsRb;
        private Camera _mainCamera;
        private bool _rigReady;
        private float _autoWalkSpeed = -1f;
        private float _startupDelayUntil;

        private Vector3 _lockedLocalPosition;
        private bool _lastPositionValid;
        private Vector3 _physicsLockedLocalPosition;
        private bool _physicsLastPositionValid;
        private Vector3 _vrLockedLocalPosition;
        private bool _vrLastPositionValid;

        private Vector3 _desiredVrPos;
        private bool _desiredVrInit;

        private float _nextDebugLogTime;
        private float _nextMoveLogTime;
        private float _nextSprintLogTime;
        private float _nextCandidateLogTime;

        private bool _lastButtonX;
        private bool _lastButtonY;
        private bool _lastButtonA;
        private bool _lastButtonB;
        private string _lastButtonSource = "none";

        private bool _moveHeld;

        private bool _pollerTypeMissingLogged;
        private bool _pollerControllerNullLogged;
        private bool _managerTypeMissingLogged;
        private bool _managerInstanceNullLogged;
        private bool _managerControllerNullLogged;
        private bool _buttonNoneLogged;
        private bool _overrideOffLogged;

        private bool _lastLogAllowMove;
        private string _lastLogTargetName = "";
        private string _lastLogTargetPath = "";
        private bool _lastLogAppliedLock;
        private bool _lastLogValid;
        private bool _lastLogButtonX;
        private bool _lastLogButtonY;
        private bool _lastLogButtonA;
        private bool _lastLogButtonB;
        private string _lastLogButtonSource = "";

        private readonly Dictionary<int, Vector3> _candidateLastLocal = new Dictionary<int, Vector3>();

        private object _managerUnityInstance;
        private bool _managerUnitySearched;

        private Transform _leftController;
        private Transform _rightController;
        private Vector3 _lastLeftControllerPos;
        private Vector3 _lastRightControllerPos;
        private bool _lastControllerPosValid;
        private float _lastArmSpeed;

        private bool _sprintTargetsResolved;
        private bool _sprintPoseLogged;
        private readonly List<SprintTarget> _sprintTargets = new List<SprintTarget>();

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory(PrefCategory, "Trigger Locomotion");
            _walkSpeed = cat.CreateEntry("WalkSpeed", DefaultWalkSpeed, "Walk Speed (m/s)", "Fallback walking speed if auto-detect fails.");
            _buttonThreshold = cat.CreateEntry("GripThreshold", DefaultButtonThreshold, "Button Threshold", "Threshold for button values if they are floats.");
            _overrideJoystickMovement = cat.CreateEntry("OverrideJoystickMovement", true, "Override Joystick Movement", "Cancels horizontal movement when buttons are not held.");
            _debugLogging = cat.CreateEntry("DebugLogging", true, "Debug Logging", "Logs locomotion target + cancel info.");
            _sprintMultiplier = cat.CreateEntry("SprintMultiplier", DefaultSprintSpeedMultiplier, "Sprint Speed Multiplier", "Max sprint multiplier when arm speed is high.");
            _sprintArmSpeedThreshold = cat.CreateEntry("SprintArmSpeedThreshold", DefaultSprintArmSpeedThreshold, "Sprint Start Arm Speed", "Arm speed required to begin sprinting.");
            _sprintArmSpeedFull = cat.CreateEntry("SprintArmSpeedFull", DefaultSprintArmSpeedFull, "Sprint Full Arm Speed", "Arm speed for full sprint multiplier.");
            _sprintPoseHack = cat.CreateEntry("SprintPoseHack", true, "Sprint Pose Hack", "Attempts to toggle sprint/running pose on movement components.");

            ResetState();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            ResetState();
        }

        private void ResetState()
        {
            _rigReady = false;
            _rigRoot = null;
            _locomotionRoot = null;
            _vrRoot = null;
            _physicsRoot = null;
            _physicsRb = null;
            _mainCamera = null;
            _autoWalkSpeed = -1f;
            _startupDelayUntil = Time.realtimeSinceStartup + 5f;

            _lockedLocalPosition = Vector3.zero;
            _lastPositionValid = false;
            _physicsLockedLocalPosition = Vector3.zero;
            _physicsLastPositionValid = false;
            _vrLockedLocalPosition = Vector3.zero;
            _vrLastPositionValid = false;

            _desiredVrPos = Vector3.zero;
            _desiredVrInit = false;

            _nextDebugLogTime = 0f;
            _nextMoveLogTime = 0f;
            _nextSprintLogTime = 0f;
            _nextCandidateLogTime = 0f;

            _lastButtonX = false;
            _lastButtonY = false;
            _lastButtonA = false;
            _lastButtonB = false;
            _lastButtonSource = "none";

            _moveHeld = false;

            _pollerTypeMissingLogged = false;
            _pollerControllerNullLogged = false;
            _managerTypeMissingLogged = false;
            _managerInstanceNullLogged = false;
            _managerControllerNullLogged = false;
            _buttonNoneLogged = false;
            _overrideOffLogged = false;

            _lastLogAllowMove = false;
            _lastLogTargetName = "";
            _lastLogTargetPath = "";
            _lastLogAppliedLock = false;
            _lastLogValid = false;
            _lastLogButtonX = false;
            _lastLogButtonY = false;
            _lastLogButtonA = false;
            _lastLogButtonB = false;
            _lastLogButtonSource = "";

            _candidateLastLocal.Clear();

            _managerUnityInstance = null;
            _managerUnitySearched = false;

            _leftController = null;
            _rightController = null;
            _lastControllerPosValid = false;
            _lastArmSpeed = 0f;

            _sprintTargetsResolved = false;
            _sprintPoseLogged = false;
            _sprintTargets.Clear();
        }
        public override void OnUpdate()
        {
            if (Time.realtimeSinceStartup < _startupDelayUntil)
                return;

            try
            {
                if (!IsApiReady())
                    return;

                EnsureRig();
                if (!_rigReady)
                    return;

                _lastButtonSource = "none";
                bool x = GetButtonPressed(Button.X);
                bool y = GetButtonPressed(Button.Y);
                bool a = GetButtonPressed(Button.A);
                bool b = GetButtonPressed(Button.B);
                SetButtonDebug(x, y, a, b, _lastButtonSource);

                _moveHeld = x || y || a || b;
                if (!_moveHeld)
                    ApplySprintPose(false);

                if (_moveHeld)
                {
                    var cam = _mainCamera ?? Camera.main;
                    if (cam == null)
                        return;

                    Vector3 forwardWorld = cam.transform.forward;
                    forwardWorld.y = 0f;
                    Vector3 rightWorld = cam.transform.right;
                    rightWorld.y = 0f;

                    if (forwardWorld.sqrMagnitude < 0.0001f || rightWorld.sqrMagnitude < 0.0001f)
                        return;

                    forwardWorld.Normalize();
                    rightWorld.Normalize();

                    float forwardInput = (x ? 1f : 0f) + (a ? -1f : 0f);
                    float rightInput = (b ? 1f : 0f) + (y ? -1f : 0f);

                    Vector3 moveDirWorld = forwardWorld * forwardInput + rightWorld * rightInput;
                    if (moveDirWorld.sqrMagnitude > 0.0001f)
                    {
                        if (moveDirWorld.sqrMagnitude > 1f)
                            moveDirWorld.Normalize();

                        float sprintMultiplier = GetSprintMultiplier();
                        ApplySprintPose(ShouldApplySprintPose(sprintMultiplier));
                        ApplyMove(moveDirWorld, sprintMultiplier);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[TriggerLocomotion] error: {ex}");
            }
        }

        public override void OnFixedUpdate()
        {
            if (Time.realtimeSinceStartup < _startupDelayUntil)
                return;

            if (!IsApiReady())
                return;

            FollowPhysicsToVr();
        }

        public override void OnLateUpdate()
        {
            if (Time.realtimeSinceStartup < _startupDelayUntil)
                return;

            if (!IsApiReady())
                return;

            EnforceMovementLock(_moveHeld);
            ClampVrLead();
        }

        private bool IsApiReady()
        {
            try
            {
                return Calls.IsInitialized() && Calls.IsMapInitialized();
            }
            catch
            {
                return false;
            }
        }

        private void EnsureRig()
        {
            if (_rigReady)
                return;

            _mainCamera = Camera.main;

            Transform playerRoot = null;
            try
            {
                var localPlayer = Calls.Players.GetLocalPlayer();
                playerRoot = TryGetTransform(localPlayer);
                if (playerRoot != null)
                    playerRoot = playerRoot.root;
            }
            catch
            {
                // Best-effort only.
            }

            Transform camRoot = _mainCamera != null ? _mainCamera.transform.root : null;
            Transform root = playerRoot ?? camRoot;
            if (root == null)
                return;

            _rigRoot = root;
            _vrRoot = FindVROrigin(_rigRoot);
            _physicsRoot = FindPhysicsRoot(_rigRoot);
            _locomotionRoot = FindLocomotionRoot(_rigRoot);
            _physicsRb = _physicsRoot != null ? (_physicsRoot.GetComponent<Rigidbody>() ?? _physicsRoot.GetComponentInChildren<Rigidbody>()) : null;

            TryResolveWalkSpeed();
            EnsureControllers();

            if (_debugLogging.Value)
                LoggerInstance.Msg($"[TriggerLocomotion] Locomotion root: {_locomotionRoot?.name} path={GetTransformPath(_locomotionRoot)}");

            if (_debugLogging.Value && _physicsRoot != null)
                LoggerInstance.Msg($"[TriggerLocomotion] Physics root: {_physicsRoot.name} path={GetTransformPath(_physicsRoot)}");

            _rigReady = true;

            var target = _locomotionRoot ?? _rigRoot;
            if (target != null)
            {
                _lockedLocalPosition = target.localPosition;
                _lastPositionValid = true;
            }

            if (_vrRoot != null)
            {
                _desiredVrPos = _vrRoot.position;
                _desiredVrInit = true;
            }
        }

        private Transform FindLocomotionRoot(Transform root)
        {
            if (root == null)
                return null;

            var physics = FindPhysicsRoot(root);
            if (physics != null)
                return physics;

            var vrRoot = FindVROrigin(root);
            if (vrRoot != null)
                return vrRoot;

            string[] priorityNames =
            {
                "XROrigin",
                "XR Origin",
                "Origin",
                "Locomotion",
                "LocomotionRoot"
            };

            foreach (var name in priorityNames)
            {
                var found = FindChildContainsSkipSelf(root, name);
                if (found != null)
                    return found;
            }

            return root;
        }

        private Transform FindVROrigin(Transform root)
        {
            if (root == null)
                return null;

            var vrChild = root.Find("VR");
            if (vrChild != null)
                return vrChild;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var tn = (t.name ?? "").Trim();
                if (string.Equals(tn, "VR", StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        private Transform FindPhysicsRoot(Transform root)
        {
            if (root == null)
                return null;

            var physics = root.Find("Physics");
            if (physics != null)
                return physics;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var tn = (t.name ?? "").Trim();
                if (string.Equals(tn, "Physics", StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }
        private void EnsureControllers()
        {
            var root = _vrRoot ?? _rigRoot;
            if ((_leftController != null && _rightController != null) || root == null)
                return;

            var prevLeft = _leftController;
            var prevRight = _rightController;

            _leftController = _leftController
                              ?? FindChildExact(root, "Left Controller")
                              ?? FindChildExact(root, "LeftController")
                              ?? FindChildContainsSkipSelf(root, "Left Controller")
                              ?? FindChildContainsSkipSelf(root, "LeftController")
                              ?? FindControllerHeuristic(root, true);

            _rightController = _rightController
                               ?? FindChildExact(root, "Right Controller")
                               ?? FindChildExact(root, "RightController")
                               ?? FindChildContainsSkipSelf(root, "Right Controller")
                               ?? FindChildContainsSkipSelf(root, "RightController")
                               ?? FindControllerHeuristic(root, false);

            if (!ReferenceEquals(prevLeft, _leftController) || !ReferenceEquals(prevRight, _rightController))
            {
                _lastControllerPosValid = false;
                _lastArmSpeed = 0f;

                if (_debugLogging.Value)
                    LoggerInstance.Msg($"[TriggerLocomotion] controllers left={GetTransformPath(_leftController)} right={GetTransformPath(_rightController)}");
            }
        }

        private static Transform FindChildExact(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(t.name, name, StringComparison.Ordinal))
                    return t;
            }

            return null;
        }

        private static Transform FindChildContainsSkipSelf(Transform root, string contains)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root)
                    continue;
                if (t.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }

            return null;
        }

        private static Transform FindControllerHeuristic(Transform root, bool left)
        {
            if (root == null)
                return null;

            string side = left ? "left" : "right";
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                string n = (t.name ?? "").ToLowerInvariant();
                if (!n.Contains(side))
                    continue;
                if (!(n.Contains("controller") || n.Contains("hand")))
                    continue;

                return t;
            }

            return null;
        }
        private void ApplyMove(Vector3 moveDirWorld, float speedMultiplier)
        {
            Vector3 dirWorld = moveDirWorld;
            dirWorld.y = 0f;
            if (dirWorld.sqrMagnitude < 0.0001f)
                return;
            dirWorld.Normalize();

            float speed = GetWalkSpeed() * Mathf.Max(0.1f, speedMultiplier);
            Vector3 deltaWorld = dirWorld * speed * Time.deltaTime;

            if (_debugLogging.Value && Time.realtimeSinceStartup >= _nextMoveLogTime)
            {
                string sprintText = speedMultiplier > 1.01f ? $" sprintX{speedMultiplier:F2}" : "";
                string targetName = _vrRoot != null ? "VR" : (_locomotionRoot ?? _rigRoot)?.name;
                LoggerInstance.Msg($"[TriggerLocomotion] applyMove target={targetName} deltaWorld={deltaWorld.magnitude:F4}{sprintText}");
                _nextMoveLogTime = Time.realtimeSinceStartup + DefaultDebugLogInterval;
            }

            if (_vrRoot != null)
            {
                MoveVr(deltaWorld);
                return;
            }

            var target = _locomotionRoot ?? _rigRoot;
            if (target == null)
                return;

            Vector3 deltaLocal = target.parent != null
                ? target.parent.InverseTransformVector(deltaWorld)
                : deltaWorld;
            deltaLocal.y = 0f;

            Vector3 currentLocal = target.localPosition;
            if (!_lastPositionValid)
            {
                _lockedLocalPosition = currentLocal;
                _lastPositionValid = true;
            }

            if (_overrideJoystickMovement.Value)
            {
                _lockedLocalPosition.x += deltaLocal.x;
                _lockedLocalPosition.z += deltaLocal.z;

                currentLocal.x = _lockedLocalPosition.x;
                currentLocal.z = _lockedLocalPosition.z;
                target.localPosition = currentLocal;
            }
            else
            {
                currentLocal.x += deltaLocal.x;
                currentLocal.z += deltaLocal.z;
                target.localPosition = currentLocal;
                _lockedLocalPosition = target.localPosition;
                _lastPositionValid = true;
            }
        }

        private void MoveVr(Vector3 deltaWorld)
        {
            if (_vrRoot == null)
                return;

            deltaWorld.y = 0f;

            if (!_desiredVrInit)
            {
                _desiredVrPos = _vrRoot.position;
                _desiredVrInit = true;
            }
            else
            {
                Vector3 diff = _vrRoot.position - _desiredVrPos;
                diff.y = 0f;
                if (diff.sqrMagnitude > ExternalRecenterThreshold * ExternalRecenterThreshold)
                    ResyncVrToCurrent();
            }

            _desiredVrPos += deltaWorld;
            _desiredVrPos.y = _vrRoot.position.y;
            _vrRoot.position = _desiredVrPos;
        }

        private void ResyncVrToCurrent()
        {
            if (_vrRoot == null)
                return;

            _desiredVrPos = _vrRoot.position;
            _desiredVrInit = true;
        }
        private void FollowPhysicsToVr()
        {
            if (_physicsRb == null || _vrRoot == null)
                return;
            if (IsInAir())
                return;

            try
            {
                Vector3 rbPos = _physicsRb.position;
                Vector3 target = new Vector3(_vrRoot.position.x, rbPos.y, _vrRoot.position.z);

                Vector3 toTarget = target - rbPos;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                if (dist < 0.0001f)
                    return;

                if (dist > PhysSnapDist)
                {
                    _physicsRb.position = target;
                    Vector3 v = _physicsRb.velocity;
                    _physicsRb.velocity = new Vector3(0f, v.y, 0f);
                    return;
                }

                float maxStep = PhysFollowSpeed * Time.fixedDeltaTime;
                Vector3 next = rbPos + Vector3.ClampMagnitude(toTarget, maxStep);
                _physicsRb.MovePosition(next);

                if (!IsInAir())
                {
                    Vector3 vel = (next - rbPos) / Time.fixedDeltaTime;
                    Vector3 cur = _physicsRb.velocity;
                    _physicsRb.velocity = new Vector3(vel.x, cur.y, vel.z);
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        private void ClampVrLead()
        {
            if (_physicsRb == null || _vrRoot == null)
                return;
            if (IsInAir())
                return;

            Vector3 vr = _vrRoot.position;
            Vector3 phys = _physicsRb.position;

            Vector3 diff = new Vector3(vr.x - phys.x, 0f, vr.z - phys.z);
            float d = diff.magnitude;
            if (d > MaxVrLead)
            {
                Vector3 clamped = diff.normalized * MaxVrLead;
                _desiredVrPos = new Vector3(phys.x + clamped.x, vr.y, phys.z + clamped.z);
                _desiredVrInit = true;
                _vrRoot.position = _desiredVrPos;
            }
        }
        private void EnforceMovementLock(bool allowMove)
        {
            if (!_overrideJoystickMovement.Value)
            {
                LogOnce(ref _overrideOffLogged, "[TriggerLocomotion] joystick override disabled (OverrideJoystickMovement=false)");
                _lastPositionValid = false;
                _physicsLastPositionValid = false;
                _vrLastPositionValid = false;
                return;
            }

            _overrideOffLogged = false;

            var target = _locomotionRoot ?? _rigRoot;
            if (!_rigReady || target == null)
            {
                _lastPositionValid = false;
                _physicsLastPositionValid = false;
                _vrLastPositionValid = false;
                return;
            }

            if (!allowMove)
                LogLocomotionCandidates(_rigRoot);

            if (!_lastPositionValid)
            {
                _lockedLocalPosition = target.localPosition;
                _lastPositionValid = true;

                if (_vrRoot != null)
                {
                    _vrLockedLocalPosition = _vrRoot.localPosition;
                    _vrLastPositionValid = true;
                }

                if (_physicsRoot != null)
                {
                    _physicsLockedLocalPosition = _physicsRoot.localPosition;
                    _physicsLastPositionValid = true;
                }

                return;
            }

            bool appliedLock = false;
            bool vrAppliedLock = false;
            bool physAppliedLock = false;

            if (allowMove)
            {
                _lockedLocalPosition = target.localPosition;
            }
            else
            {
                Vector3 current = target.localPosition;
                Vector2 horizontalDelta = new Vector2(current.x - _lockedLocalPosition.x, current.z - _lockedLocalPosition.z);
                appliedLock = horizontalDelta.sqrMagnitude > 0.0000001f;
                current.x = _lockedLocalPosition.x;
                current.z = _lockedLocalPosition.z;
                target.localPosition = current;
            }

            if (_vrRoot != null)
            {
                if (!_vrLastPositionValid)
                {
                    _vrLockedLocalPosition = _vrRoot.localPosition;
                    _vrLastPositionValid = true;
                }
                else if (allowMove)
                {
                    _vrLockedLocalPosition = _vrRoot.localPosition;
                }
                else
                {
                    Vector3 vrCurrent = _vrRoot.localPosition;
                    Vector2 vrDelta = new Vector2(vrCurrent.x - _vrLockedLocalPosition.x, vrCurrent.z - _vrLockedLocalPosition.z);
                    vrAppliedLock = vrDelta.sqrMagnitude > 0.0000001f;
                    if (vrAppliedLock)
                    {
                        vrCurrent.x = _vrLockedLocalPosition.x;
                        vrCurrent.z = _vrLockedLocalPosition.z;
                        _vrRoot.localPosition = vrCurrent;
                    }
                }
            }

            if (_physicsRoot != null)
            {
                if (!_physicsLastPositionValid)
                {
                    _physicsLockedLocalPosition = _physicsRoot.localPosition;
                    _physicsLastPositionValid = true;
                }
                else if (allowMove)
                {
                    _physicsLockedLocalPosition = _physicsRoot.localPosition;
                }
                else
                {
                    Vector3 physLocal = _physicsRoot.localPosition;
                    Vector2 physDelta = new Vector2(physLocal.x - _physicsLockedLocalPosition.x, physLocal.z - _physicsLockedLocalPosition.z);
                    physAppliedLock = physDelta.sqrMagnitude > 0.0000001f;
                    if (physAppliedLock)
                    {
                        physLocal.x = _physicsLockedLocalPosition.x;
                        physLocal.z = _physicsLockedLocalPosition.z;

                        if (_physicsRb != null)
                        {
                            Vector3 targetWorld = _physicsRoot.parent != null
                                ? _physicsRoot.parent.TransformPoint(physLocal)
                                : physLocal;
                            _physicsRb.MovePosition(targetWorld);
                            Vector3 v = _physicsRb.velocity;
                            _physicsRb.velocity = new Vector3(0f, v.y, 0f);
                        }
                        else
                        {
                            _physicsRoot.localPosition = physLocal;
                        }
                    }
                }
            }

            bool anyLock = appliedLock || vrAppliedLock || physAppliedLock;
            if (!allowMove && anyLock && !IsInAir())
                ResyncVrToCurrent();

            if (_debugLogging.Value && Time.realtimeSinceStartup >= _nextDebugLogTime)
            {
                string name = target.name;
                string path = GetTransformPath(target);
                bool logChanged = !_lastLogValid
                    || allowMove != _lastLogAllowMove
                    || _lastButtonX != _lastLogButtonX
                    || _lastButtonY != _lastLogButtonY
                    || _lastButtonA != _lastLogButtonA
                    || _lastButtonB != _lastLogButtonB
                    || !string.Equals(_lastButtonSource, _lastLogButtonSource, StringComparison.Ordinal)
                    || !string.Equals(name, _lastLogTargetName, StringComparison.Ordinal)
                    || !string.Equals(path, _lastLogTargetPath, StringComparison.Ordinal)
                    || anyLock != _lastLogAppliedLock;

                if (logChanged)
                {
                    LoggerInstance.Msg($"[TriggerLocomotion] lock={!allowMove} lockApplied={anyLock} allowMove={allowMove} x={_lastButtonX} y={_lastButtonY} a={_lastButtonA} b={_lastButtonB} src={_lastButtonSource} target={name} path={path}");
                    _lastLogAllowMove = allowMove;
                    _lastLogButtonX = _lastButtonX;
                    _lastLogButtonY = _lastButtonY;
                    _lastLogButtonA = _lastButtonA;
                    _lastLogButtonB = _lastButtonB;
                    _lastLogButtonSource = _lastButtonSource ?? "";
                    _lastLogTargetName = name ?? "";
                    _lastLogTargetPath = path ?? "";
                    _lastLogAppliedLock = anyLock;
                    _lastLogValid = true;
                }

                _nextDebugLogTime = Time.realtimeSinceStartup + DefaultDebugLogInterval;
            }
        }
        private float GetWalkSpeed()
        {
            return _autoWalkSpeed > 0f ? _autoWalkSpeed : _walkSpeed.Value;
        }

        private float GetSprintMultiplier()
        {
            if (!_moveHeld)
                return 1f;

            EnsureControllers();
            float armSpeed = GetArmSpeed();
            float startSpeed = Mathf.Max(0f, _sprintArmSpeedThreshold.Value);
            float fullSpeed = Mathf.Max(startSpeed + 0.01f, _sprintArmSpeedFull.Value);
            float maxMultiplier = Mathf.Clamp(_sprintMultiplier.Value, 1f, 2f);
            if (armSpeed <= startSpeed)
                return 1f;

            float denom = Mathf.Max(0.01f, fullSpeed - startSpeed);
            float t = Mathf.Clamp01((armSpeed - startSpeed) / denom);
            float multiplier = Mathf.Lerp(1f, maxMultiplier, t);

            if (_debugLogging.Value && Time.realtimeSinceStartup >= _nextSprintLogTime)
            {
                LoggerInstance.Msg($"[TriggerLocomotion] sprint armSpeed={armSpeed:F3} sprintX{multiplier:F2}");
                _nextSprintLogTime = Time.realtimeSinceStartup + DefaultDebugLogInterval;
            }

            return multiplier;
        }

        private bool ShouldApplySprintPose(float sprintMultiplier)
        {
            if (!_moveHeld)
                return false;
            if (sprintMultiplier <= 1.05f)
                return false;

            float fullSpeed = Mathf.Max(_sprintArmSpeedThreshold.Value + 0.01f, _sprintArmSpeedFull.Value);
            return _lastArmSpeed >= fullSpeed * SprintPoseMinArmSpeedFactor;
        }

        private bool IsInAir()
        {
            if (_physicsRb == null)
                return false;

            return Mathf.Abs(_physicsRb.velocity.y) > 0.15f;
        }

        private Vector3 GetControllerLocalPosition(Transform controller)
        {
            if (controller == null)
                return Vector3.zero;

            var reference = _vrRoot ?? _rigRoot;
            if (reference != null)
                return reference.InverseTransformPoint(controller.position);

            return controller.position;
        }

        private float GetArmSpeed()
        {
            if (_leftController == null && _rightController == null)
                return 0f;

            float dt = Time.deltaTime;
            if (dt <= 0.0001f)
                return _lastArmSpeed;

            if (!_lastControllerPosValid)
            {
                if (_leftController != null)
                    _lastLeftControllerPos = GetControllerLocalPosition(_leftController);
                if (_rightController != null)
                    _lastRightControllerPos = GetControllerLocalPosition(_rightController);
                _lastControllerPosValid = true;
                _lastArmSpeed = 0f;
                return 0f;
            }

            float leftSpeed = 0f;
            float rightSpeed = 0f;
            if (_leftController != null)
            {
                Vector3 pos = GetControllerLocalPosition(_leftController);
                leftSpeed = (pos - _lastLeftControllerPos).magnitude / dt;
                _lastLeftControllerPos = pos;
            }

            if (_rightController != null)
            {
                Vector3 pos = GetControllerLocalPosition(_rightController);
                rightSpeed = (pos - _lastRightControllerPos).magnitude / dt;
                _lastRightControllerPos = pos;
            }

            _lastArmSpeed = Mathf.Max(leftSpeed, rightSpeed);
            return _lastArmSpeed;
        }

        private void TryResolveWalkSpeed()
        {
            if (_rigRoot == null)
                return;

            try
            {
                var components = _rigRoot.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var comp in components)
                {
                    if (comp == null)
                        continue;

                    var type = comp.GetType();
                    string typeName = type.Name.ToLowerInvariant();
                    if (!typeName.Contains("locomotion") && !typeName.Contains("movement") && !typeName.Contains("move") && !typeName.Contains("controller"))
                        continue;

                    if (TryGetSpeedValue(comp, type, new[] { "walkSpeed", "WalkSpeed", "moveSpeed", "MoveSpeed", "movementSpeed", "MovementSpeed" }, out float speed))
                    {
                        if (speed > 0.1f && speed < 10f)
                        {
                            _autoWalkSpeed = speed;
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        private void ApplySprintPose(bool sprinting)
        {
            if (_sprintPoseHack != null && !_sprintPoseHack.Value)
                return;

            EnsureSprintTargets();
            if (_sprintTargets.Count == 0)
                return;

            foreach (var target in _sprintTargets)
            {
                try
                {
                    if (target.Member is FieldInfo field)
                        field.SetValue(target.Instance, sprinting);
                    else if (target.Member is PropertyInfo prop && prop.CanWrite)
                        prop.SetValue(target.Instance, sprinting, null);
                }
                catch
                {
                    // Best-effort only.
                }
            }
        }

        private void EnsureSprintTargets()
        {
            if (_sprintTargetsResolved || _rigRoot == null)
                return;

            _sprintTargetsResolved = true;
            _sprintTargets.Clear();

            var components = _rigRoot.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var comp in components)
            {
                if (comp == null)
                    continue;

                var type = comp.GetType();
                string typeName = type.Name.ToLowerInvariant();
                if (!typeName.Contains("locomotion") && !typeName.Contains("movement") && !typeName.Contains("controller") && !typeName.Contains("player"))
                    continue;

                var member = GetMember(type, new[]
                {
                    "IsSprinting", "isSprinting", "Sprinting", "sprinting",
                    "IsRunning", "isRunning", "Running", "running",
                    "Sprint", "sprint", "Run", "run"
                });

                if (member is FieldInfo field && field.FieldType == typeof(bool))
                    _sprintTargets.Add(new SprintTarget(comp, member));
                else if (member is PropertyInfo prop && prop.PropertyType == typeof(bool))
                    _sprintTargets.Add(new SprintTarget(comp, member));
            }

            if (_debugLogging.Value && !_sprintPoseLogged)
            {
                _sprintPoseLogged = true;
                LoggerInstance.Msg($"[TriggerLocomotion] sprintPose targets={_sprintTargets.Count}");
            }
        }
        private bool GetButtonPressed(Button button)
        {
            bool left = button == Button.X || button == Button.Y;
            if (TryGetButtonPressedFromPoller(left, button, out bool pressed, out float rawValue, out string source))
            {
                _lastButtonSource = source ?? _lastButtonSource;
                return pressed;
            }

            if (TryGetButtonPressedFromManager(left, button, out pressed, out rawValue, out source))
            {
                _lastButtonSource = source ?? _lastButtonSource;
                return pressed;
            }

            LogOnce(ref _buttonNoneLogged, "[TriggerLocomotion] button source not found (src=none)");
            return false;
        }

        private void SetButtonDebug(bool x, bool y, bool a, bool b, string source)
        {
            _lastButtonX = x;
            _lastButtonY = y;
            _lastButtonA = a;
            _lastButtonB = b;
            if (!string.IsNullOrEmpty(source))
                _lastButtonSource = source;
        }

        private bool TryGetButtonPressedFromPoller(bool left, Button button, out bool pressed, out float rawValue, out string source)
        {
            pressed = false;
            rawValue = 0f;
            source = "poller";

            try
            {
                var pollerType = FindType(
                                     "RumbleModdingAPI.Calls+Utilities+ControllerInputPoller",
                                     "RumbleModdingAPI.Calls.Utilities.ControllerInputPoller")
                                 ?? FindTypeByName("ControllerInputPoller");
                if (pollerType == null)
                {
                    LogOnce(ref _pollerTypeMissingLogged, "[TriggerLocomotion] ControllerInputPoller type not found");
                    return false;
                }

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var controllerProp = pollerType.GetProperty(left ? "LeftController" : "RightController", flags);
                object controller = controllerProp?.GetValue(null, null);
                if (controller == null)
                {
                    var controllerField = pollerType.GetField(left ? "LeftController" : "RightController", flags);
                    controller = controllerField?.GetValue(null);
                }
                if (controller == null)
                {
                    LogOnce(ref _pollerControllerNullLogged, "[TriggerLocomotion] ControllerInputPoller controller is null");
                    return false;
                }

                if (TryReadButtonFromObject(controller, GetButtonNames(button, left), out pressed, out rawValue, out string memberName))
                {
                    source = "poller:" + memberName;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetButtonPressedFromManager(bool left, Button button, out bool pressed, out float rawValue, out string source)
        {
            pressed = false;
            rawValue = 0f;
            source = "manager";

            try
            {
                var managerType = FindType(
                                      "RumbleModdingAPI.Calls+Utilities+ControllerInputManager",
                                      "RumbleModdingAPI.Calls.Utilities.ControllerInputManager")
                                  ?? FindTypeByName("ControllerInputManager");
                if (managerType == null)
                {
                    LogOnce(ref _managerTypeMissingLogged, "[TriggerLocomotion] ControllerInputManager type not found");
                    return false;
                }

                object manager = null;
                string instSrc = "none";
                manager = TryGetSingletonLikeInstance(managerType, out instSrc);

                if (manager == null)
                {
                    if (!_managerUnitySearched)
                    {
                        _managerUnityInstance = FindUnityObjectInstance(managerType);
                        _managerUnitySearched = true;
                    }
                    manager = _managerUnityInstance;
                }

                if (manager == null)
                    LogOnce(ref _managerInstanceNullLogged, "[TriggerLocomotion] ControllerInputManager instance is null");

                if (manager != null)
                {
                    object controller = GetControllerFromManager(managerType, manager, left);
                    if (controller == null)
                        LogOnce(ref _managerControllerNullLogged, "[TriggerLocomotion] ControllerInputManager controller is null");
                    if (controller != null && TryReadButtonFromObject(controller, GetButtonNames(button, left), out pressed, out rawValue, out string memberName))
                    {
                        source = "manager.controller:" + memberName;
                        return true;
                    }

                    if (TryReadButtonFromObject(manager, GetButtonNames(button, left), out pressed, out rawValue, out string memberName2))
                    {
                        source = "manager:" + memberName2;
                        return true;
                    }
                }

                if (TryReadButtonFromType(managerType, GetButtonNames(button, left), out pressed, out rawValue, out string memberName3))
                {
                    source = "manager.static:" + memberName3;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private object GetControllerFromManager(Type managerType, object managerInstance, bool left)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            try
            {
                var prop = managerType.GetProperty(left ? "LeftController" : "RightController", flags);
                if (prop != null)
                    return prop.GetValue(managerInstance, null);

                var field = managerType.GetField(left ? "LeftController" : "RightController", flags);
                if (field != null)
                    return field.GetValue(managerInstance);
            }
            catch
            {
                // Best-effort only.
            }

            return null;
        }
        private bool TryReadButtonFromObject(object instance, string[] names, out bool pressed, out float rawValue, out string memberName)
        {
            pressed = false;
            rawValue = 0f;
            memberName = "";

            if (instance == null || names == null)
                return false;

            foreach (var name in names)
            {
                var member = GetMember(instance.GetType(), new[] { name });
                if (member == null)
                    continue;

                var value = GetMemberValue(instance, member);
                if (TryGetPressedFromValue(value, out pressed, out rawValue))
                {
                    memberName = name;
                    return true;
                }
            }

            return false;
        }

        private bool TryReadButtonFromType(Type type, string[] names, out bool pressed, out float rawValue, out string memberName)
        {
            pressed = false;
            rawValue = 0f;
            memberName = "";

            if (type == null || names == null)
                return false;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var name in names)
            {
                var field = type.GetField(name, flags);
                if (field != null && TryGetPressedFromValue(field.GetValue(null), out pressed, out rawValue))
                {
                    memberName = name;
                    return true;
                }

                var prop = type.GetProperty(name, flags);
                if (prop != null && prop.CanRead && TryGetPressedFromValue(prop.GetValue(null, null), out pressed, out rawValue))
                {
                    memberName = name;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPressedFromValue(object value, out bool pressed, out float rawValue)
        {
            pressed = false;
            rawValue = 0f;

            if (value == null)
                return false;

            float threshold = GetButtonThreshold();

            if (value is bool boolValue)
            {
                pressed = boolValue;
                rawValue = boolValue ? 1f : 0f;
                return true;
            }

            if (value is float floatValue)
            {
                rawValue = floatValue;
                pressed = floatValue >= threshold;
                return true;
            }

            if (value is double doubleValue)
            {
                rawValue = (float)doubleValue;
                pressed = rawValue >= threshold;
                return true;
            }

            if (value is int intValue)
            {
                rawValue = intValue;
                pressed = rawValue >= threshold;
                return true;
            }

            if (value is long longValue)
            {
                rawValue = longValue;
                pressed = rawValue >= threshold;
                return true;
            }

            if (value is byte byteValue)
            {
                rawValue = byteValue;
                pressed = rawValue >= threshold;
                return true;
            }

            if (value is short shortValue)
            {
                rawValue = shortValue;
                pressed = rawValue >= threshold;
                return true;
            }

            if (TryUnwrapIl2CppPrimitive(value, out bool il2cppBool, out float il2cppFloat))
            {
                string full = value.GetType().FullName ?? "";
                if (full == "Il2CppSystem.Boolean")
                {
                    pressed = il2cppBool;
                    rawValue = il2cppBool ? 1f : 0f;
                    return true;
                }

                rawValue = il2cppFloat;
                pressed = il2cppFloat >= threshold;
                return true;
            }

            float nestedValue = GetFloatMember(value, new[] { "value", "Value", "axis", "Axis", "amount", "Amount", "strength", "Strength" });
            bool nestedPressed = GetBoolMember(value, new[] { "pressed", "Pressed", "isPressed", "IsPressed", "down", "Down" });
            if (nestedPressed || nestedValue > 0f)
            {
                rawValue = nestedValue;
                pressed = nestedPressed || rawValue >= threshold;
                return true;
            }

            return false;
        }

        private float GetButtonThreshold()
        {
            if (_buttonThreshold == null)
                return 0.5f;

            return Mathf.Clamp(_buttonThreshold.Value, 0.01f, 0.99f);
        }

        private static bool TryUnwrapIl2CppPrimitive(object boxed, out bool b, out float f)
        {
            b = false;
            f = 0f;
            if (boxed == null)
                return false;

            var t = boxed.GetType();
            var full = t.FullName ?? "";

            if (full == "Il2CppSystem.Single")
            {
                var field = t.GetField("m_value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(float))
                {
                    f = (float)field.GetValue(boxed);
                    return true;
                }
            }

            if (full == "Il2CppSystem.Boolean")
            {
                var field = t.GetField("m_value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(bool))
                {
                    b = (bool)field.GetValue(boxed);
                    return true;
                }
            }

            return false;
        }

        private static string[] GetButtonNames(Button button, bool left)
        {
            switch (button)
            {
                case Button.X:
                    return left ? new[] { "xButton", "XButton", "buttonX", "ButtonX", "primaryButton", "PrimaryButton", "buttonOne", "ButtonOne", "primary", "Primary", "x", "X" } : Array.Empty<string>();
                case Button.Y:
                    return left ? new[] { "yButton", "YButton", "buttonY", "ButtonY", "secondaryButton", "SecondaryButton", "buttonTwo", "ButtonTwo", "secondary", "Secondary", "y", "Y" } : Array.Empty<string>();
                case Button.A:
                    return left ? Array.Empty<string>() : new[] { "aButton", "AButton", "buttonA", "ButtonA", "primaryButton", "PrimaryButton", "buttonOne", "ButtonOne", "primary", "Primary", "a", "A" };
                case Button.B:
                    return left ? Array.Empty<string>() : new[] { "bButton", "BButton", "buttonB", "ButtonB", "secondaryButton", "SecondaryButton", "buttonTwo", "ButtonTwo", "secondary", "Secondary", "b", "B" };
                default:
                    return Array.Empty<string>();
            }
        }
        private void LogOnce(ref bool flag, string message)
        {
            if (!_debugLogging.Value || flag)
                return;

            flag = true;
            LoggerInstance.Msg(message);
        }

        private bool IsLocomotionCandidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string n = name.ToLowerInvariant();
            return n.Contains("origin") || n.Contains("offset") || n.Contains("locomotion") ||
                   n.Contains("player") || n.Contains("rig") || n.Contains("controller") ||
                   n.Contains("vr") || n.Contains("root") || n.Contains("headset");
        }

        private void LogLocomotionCandidates(Transform root)
        {
            if (!_debugLogging.Value || root == null)
                return;

            if (Time.realtimeSinceStartup < _nextCandidateLogTime)
                return;

            _nextCandidateLogTime = Time.realtimeSinceStartup + DefaultDebugLogInterval;

            int logged = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                if (!IsLocomotionCandidateName(t.name))
                    continue;

                int id = t.GetInstanceID();
                if (_candidateLastLocal.TryGetValue(id, out Vector3 prev))
                {
                    Vector3 delta = t.localPosition - prev;
                    float mag = delta.magnitude;
                    if (mag > 0.0005f)
                    {
                        LoggerInstance.Msg($"[TriggerLocomotion] candidateMove name={t.name} path={GetTransformPath(t)} localDelta={mag:F4}");
                        logged++;
                        if (logged >= 5)
                            break;
                    }
                }

                _candidateLastLocal[id] = t.localPosition;
            }
        }

        private static string GetTransformPath(Transform t)
        {
            if (t == null)
                return "<null>";

            var stack = new Stack<string>();
            Transform current = t;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack);
        }

        private static bool TryGetSpeedValue(object instance, Type type, IEnumerable<string> names, out float value)
        {
            foreach (var name in names)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(float))
                {
                    value = (float)field.GetValue(instance);
                    return true;
                }

                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(float) && prop.CanRead)
                {
                    value = (float)prop.GetValue(instance, null);
                    return true;
                }
            }

            value = 0f;
            return false;
        }

        private static object FindUnityObjectInstance(Type type)
        {
            try
            {
                var objs = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                if (objs == null)
                    return null;

                foreach (var obj in objs)
                {
                    if (obj == null)
                        continue;
                    var objType = obj.GetType();
                    if (objType == type || (type != null && type.IsAssignableFrom(objType)))
                        return obj;
                }
            }
            catch
            {
                // Best-effort only.
            }

            return null;
        }

        private static Type FindType(params string[] fullNames)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var fullName in fullNames)
                {
                    var type = asm.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
            }

            return null;
        }

        private static Type FindTypeByName(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null)
                    continue;

                foreach (var type in types)
                {
                    if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                        return type;
                }
            }

            return null;
        }

        private static Transform TryGetTransform(object instance)
        {
            if (instance == null)
                return null;

            if (instance is Component comp)
                return comp.transform;

            try
            {
                var type = instance.GetType();
                var prop = type.GetProperty("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                           ?? type.GetProperty("Transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && typeof(Transform).IsAssignableFrom(prop.PropertyType))
                    return (Transform)prop.GetValue(instance, null);

                var field = type.GetField("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? type.GetField("Transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && typeof(Transform).IsAssignableFrom(field.FieldType))
                    return (Transform)field.GetValue(instance);
            }
            catch
            {
                // Best-effort only.
            }

            return null;
        }
        private static MemberInfo GetMember(Type type, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;

                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                    return prop;
            }

            return null;
        }

        private static object GetMemberValue(object instance, MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.GetValue(instance);
            if (member is PropertyInfo prop)
                return prop.GetValue(instance, null);
            return null;
        }

        private static float GetFloatMember(object instance, IEnumerable<string> names)
        {
            var member = GetMember(instance.GetType(), names);
            if (member == null)
                return 0f;

            var value = GetMemberValue(instance, member);
            if (TryConvertToFloat(value, out float f))
                return f;

            return 0f;
        }

        private static bool GetBoolMember(object instance, IEnumerable<string> names)
        {
            var member = GetMember(instance.GetType(), names);
            if (member == null)
                return false;

            var value = GetMemberValue(instance, member);
            if (TryConvertToBool(value, out bool b))
                return b;

            return false;
        }

        private static bool TryConvertToFloat(object value, out float f)
        {
            f = 0f;
            if (value == null)
                return false;

            if (value is float ff) { f = ff; return true; }
            if (value is double dd) { f = (float)dd; return true; }
            if (value is int ii) { f = ii; return true; }
            if (value is long ll) { f = ll; return true; }
            if (value is byte bb) { f = bb; return true; }
            if (value is short ss) { f = ss; return true; }

            if (value is IConvertible conv)
            {
                try { f = conv.ToSingle(System.Globalization.CultureInfo.InvariantCulture); return true; }
                catch { }
            }

            var t = value.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "m_value", "value", "Value", "m_Value" })
            {
                try
                {
                    var fi = t.GetField(name, flags);
                    if (fi != null)
                    {
                        var inner = fi.GetValue(value);
                        if (TryConvertToFloat(inner, out f))
                            return true;
                    }

                    var pi = t.GetProperty(name, flags);
                    if (pi != null && pi.CanRead)
                    {
                        var inner = pi.GetValue(value, null);
                        if (TryConvertToFloat(inner, out f))
                            return true;
                    }
                }
                catch
                {
                    // Best-effort only.
                }
            }

            return false;
        }

        private static bool TryConvertToBool(object value, out bool b)
        {
            b = false;
            if (value == null)
                return false;

            if (value is bool bb)
            {
                b = bb;
                return true;
            }

            if (TryConvertToFloat(value, out float f))
            {
                b = f >= 0.5f;
                return true;
            }

            var t = value.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "m_value", "value", "Value", "m_Value" })
            {
                try
                {
                    var fi = t.GetField(name, flags);
                    if (fi != null)
                    {
                        var inner = fi.GetValue(value);
                        if (TryConvertToBool(inner, out b))
                            return true;
                    }

                    var pi = t.GetProperty(name, flags);
                    if (pi != null && pi.CanRead)
                    {
                        var inner = pi.GetValue(value, null);
                        if (TryConvertToBool(inner, out b))
                            return true;
                    }
                }
                catch
                {
                    // Best-effort only.
                }
            }

            return false;
        }

        private object TryGetSingletonLikeInstance(Type type, out string source)
        {
            source = "none";
            if (type == null)
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            foreach (var name in new[] { "Instance", "Current", "Singleton" })
            {
                try
                {
                    var p = type.GetProperty(name, flags);
                    if (p != null && p.CanRead)
                    {
                        var v = p.GetValue(null, null);
                        source = $"prop:{name}";
                        if (v != null)
                            return v;
                    }
                }
                catch
                {
                    // Best-effort only.
                }
            }

            foreach (var name in new[] { "Instance", "Current", "Singleton", "_instance", "s_instance" })
            {
                try
                {
                    var f = type.GetField(name, flags);
                    if (f != null)
                    {
                        var v = f.GetValue(null);
                        source = $"field:{name}";
                        if (v != null)
                            return v;
                    }
                }
                catch
                {
                    // Best-effort only.
                }
            }

            return null;
        }

        private enum Button
        {
            X,
            Y,
            A,
            B
        }

        private readonly struct SprintTarget
        {
            public readonly object Instance;
            public readonly MemberInfo Member;

            public SprintTarget(object instance, MemberInfo member)
            {
                Instance = instance;
                Member = member;
            }
        }
    }
}
