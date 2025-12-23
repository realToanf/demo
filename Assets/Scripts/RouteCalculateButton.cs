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
    [SerializeField] private Toggle previewToggle; // ON = StartToEndPreview, OFF = PlayerToEnd

    [Header("Start Sources")]
    [SerializeField] private Transform playerTransform; // used for PlayerToEnd mode

    [Header("Route Output")]
    [SerializeField] private RouteManager routeManager; // draws + stores the route used by Hold-to-Walk

    [Header("NavMesh")]
    [SerializeField] private float snapRadius = 1.0f;
    [SerializeField] private int areaMask = NavMesh.AllAreas;

    private NavMeshPath tmpPath;

    void Awake()
    {
        tmpPath = new NavMeshPath();

        if (!playerTransform)
        {
            var agent = FindFirstObjectByType<NavMeshAgent>();
            if (agent) playerTransform = agent.transform;
        }

        if (previewToggle)
            previewToggle.onValueChanged.AddListener(on =>
                mode = on ? RouteMode.StartToEndPreview : RouteMode.PlayerToEnd
            );
    }

    public void Calculate()
    {
        if (!poiDropdowns || !routeManager) return;

        Transform startT = poiDropdowns.GetStart();
        Transform endT   = poiDropdowns.GetEnd();
        if (!endT) { routeManager.ClearRoute(); return; }

        // choose mode (toggle overrides enum if present)
        RouteMode useMode = previewToggle
            ? (previewToggle.isOn ? RouteMode.StartToEndPreview : RouteMode.PlayerToEnd)
            : mode;

        Vector3 endPos = SnapToNavMesh(endT.position);

        Vector3 startPos;
        if (useMode == RouteMode.PlayerToEnd)
        {
            if (!playerTransform) { routeManager.ClearRoute(); return; }
            startPos = SnapToNavMesh(playerTransform.position);
        }
        else
        {
            if (!startT) { routeManager.ClearRoute(); return; }
            startPos = SnapToNavMesh(startT.position);
        }

        bool ok = NavMesh.CalculatePath(startPos, endPos, areaMask, tmpPath);

        if (!ok || tmpPath.corners == null || tmpPath.corners.Length < 2)
        {
            routeManager.ClearRoute();
            return;
        }

        // Store + draw the route (this is what FP Hold-to-Walk will follow)
        routeManager.SetRouteFromCorners(tmpPath.corners);
    }

    public void Clear()
    {
        if (routeManager) routeManager.ClearRoute();
    }

    private Vector3 SnapToNavMesh(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, snapRadius, areaMask))
            return hit.position;
        return p;
    }
}
