using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.InputSystem.PlayerInput))]
public class PlayerOrbitCameraController : MonoBehaviour
{
    [Header("Orbit")]
    [SerializeField] private Vector2 _pitchLimits = new(-10f, 65f);
    [SerializeField] private float _gamepadOrbitSpeed = 120f;
    [SerializeField] private float _mouseOrbitSensitivity = 0.12f;
    [SerializeField] private float _cameraDamping = 12f;

    [Header("Zoom")]
    [SerializeField] private Vector2 _zoomLimits = new(0.75f, 1.25f);
    [SerializeField] private float _bumperZoomSpeed = 8f;
    [SerializeField] private float _mouseWheelZoomStep = 1f;

    [Header("Collision")]
    [SerializeField] private LayerMask _collisionLayers = ~((1 << 2) | (1 << 5) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 9) | (1 << 10));
    [SerializeField] private float _cameraCollisionRadius = 0.35f;
    [SerializeField] private float _minimumCollisionDistance = 2f;
    [SerializeField] private float _returnDamping = 10f;

    private readonly RaycastHit[] _collisionHits = new RaycastHit[16];

    private UnityEngine.InputSystem.PlayerInput _playerInput;
    private InputAction _lookAction;
    private InputAction _respawnAction;
    private CinemachineVirtualCamera _virtualCamera;
    private CinemachineTransposer _transposer;
    private CinemachineTransposer.BindingMode _originalBindingMode;
    private Vector3 _originalFollowOffset;
    private float _originalRadius;
    private float _orbitRadius;
    private float _yaw;
    private float _pitch;
    private bool _isOrbiting;
    private bool _isReturning;

    private void Awake()
    {
        _playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        _lookAction = _playerInput.actions.FindAction("Look");
        _respawnAction = _playerInput.actions.FindAction("Respawn");

        FindActiveCamera();
        if (_transposer == null)
        {
            enabled = false;
            return;
        }

        _originalBindingMode = _transposer.m_BindingMode;
        _originalFollowOffset = _transposer.m_FollowOffset;
        _originalRadius = _originalFollowOffset.magnitude;
        _orbitRadius = _originalRadius;
    }

    private void Update()
    {
        if (TogglePressed())
        {
            if (_isOrbiting)
            {
                ExitOrbit();
            }
            else
            {
                EnterOrbit();
            }
        }

        if (_respawnAction != null && _respawnAction.WasPerformedThisFrame())
        {
            ExitOrbit();
        }

        if (_isOrbiting)
        {
            UpdateOrbit();
        }
        else if (_isReturning)
        {
            UpdateReturn();
        }
    }

    private void OnDisable()
    {
        RestoreChaseCamera();
    }

    private void FindActiveCamera()
    {
        foreach (CinemachineVirtualCamera virtualCamera in GetComponentsInChildren<CinemachineVirtualCamera>(true))
        {
            CinemachineTransposer transposer = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (virtualCamera.gameObject.activeSelf && transposer != null)
            {
                _virtualCamera = virtualCamera;
                _transposer = transposer;
                return;
            }
        }
    }

    private bool TogglePressed()
    {
        foreach (InputDevice device in _playerInput.devices)
        {
            if (device is Gamepad gamepad && gamepad.rightStickButton.wasPressedThisFrame)
            {
                return true;
            }

            if (device is Keyboard keyboard && keyboard.cKey.wasPressedThisFrame)
            {
                return true;
            }
        }

        return false;
    }

    private void EnterOrbit()
    {
        Vector3 worldOffset = GetCurrentWorldOffset();
        _transposer.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
        _transposer.m_FollowOffset = worldOffset;

        _orbitRadius = Mathf.Clamp(worldOffset.magnitude, _originalRadius * _zoomLimits.x, _originalRadius * _zoomLimits.y);
        _yaw = Mathf.Atan2(-worldOffset.x, -worldOffset.z) * Mathf.Rad2Deg;
        _pitch = Mathf.Asin(Mathf.Clamp(worldOffset.y / Mathf.Max(worldOffset.magnitude, 0.001f), -1f, 1f)) * Mathf.Rad2Deg;
        _pitch = Mathf.Clamp(_pitch, _pitchLimits.x, _pitchLimits.y);

        _isReturning = false;
        _isOrbiting = true;
    }

    private void ExitOrbit()
    {
        if (!_isOrbiting)
        {
            return;
        }

        _isOrbiting = false;
        _isReturning = true;
    }

    private void UpdateOrbit()
    {
        float deltaTime = Time.unscaledDeltaTime;
        Vector2 look = _lookAction?.ReadValue<Vector2>() ?? Vector2.zero;
        bool usingMouse = _lookAction?.activeControl?.device is Mouse;
        float lookScale = usingMouse ? _mouseOrbitSensitivity : _gamepadOrbitSpeed * deltaTime;

        _yaw += look.x * lookScale;
        _pitch = Mathf.Clamp(_pitch - look.y * lookScale, _pitchLimits.x, _pitchLimits.y);
        UpdateZoom(deltaTime);

        Vector3 desiredOffset = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back * _orbitRadius;
        desiredOffset = ConstrainAgainstWorld(desiredOffset);
        _transposer.m_FollowOffset = Damp(_transposer.m_FollowOffset, desiredOffset, _cameraDamping, deltaTime);
    }

    private void UpdateZoom(float deltaTime)
    {
        float bumperInput = 0f;
        float wheelInput = 0f;

        foreach (InputDevice device in _playerInput.devices)
        {
            if (device is Gamepad gamepad)
            {
                bumperInput += (gamepad.rightShoulder.isPressed ? 1f : 0f) - (gamepad.leftShoulder.isPressed ? 1f : 0f);
            }
            else if (device is Mouse mouse)
            {
                wheelInput += mouse.scroll.ReadValue().y;
            }
        }

        _orbitRadius -= bumperInput * _bumperZoomSpeed * deltaTime;
        _orbitRadius -= Mathf.Sign(wheelInput) * _mouseWheelZoomStep;
        _orbitRadius = Mathf.Clamp(_orbitRadius, _originalRadius * _zoomLimits.x, _originalRadius * _zoomLimits.y);
    }

    private Vector3 ConstrainAgainstWorld(Vector3 desiredOffset)
    {
        Transform follow = _virtualCamera.Follow;
        if (follow == null)
        {
            return desiredOffset;
        }

        float desiredDistance = desiredOffset.magnitude;
        Vector3 direction = desiredOffset / Mathf.Max(desiredDistance, 0.001f);
        int hitCount = Physics.SphereCastNonAlloc(
            follow.position,
            _cameraCollisionRadius,
            direction,
            _collisionHits,
            desiredDistance,
            _collisionLayers,
            QueryTriggerInteraction.Ignore);

        float allowedDistance = desiredDistance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _collisionHits[i];
            if (hit.collider == null || hit.collider.transform.root == transform)
            {
                continue;
            }

            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(_minimumCollisionDistance, hit.distance - _cameraCollisionRadius));
        }

        return direction * allowedDistance;
    }

    private void UpdateReturn()
    {
        Vector3 targetOffset = GetOriginalWorldOffset();
        _transposer.m_FollowOffset = Damp(_transposer.m_FollowOffset, targetOffset, _returnDamping, Time.unscaledDeltaTime);

        if ((_transposer.m_FollowOffset - targetOffset).sqrMagnitude > 0.0001f)
        {
            return;
        }

        RestoreChaseCamera();
    }

    private Vector3 GetCurrentWorldOffset()
    {
        if (_transposer.m_BindingMode == CinemachineTransposer.BindingMode.WorldSpace)
        {
            return _transposer.m_FollowOffset;
        }

        return GetTargetYawRotation() * _transposer.m_FollowOffset;
    }

    private Vector3 GetOriginalWorldOffset()
    {
        if (_originalBindingMode == CinemachineTransposer.BindingMode.WorldSpace)
        {
            return _originalFollowOffset;
        }

        return GetTargetYawRotation() * _originalFollowOffset;
    }

    private Quaternion GetTargetYawRotation()
    {
        Transform follow = _virtualCamera.Follow;
        return follow == null ? Quaternion.identity : Quaternion.Euler(0f, follow.eulerAngles.y, 0f);
    }

    private static Vector3 Damp(Vector3 current, Vector3 target, float damping, float deltaTime)
    {
        return Vector3.Lerp(current, target, 1f - Mathf.Exp(-damping * deltaTime));
    }

    private void RestoreChaseCamera()
    {
        if (_transposer == null)
        {
            return;
        }

        _isOrbiting = false;
        _isReturning = false;
        _orbitRadius = _originalRadius;
        _transposer.m_BindingMode = _originalBindingMode;
        _transposer.m_FollowOffset = _originalFollowOffset;
    }
}
