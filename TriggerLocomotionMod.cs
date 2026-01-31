
using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using RumbleModdingAPI;
using UnityEngine;

[assembly: MelonInfo(typeof(TriggerLocomotion.TriggerLocomotionMod), "Stone Stick By Nano", "2.1.1", "Nano")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace TriggerLocomotion
{
    public sealed class TriggerLocomotionMod : MelonMod
    {
        private const string PrefCategory = "TriggerLocomotion";
        private const float DefaultButtonThreshold = 0.5f;
        private const int DebugLogMaxCount = 2;

        private MelonPreferences_Entry<float> _buttonThreshold;
        private MelonPreferences_Entry<bool> _overrideJoystickMovement;
        private MelonPreferences_Entry<bool> _debugLogging;

        private Transform _rigRoot;
        private Transform _locomotionRoot;
        private Transform _vrRoot;
        private Camera _mainCamera;
        private bool _rigReady;
        private float _startupDelayUntil;

        private bool _lastButtonX;
        private bool _lastButtonY;
        private bool _lastButtonA;
        private bool _lastButtonB;
        private string _lastButtonSource = "none";

        private bool _pollerTypeMissingLogged;
        private bool _pollerControllerNullLogged;
        private bool _managerTypeMissingLogged;
        private bool _managerInstanceNullLogged;
        private bool _managerControllerNullLogged;
        private bool _buttonNoneLogged;
        private bool _overrideOffLogged;
        private bool _controllerMapMissingLogged;

        private object _managerUnityInstance;
        private bool _managerUnitySearched;
        private int _debugLogRemaining;
        private object _playerMovement;
        private MethodInfo _playerMoveMethod;
        private bool _playerMovementMissingLogged;
        private Vector2 _lastMoveInput;
        private bool _inputTargetsResolved;
        private bool _movementResolvedLogged;
        private readonly List<InputTarget> _inputTargets = new List<InputTarget>();

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory(PrefCategory, "Trigger Locomotion");
            _buttonThreshold = cat.CreateEntry("GripThreshold", DefaultButtonThreshold, "Button Threshold", "Threshold for button values if they are floats.");
            _overrideJoystickMovement = cat.CreateEntry("OverrideJoystickMovement", true, "Override Joystick Movement", "Cancels horizontal movement when buttons are not held.");
            _debugLogging = cat.CreateEntry("DebugLogging", true, "Debug Logging", "Logs locomotion target + cancel info.");

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
            _mainCamera = null;
            _startupDelayUntil = Time.realtimeSinceStartup + 5f;


            _lastButtonX = false;
            _lastButtonY = false;
            _lastButtonA = false;
            _lastButtonB = false;
            _lastButtonSource = "none";

            _pollerTypeMissingLogged = false;
            _pollerControllerNullLogged = false;
            _managerTypeMissingLogged = false;
            _managerInstanceNullLogged = false;
            _managerControllerNullLogged = false;
            _buttonNoneLogged = false;
            _overrideOffLogged = false;
            _controllerMapMissingLogged = false;


            _managerUnityInstance = null;
            _managerUnitySearched = false;

            _debugLogRemaining = DebugLogMaxCount;
            _playerMovement = null;
            _playerMoveMethod = null;
            _playerMovementMissingLogged = false;
            _lastMoveInput = Vector2.zero;
            _inputTargetsResolved = false;
            _movementResolvedLogged = false;
            _inputTargets.Clear();
        }
        public override void OnUpdate()
        {
            if (Time.realtimeSinceStartup < _startupDelayUntil)
                return;

            try
            {
                if (!IsApiReady())
                {
                    _lastMoveInput = Vector2.zero;
                    ApplyMoveInput(Vector2.zero);
                    return;
                }

                EnsureRig();
                if (!_rigReady)
                {
                    _lastMoveInput = Vector2.zero;
                    ApplyMoveInput(Vector2.zero);
                    return;
                }

                _lastButtonSource = "none";
                bool x = GetButtonPressed(Button.X);
                bool y = GetButtonPressed(Button.Y);
                bool a = GetButtonPressed(Button.A);
                bool b = GetButtonPressed(Button.B);
                SetButtonDebug(x, y, a, b, _lastButtonSource);

                bool anyButton = x || y || a || b;
                if (!anyButton)
                {
                    _lastMoveInput = Vector2.zero;
                    ApplyMoveInput(Vector2.zero);
                    return;
                }

                bool forward = x && !a;
                bool back = a && !x;
                bool right = b && !y;
                bool left = y && !b;

                float forwardInput = forward ? 1f : (back ? -1f : 0f);
                float rightInput = right ? 1f : (left ? -1f : 0f);

                Vector2 moveInput = new Vector2(rightInput, forwardInput);
                if (moveInput.sqrMagnitude <= 0.0001f)
                {
                    _lastMoveInput = Vector2.zero;
                    ApplyMoveInput(Vector2.zero);
                    return;
                }

                if (moveInput.sqrMagnitude > 1f)
                    moveInput.Normalize();

                _lastMoveInput = moveInput;
                ApplyMoveInput(moveInput);
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

            if (!_rigReady)
                return;

            if (_overrideJoystickMovement.Value)
            {
                ApplyMoveInput(_lastMoveInput);
                TryClearJoystickInput();
            }
        }

        public override void OnLateUpdate()
        {
            if (Time.realtimeSinceStartup < _startupDelayUntil)
                return;

            if (!IsApiReady())
                return;

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
            _locomotionRoot = FindLocomotionRoot(_rigRoot);

            TryResolveWalkSpeed();

            if (_debugLogging.Value && !_speedLogged)
            {
                _speedLogged = true;
                LoggerInstance.Msg($"[TriggerLocomotion] resolved speed walk={_autoWalkSpeed:F2} run={_autoRunSpeed:F2}");
            }

            if (_debugLogging.Value)
                LoggerInstance.Msg($"[TriggerLocomotion] Locomotion root: {_locomotionRoot?.name} path={GetTransformPath(_locomotionRoot)}");

            _rigReady = true;

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

        private bool EnsurePlayerMovement()
        {
            if (_playerMovement != null && _playerMoveMethod != null)
                return true;

            EnsureInputTargets();
            if (_playerMovement != null && _playerMoveMethod != null)
                return true;

            if (_debugLogging.Value && !_playerMovementMissingLogged)
            {
                _playerMovementMissingLogged = true;
                LoggerInstance.Msg("[TriggerLocomotion] PlayerMovement not found");
            }

            return false;
        }

        private void EnsureInputTargets()
        {
            if (_inputTargetsResolved)
                return;

            _inputTargetsResolved = true;
            _inputTargets.Clear();

            var roots = new List<Transform>(2);
            if (_rigRoot != null)
                roots.Add(_rigRoot);

            try
            {
                var controller = Calls.Players.GetPlayerController();
                var controllerRoot = TryGetTransform(controller);
                if (controllerRoot != null && controllerRoot != _rigRoot)
                    roots.Add(controllerRoot);
            }
            catch
            {
                // Best-effort only.
            }

            MethodInfo preferredMethod = null;
            object preferredInstance = null;
            MethodInfo anyMethod = null;
            object anyInstance = null;

            for (int r = 0; r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null)
                    continue;

                var comps = root.GetComponentsInChildren<MonoBehaviour>(true);
                if (comps == null)
                    continue;

                foreach (var comp in comps)
                {
                    if (comp == null)
                        continue;

                    var type = comp.GetType();
                    bool preferred = false;
                    var name = type.Name ?? "";
                    if (name.IndexOf("PlayerMovement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Locomotion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Movement", StringComparison.OrdinalIgnoreCase) >= 0)
                        preferred = true;

                    var moveMethod = FindMoveMethod(type);
                    if (moveMethod != null)
                    {
                        if (preferred && preferredMethod == null)
                        {
                            preferredMethod = moveMethod;
                            preferredInstance = comp;
                        }
                        if (anyMethod == null)
                        {
                            anyMethod = moveMethod;
                            anyInstance = comp;
                        }
                    }

                    AddInputMembers(comp, type, _inputTargets);
                }
            }

            if (preferredMethod != null)
            {
                _playerMovement = preferredInstance;
                _playerMoveMethod = preferredMethod;
            }
            else if (anyMethod != null)
            {
                _playerMovement = anyInstance;
                _playerMoveMethod = anyMethod;
            }

            if (_playerMovement != null && _playerMoveMethod != null && _debugLogging.Value && !_movementResolvedLogged)
            {
                _movementResolvedLogged = true;
                var t = _playerMovement.GetType();
                var p = _playerMoveMethod.GetParameters();
                var pName = p.Length == 1 ? p[0].ParameterType.Name : "unknown";
                LoggerInstance.Msg($"[TriggerLocomotion] movement method: {t.FullName}.{_playerMoveMethod.Name}({pName})");
            }
        }

        private static MethodInfo FindMoveMethod(Type type)
        {
            if (type == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in type.GetMethods(flags))
            {
                if (!string.Equals(m.Name, "Move", StringComparison.Ordinal))
                    continue;
                var ps = m.GetParameters();
                if (ps.Length != 1)
                    continue;
                var pt = ps[0].ParameterType;
                if (pt == typeof(Vector2))
                    return m;
                if (string.Equals(pt.Name, "Vector2", StringComparison.Ordinal) ||
                    string.Equals(pt.FullName, "UnityEngine.Vector2", StringComparison.Ordinal))
                    return m;
            }

            return null;
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
        private void ApplyMoveInput(Vector2 input)
        {
            if (input.sqrMagnitude > 1f)
                input.Normalize();

            if (!EnsurePlayerMovement())
                return;

            try
            {
                _playerMoveMethod?.Invoke(_playerMovement, new object[] { input });
            }
            catch
            {
                // Best-effort only.
            }
        }

        private void TryClearJoystickInput()
        {
            EnsureInputTargets();

            if (_inputTargets.Count == 0)
                return;

            for (int i = 0; i < _inputTargets.Count; i++)
            {
                var target = _inputTargets[i];
                if (target.Instance == null || target.Member == null)
                    continue;

                try
                {
                    if (target.Member is FieldInfo field)
                    {
                        if (target.ValueType == typeof(Vector2))
                            field.SetValue(target.Instance, Vector2.zero);
                        else if (target.ValueType == typeof(Vector3))
                            field.SetValue(target.Instance, Vector3.zero);
                        else if (target.ValueType == typeof(float))
                            field.SetValue(target.Instance, 0f);
                    }
                    else if (target.Member is PropertyInfo prop && prop.CanWrite)
                    {
                        if (target.ValueType == typeof(Vector2))
                            prop.SetValue(target.Instance, Vector2.zero, null);
                        else if (target.ValueType == typeof(Vector3))
                            prop.SetValue(target.Instance, Vector3.zero, null);
                        else if (target.ValueType == typeof(float))
                            prop.SetValue(target.Instance, 0f, null);
                    }
                }
                catch
                {
                    // Best-effort only.
                }
            }
        }

        private static bool LooksLikeInputName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string n = name.ToLowerInvariant();
            return n.Contains("input") || n.Contains("move") || n.Contains("movement") ||
                   n.Contains("stick") || n.Contains("joystick") || n.Contains("axis");
        }

        private static void AddInputMembers(object instance, Type type, List<InputTarget> output)
        {
            if (instance == null || type == null || output == null)
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in type.GetFields(flags))
            {
                try
                {
                    if (field == null)
                        continue;
                    if (!LooksLikeInputName(field.Name))
                        continue;
                    var ft = field.FieldType;
                    if (ft == typeof(Vector2) || ft == typeof(Vector3) || ft == typeof(float))
                        output.Add(new InputTarget(instance, field, ft));
                }
                catch
                {
                    // Best-effort only.
                }
            }

            foreach (var prop in type.GetProperties(flags))
            {
                try
                {
                    if (prop == null || !prop.CanWrite)
                        continue;
                    if (!LooksLikeInputName(prop.Name))
                        continue;
                    var pt = prop.PropertyType;
                    if (pt == typeof(Vector2) || pt == typeof(Vector3) || pt == typeof(float))
                        output.Add(new InputTarget(instance, prop, pt));
                }
                catch
                {
                    // Best-effort only.
                }
            }
        }

        private float GetWalkSpeed()
        {
            return _autoWalkSpeed > 0f ? _autoWalkSpeed : _walkSpeed.Value;
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

                    if (_autoWalkSpeed <= 0f &&
                        TryGetSpeedValue(comp, type, new[] { "walkSpeed", "WalkSpeed", "moveSpeed", "MoveSpeed", "movementSpeed", "MovementSpeed" }, out float speed))
                    {
                        if (speed > 0.1f && speed < 6f)
                        {
                            _autoWalkSpeed = speed;
                        }
                    }

                    if (_autoRunSpeed <= 0f &&
                        TryGetSpeedValue(comp, type, new[] { "runSpeed", "RunSpeed", "sprintSpeed", "SprintSpeed" }, out float runSpeed))
                    {
                        if (runSpeed > 0.1f && runSpeed < 10f)
                            _autoRunSpeed = runSpeed;
                    }

                    if (_autoWalkSpeed > 0f && _autoRunSpeed > 0f)
                        return;
                }

                try
                {
                    var pc = Calls.Players.GetPlayerController();
                    if (pc != null)
                    {
                        var pcType = pc.GetType();
                        if (_autoWalkSpeed <= 0f &&
                            TryGetSpeedValue(pc, pcType, new[] { "walkSpeed", "WalkSpeed", "moveSpeed", "MoveSpeed", "movementSpeed", "MovementSpeed" }, out float pcWalk))
                        {
                            if (pcWalk > 0.1f && pcWalk < 6f)
                                _autoWalkSpeed = pcWalk;
                        }

                        if (_autoRunSpeed <= 0f &&
                            TryGetSpeedValue(pc, pcType, new[] { "runSpeed", "RunSpeed", "sprintSpeed", "SprintSpeed" }, out float pcRun))
                        {
                            if (pcRun > 0.1f && pcRun < 10f)
                                _autoRunSpeed = pcRun;
                        }
                    }
                }
                catch
                {
                    // Best-effort only.
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
            if (_sprintTargets.Count == 0 && _animSprintTargets.Count == 0 && _sprintMethodTargets.Count == 0)
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

            for (int i = 0; i < _animSprintTargets.Count; i++)
            {
                try
                {
                    var t = _animSprintTargets[i];
                    if (t.Animator == null || string.IsNullOrEmpty(t.ParamName))
                        continue;

                    var type = t.Animator.GetType();
                    var m = type.GetMethod("SetBool", new[] { typeof(string), typeof(bool) });
                    if (m == null)
                        continue;

                    m.Invoke(t.Animator, new object[] { t.ParamName, sprinting });
                }
                catch
                {
                    // Best-effort only.
                }
            }

            for (int i = 0; i < _sprintMethodTargets.Count; i++)
            {
                try
                {
                    var t = _sprintMethodTargets[i];
                    t.Method?.Invoke(t.Instance, new object[] { sprinting });
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
            _animSprintTargets.Clear();
            _sprintMethodTargets.Clear();

            var components = _rigRoot.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var comp in components)
            {
                if (comp == null)
                    continue;

                var type = comp.GetType();
                string typeName = (type.Name ?? "").ToLowerInvariant();
                if (!typeName.Contains("locomotion") && !typeName.Contains("movement") && !typeName.Contains("controller") && !typeName.Contains("player"))
                    continue;

                AddBoolMembersByContains(comp, type, _sprintTargets);
                AddBoolSetterMethodsByContains(comp, type, _sprintMethodTargets);
            }

            try
            {
                var pc = Calls.Players.GetPlayerController();
                if (pc != null)
                {
                    var pcType = pc.GetType();
                    AddBoolMembersByContains(pc, pcType, _sprintTargets);
                    AddBoolSetterMethodsByContains(pc, pcType, _sprintMethodTargets);
                }
            }
            catch
            {
                // Best-effort only.
            }

            try
            {
                var comps = _rigRoot.GetComponentsInChildren<Component>(true);
                foreach (var comp in comps)
                {
                    if (comp == null)
                        continue;

                    var type = comp.GetType();
                    if (!string.Equals(type.Name, "Animator", StringComparison.Ordinal))
                        continue;

                    var parmsProp = type.GetProperty("parameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var parms = parmsProp?.GetValue(comp, null) as Array;
                    if (parms == null)
                        continue;

                    foreach (var p in parms)
                    {
                        if (p == null)
                            continue;

                        var pType = p.GetType();
                        var nameProp = pType.GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var typeProp = pType.GetProperty("type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        string name = nameProp?.GetValue(p, null) as string;
                        object typeVal = typeProp?.GetValue(p, null);

                        if (string.IsNullOrEmpty(name))
                            continue;

                        if (!LooksLikeSprintOrRun(name))
                            continue;

                        if (typeVal != null)
                        {
                            string typeName = typeVal.ToString();
                            if (!string.Equals(typeName, "Bool", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        _animSprintTargets.Add(new AnimatorBoolTarget(comp, name));
                    }
                }
            }
            catch
            {
                // Best-effort only.
            }

            if (_debugLogging.Value && !_sprintPoseLogged)
            {
                _sprintPoseLogged = true;
                LoggerInstance.Msg($"[TriggerLocomotion] sprintPose targets fields={_sprintTargets.Count} animBools={_animSprintTargets.Count} methods={_sprintMethodTargets.Count}");
            }
        }
        private bool GetButtonPressed(Button button)
        {
            if (TryGetButtonPressedFromControllerMap(button, out bool pressed, out float rawValue, out string source))
            {
                _lastButtonSource = source ?? _lastButtonSource;
                return pressed;
            }

            bool left = button == Button.X || button == Button.Y;
            if (TryGetButtonPressedFromPoller(left, button, out pressed, out rawValue, out source))
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

        private bool TryGetButtonPressedFromControllerMap(Button button, out bool pressed, out float rawValue, out string source)
        {
            pressed = false;
            rawValue = 0f;
            source = "controllerMap";

            try
            {
                var callsType = typeof(Calls);
                var cmProp = callsType.GetProperty("ControllerMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var cm = cmProp?.GetValue(null);
                if (cm == null)
                {
                    if (_debugLogging.Value && !_controllerMapMissingLogged)
                    {
                        _controllerMapMissingLogged = true;
                        LoggerInstance.Msg("[TriggerLocomotion] Calls.ControllerMap not found/ready");
                    }
                    return false;
                }

                bool left = button == Button.X || button == Button.Y;
                string controllerPropName = left ? "LeftController" : "RightController";
                var controllerProp = cm.GetType().GetProperty(controllerPropName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var controller = controllerProp?.GetValue(cm);
                if (controller == null)
                    return false;

                string inputName =
                    (button == Button.X || button == Button.A) ? "Primary" :
                    (button == Button.Y || button == Button.B) ? "Secondary" : null;

                if (inputName == null)
                    return false;

                MethodInfo getMethod = null;
                var methods = controller.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!string.Equals(m.Name, "Get", StringComparison.Ordinal))
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1)
                        continue;
                    getMethod = m;
                    break;
                }

                if (getMethod == null)
                    return false;

                var paramType = getMethod.GetParameters()[0].ParameterType;
                if (!paramType.IsEnum)
                    return false;

                object enumVal = Enum.Parse(paramType, inputName, true);
                object val = getMethod.Invoke(controller, new object[] { enumVal });

                if (TryGetPressedFromValue(val, out pressed, out rawValue))
                {
                    source = $"controllerMap:{controllerPropName}.{inputName}";
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
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

        private static bool LooksLikeSprintOrRun(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string n = name.ToLowerInvariant();
            return n.Contains("sprint") || n.Contains("run");
        }

        private static void AddBoolMembersByContains(object instance, Type type, List<SprintTarget> outTargets)
        {
            if (instance == null || type == null)
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in type.GetFields(flags))
            {
                try
                {
                    if (f.FieldType != typeof(bool))
                        continue;
                    if (!LooksLikeSprintOrRun(f.Name))
                        continue;
                    outTargets.Add(new SprintTarget(instance, f));
                }
                catch
                {
                    // Best-effort only.
                }
            }

            foreach (var p in type.GetProperties(flags))
            {
                try
                {
                    if (p.PropertyType != typeof(bool))
                        continue;
                    if (!p.CanWrite)
                        continue;
                    if (!LooksLikeSprintOrRun(p.Name))
                        continue;
                    outTargets.Add(new SprintTarget(instance, p));
                }
                catch
                {
                    // Best-effort only.
                }
            }
        }

        private static void AddBoolSetterMethodsByContains(object instance, Type type, List<MethodTarget> outTargets)
        {
            if (instance == null || type == null)
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in type.GetMethods(flags))
            {
                try
                {
                    if (!LooksLikeSprintOrRun(m.Name))
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1)
                        continue;
                    if (ps[0].ParameterType != typeof(bool))
                        continue;
                    outTargets.Add(new MethodTarget(instance, m));
                }
                catch
                {
                    // Best-effort only.
                }
            }
        }
        private void LogOnce(ref bool flag, string message)
        {
            if (!_debugLogging.Value || flag)
                return;

            flag = true;
            LoggerInstance.Msg(message);
        }

        private bool TryConsumeDebugLog()
        {
            if (!_debugLogging.Value)
                return false;
            if (_debugLogRemaining <= 0)
                return false;
            _debugLogRemaining--;
            return true;
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

        private readonly struct AnimatorBoolTarget
        {
            public readonly object Animator;
            public readonly string ParamName;

            public AnimatorBoolTarget(object animator, string paramName)
            {
                Animator = animator;
                ParamName = paramName;
            }
        }

        private readonly struct MethodTarget
        {
            public readonly object Instance;
            public readonly MethodInfo Method;

            public MethodTarget(object instance, MethodInfo method)
            {
                Instance = instance;
                Method = method;
            }
        }

        private readonly struct InputTarget
        {
            public readonly object Instance;
            public readonly MemberInfo Member;
            public readonly Type ValueType;

            public InputTarget(object instance, MemberInfo member, Type valueType)
            {
                Instance = instance;
                Member = member;
                ValueType = valueType;
            }
        }
    }
}
