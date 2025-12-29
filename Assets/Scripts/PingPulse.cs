using UnityEngine;

public class PingPulse : MonoBehaviour
{
    public float floatAmp = 0.12f;
    public float floatSpeed = 2f;
    public float pulseSpeed = 3f;
    public float pulseAmp = 0.12f;

    private Vector3 basePos;
    private Vector3 baseScale;

    void Start()
    {
        basePos = transform.position;
        baseScale = transform.localScale;
    }

    void LateUpdate()
    {
        float t = Time.time;

        transform.position = basePos + Vector3.up * Mathf.Sin(t * floatSpeed) * floatAmp;
        transform.localScale = baseScale * (1f + Mathf.Sin(t * pulseSpeed) * pulseAmp);
    }
}
