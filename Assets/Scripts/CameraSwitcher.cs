using UnityEngine;
using UnityEngine.InputSystem;

public class CameraSwitcher : MonoBehaviour
{
    public static bool UiMode { get; private set; }
    public Camera firstPersonCam;
    public Camera birdEyeCam;

    FreeCam birdEyeFreeCam;

    void Awake()
    {
        birdEyeFreeCam = birdEyeCam.GetComponentInParent<FreeCam>();
    }

    void Start()
    {
        SetFirstPerson(true);
    }

    public void ToggleCamera()
    {
        bool toFirst = !firstPersonCam.enabled;
        SetFirstPerson(toFirst);
    }

    private void SetFirstPerson(bool firstPerson)
    {
        firstPersonCam.enabled = firstPerson;
        birdEyeCam.enabled = !firstPerson;

        if (birdEyeFreeCam != null)
        {
            if (firstPerson)
            {
                birdEyeFreeCam.enabled = false;
                birdEyeFreeCam.ResetToOrigin();
            }
            else
            {
                birdEyeFreeCam.ResetToOrigin();
                birdEyeFreeCam.enabled = true;
            }
        }

        // Set the default cursor state for this mode
        SetCursorForMode(firstPerson);

        ToggleAudio(firstPersonCam, firstPerson);
        ToggleAudio(birdEyeCam, !firstPerson);
    }

    private void ToggleAudio(Camera cam, bool on)
    {
        var al = cam.GetComponent<AudioListener>();
        if (al != null) al.enabled = on;
    }

    void SetCursorForMode(bool firstPerson)
    {
        // Default behavior: lock in both modes (Alt will temporarily unlock)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        UiMode = Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed;

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
}
