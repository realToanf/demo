using UnityEngine;
using UnityEngine.InputSystem;   // <- quan trọng

public class CameraController : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float mouseSensitivity = 0.5f;

    float rotX = 0f;
    float rotY = 0f;

    void Start()
    {
        Application.targetFrameRate = 60;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // void FixedUpdate()
    // {
    //     rotY += Time.fixedDeltaTime * 4f;
    //     if(rotY > 360)
    //         rotY -= 360;
    //     transform.rotation = Quaternion.Euler(0, rotY, 0);
    // }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return;

        // Mouse look
        Vector2 mouseDelta = mouse.delta.ReadValue() * mouseSensitivity;

        rotY += mouseDelta.x;
        rotX -= mouseDelta.y;
        rotX = Mathf.Clamp(rotX, -80f, 80f);

        transform.rotation = Quaternion.Euler(rotX, rotY, 0);

        // Movement
        Vector3 dir = Vector3.zero;

        if (keyboard.wKey.isPressed) dir += transform.forward;
        if (keyboard.sKey.isPressed) dir -= transform.forward;
        if (keyboard.dKey.isPressed) dir += transform.right;
        if (keyboard.aKey.isPressed) dir -= transform.right;

        // Up / Down
        if (keyboard.eKey.isPressed) dir += transform.up;
        if (keyboard.qKey.isPressed) dir -= transform.up;

        transform.position += dir.normalized * moveSpeed * Time.deltaTime;
    }
}
