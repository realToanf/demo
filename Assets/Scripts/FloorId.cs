using UnityEngine;

public class FloorId : MonoBehaviour
{
    [Tooltip("Stable floor id. Example: -1=Basement, 0=T1, 1=T2 ...")]
    public int id;

    [Tooltip("Optional display name for UI. If empty, uses GameObject name.")]
    public string displayName;
}