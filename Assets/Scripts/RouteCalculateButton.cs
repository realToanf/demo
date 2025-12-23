using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public class RouteCalculateButton : MonoBehaviour
{
    public enum RouteMode
    {
        PlayerToEnd,
        StartToEndPreview
    }

    [Header("UI Source")]
    [SerializeField] private RoomPOIDropdowns poiDropdowns;

    [Header("Mode UI (optional)")]
    [SerializeField] private RouteMode mode = RouteMode.PlayerToEnd;
    [SerializeField] private Toggle previewToggle; // if set: ON = StartToEndPreview, OFF = PlayerToEnd

    [Header("Player Route (uses ClickToMove)")]
    [SerializeField] private NavMeshAgent playerAgent;
    [SerializeField] private ClickToMove clickToMove;

    [Header("Preview Route (POI -> POI)")]
    [SerializeField] private LineRenderer previewLine;

    [Header("Preview Rendering")]
    [SerializeField] private float pointSpacing = 0.25f;
    [SerializeField] private float lineYOffset = 0.05f;
    [SerializeField] private LayerMask walkableMask = ~0;
    [SerializeField] private float projectRayStart = 1.0f;
    [SerializeField] private float projectRayLength = 3.0f;

    [Header("NavMesh")]
    [SerializeField] private float snapRadius = 1.0f;

    private readonly NavMeshPath tmpPath = new NavMeshPath();

    void Awake()
    {
        if (!playerAgent) playerAgent = FindObjectOfType<NavMeshAgent>();
        if (!clickToMove && playerAgent) clickToMove = playerAgent.GetComponent<ClickToMove>();

        if (previewToggle)
            previewToggle.onValueChanged.AddListener(on => mode = on ? RouteMode.StartToEndPreview : RouteMode.PlayerToEnd);
    }

    public void Calculate()
    {
        if (!poiDropdowns) return;

        Transform startT = poiDropdowns.GetStart();
        Transform endT   = poiDropdowns.GetEnd();
        if (!endT) return;

        // allow "start" to be null if you're doing PlayerToEnd
        Vector3 startPos = startT ? SnapToNavMesh(startT.position) : Vector3.zero;
        Vector3 endPos   = SnapToNavMesh(endT.position);

        // choose mode (toggle overrides enum if present)
        RouteMode useMode = previewToggle ? (previewToggle.isOn ? RouteMode.StartToEndPreview : RouteMode.PlayerToEnd) : mode;

        if (useMode == RouteMode.PlayerToEnd)
        {
            // clear preview line if present
            if (previewLine) previewLine.positionCount = 0;

            if (!clickToMove) return;
            clickToMove.ForceDestination(endPos); // draws from player to end (and updates live while WASD)
            return;
        }

        // Start -> End preview
        if (!startT || !previewLine) return;

        bool ok = NavMesh.CalculatePath(startPos, endPos, NavMesh.AllAreas, tmpPath);
        if (!ok || tmpPath.corners == null || tmpPath.corners.Length < 2)
        {
            previewLine.positionCount = 0;
            return;
        }

        DrawProjectedSmoothed(previewLine, tmpPath.corners);
    }

    private Vector3 SnapToNavMesh(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, snapRadius, NavMesh.AllAreas))
            return hit.position;
        return p;
    }

    private void DrawProjectedSmoothed(LineRenderer line, Vector3[] corners)
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
}
