using UnityEngine;
using UnityEngine.AI;

public class RouteCalculateButton : MonoBehaviour
{
    [Header("UI Source")]
    [SerializeField] private RoomPOIDropdowns poiDropdowns;

    [Header("Player")]
    [SerializeField] private Transform playerRoot;     // the object that moves
    [SerializeField] private NavMeshAgent agent;       // player's agent
    [SerializeField] private ClickToMove clickToMove;  // draws the line

    [Header("Behavior")]
    [SerializeField] private bool teleportToStart = false; // set true for POI-to-POI demo like a map
    [SerializeField] private float snapRadius = 1.0f;       // snap POIs to navmesh

    void Awake()
    {
        // optional auto-find (still recommend assigning in Inspector)
        if (!agent) agent = FindObjectOfType<NavMeshAgent>();
        if (!playerRoot && agent) playerRoot = agent.transform;
        if (!clickToMove && agent) clickToMove = agent.GetComponent<ClickToMove>();
    }

    public void Calculate()
    {
        if (!poiDropdowns || !agent || !clickToMove) return;

        Transform start = poiDropdowns.GetStart();
        Transform end   = poiDropdowns.GetEnd();
        if (!start || !end) return;

        Vector3 startPos = SnapToNavMesh(start.position);
        Vector3 endPos   = SnapToNavMesh(end.position);

        if (teleportToStart && playerRoot)
        {
            agent.Warp(startPos);
            playerRoot.position = startPos;
            agent.nextPosition = startPos;
        }

        clickToMove.ForceDestination(endPos);
    }

    private Vector3 SnapToNavMesh(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, snapRadius, NavMesh.AllAreas))
            return hit.position;
        return p;
    }
}
