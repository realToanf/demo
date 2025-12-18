using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(LineRenderer))]
public class ClickToMove : MonoBehaviour
{
    [SerializeField] private Camera cam;

    private NavMeshAgent agent;
    private LineRenderer line;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        line = GetComponent<LineRenderer>();

        if (cam == null)
            cam = Camera.main;
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 clickPos = Mouse.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(clickPos);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                agent.SetDestination(hit.point);
            }
        }

        DrawPath();
    }

    void DrawPath()
    {
        if (agent.path.corners.Length < 2)
        {
            line.positionCount = 0;
            return;
        }

        line.positionCount = agent.path.corners.Length;
        line.SetPositions(agent.path.corners);
    }
}
