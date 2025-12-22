using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class MobileLookDrag : MonoBehaviour
{
    [Header("Assign these")]
    public Transform yawPivot;     // player root (rotates left/right)
    public Transform pitchPivot;   // camera (rotates up/down)

    [Header("Tuning")]
    public float sensitivity = 0.12f;
    public float maxPitch = 75f;

    private float pitch;

    void Awake()
    {
        if (pitchPivot == null) pitchPivot = transform;
    }

    void Update()
    {
        // Ignore if touching UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(0))
            return;

        if (Touchscreen.current == null || Touchscreen.current.touches.Count == 0)
            return;

        var t = Touchscreen.current.primaryTouch;
        if (!t.press.isPressed) return;

        Vector2 delta = t.delta.ReadValue();
        if (delta.sqrMagnitude < 0.01f) return;

        // yaw (left/right)
        if (yawPivot != null)
            yawPivot.Rotate(0f, delta.x * sensitivity, 0f, Space.World);

        // pitch (up/down)
        pitch -= delta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        if (pitchPivot != null)
            pitchPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }
}
