using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCam : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float lookSpeed = 0.1f;
    public float fastMultiplier = 3f;

    [Header("Reset")]
    public Transform origin; // assign an empty GameObject in the scene

    float yaw;
    float pitch;
    int ignoreMouseFrames;

    public void ResetToOrigin()
    {
        if (origin == null) return;
        transform.SetPositionAndRotation(origin.position, origin.rotation);

        // Sync internal angles so it won't snap next frame
        var e = transform.rotation.eulerAngles;
        yaw = e.y;
        pitch = e.x;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        ignoreMouseFrames = 2;
    }

    void OnEnable()
    {
        // Sync when enabled (prevents jump)
        var e = transform.rotation.eulerAngles;
        yaw = e.y;
        pitch = e.x;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        ignoreMouseFrames = 2;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null) return;

        if (ignoreMouseFrames > 0)
        {
            ignoreMouseFrames--;
            return;
        }

        // Mouse look
        if (!CameraSwitcher.UiMode)
        {
            Vector2 mouse = Mouse.current.delta.ReadValue();
            yaw += mouse.x * lookSpeed;
            pitch -= mouse.y * lookSpeed;
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        
        // Movement
        float speed = moveSpeed * (Keyboard.current.leftShiftKey.isPressed ? fastMultiplier : 1f);

        Vector3 move = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) move += transform.forward;
        if (Keyboard.current.sKey.isPressed) move -= transform.forward;
        if (Keyboard.current.dKey.isPressed) move += transform.right;
        if (Keyboard.current.aKey.isPressed) move -= transform.right;
        if (Keyboard.current.eKey.isPressed) move += transform.up;
        if (Keyboard.current.qKey.isPressed) move -= transform.up;

        if (move.sqrMagnitude > 0f)
            transform.position += move.normalized * speed * Time.deltaTime;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
