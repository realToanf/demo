using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(LineRenderer))]
public class ClickToMove : MonoBehaviour
{
    public enum Mode { Overview3D, FirstPerson }
    [Header("Mode")]
    public Mode mode = Mode.Overview3D;

    [Header("Input")]
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask raycastMask = ~0;      // what your screen ray can hit
    [SerializeField] private LayerMask poiMask = 0;           // set this if you want POI-only selection
    [SerializeField] private bool selectPOIOnly = true;

    [Header("Path Rendering")]
    [SerializeField] private float pointSpacing = 0.25f;
    [SerializeField] private float lineYOffset = 0.05f;
    [SerializeField] private LayerMask walkableMask = ~0;
    [SerializeField] private float projectRayStart = 1.0f;
    [SerializeField] private float projectRayLength = 3.0f;

    [Header("Repath")]
    [SerializeField] private float repathInterval = 0.1f;
    [SerializeField] private float repathMoveThreshold = 0.15f;
    [SerializeField] private float destinationChangeThreshold = 0.05f;
    [SerializeField] private float stopDrawDistance = 0.25f;

    private NavMeshAgent agent;
    private LineRenderer line;

    private float repathTimer;
    private Vector3 lastRepathPos;
    private Vector3 lastDestination;

    private NavMeshPath cachedPath;
    private bool hasDestination;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        line = GetComponent<LineRenderer>();

        cachedPath = new NavMeshPath();
        lastRepathPos = transform.position;
        lastDestination = new Vector3(float.PositiveInfinity, 0f, 0f);

        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (mode == Mode.Overview3D)
        {
            HandleClickOrTap_SetDestination();
        }

        UpdateAndDrawPath();
    }

    private void HandleClickOrTap_SetDestination()
    {
        // Mouse (desktop)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            TrySetDestination(Mouse.current.position.ReadValue());
        }

        // Touch (mobile)
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (!touch.press.wasPressedThisFrame) continue;

                int fingerId = touch.touchId.ReadValue();
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(fingerId))
                    continue;

                TrySetDestination(touch.position.ReadValue());
                break; // just use first valid touch
            }
        }
    }

    private void TrySetDestination(Vector2 screenPos)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, raycastMask))
            return;

        Vector3 dest;

        if (selectPOIOnly)
        {
            // Require a POI hit
            if (((1 << hit.collider.gameObject.layer) & poiMask.value) == 0)
                return;

            // Use POI transform position (or a marker)
            dest = hit.collider.transform.position;
        }
        else
        {
            // Use ground hit point
            dest = hit.point;
        }

        // Snap to NavMesh (prevents “invalid destination” issues)
        if (NavMesh.SamplePosition(dest, out NavMeshHit navHit, 1.0f, NavMesh.AllAreas))
        {
            agent.SetDestination(navHit.position);
            hasDestination = true;
        }
    }

    private void UpdateAndDrawPath()
    {
        if (!hasDestination)
        {
            ClearLine();
            return;
        }

        if (Vector3.Distance(transform.position, agent.destination) <= stopDrawDistance)
        {
            ClearLine();
            return;
        }

        repathTimer -= Time.deltaTime;

        bool movedEnough = Vector3.Distance(transform.position, lastRepathPos) > repathMoveThreshold;
        bool destChanged = Vector3.Distance(agent.destination, lastDestination) > destinationChangeThreshold;
        bool pathInvalid = cachedPath == null || cachedPath.corners == null || cachedPath.corners.Length < 2;

        if ((repathTimer <= 0f && (movedEnough || destChanged)) || pathInvalid)
        {
            repathTimer = repathInterval;
            lastRepathPos = transform.position;
            lastDestination = agent.destination;

            NavMesh.CalculatePath(transform.position, agent.destination, NavMesh.AllAreas, cachedPath);
        }

        if (cachedPath == null || cachedPath.corners == null || cachedPath.corners.Length < 2)
        {
            ClearLine();
            return;
        }

        DrawProjectedSmoothed(cachedPath.corners);
    }

    private void DrawProjectedSmoothed(Vector3[] corners)
    {
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

                Vector3 rayStart = p + Vector3.up * projectRayStart;
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, projectRayLength, walkableMask))
                    p = hit.point;

                p.y += lineYOffset;
                pts.Add(p);
            }
        }

        line.positionCount = pts.Count;
        line.SetPositions(pts.ToArray());
    }

    private void ClearLine() => line.positionCount = 0;

    public void CancelDestination()
    {
        hasDestination = false;
        agent.ResetPath();
        ClearLine();
    }

    public void ForceDestination(Vector3 dest)
    {
        if (NavMesh.SamplePosition(dest, out NavMeshHit navHit, 1.0f, NavMesh.AllAreas))
        {
            agent.SetDestination(navHit.position);
            hasDestination = true;
        }
    }
}
