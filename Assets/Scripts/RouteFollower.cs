using UnityEngine;

public class RouteFollower : MonoBehaviour
{
    public RouteManager route;
    public Transform player;
    public float speed = 1.2f;
    public float arriveDistance = 0.15f;
    public float turnSlerp = 10f;

    [HideInInspector] public bool moving;

    int idx = 0;

    void Awake()
    {
        if (!player) player = transform;
    }

    public void ResetProgressToNearestPoint()
    {
        if (route == null || !route.HasRoute) { idx = 0; return; }

        // Find nearest point on the polyline
        float best = float.PositiveInfinity;
        int bestIdx = 0;
        Vector3 p = player.position;

        for (int i = 0; i < route.routePoints.Count; i++)
        {
            float d = (route.routePoints[i] - p).sqrMagnitude;
            if (d < best) { best = d; bestIdx = i; }
        }

        idx = bestIdx;
        player.position = route.routePoints[idx];
    }

    void Update()
    {
        if (!moving || route == null || !route.HasRoute || player == null) return;

        if (idx >= route.routePoints.Count) { moving = false; return; }

        Vector3 target = route.routePoints[idx];
        Vector3 delta = target - player.position;

        // Arrive at current point -> advance
        if (delta.magnitude <= arriveDistance)
        {
            idx++;
            if (idx >= route.routePoints.Count) { moving = false; return; }
            target = route.routePoints[idx];
            delta = target - player.position;
        }

        // Move
        Vector3 step = delta.normalized * speed * Time.deltaTime;
        if (step.magnitude > delta.magnitude) step = delta;
        player.position += step;

        // Face along path direction (helps patients a lot)
        Vector3 flatDir = delta; flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f)
        {
            Quaternion want = Quaternion.LookRotation(flatDir);
            player.rotation = Quaternion.Slerp(player.rotation, want, turnSlerp * Time.deltaTime);
        }
    }
}
