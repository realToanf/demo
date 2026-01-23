// Đánh dấu điểm dừng thang máy thuộc tầng nào.
using UnityEngine;

public class ElevatorStopFloor : MonoBehaviour
{
    [Tooltip("ID của tầng mà điểm dừng thang máy này thuộc về. Phải khớp với FloorId.id.")]
    public int floorId;
}
