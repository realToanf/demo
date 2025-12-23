using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Controls;

public class MobileLookDrag : MonoBehaviour
{
    [Header("Assign these")]
    public Transform yawPivot;     // player root (rotates left/right)
    public Transform pitchPivot;   // camera (rotates up/down)

    [Header("Tuning")]
    public float sensitivity = 0.12f;
    public float maxPitch = 45f;   // patients: consider 30–45
    public float minDeltaSqr = 0.25f; // deadzone for jitter

    [Header("Optional Constraints")]
    public bool requireLookRegion = true;
    [Range(0f, 1f)] public float lookRegionStartX01 = 0.5f; // right half by default

    float pitch;
    int lookFingerId = -1;

    void Awake()
    {
        if (pitchPivot == null) pitchPivot = transform;
    }

    void Update()
    {
        if (Touchscreen.current == null) return;

        // Acquire a finger for looking (only when a touch BEGINS).
        if (lookFingerId == -1)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (!touch.press.isPressed) continue;

                var phase = touch.phase.ReadValue();
                if (phase != UnityEngine.InputSystem.TouchPhase.Began) continue;

                int fingerId = touch.touchId.ReadValue();
                Vector2 startPos = touch.position.ReadValue();

                // Optional: only allow look swipe in right half (prevents conflicts)
                if (requireLookRegion)
                {
                    float x01 = startPos.x / Screen.width;
                    if (x01 < lookRegionStartX01) continue;
                }

                // Ignore touches that start on UI
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(fingerId))
                    continue;

                lookFingerId = fingerId;
                break;
            }
        }

        if (lookFingerId == -1) return;

        // Find the active touch that matches our locked finger id
        TouchControl lookTouch = null;
        foreach (var touch in Touchscreen.current.touches)
        {
            if (touch.touchId.ReadValue() == lookFingerId)
            {
                lookTouch = touch;
                break;
            }
        }

        // If finger lifted or disappeared, release lock
        if (lookTouch == null || !lookTouch.press.isPressed)
        {
            lookFingerId = -1;
            return;
        }

        // If user moved onto UI after starting, we still keep control (less annoying).
        // If you prefer to cancel when crossing UI, add a check here.

        Vector2 delta = lookTouch.delta.ReadValue();
        if (delta.sqrMagnitude < minDeltaSqr) return;

        // yaw (left/right)
        if (yawPivot != null)
            yawPivot.Rotate(0f, delta.x * sensitivity, 0f, Space.World);

        // pitch (up/down)
        pitch -= delta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // Call this when switching modes/cameras to avoid weird carryover
    public void ResetLook(float newPitch = 0f)
    {
        pitch = Mathf.Clamp(newPitch, -maxPitch, maxPitch);
        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        lookFingerId = -1;
    }
}
