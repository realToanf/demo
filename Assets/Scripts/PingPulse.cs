// Script tạo hiệu ứng nhấp nhô và co giãn cho các điểm đánh dấu (ping)
using UnityEngine;

public class PingPulse : MonoBehaviour
{
    public float floatAmp = 0.12f;   // Độ cao nhấp nhô
    public float floatSpeed = 2f;    // Tốc độ nhấp nhô
    public float pulseSpeed = 3f;    // Tốc độ co giãn
    public float pulseAmp = 0.12f;   // Độ lớn co giãn

    private Vector3 basePos;
    private Vector3 baseScale;

    void Start()
    {
        // Lưu vị trí và kích thước gốc
        basePos = transform.position;
        baseScale = transform.localScale;
    }

    void LateUpdate()
    {
        float t = Time.time;

        // Hiệu ứng di chuyển lên xuống (Sin)
        transform.position = basePos + Vector3.up * Mathf.Sin(t * floatSpeed) * floatAmp;
        
        // Hiệu ứng co giãn to nhỏ (Sin)
        transform.localScale = baseScale * (1f + Mathf.Sin(t * pulseSpeed) * pulseAmp);
    }
}
