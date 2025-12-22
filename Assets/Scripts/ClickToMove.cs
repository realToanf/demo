using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(LineRenderer))]
public class ClickToMove : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private Camera cam;

    [Header("Path Rendering")]
    [SerializeField] private float pointSpacing = 0.25f;      // meters between points (smaller = smoother)
    [SerializeField] private float lineYOffset = 0.05f;       // lift line above surface
    [SerializeField] private LayerMask walkableMask = ~0;     // set to your Floor/Stairs layer to avoid snapping to upper floor
    [SerializeField] private float projectRayStart = 1.0f;    // start ray this high above point
    [SerializeField] private float projectRayLength = 3.0f;   // ray length downward

    private NavMeshAgent agent;
    private LineRenderer line;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        line = GetComponent<LineRenderer>();

        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        HandleClick();
        HandleTap();
        DrawPathProjected();
    }

    private void HandleClick()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 clickPos = Mouse.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(clickPos);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                agent.SetDestination(hit.point);
            }
        }
    }

    private void HandleTap()
    {
        if (Touchscreen.current == null) return;

        if (Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 touchPos = Touchscreen.current.primaryTouch.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(touchPos);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                agent.SetDestination(hit.point);
            }
        }
    }

    private void DrawPathProjected()
    {
        if (!agent.hasPath || agent.path == null || agent.path.corners == null || agent.path.corners.Length < 2)
        {
            line.positionCount = 0;
            return;
        }

        Vector3[] corners = agent.path.corners;

        // Build a denser point list so the line looks smooth
        List<Vector3> pts = new List<Vector3>(256);

        for (int i = 0; i < corners.Length - 1; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[i + 1];

            float dist = Vector3.Distance(a, b);
            int n = Mathf.Max(2, Mathf.CeilToInt(dist / pointSpacing) + 1);

            for (int k = 0; k < n; k++)
            {
                float t = k / (float)(n - 1);
                Vector3 p = Vector3.Lerp(a, b, t);

                // Project point down onto WALKABLE surfaces only (prevents line cutting through upper floor)
                Vector3 rayStart = p + Vector3.up * projectRayStart;
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, projectRayLength, walkableMask))
                {
                    p = hit.point;
                }

                p.y += lineYOffset;
                pts.Add(p);
            }
        }

        line.positionCount = pts.Count;
        line.SetPositions(pts.ToArray());
    }
}
