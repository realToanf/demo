using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

[RequireComponent(typeof(NavMeshAgent))]
public class WASDMove : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float navmeshSnapRadius = 0.3f;

    [Header("Height / Pivot")]
    [Tooltip("How far the player's pivot is above the ground (feet). If pivot is at feet, set 0.")]
    [SerializeField] private float pivotToFeet = 0.9f; // start ~ characterHeight/2 if pivot is center

    [Header("Only move in First Person")]
    [SerializeField] private Camera firstPersonCam;

    [Header("Optional")]
    [SerializeField] private bool blockWhenOverUI = true;
    [SerializeField] private bool cancelClickToMoveWhenWASD = false;

    private NavMeshAgent agent;
    private ClickToMove clickToMove;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        clickToMove = GetComponent<ClickToMove>();

        // We move manually
        agent.updatePosition = false;
        agent.updateRotation = false;

        if (firstPersonCam == null) firstPersonCam = Camera.main;
    }

    void Update()
    {
        if (firstPersonCam == null || !firstPersonCam.enabled) return;

        if (blockWhenOverUI && EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject(-1))
            return;

        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0f, z = 0f;
        if (kb.aKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed) x += 1f;
        if (kb.wKey.isPressed) z += 1f;
        if (kb.sKey.isPressed) z -= 1f;

        Vector3 input = new Vector3(x, 0f, z);
        if (input.sqrMagnitude < 0.01f) return;
        if (input.sqrMagnitude > 1f) input.Normalize();

        if (cancelClickToMoveWhenWASD)
        {
            agent.ResetPath();
            if (clickToMove) clickToMove.CancelDestination();
        }

        // camera-relative XZ movement
        Vector3 forward = firstPersonCam.transform.forward; forward.y = 0f;
        Vector3 right = firstPersonCam.transform.right; right.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        if (right.sqrMagnitude < 0.0001f) right = transform.right;
        forward.Normalize();
        right.Normalize();

        Vector3 moveDir = (right * input.x + forward * input.z).normalized;
        Vector3 desiredPivot = transform.position + moveDir * moveSpeed * Time.deltaTime;

        // Convert pivot -> feet, snap feet to navmesh, then restore pivot height
        Vector3 desiredFeet = desiredPivot - Vector3.up * pivotToFeet;

        if (NavMesh.SamplePosition(desiredFeet, out NavMeshHit hit, navmeshSnapRadius, NavMesh.AllAreas))
        {
            transform.position = hit.position + Vector3.up * pivotToFeet;
        }
        else
        {
            transform.position = desiredPivot;
        }

        // keep agent synced for path drawing
        agent.nextPosition = transform.position;
    }
}
