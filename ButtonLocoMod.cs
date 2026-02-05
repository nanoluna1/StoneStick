using System;
using HarmonyLib;
using MelonLoader;
using Il2CppRUMBLE.Players.Subsystems;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.InputSystem;
using UnityEngine;

[assembly: MelonInfo(typeof(ButtonLoco.ButtonLocoMod), "Stone Stick By Nano", "3.0.2", "Nano")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace ButtonLoco
{
    public sealed class ButtonLocoMod : MelonMod
    {
        internal static ButtonLocoMod Instance;

        private const string HarmonyId = "nano.stonestick.buttonloco";
        private static bool _patched;
        private static bool _inputFaulted;
        private static float _lastInputFaultLogTime;
        private static float _lastDebugLogTime;
        private static bool _movementMethodsLogged;
        private static MethodInfo _sprintPoseMethod;
        private static bool _sprintPosePatched;
        private static bool _voiceMethodsLogged;
        private static float _lastStateLogTime;
        private static object _lastActiveMovementType;
        private static Camera _cachedCamera;
        private static bool _pttInputReady;
        private static InputAction _pttAction;
        private static bool _pttPatched;
        private static float _lastPttLogTime;
        private static float _lastVoiceFindTime;
        private static PlayerVoiceSystem _voiceSystem;
        private static bool _pttTargetsCached;
        private static FieldInfo[] _pttBoolFields;
        private static PropertyInfo[] _pttBoolProps;
        private static float _lastSprintScaleLogTime;

        private const bool AllowAForPtt = false;
        private const bool EnableSprintMultiplier = true;
        private const float SprintMultiplier = 1f;
        private const bool ForceInstantSprint = false;

        private static bool _inputSystemReady;
        private static InputAction _moveAction;

        public override void OnInitializeMelon()
        {
            Instance = this;
            TryPatch();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!_patched)
            {
                TryPatch();
            }
        }

        public override void OnLateInitializeMelon()
        {
            TrySetupInputSystem();
            TrySetupPttInput();
            LogMovementMethodsOnce();
            TryPatchSprintPose();
            LogVoiceMethodsOnce();
            TryPatchPtt();
        }

        public override void OnUpdate()
        {
            TryUpdatePttOverride();
        }

        private void TryPatch()
        {
            var moveMethod = AccessTools.Method(typeof(PlayerMovement), "Move");
            if (moveMethod == null)
            {
                LoggerInstance.Error("PlayerMovement.Move not found.");
                return;
            }

            var harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.Patch(moveMethod, prefix: new HarmonyMethod(typeof(ButtonLocoMod), nameof(PlayerMovement_Move_Prefix)));
            _patched = true;
            LoggerInstance.Msg("Stone Stick: patched PlayerMovement.Move");
        }

        private void TryPatchSprintPose()
        {
            if (_sprintPosePatched)
            {
                return;
            }

            _sprintPoseMethod = AccessTools.Method(typeof(PlayerMovement), "OnSprintingPoseInputDone");
            if (_sprintPoseMethod == null)
            {
                return;
            }

            var harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.Patch(_sprintPoseMethod, postfix: new HarmonyMethod(typeof(ButtonLocoMod), nameof(PlayerMovement_OnSprintingPoseInputDone_Postfix)));
            _sprintPosePatched = true;
            LoggerInstance.Msg("Stone Stick: patched PlayerMovement.OnSprintingPoseInputDone (log only)");
        }

        private static void PlayerMovement_Move_Prefix(PlayerMovement __instance, ref Vector2 __0)
        {
            if (Instance == null)
            {
                return;
            }

            if (__instance == null)
            {
                return;
            }

            if (_inputFaulted)
            {
                __0 = Vector2.zero;
                return;
            }

            if (!Instance.ShouldOverrideMovement(__instance))
            {
                return;
            }

            try
            {
                __0 = Instance.GetButtonMovementVector(__instance);
            }
            catch (Exception ex)
            {
                // Guard against XR input exceptions when headset is active.
                _inputFaulted = true;
                if (Time.time - _lastInputFaultLogTime > 5f)
                {
                    _lastInputFaultLogTime = Time.time;
                    Instance.LoggerInstance.Error($"Stone Stick input disabled due to exception: {ex}");
                }
                __0 = Vector2.zero;
            }
        }

        private Vector2 GetButtonMovementVector(PlayerMovement movement)
        {
            var input = ReadMoveInput();
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            // IMPORTANT: RUMBLE already applies camera-relative movement internally.
            // Feeding raw input avoids double-rotating the direction.
            var baseInput = input;
            input = ApplySprintMultiplier(movement, baseInput);
            TrySetLatestSprintingHeadingVector(movement, baseInput);
            LogMovementState(movement, input, "input");
            DebugLog(input);
            return input;
        }

        private static void DebugLog(Vector2 finalInput)
        {
            if (Instance == null)
            {
                return;
            }

            if (finalInput.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (Time.time - _lastDebugLogTime < 2f)
            {
                return;
            }

            _lastDebugLogTime = Time.time;
            Instance.LoggerInstance.Msg(
                $"StoneStick debug | final={finalInput}");
        }

        private bool ShouldOverrideMovement(PlayerMovement movement)
        {
            var activeType = ReadPropertyValue(movement, "activeMovementType");
            if (!Equals(_lastActiveMovementType, activeType))
            {
                _lastActiveMovementType = activeType;
                LoggerInstance.Msg($"StoneStick state | activeMovementType={activeType ?? "n/a"}");
            }

            if (activeType == null)
            {
                return true;
            }

            var activeStr = activeType.ToString();
            if (!string.Equals(activeStr, "Normal", StringComparison.OrdinalIgnoreCase))
            {
                LogMovementState(movement, Vector2.zero, $"skip:{activeStr}");
                return false;
            }

            return true;
        }

        private static void PlayerMovement_OnSprintingPoseInputDone_Postfix(PlayerMovement __instance)
        {
            if (Instance == null || __instance == null)
            {
                return;
            }

            try
            {
                var sprintSource = ReadPropertyValue(__instance, "sprintingInputSource");
                var sprintFactor = ReadPropertyValue(__instance, "currentSprintFactor");
                var activeMoveType = ReadPropertyValue(__instance, "activeMovementType");
                var moveVelTarget = ReadPropertyValue(__instance, "movementVelocityTarget");
                var desiredVel = ReadPropertyValue(__instance, "desiredMovementVelocity");
                var maxSprintLen = ReadPropertyValue(__instance, "maxSprintVectorLength");
                var sprintAccel = ReadPropertyValue(__instance, "sprintAccelerationRate");
                Instance.LoggerInstance.Msg(
                    $"StoneStick sprint log | inputSource={sprintSource ?? "n/a"} currentSprintFactor={sprintFactor ?? "n/a"} " +
                    $"activeMovementType={activeMoveType ?? "n/a"} movementVelocityTarget={moveVelTarget ?? "n/a"} " +
                    $"desiredMovementVelocity={desiredVel ?? "n/a"} maxSprintVectorLength={maxSprintLen ?? "n/a"} " +
                    $"sprintAccelerationRate={sprintAccel ?? "n/a"}");
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick sprint log failed: {ex.Message}");
            }
        }

        private static object ReadPropertyValue(PlayerMovement movement, string propName)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = movement.GetType().GetProperty(propName, flags);
            if (prop == null)
            {
                return null;
            }

            try
            {
                return prop.GetValue(movement);
            }
            catch
            {
                return null;
            }
        }

        private Vector2 ReadMoveInput()
        {
            if (!_inputSystemReady)
            {
                TrySetupInputSystem();
            }

            if (_inputSystemReady && _moveAction != null)
            {
                try
                {
                    return _moveAction.ReadValue<Vector2>();
                }
                catch
                {
                    _inputSystemReady = false;
                }
            }

            return Vector2.zero;
        }

        private void TrySetupInputSystem()
        {
            if (_inputSystemReady)
            {
                return;
            }

            try
            {
                _moveAction = new InputAction(
                    name: "StoneStickMove",
                    type: InputActionType.Value,
                    expectedControlType: "Vector2");

                // OpenXR common usages for face buttons.
                _moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<XRController>{LeftHand}/primaryButton")
                    .With("Left", "<XRController>{LeftHand}/secondaryButton")
                    .With("Down", "<XRController>{RightHand}/primaryButton")
                    .With("Right", "<XRController>{RightHand}/secondaryButton");

                _moveAction.Enable();
                _inputSystemReady = true;
            }
            catch (Exception ex)
            {
                _inputSystemReady = false;
                LoggerInstance.Warning($"Stone Stick input system init failed: {ex.Message}");
            }
        }

        private void TrySetupPttInput()
        {
            if (_pttInputReady)
            {
                return;
            }

            try
            {
                _pttAction = new InputAction(
                    name: "StoneStickPTT",
                    type: InputActionType.Value,
                    expectedControlType: "Axis");

                _pttAction.AddBinding("<XRController>{RightHand}/trigger");
                _pttAction.AddBinding("<XRController>{RightHand}/triggerPressed");
                _pttAction.AddBinding("<XRController>{LeftHand}/trigger");
                _pttAction.AddBinding("<XRController>{LeftHand}/triggerPressed");

                if (AllowAForPtt)
                {
                    _pttAction.AddBinding("<XRController>{RightHand}/primaryButton");
                }

                _pttAction.Enable();
                _pttInputReady = true;
            }
            catch (Exception ex)
            {
                _pttInputReady = false;
                LoggerInstance.Warning($"Stone Stick PTT input init failed: {ex.Message}");
            }
        }

        private static void LogMovementMethodsOnce()
        {
            if (_movementMethodsLogged || Instance == null)
            {
                return;
            }

            _movementMethodsLogged = true;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var methods = typeof(PlayerMovement).GetMethods(flags);
                var hits = new List<string>();
                foreach (var method in methods)
                {
                    var name = method.Name;
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("run") || lower.Contains("sprint") || lower.Contains("dash")
                        || lower.Contains("jump") || lower.Contains("move") || lower.Contains("locom"))
                    {
                        hits.Add(name);
                    }
                }

                hits.Sort(StringComparer.Ordinal);
                if (hits.Count > 0)
                {
                    Instance.LoggerInstance.Msg("StoneStick: PlayerMovement methods of interest:");
                    foreach (var name in hits)
                    {
                        Instance.LoggerInstance.Msg($" - {name}");
                    }
                }
                else
                {
                    Instance.LoggerInstance.Msg("StoneStick: no obvious sprint/move methods found in PlayerMovement.");
                }
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick: method scan failed: {ex.Message}");
            }
        }

        private static void LogMovementState(PlayerMovement movement, Vector2 input, string tag)
        {
            if (Instance == null || movement == null)
            {
                return;
            }

            if (Time.time - _lastStateLogTime < 0.5f)
            {
                return;
            }

            _lastStateLogTime = Time.time;
            var activeMoveType = ReadPropertyValue(movement, "activeMovementType");
            var sprintFactor = ReadPropertyValue(movement, "currentSprintFactor");
            var desiredVel = ReadPropertyValue(movement, "desiredMovementVelocity");
            var maxSprintLen = ReadPropertyValue(movement, "maxSprintVectorLength");
            var moveVelTarget = ReadPropertyValue(movement, "movementVelocityTarget");
            var sprintAccel = ReadPropertyValue(movement, "sprintAccelerationRate");
            var sprintPoseSet = ReadPropertyValue(movement, "sprintingPoseSet");
            var sprintSource = ReadPropertyValue(movement, "sprintingInputSource");
            var sprintHeading = ReadPropertyValue(movement, "latestSprintingHeadingVector");
            var grounded = TryCallMethod(movement, "IsGrounded");
            var surface = TryCallMethod(movement, "GetSurfaceSpeedMultiplier");
            var knockback = TryCallMethod(movement, "IsExperiencingKnockback");

            Instance.LoggerInstance.Msg(
                $"StoneStick state [{tag}] input={input} active={activeMoveType ?? "n/a"} grounded={grounded ?? "n/a"} " +
                $"surface={surface ?? "n/a"} knockback={knockback ?? "n/a"} sprintFactor={sprintFactor ?? "n/a"} " +
                $"desiredVel={desiredVel ?? "n/a"} maxSprint={maxSprintLen ?? "n/a"} sprintAccel={sprintAccel ?? "n/a"} " +
                $"moveVelTarget={moveVelTarget ?? "n/a"} sprintPoseSet={sprintPoseSet ?? "n/a"} sprintSource={sprintSource ?? "n/a"} " +
                $"sprintHeading={sprintHeading ?? "n/a"}");
        }

        private static object TryCallMethod(PlayerMovement movement, string methodName)
        {
            if (movement == null)
            {
                return null;
            }

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var method = movement.GetType().GetMethod(methodName, flags);
                if (method == null || method.GetParameters().Length != 0)
                {
                    return null;
                }

                return method.Invoke(movement, null);
            }
            catch
            {
                return null;
            }
        }

        private Vector2 ApplySprintMultiplier(PlayerMovement movement, Vector2 input)
        {
            if (!EnableSprintMultiplier || movement == null || input.sqrMagnitude < 0.0001f)
            {
                return input;
            }

            var sprintFactorObj = ReadPropertyValue(movement, "currentSprintFactor");
            if (sprintFactorObj == null)
            {
                return input;
            }

            var sprintPoseSet = ReadPropertyValue(movement, "sprintingPoseSet");
            if (sprintPoseSet == null)
            {
                return input;
            }

            float sprintFactor;
            try
            {
                sprintFactor = Convert.ToSingle(sprintFactorObj);
            }
            catch
            {
                return input;
            }

            if (sprintFactor <= 1f)
            {
                return input;
            }

            float maxSprintLen = 1f;
            var maxSprintObj = ReadPropertyValue(movement, "maxSprintVectorLength");
            if (maxSprintObj != null)
            {
                try
                {
                    maxSprintLen = Mathf.Max(1f, Convert.ToSingle(maxSprintObj));
                }
                catch
                {
                    maxSprintLen = 1f;
                }
            }

            var scale = ForceInstantSprint ? maxSprintLen : Mathf.Min(sprintFactor, maxSprintLen);
            scale *= SprintMultiplier;

            var scaled = input * scale;
            if (Time.time - _lastSprintScaleLogTime > 1f)
            {
                _lastSprintScaleLogTime = Time.time;
                LoggerInstance.Msg($"StoneStick sprint scale | base={input} factor={sprintFactor:0.00} scale={scale:0.00} result={scaled}");
            }

            return scaled;
        }

        private static void TrySetLatestSprintingHeadingVector(PlayerMovement movement, Vector2 input)
        {
            if (movement == null || input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float sprintFactor;
            try
            {
                var factorObj = ReadPropertyValue(movement, "currentSprintFactor");
                if (factorObj == null)
                {
                    return;
                }

                sprintFactor = Convert.ToSingle(factorObj);
            }
            catch
            {
                return;
            }

            if (sprintFactor <= 0f)
            {
                return;
            }

            if (!TryGetHeadingRotation(movement, out var headingRotation))
            {
                return;
            }

            var world = headingRotation * new Vector3(input.x, 0f, input.y);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = movement.GetType().GetProperty("latestSprintingHeadingVector", flags);
            if (prop == null || !prop.CanWrite)
            {
                return;
            }

            try
            {
                if (prop.PropertyType == typeof(Vector3))
                {
                    prop.SetValue(movement, world);
                }
                else if (prop.PropertyType == typeof(Vector2))
                {
                    prop.SetValue(movement, new Vector2(world.x, world.z));
                }
            }
            catch
            {
                // Ignore if we can't set it.
            }
        }

        private static bool TryGetHeadingRotation(PlayerMovement movement, out Quaternion headingRotation)
        {
            headingRotation = Quaternion.identity;
            var cam = GetCamera();
            if (cam != null)
            {
                var camRot = cam.transform.rotation;
                headingRotation = Quaternion.Euler(0f, camRot.eulerAngles.y, 0f);
                return true;
            }

            if (movement != null && movement.transform != null)
            {
                var rot = movement.transform.rotation;
                headingRotation = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
                return true;
            }

            return false;
        }

        private static Camera GetCamera()
        {
            if (_cachedCamera != null && _cachedCamera.enabled && _cachedCamera.gameObject.activeInHierarchy)
            {
                return _cachedCamera;
            }

            var main = Camera.main;
            if (main != null && main.enabled && main.gameObject.activeInHierarchy)
            {
                _cachedCamera = main;
                return _cachedCamera;
            }

            var count = Camera.allCamerasCount;
            if (count <= 0)
            {
                return null;
            }

            var cams = new Camera[count];
            var filled = Camera.GetAllCameras(cams);
            for (var i = 0; i < filled; i++)
            {
                var cam = cams[i];
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (cam.stereoEnabled)
                {
                    _cachedCamera = cam;
                    return _cachedCamera;
                }
            }

            _cachedCamera = cams[0];
            return _cachedCamera;
        }

        private static void LogVoiceMethodsOnce()
        {
            if (_voiceMethodsLogged || Instance == null)
            {
                return;
            }

            _voiceMethodsLogged = true;
            try
            {
                var hits = new List<string>();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    if (asm == null)
                    {
                        continue;
                    }

                    var asmName = asm.GetName().Name ?? string.Empty;
                    if (asmName.IndexOf("RUMBLE", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var type in types)
                    {
                        if (type == null)
                        {
                            continue;
                        }

                        var typeName = type.FullName ?? type.Name ?? string.Empty;
                        var typeLower = typeName.ToLowerInvariant();
                        if (!(typeLower.Contains("voice") || typeLower.Contains("chat") || typeLower.Contains("mic")
                            || typeLower.Contains("ptt") || typeLower.Contains("push") || typeLower.Contains("talk")))
                        {
                            continue;
                        }

                        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                        MethodInfo[] methods;
                        try
                        {
                            methods = type.GetMethods(flags);
                        }
                        catch
                        {
                            continue;
                        }

                        foreach (var method in methods)
                        {
                            var name = method?.Name;
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                continue;
                            }

                            var lower = name.ToLowerInvariant();
                            if (lower.Contains("voice") || lower.Contains("chat") || lower.Contains("mic")
                                || lower.Contains("ptt") || lower.Contains("push") || lower.Contains("talk"))
                            {
                                hits.Add($"{typeName}::{name}");
                            }
                        }
                    }
                }

                hits.Sort(StringComparer.Ordinal);
                if (hits.Count == 0)
                {
                    Instance.LoggerInstance.Msg("StoneStick: no obvious voice/ptt methods found.");
                    return;
                }

                Instance.LoggerInstance.Msg("StoneStick: voice/ptt methods of interest:");
                foreach (var entry in hits)
                {
                    Instance.LoggerInstance.Msg($" - {entry}");
                }
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick: voice scan failed: {ex.Message}");
            }
        }

        private static void TryPatchPtt()
        {
            if (_pttPatched || Instance == null)
            {
                return;
            }

            _pttPatched = true;
            try
            {
                var voiceType = typeof(PlayerVoiceSystem);
                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var methods = voiceType.GetMethods(flags);
                var harmony = new HarmonyLib.Harmony(HarmonyId);
                var patchedCount = 0;

                foreach (var method in methods)
                {
                    if (method == null || method.ReturnType != typeof(bool))
                    {
                        continue;
                    }

                    var name = method.Name ?? string.Empty;
                    var lower = name.ToLowerInvariant();
                    if (!(lower.Contains("ptt") || lower.Contains("push") || lower.Contains("talk")))
                    {
                        continue;
                    }

                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(ButtonLocoMod), nameof(PlayerVoiceSystem_PttCheck_Postfix)));
                        patchedCount++;
                    }
                    catch
                    {
                        // Ignore patch failures (field accessors, etc).
                    }
                }

                if (patchedCount > 0)
                {
                    Instance.LoggerInstance.Msg($"StoneStick: patched {patchedCount} PTT method(s) in PlayerVoiceSystem.");
                }
                else
                {
                    Instance.LoggerInstance.Msg("StoneStick: no patchable PTT methods found in PlayerVoiceSystem.");
                }
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick: PTT patch failed: {ex.Message}");
            }
        }

        private static void PlayerVoiceSystem_PttCheck_Postfix(MethodBase __originalMethod, ref bool __result)
        {
            if (Instance == null)
            {
                return;
            }

            if (Instance.IsPttPressed())
            {
                __result = true;
                Instance.LogPttDebug(__originalMethod?.Name ?? "unknown");
            }
        }

        private bool IsPttPressed()
        {
            if (!_pttInputReady)
            {
                TrySetupPttInput();
            }

            if (_pttInputReady && _pttAction != null)
            {
                try
                {
                    return _pttAction.ReadValue<float>() > 0.5f;
                }
                catch
                {
                    _pttInputReady = false;
                }
            }

            return false;
        }

        private void LogPttDebug(string methodName)
        {
            if (Time.time - _lastPttLogTime < 2f)
            {
                return;
            }

            _lastPttLogTime = Time.time;
            LoggerInstance.Msg($"StoneStick PTT | pressed=true method={methodName}");
        }

        private void TryUpdatePttOverride()
        {
            if (!_pttInputReady)
            {
                TrySetupPttInput();
            }

            if (_voiceSystem == null || _voiceSystem.Equals(null))
            {
                if (Time.time - _lastVoiceFindTime < 2f)
                {
                    return;
                }

                _lastVoiceFindTime = Time.time;
                _voiceSystem = UnityEngine.Object.FindObjectOfType<PlayerVoiceSystem>();
                _pttTargetsCached = false;
                if (_voiceSystem != null && !_voiceSystem.Equals(null))
                {
                    LoggerInstance.Msg("StoneStick: PlayerVoiceSystem found for PTT override.");
                }
                else
                {
                    return;
                }
            }

            if (!_pttTargetsCached)
            {
                CachePttTargets(_voiceSystem);
            }

            if ((_pttBoolFields == null || _pttBoolFields.Length == 0)
                && (_pttBoolProps == null || _pttBoolProps.Length == 0))
            {
                return;
            }

            var pressed = IsPttPressed();
            ApplyPttTargets(_voiceSystem, pressed);
        }

        private void CachePttTargets(PlayerVoiceSystem voice)
        {
            _pttTargetsCached = true;
            _pttBoolFields = Array.Empty<FieldInfo>();
            _pttBoolProps = Array.Empty<PropertyInfo>();

            if (voice == null)
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = voice.GetType().GetFields(flags);
            var props = voice.GetType().GetProperties(flags);
            var stateFields = new List<FieldInfo>();
            var broadFields = new List<FieldInfo>();
            var stateProps = new List<PropertyInfo>();
            var broadProps = new List<PropertyInfo>();

            foreach (var field in fields)
            {
                if (field == null || field.FieldType != typeof(bool))
                {
                    continue;
                }

                if (field.IsInitOnly || field.IsLiteral)
                {
                    continue;
                }

                var lower = (field.Name ?? string.Empty).ToLowerInvariant();
                if (!IsPttBroadName(lower))
                {
                    continue;
                }

                broadFields.Add(field);
                if (IsPttStateName(lower))
                {
                    stateFields.Add(field);
                }
            }

            foreach (var prop in props)
            {
                if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanWrite)
                {
                    continue;
                }

                var lower = (prop.Name ?? string.Empty).ToLowerInvariant();
                if (!IsPttBroadName(lower))
                {
                    continue;
                }

                broadProps.Add(prop);
                if (IsPttStateName(lower))
                {
                    stateProps.Add(prop);
                }
            }

            var finalFields = stateFields.Count > 0 ? stateFields : broadFields;
            var finalProps = stateProps.Count > 0 ? stateProps : broadProps;
            _pttBoolFields = finalFields.ToArray();
            _pttBoolProps = finalProps.ToArray();

            if (_pttBoolFields.Length == 0 && _pttBoolProps.Length == 0)
            {
                LoggerInstance.Msg("StoneStick: no PTT bool fields/properties found in PlayerVoiceSystem.");
                return;
            }

            if (_pttBoolFields.Length > 0)
            {
                LoggerInstance.Msg("StoneStick: PTT fields = " + string.Join(", ", new List<FieldInfo>(_pttBoolFields).ConvertAll(f => f.Name)));
            }

            if (_pttBoolProps.Length > 0)
            {
                LoggerInstance.Msg("StoneStick: PTT properties = " + string.Join(", ", new List<PropertyInfo>(_pttBoolProps).ConvertAll(p => p.Name)));
            }
        }

        private static bool IsPttBroadName(string lower)
        {
            if (string.IsNullOrEmpty(lower))
            {
                return false;
            }

            if (lower.Contains("mute"))
            {
                return false;
            }

            return lower.Contains("ptt") || lower.Contains("push") || lower.Contains("talk");
        }

        private static bool IsPttStateName(string lower)
        {
            if (string.IsNullOrEmpty(lower))
            {
                return false;
            }

            return lower.Contains("press") || lower.Contains("down") || lower.Contains("hold") || lower.Contains("active");
        }

        private static void ApplyPttTargets(PlayerVoiceSystem voice, bool pressed)
        {
            if (voice == null)
            {
                return;
            }

            if (_pttBoolFields != null)
            {
                foreach (var field in _pttBoolFields)
                {
                    try
                    {
                        field.SetValue(voice, pressed);
                    }
                    catch
                    {
                        // Ignore set failures.
                    }
                }
            }

            if (_pttBoolProps != null)
            {
                foreach (var prop in _pttBoolProps)
                {
                    try
                    {
                        prop.SetValue(voice, pressed);
                    }
                    catch
                    {
                        // Ignore set failures.
                    }
                }
            }
        }

    }
}
