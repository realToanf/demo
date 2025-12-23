using System.Collections.Generic;
using UnityEngine;

public class RouteManager : MonoBehaviour
{
    [Header("Route Rendering")]
    public LineRenderer line;
    public float pointSpacing = 0.25f;
    public float lineYOffset = 0.05f;
    public LayerMask walkableMask = ~0;
    public float projectRayStart = 1.0f;
    public float projectRayLength = 3.0f;

    // The “truth” for FP walking
    public readonly List<Vector3> routePoints = new List<Vector3>(512);

    public bool HasRoute => routePoints.Count >= 2;

    public void ClearRoute()
    {
        routePoints.Clear();
        if (line) line.positionCount = 0;
    }

    public void SetRouteFromCorners(Vector3[] corners)
    {
        if (corners == null || corners.Length < 2)
        {
            ClearRoute();
            return;
        }

        routePoints.Clear();

        // Build a smoothed + projected polyline (same logic you already use)
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
                routePoints.Add(p);
            }
        }

        if (line)
        {
            line.positionCount = routePoints.Count;
            line.SetPositions(routePoints.ToArray());
        }
    }
}
