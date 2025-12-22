using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCam : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float lookSpeed = 0.1f;
    public float fastMultiplier = 3f;

    float yaw;
    float pitch;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null)
            return;

        // Mouse look
        Vector2 mouse = Mouse.current.delta.ReadValue();
        yaw += mouse.x * lookSpeed;
        pitch -= mouse.y * lookSpeed;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Movement
        float speed = moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed)
            speed *= fastMultiplier;

        Vector3 move = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) move += transform.forward;
        if (Keyboard.current.sKey.isPressed) move -= transform.forward;
        if (Keyboard.current.dKey.isPressed) move += transform.right;
        if (Keyboard.current.aKey.isPressed) move -= transform.right;
        if (Keyboard.current.eKey.isPressed) move += transform.up;
        if (Keyboard.current.qKey.isPressed) move -= transform.up;

        transform.position += move * speed * Time.deltaTime;

        // Unlock mouse
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
