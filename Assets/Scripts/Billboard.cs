using UnityEngine;

// Script dùng để làm cho đối tượng luôn quay mặt về phía camera chính
public class Billboard : MonoBehaviour
{
    // LateUpdate được gọi sau khi tất cả Update khác đã chạy xong
    void LateUpdate()
    {
        // Kiểm tra xem camera chính có tồn tại hay không
        if (Camera.main)
        {
            // Gán hướng phía trước của đối tượng trùng với hướng phía trước của camera
            // Điều này giúp đối tượng luôn hướng về camera
            transform.forward = Camera.main.transform.forward;
        }
    }
}
