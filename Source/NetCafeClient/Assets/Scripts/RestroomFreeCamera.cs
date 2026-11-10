using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Free-fly camera for the Restroom scene. Works in Editor Play Mode and Player builds.
/// Hold RMB to look, WASD/arrow keys to move, Shift to accelerate, Q/E to descend/ascend,
/// and Esc to release the cursor.
/// </summary>
[DisallowMultipleComponent]
public sealed class RestroomFreeCamera : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0.1f), Tooltip("Horizontal movement speed in metres per second.")]
    private float moveSpeed = 3.0f;
    [SerializeField, Min(1.0f), Tooltip("Movement multiplier while either Shift key is held.")]
    private float sprintMultiplier = 3.0f;
    [SerializeField, Min(0.1f), Tooltip("Vertical movement speed in metres per second.")]
    private float verticalSpeed = 2.0f;

    [Header("Look")]
    [SerializeField, Min(0.001f), Tooltip("Mouse look sensitivity in degrees per input pixel.")]
    private float lookSensitivity = 0.12f;
    [SerializeField, Tooltip("Invert vertical mouse look.")]
    private bool invertMouseY = false;

    private float yaw;
    private float pitch;
    private bool cursorCaptured;
    private bool initialized;

    private void OnEnable()
    {
        InitializeAngles();
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ReleaseCursor();
    }

    private void InitializeAngles()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = NormalizeAngle(euler.x);
        initialized = true;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private void Update()
    {
        if (!initialized) InitializeAngles();

#if ENABLE_INPUT_SYSTEM
        UpdateInputSystem();
#elif ENABLE_LEGACY_INPUT_MANAGER
        UpdateLegacyInput();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private void UpdateInputSystem()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            ReleaseCursor();

        bool lookHeld = mouse != null && mouse.rightButton.isPressed;
        if (lookHeld)
        {
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            float ySign = invertMouseY ? 1f : -1f;
            pitch += delta.y * lookSensitivity * ySign;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else if (cursorCaptured && mouse != null && mouse.rightButton.wasReleasedThisFrame)
        {
            ReleaseCursor();
        }

        float forward = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                      - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
        float strafe = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                     - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
        bool sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        Move(forward, strafe, vertical, sprint);
    }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
    private void UpdateLegacyInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) ReleaseCursor();

        bool lookHeld = Input.GetMouseButton(1);
        if (lookHeld)
        {
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity * 8f;
            pitch += Input.GetAxisRaw("Mouse Y") * lookSensitivity * 8f * (invertMouseY ? 1f : -1f);
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else if (cursorCaptured && Input.GetMouseButtonUp(1))
        {
            ReleaseCursor();
        }

        float forward = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                      - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        float strafe = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                     - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        float vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        Move(forward, strafe, vertical, sprint);
    }
#endif

    private void Move(float forward, float strafe, float vertical, bool sprint)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 planar = flatForward * forward + flatRight * strafe;
        if (planar.sqrMagnitude > 1f) planar.Normalize();

        float multiplier = sprint ? sprintMultiplier : 1f;
        Vector3 displacement = planar * (moveSpeed * multiplier)
                             + Vector3.up * (vertical * verticalSpeed * multiplier);
        transform.position += displacement * Time.unscaledDeltaTime;
    }

    private void ReleaseCursor()
    {
        cursorCaptured = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
