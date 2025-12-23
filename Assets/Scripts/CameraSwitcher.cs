using UnityEngine;
using UnityEngine.InputSystem;

public class CameraSwitcher : MonoBehaviour
{
    public static bool UiMode { get; private set; }          // desktop: Alt held
    public static bool IsFirstPerson { get; private set; }   // authoritative mode flag

    [Header("Cameras")]
    public Camera firstPersonCam;
    public Camera birdEyeCam;

    [Header("3D Mode Controls")]
    public MonoBehaviour birdEyeController;   // e.g., FreeCam (assign in inspector)
    public MonoBehaviour poiTapSelector;      // optional: tap POIs / click selection in 3D

    [Header("FP Mode Controls")]
    public MonoBehaviour mobileLookDrag;      // your MobileLookDrag (or FP look script)
    public HoldToWalkUI holdToWalkUI;        // HoldToWalkUI (optional to toggle)
    public RouteFollower routeFollower;       // RouteFollower (kept enabled usually; can leave on)

    void Start()
    {
        SetFirstPerson(true);
    }

    public void ToggleCamera()
    {
        SetFirstPerson(!IsFirstPerson);
    }

    public void SetFirstPerson(bool firstPerson)
    {
        IsFirstPerson = firstPerson;

        // Cameras
        if (firstPersonCam) firstPersonCam.enabled = firstPerson;
        if (birdEyeCam)     birdEyeCam.enabled = !firstPerson;

        // Enable/disable mode-specific controllers
        if (birdEyeController) birdEyeController.enabled = !firstPerson;
        if (poiTapSelector)    poiTapSelector.enabled = !firstPerson;

        if (mobileLookDrag)    mobileLookDrag.enabled = firstPerson;
        if (holdToWalkUI)      holdToWalkUI.enabled = firstPerson;

        // RouteFollower can stay enabled; it only moves when "moving" is true.
        // But you can disable it in 3D mode if you prefer:
        // if (routeFollower) routeFollower.enabled = firstPerson;

        // Audio listeners
        ToggleAudio(firstPersonCam, firstPerson);
        ToggleAudio(birdEyeCam, !firstPerson);

        // Cursor handling (desktop only)
        ApplyCursorRules();
    }

    void Update()
    {
        // Desktop only: allow Alt to temporarily show cursor for UI interaction
        if (Keyboard.current != null)
        {
            UiMode = Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed;
            ApplyCursorRules();
        }
        else
        {
            UiMode = false; // mobile
        }
    }

    void ApplyCursorRules()
    {
        // Mobile: no cursor
        if (Mouse.current == null)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        // Desktop: in 3D mode, cursor should generally be available
        // In FP mode, lock cursor unless UiMode (Alt) is held
        if (!IsFirstPerson)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (UiMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void ToggleAudio(Camera cam, bool on)
    {
        if (!cam) return;
        var al = cam.GetComponent<AudioListener>();
        if (al != null) al.enabled = on;
    }
}
