using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class WASDMove : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float navmeshSnapRadius = 0.2f;

    [Header("Only move in First Person")]
    [SerializeField] private Camera firstPersonCam;

    [Header("Optional")]
    [SerializeField] private bool blockWhenOverUI = true;

    void Awake()
    {
        // Best: assign in Inspector. This is just a fallback.
        if (firstPersonCam == null)
            firstPersonCam = Camera.main;
    }

    void Update()
    {
        if (firstPersonCam == null || !firstPersonCam.enabled)
            return;

        if (blockWhenOverUI && EventSystem.current != null)
        {
            if (EventSystem.current.IsPointerOverGameObject(-1))
                return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0f;
        float z = 0f;

        if (kb.aKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed) x += 1f;
        if (kb.wKey.isPressed) z += 1f;
        if (kb.sKey.isPressed) z -= 1f;

        Vector3 input = new Vector3(x, 0f, z);
        if (input.sqrMagnitude < 0.01f) return;

        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 forward = firstPersonCam.transform.forward;
        Vector3 right = firstPersonCam.transform.right;
        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        if (right.sqrMagnitude < 0.0001f) right = transform.right;

        forward.Normalize();
        right.Normalize();

        Vector3 moveDir = (right * input.x + forward * input.z).normalized;
        Vector3 desired = transform.position + moveDir * moveSpeed * Time.deltaTime;

        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, navmeshSnapRadius, NavMesh.AllAreas))
            transform.position = hit.position;
        else
            transform.position = desired;
    }
}
