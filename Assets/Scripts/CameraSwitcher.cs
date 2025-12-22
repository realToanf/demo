using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    public Camera firstPersonCam;
    public Camera birdEyeCam;

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

        // Prevent multiple AudioListeners
        ToggleAudio(firstPersonCam, firstPerson);
        ToggleAudio(birdEyeCam, !firstPerson);
    }

    private void ToggleAudio(Camera cam, bool on)
    {
        var al = cam.GetComponent<AudioListener>();
        if (al != null) al.enabled = on;
    }
}
