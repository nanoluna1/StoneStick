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
        private static bool _moveFieldsSearched;
        private static FieldInfo[] _moveInputFields;
        private static bool _movementMethodsLogged;
        private static MethodInfo _sprintPoseMethod;
        private static bool _sprintPosePatched;
        private static bool _sprintSourceResolved;
        private static object _preferredSprintSource;
        private static bool _sprintSourceEnumLogged;
        private static bool _baseWalkSpeedCaptured;
        private static float _baseDesiredMovementVelocity;

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
            LogMovementMethodsOnce();
            TryPatchSprintPose();
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
            ApplyMoveInputToFields(movement, input);
            TrySetSprintingInputSource(movement, input);
            TryApplySprintVelocity(movement, input);
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

        private static void ApplyMoveInputToFields(PlayerMovement movement, Vector2 input)
        {
            if (movement == null)
            {
                return;
            }

            CacheMoveInputFields();
            if (_moveInputFields == null || _moveInputFields.Length == 0)
            {
                return;
            }

            foreach (var field in _moveInputFields)
            {
                try
                {
                    field.SetValue(movement, input);
                }
                catch
                {
                    // Ignore fields we can't set.
                }
            }
        }

        private static void CacheMoveInputFields()
        {
            if (_moveFieldsSearched)
            {
                return;
            }

            _moveFieldsSearched = true;
            var list = new List<FieldInfo>();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = typeof(PlayerMovement).GetFields(flags);

            foreach (var field in fields)
            {
                if (field.FieldType != typeof(Vector2))
                {
                    continue;
                }

                var name = field.Name.ToLowerInvariant();
                // Avoid overriding smoothed/velocity/accel fields to preserve buildup behavior.
                if (name.Contains("smooth") || name.Contains("smoothing") || name.Contains("velocity") || name.Contains("vel")
                    || name.Contains("speed") || name.Contains("accel") || name.Contains("acceleration")
                    || name.Contains("desired") || name.Contains("target"))
                {
                    continue;
                }

                if (name.Contains("move") || name.Contains("input") || name.Contains("stick") || name.Contains("joystick"))
                {
                    list.Add(field);
                }
            }

            // If we find explicit raw/unfiltered/source inputs, prefer those only.
            var rawList = new List<FieldInfo>();
            foreach (var field in list)
            {
                var name = field.Name.ToLowerInvariant();
                if (name.Contains("raw") || name.Contains("unfiltered") || name.Contains("source"))
                {
                    rawList.Add(field);
                }
            }

            _moveInputFields = rawList.Count > 0 ? rawList.ToArray() : list.ToArray();
            if (Instance != null && _moveInputFields.Length > 0)
            {
                var names = string.Join(", ", new List<FieldInfo>(_moveInputFields).ConvertAll(f => f.Name));
                Instance.LoggerInstance.Msg($"StoneStick: using move fields: {names}");
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

        private static void TryApplySprintVelocity(PlayerMovement movement, Vector2 input)
        {
            if (movement == null || input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sprintProp = movement.GetType().GetProperty("currentSprintFactor", flags);
            var desiredProp = movement.GetType().GetProperty("desiredMovementVelocity", flags);
            if (sprintProp == null || desiredProp == null || !desiredProp.CanWrite)
            {
                return;
            }

            try
            {
                var sprintObj = sprintProp.GetValue(movement);
                if (sprintObj == null)
                {
                    return;
                }

                var sprintFactor = Convert.ToSingle(sprintObj);
                var desiredObj = desiredProp.GetValue(movement);
                if (desiredObj == null)
                {
                    return;
                }

                // Capture base walk speed when sprint is not boosted.
                if (!_baseWalkSpeedCaptured && sprintFactor <= 1.01f)
                {
                    _baseDesiredMovementVelocity = Convert.ToSingle(desiredObj);
                    _baseWalkSpeedCaptured = _baseDesiredMovementVelocity > 0f;
                }

                if (_baseWalkSpeedCaptured)
                {
                    if (sprintFactor > 1.01f)
                    {
                        var target = _baseDesiredMovementVelocity * sprintFactor;
                        desiredProp.SetValue(movement, target);
                    }
                    else
                    {
                        // Reset to base walk speed when sprint ends.
                        desiredProp.SetValue(movement, _baseDesiredMovementVelocity);
                    }
                }
            }
            catch
            {
                // Ignore failures.
            }
        }

        private static void TrySetSprintingInputSource(PlayerMovement movement, Vector2 input)
        {
            if (movement == null)
            {
                return;
            }

            if (input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = movement.GetType().GetProperty("sprintingInputSource", flags);
            if (prop == null || !prop.CanWrite)
            {
                return;
            }

            if (!_sprintSourceResolved)
            {
                _sprintSourceResolved = true;
                _preferredSprintSource = ResolvePreferredSprintSource(prop.PropertyType);
                if (Instance != null)
                {
                    Instance.LoggerInstance.Msg(
                        $"StoneStick: sprint input source preferred = {_preferredSprintSource ?? "n/a"}");
                }
            }

            if (_preferredSprintSource == null)
            {
                LogSprintSourceEnumOnce(prop.PropertyType);
                return;
            }

            try
            {
                prop.SetValue(movement, _preferredSprintSource);
            }
            catch
            {
                // Ignore if we can't set it.
            }
        }

        private static object ResolvePreferredSprintSource(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                return null;
            }

            var names = Enum.GetNames(enumType);
            string Pick(params string[] tokens)
            {
                foreach (var name in names)
                {
                    var lower = name.ToLowerInvariant();
                    var ok = true;
                    foreach (var token in tokens)
                    {
                        if (!lower.Contains(token))
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok && !lower.Contains("pose"))
                    {
                        return name;
                    }
                }

                return null;
            }

            var preferred =
                Pick("joystick") ??
                Pick("stick") ??
                Pick("move") ??
                Pick("movement") ??
                Pick("locom") ??
                Pick("controller") ??
                Pick("input");

            if (preferred == null)
            {
                return null;
            }

            try
            {
                return Enum.Parse(enumType, preferred);
            }
            catch
            {
                return null;
            }
        }

        private static void LogSprintSourceEnumOnce(Type enumType)
        {
            if (_sprintSourceEnumLogged || Instance == null)
            {
                return;
            }

            _sprintSourceEnumLogged = true;
            if (enumType == null)
            {
                Instance.LoggerInstance.Msg("StoneStick: sprint input source type is null.");
                return;
            }

            try
            {
                if (enumType.IsEnum)
                {
                    Instance.LoggerInstance.Msg($"StoneStick: sprint input source enum = {enumType.FullName}");
                    var names = Enum.GetNames(enumType);
                    foreach (var name in names)
                    {
                        var value = Enum.Parse(enumType, name);
                        var raw = Convert.ToInt32(value);
                        Instance.LoggerInstance.Msg($" - {name} ({raw})");
                    }
                    return;
                }

                Instance.LoggerInstance.Msg($"StoneStick: sprint input source type = {enumType.FullName}");

                // Dump all static fields (il2cpp "enum-like" classes often expose instances here).
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var fields = enumType.GetFields(flags);
                foreach (var field in fields)
                {
                    object value = null;
                    try { value = field.GetValue(null); } catch { }
                    var valueStr = value != null ? value.ToString() : "n/a";
                    Instance.LoggerInstance.Msg($" - field {field.Name} : {field.FieldType.Name} = {valueStr}");
                }

                // Dump all static properties.
                var props = enumType.GetProperties(flags);
                foreach (var prop in props)
                {
                    if (!prop.CanRead)
                    {
                        continue;
                    }

                    object value = null;
                    try { value = prop.GetValue(null); } catch { }
                    var valueStr = value != null ? value.ToString() : "n/a";
                    Instance.LoggerInstance.Msg($" - prop {prop.Name} : {prop.PropertyType.Name} = {valueStr}");
                }
            }
            catch (Exception ex)
            {
                Instance.LoggerInstance.Warning($"StoneStick: enum dump failed: {ex.Message}");
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

    }
}
