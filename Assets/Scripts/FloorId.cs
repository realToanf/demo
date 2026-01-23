// Gán ID ổn định và tên hiển thị cho tầng.
using UnityEngine;

public class FloorId : MonoBehaviour
{
    [Tooltip("ID cố định của tầng. Ví dụ: -1=Hầm, 0=Tầng 1, 1=Tầng 2...")]
    public int id;

    [Tooltip("Tên hiển thị tùy chọn trên giao diện. Nếu để trống sẽ dùng tên của GameObject.")]
    public string displayName;
}
