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

    [Header("Teleport (Preview Mode)")]
    [SerializeField] private bool teleportPlayerToStartInPreview = true;
    [SerializeField] private bool faceAlongRouteAfterTeleport = true;

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
        if (!poiDropdowns || !routeManager)
            return;

        Transform startT = poiDropdowns.GetStart();
        Transform endT   = poiDropdowns.GetEnd();

        if (!endT)
        {
            routeManager.ClearRoute();
            return;
        }

        Vector3 endPos = SnapToNavMesh(endT.position);

        // --- TELEPORT ALWAYS ---
        // If start exists, teleport to start. Otherwise teleport to end.
        Vector3 teleportPos = startT ? SnapToNavMesh(startT.position) : endPos;

        if (playerTransform)
            TeleportPlayer(playerTransform, teleportPos);

        // Now compute route from the player's REAL position (after teleport)
        Vector3 startPos = SnapToNavMesh(playerTransform ? playerTransform.position : teleportPos);

        bool ok = NavMesh.CalculatePath(startPos, endPos, areaMask, tmpPath);

        if (!ok || tmpPath.corners == null || tmpPath.corners.Length < 2)
        {
            routeManager.ClearRoute();
            return;
        }

        // Face along route so FP starts correctly
        FaceAlongFirstSegment(playerTransform, tmpPath.corners);

        // Store + draw the route
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

    /// <summary>
    /// Best-practice teleport: prefer NavMeshAgent.Warp(), else disable and move.
    /// </summary>
    private void TeleportPlayer(Transform player, Vector3 worldPos)
    {
        if (!player) return;

        var cc = player.GetComponent<CharacterController>() ?? player.GetComponentInChildren<CharacterController>();
        var agent = player.GetComponent<NavMeshAgent>() ?? player.GetComponentInChildren<NavMeshAgent>();

        if (cc) cc.enabled = false;

        if (agent && agent.enabled)
        {
            agent.Warp(worldPos);
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }
        else
        {
            player.position = worldPos;
        }

        if (cc) cc.enabled = true;
    }


    private void FaceAlongFirstSegment(Transform player, Vector3[] corners)
    {
        if (!player || corners == null || corners.Length < 2) return;

        Vector3 dir = corners[1] - corners[0];
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) return;

        player.rotation = Quaternion.LookRotation(dir.normalized);
    }
}
