using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppRaycastList = Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(FlatOrbus.FlatOrbusMod), "FlatOrbus", "0.1.0", "fizzjay")]

namespace FlatOrbus
{
    /// <summary>
    /// Desktop input layer for Orbus. It intentionally uses reflection for Orbus-side
    /// components so the plugin does not bind to one revision of Assembly-CSharp.dll.
    /// </summary>
    public sealed class FlatOrbusMod : MelonMod
    {
        private bool _enabled = true;
        private bool _originalXrEnabled = true;
        private bool _cursorCaptured;
        private bool _showHelp = true;
        private bool _handMoveMode;
        private float _moveSpeed = 4.0f;
        private float _handSpeed = 1.5f;
        private readonly List<Transform> _localHands = new List<Transform>();
        private readonly List<Vector3> _handOffsets = new List<Vector3>();
        private int _selectedHand;
        private float _mouseSensitivity = 2.2f;
        private float _pitch;
        private Transform _playerRoot;
        private Transform _view;
        private CharacterController _characterController;
        private EventSystem _eventSystem;
        private GameObject _pointerOver;
        private GameObject _pointerPress;
        private PointerEventData _pointerData;
        private readonly Il2CppRaycastList _raycastResults = new Il2CppRaycastList();
        private float _nextRigSearch;
        private bool _lastLeftDown;
        private bool _lastLeftUp;
        private bool _lastMiddleDown;
        private bool _lastRightDown;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Flat desktop controls loaded and enabled. P or F8 toggles the mode.");
            _originalXrEnabled = ReadXrEnabled();
            SetEnabled(true);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _playerRoot = null;
            _view = null;
            _characterController = null;
            _eventSystem = null;
            _pointerData = null;
            _pointerOver = null;
            _pointerPress = null;
            _localHands.Clear();
            _handOffsets.Clear();
            _selectedHand = 0;
            _nextRigSearch = 0f;
            if (_enabled)
                SetXrEnabled(false);
            LoggerInstance.Msg($"Scene loaded: {sceneName}; waiting for the local rig.");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F8) || Input.GetKeyDown(KeyCode.P))
                SetEnabled(!_enabled);

            if (Input.GetKeyDown(KeyCode.F1))
                _showHelp = !_showHelp;
            if (Input.GetKeyDown(KeyCode.H))
                _handMoveMode = !_handMoveMode;
            if (Input.GetKeyDown(KeyCode.Tab) && _localHands.Count > 0)
                _selectedHand = (_selectedHand + 1) % _localHands.Count;

            if (!_enabled)
                return;

            if (Time.unscaledTime >= _nextRigSearch)
            {
                _nextRigSearch = Time.unscaledTime + 1.0f;
                FindRig();
            }

            // Right mouse captures the pointer for mouselook. Press Escape to release.
            if (Input.GetKeyDown(KeyCode.Escape))
                SetCursor(false);
            if (Input.GetMouseButtonDown(1))
                SetCursor(!_cursorCaptured);

            // B opens/closes the actual in-game backpack, using Orbus' own API.
            if (Input.GetKeyDown(KeyCode.B))
                ToggleBackpack();

            if (_cursorCaptured)
                UpdateLook();
            if (_handMoveMode)
                UpdateHandMovement();
            else
                UpdateMovement();
            UpdateMouseUI();
        }

        public override void OnLateUpdate()
        {
            if (_enabled && _handMoveMode)
                ApplyHandPositions();
        }

        public override void OnGUI()
        {
            int previousDepth = GUI.depth;
            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            GUI.depth = -10000;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = _enabled ? new Color(0.12f, 0.48f, 0.24f, 1f) : new Color(0.42f, 0.16f, 0.16f, 1f);

            const float width = 250f;
            const float height = 42f;
            var rect = new Rect(Screen.width - width - 12f, 12f, width, height);
            string label = _enabled ? "FLATSCREEN: ON  [P / F8]" : "FLATSCREEN: OFF  [P / F8]";
            if (GUI.Button(rect, label))
                SetEnabled(!_enabled);

            // A second, fixed status badge makes it obvious the mod/UI loaded even
            // when the top-right button is outside a cropped or unusually sized view.
            GUI.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f);
            GUI.Box(new Rect(12f, 12f, 230f, 34f), _enabled ? "FlatOrbus active · P toggles" : "FlatOrbus paused · P toggles");
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;

            if (_enabled && _showHelp)
            {
                var help = new Rect(Screen.width - 278f, 54f, 266f, 204f);
                GUI.Box(help, "FlatOrbus controls\nWASD move · mouse look (RMB)\nB / middle mouse: backpack\nH: move hands · Tab: select hand\nF1 help · Esc release · P/F8 toggle");
                if (GUI.Button(new Rect(help.x + 8f, help.y + 116f, help.width - 16f, 27f),
                    _handMoveMode ? "Hand movement: ON" : "Hand movement: OFF"))
                    _handMoveMode = !_handMoveMode;

                if (GUI.Button(new Rect(help.x + 8f, help.y + 148f, 42f, 25f), "−"))
                    AdjustSpeed(-0.5f);
                GUI.Label(new Rect(help.x + 54f, help.y + 148f, 158f, 25f),
                    (_handMoveMode ? "Hand speed: " : "Walk speed: ") + (_handMoveMode ? _handSpeed : _moveSpeed).ToString("0.0"));
                if (GUI.Button(new Rect(help.x + 216f, help.y + 148f, 42f, 25f), "+"))
                    AdjustSpeed(0.5f);
                if (_handMoveMode)
                    GUI.Label(new Rect(help.x + 8f, help.y + 176f, help.width - 16f, 20f),
                        _localHands.Count == 0 ? "Waiting for local hands…" : "Selected hand " + (_selectedHand + 1) + " of " + _localHands.Count);
            }

            // IMGUI is used only for this small overlay; game UI clicks are routed to
            // the Unity EventSystem below, so the overlay does not replace game menus.
            if (_enabled && !_cursorCaptured)
                DrawCrosshair();

            GUI.depth = previousDepth;
        }

        private void SetEnabled(bool value)
        {
            _enabled = value;
            _cursorCaptured = false;
            SetCursor(false);
            SetXrEnabled(value ? false : _originalXrEnabled);
            if (value)
                FindRig();
            LoggerInstance.Msg(value ? "Flat desktop mode enabled." : "Flat desktop mode disabled.");
        }

        private static bool ReadXrEnabled()
        {
            try
            {
                Type t = Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.XRModule");
                PropertyInfo p = t?.GetProperty("enabled", BindingFlags.Public | BindingFlags.Static);
                if (p != null)
                    return Convert.ToBoolean(p.GetValue(null));
            }
            catch { }
            return true;
        }

        private static void SetXrEnabled(bool value)
        {
            // XRSettings is an optional attempt to leave stereo presentation. Some
            // older Orbus builds use vendor-specific VR APIs, so failure is non-fatal.
            try
            {
                Type t = Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.XRModule");
                PropertyInfo p = t?.GetProperty("enabled", BindingFlags.Public | BindingFlags.Static);
                p?.SetValue(null, value, null);
            }
            catch { }
        }

        private void SetCursor(bool captured)
        {
            _cursorCaptured = captured;
            try
            {
                Cursor.visible = !captured;
                Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            }
            catch { }
        }

        private void FindRig()
        {
            try
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                    foreach (Camera candidate in cameras)
                    {
                        if (candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy)
                        {
                            camera = candidate;
                            break;
                        }
                    }
                }
                if (camera != null)
                    _view = camera.transform;

                List<MonoBehaviour> players = FindBehavioursByTypeName("Il2CppMagicalActual.PlayerCharacter");
                if (players.Count > 0)
                {
                    UnityEngine.Component player = players[0];
                    _playerRoot = player.transform;
                    _characterController = player.GetComponent<CharacterController>();
                }

                // Fallback for scenes where the local PlayerCharacter component is not
                // instantiated yet. Move the camera's immediate rig parent, not the scene root.
                if (_playerRoot == null && _view != null)
                {
                    Transform candidate = _view.parent;
                    if (candidate != null && candidate.parent != null)
                        candidate = candidate.parent;
                    _playerRoot = candidate;
                    _characterController = candidate != null ? candidate.GetComponent<CharacterController>() : null;
                }

                FindHands();
                _eventSystem = EventSystem.current;
                if (_eventSystem != null && _pointerData == null)
                    _pointerData = new PointerEventData(_eventSystem);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("Rig discovery will retry: " + ex.Message);
            }
        }

        private void UpdateMovement()
        {
            if (_playerRoot == null || _view == null || _cursorCaptured == false && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float z = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            if (x == 0f && z == 0f)
                return;

            Vector3 forward = _view.forward;
            Vector3 right = _view.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude > 0.001f) forward.Normalize();
            if (right.sqrMagnitude > 0.001f) right.Normalize();
            Vector3 delta = (forward * z + right * x).normalized * _moveSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                delta *= 1.7f;

            if (_characterController != null && _characterController.enabled)
                _characterController.Move(delta);
            else
                _playerRoot.position += delta;
        }

        private void UpdateLook()
        {
            if (_view == null)
                return;
            float yaw = Input.GetAxis("Mouse X") * _mouseSensitivity;
            float pitchDelta = Input.GetAxis("Mouse Y") * _mouseSensitivity;
            if (_playerRoot != null)
                _playerRoot.Rotate(0f, yaw, 0f, Space.World);
            _pitch = Mathf.Clamp(_pitch - pitchDelta, -80f, 80f);
            Vector3 euler = _view.localEulerAngles;
            _view.localEulerAngles = new Vector3(_pitch, euler.y, euler.z);
        }

        private void UpdateMouseUI()
        {
            _lastLeftDown = Input.GetMouseButtonDown(0);
            _lastLeftUp = Input.GetMouseButtonUp(0);
            _lastMiddleDown = Input.GetMouseButtonDown(2);
            _lastRightDown = Input.GetMouseButtonDown(1);

            if (_lastMiddleDown)
                ToggleBackpack();

            if (_cursorCaptured || _lastRightDown || _pointerData == null || IsMouseOnOverlay())
                return;

            try
            {
                // A stock StandaloneInputModule already converts Unity mouse input to
                // clicks; avoid dispatching a second synthetic event on those scenes.
                if (_eventSystem != null && _eventSystem.currentInputModule != null &&
                    _eventSystem.currentInputModule.GetType().Name.Contains("StandaloneInputModule"))
                    return;

                _pointerData.position = Input.mousePosition;
                _pointerData.delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                _pointerData.button = PointerEventData.InputButton.Left;
                _raycastResults.Clear();
                _eventSystem.RaycastAll(_pointerData, _raycastResults);
                GameObject next = _raycastResults.Count > 0 ? _raycastResults[0].gameObject : null;
                _pointerData.pointerCurrentRaycast = _raycastResults.Count > 0 ? _raycastResults[0] : new RaycastResult();

                if (next != _pointerOver)
                {
                    if (_pointerOver != null)
                        ExecuteEvents.Execute(_pointerOver, _pointerData, ExecuteEvents.pointerExitHandler);
                    _pointerOver = next;
                    if (_pointerOver != null)
                        ExecuteEvents.Execute(_pointerOver, _pointerData, ExecuteEvents.pointerEnterHandler);
                }

                if (_lastLeftDown)
                {
                    _pointerPress = _pointerOver;
                    _pointerData.pressPosition = _pointerData.position;
                    _pointerData.pointerPressRaycast = _raycastResults.Count > 0 ? _raycastResults[0] : new RaycastResult();
                    if (_pointerPress != null)
                    {
                        GameObject pressHandler = ExecuteEvents.ExecuteHierarchy(_pointerPress, _pointerData, ExecuteEvents.pointerDownHandler);
                        _pointerData.pointerPress = pressHandler != null ? pressHandler : _pointerPress;
                        _pointerData.rawPointerPress = _pointerPress;
                    }
                }

                if (_lastLeftUp)
                {
                    if (_pointerPress != null)
                    {
                        ExecuteEvents.Execute(_pointerPress, _pointerData, ExecuteEvents.pointerUpHandler);
                        GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_pointerOver);
                        if (clickHandler != null && clickHandler == _pointerData.pointerPress)
                            ExecuteEvents.Execute(clickHandler, _pointerData, ExecuteEvents.pointerClickHandler);
                    }
                    _pointerPress = null;
                    _pointerData.pointerPress = null;
                    _pointerData.rawPointerPress = null;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("Mouse UI routing failed: " + ex.Message);
            }
        }

        private void AdjustSpeed(float amount)
        {
            if (_handMoveMode)
                _handSpeed = Mathf.Clamp(_handSpeed + amount, 0.5f, 8f);
            else
                _moveSpeed = Mathf.Clamp(_moveSpeed + amount, 0.5f, 12f);
        }

        private bool IsMouseOnOverlay()
        {
            Vector3 mouse = Input.mousePosition;
            float guiY = Screen.height - mouse.y;
            var point = new Vector2(mouse.x, guiY);
            if (new Rect(Screen.width - 262f, 12f, 250f, 42f).Contains(point) ||
                new Rect(12f, 12f, 230f, 34f).Contains(point))
                return true;
            return _showHelp && _enabled && new Rect(Screen.width - 278f, 54f, 266f, 204f).Contains(point);
        }

        private void UpdateHandMovement()
        {
            if (_view == null || _localHands.Count == 0)
                return;
            if (_selectedHand >= _localHands.Count)
                _selectedHand = 0;

            float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float z = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.R) ? 1f : 0f) - (Input.GetKey(KeyCode.F) ? 1f : 0f);
            if (x == 0f && y == 0f && z == 0f)
                return;

            Transform hand = _localHands[_selectedHand];
            Vector3 offset = _handOffsets[_selectedHand];
            Vector3 delta = (_view.right * x + _view.up * y + _view.forward * z) * _handSpeed * Time.deltaTime;
            Vector3 world = _view.TransformPoint(offset) + delta;
            _handOffsets[_selectedHand] = _view.InverseTransformPoint(world);
        }

        private void ApplyHandPositions()
        {
            if (_view == null || _localHands.Count == 0)
                return;
            for (int i = _localHands.Count - 1; i >= 0; i--)
            {
                if (_localHands[i] == null)
                {
                    _localHands.RemoveAt(i);
                    _handOffsets.RemoveAt(i);
                    continue;
                }
                _localHands[i].position = _view.TransformPoint(_handOffsets[i]);
            }
        }

        private void FindHands()
        {
            try
            {
                if (_view == null)
                    return;
                List<MonoBehaviour> grabbers = FindBehavioursByTypeName("Il2CppMagicalActual.Grabber");
                foreach (MonoBehaviour item in grabbers)
                {
                    if (item == null || _localHands.Contains(item.transform))
                        continue;
                    PropertyInfo localProperty = item.GetType().GetProperty("isLocalGrabber", BindingFlags.Public | BindingFlags.Instance);
                    if (localProperty != null && !Convert.ToBoolean(localProperty.GetValue(item)))
                        continue;
                    _localHands.Add(item.transform);
                    _handOffsets.Add(_view.InverseTransformPoint(item.transform.position));
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("Hand discovery will retry: " + ex.Message);
            }
        }

        private void ToggleBackpack()
        {
            try
            {
                List<MonoBehaviour> managers = FindBehavioursByTypeName("Il2CppMagicalActual.InventoryManager");
                if (managers.Count == 0)
                {
                    LoggerInstance.Warning("Backpack manager is not ready in this scene.");
                    return;
                }
                MonoBehaviour manager = managers[0];
                Type inventoryType = manager.GetType();
                bool open = false;
                PropertyInfo isOpen = inventoryType.GetProperty("isOpen", BindingFlags.Public | BindingFlags.Instance);
                if (isOpen != null)
                    open = Convert.ToBoolean(isOpen.GetValue(manager));
                string method = open ? "Close" : "Open";
                inventoryType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance)?.Invoke(manager, null);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("Could not toggle backpack: " + ex.Message);
            }
        }

        private static List<MonoBehaviour> FindBehavioursByTypeName(string fullName)
        {
            var matches = new List<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().FullName == fullName)
                    matches.Add(behaviour);
            }
            return matches;
        }

        private static void DrawCrosshair()
        {
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            GUI.Label(new Rect(x - 5f, y - 9f, 20f, 20f), "+");
        }
    }
}
