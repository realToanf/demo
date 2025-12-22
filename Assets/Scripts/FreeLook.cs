using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class FreeLook : MonoBehaviour
{
    [Header("Assign")]
    public Transform yawPivot;   // Player (Cylinder)

    [Header("Tuning")]
    public float sensitivity = 0.15f;
    public float maxPitch = 75f;

    private float pitch;

    void Update()
    {
        // Ignore if not first-person camera
        if (!GetComponent<Camera>().enabled)
            return;

        // Ignore UI interaction
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 delta = Vector2.zero;

        // Desktop mouse
        if (Mouse.current != null)
            delta = Mouse.current.delta.ReadValue();

        // Mobile touch
        if (Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.isPressed)
            delta = Touchscreen.current.primaryTouch.delta.ReadValue();

        if (delta.sqrMagnitude < 0.001f) return;

        // Yaw (player)
        yawPivot.Rotate(0f, delta.x * sensitivity, 0f);

        // Pitch (camera)
        pitch -= delta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }
}
