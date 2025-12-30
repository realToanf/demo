using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class NavigationController : MonoBehaviour
{
    [Header("List")]
    public Transform floorsRoot;

    [Header("Path")]
    public LineRenderer line;
    public Material lineMaterial;

    [Header("Waypoint Labels")]
    public TMP_FontAsset labelFont;
    public float labelHeight = 1f;
    public float labelSize = 8f;

    [Header("Camera")]
    public Transform mainCamera;

    [Header("Navigation Visuals")]
    public GameObject startPingPrefab;
    public GameObject endPingPrefab;

    [Header("Line Style")]
    public float lineWidth = 0.25f;
    public int lineCornerVertices = 8;
    public int lineCapVertices = 8;
    public Gradient lineGradient;
    public AnimationCurve widthCurve = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Gradual Draw")]
    public float revealSpeed = 12f; // meters per second
    public float lineHeightOffset = 0.05f; // reduce z-fighting

    [Header("Optional: Texture Scroll")]
    public bool enableTextureScroll = false;
    public float textureScrollSpeed = 1f;
    private float textureOffset = 0f;

    [Header("Navigation Transparency (DISABLED in cross-floor mode)")]
    public bool makeFloorsTransparentInNav = false; // keep false to avoid "transparent soup"
    [Range(0.05f, 1f)] public float navAlpha = 0.35f;
    public Material navTransparentMaterial;

    // cache renderer -> original materials (restore later)
    private readonly Dictionary<Renderer, Material[]> originalMats = new();

    // ----- Public state (for UI Toolkit) -----
    public IReadOnlyList<string> FloorNames => floorDropdownOptions;
    public IReadOnlyList<string> ActivePointNames => allPointNames;

    public int ActiveFloorIndex { get; private set; } = 0;

    // -1 means "not selected"
    public int SelectedFrom { get; private set; } = -1;
    public int SelectedTo { get; private set; } = -1;

    // stays true after camera finishes, until user cancels
    public bool IsRouteActive { get; private set; } = false;

    public event Action FloorsChanged;
    public event Action ActiveFloorChanged;
    public event Action SelectionChanged;
    public event Action<bool> NavigationStateChanged;

    // Floors
    private readonly List<Transform> floors = new();
    private readonly List<string> floorNames = new();

    // Per-floor points + labels
    private readonly Dictionary<int, List<Transform>> floorPoints = new();
    private readonly Dictionary<int, List<GameObject>> floorLabels = new();

    // All points across all floors (ONLY real points)
    private readonly List<Transform> allPoints = new();
    private readonly List<string> allPointNames = new();
    private readonly List<int> allPointFloorIndex = new(); // point -> floor index

    private NavMeshPath path;

    // Visible floors
    public IReadOnlyCollection<int> VisibleFloors => visibleFloors;
    private readonly HashSet<int> visibleFloors = new();

    public bool ShowAllFloors { get; private set; } = false;

    public IReadOnlyList<string> FloorDropdownOptions => floorDropdownOptions;
    private readonly List<string> floorDropdownOptions = new();
    public int SelectedFloorDropdownIndex { get; private set; } = 1;

    // Floor ranges for auto switching during camera movement
    private struct FloorRange
    {
        public Transform floor;
        public float minY;
        public float maxY;
    }
    private readonly List<FloorRange> floorRanges = new();

    // Navigation state
    private GameObject startPingInstance;
    private GameObject endPingInstance;

    private Vector3[] navCorners;
    private float revealDistance = 0f;
    private float totalDistance = 0f;
    private bool isNavigating = false;

    // Track nav floors so cross-floor keeps BOTH visible
    private int navFromFloor = -1;
    private int navToFloor = -1;
    private bool navIsCrossFloor = false;

    public Transform visualsRoot;

    // =========================================================
    // STARTUP
    // =========================================================
    IEnumerator Start()
    {
        yield return null;

        AutoAssignIfNull();

        if (floorsRoot == null)
        {
            Debug.LogError("NavigationController: floorsRoot is NULL (Environment not found).");
            yield break;
        }

        if (mainCamera == null)
        {
            Debug.LogError("NavigationController: mainCamera is NULL.");
            yield break;
        }

        if (line == null)
        {
            Debug.LogError("NavigationController: LineRenderer is NULL.");
            yield break;
        }

        path = new NavMeshPath();

        if (lineMaterial != null)
            line.material = lineMaterial;

        if (line.material == null)
        {
            Debug.LogError("NavigationController: LineRenderer has no material. Assign a URP Unlit material.");
            yield break;
        }

        line.useWorldSpace = true;
        line.sortingOrder = 999;
        line.positionCount = 0;

        LoadFloorsAndWaypoints();
        BuildFloorDropdownOptions();
        BuildFloorRanges();
        SetupLineStyle();

        FloorsChanged?.Invoke();

        SetFloor(0);

        ClampSelections();

        ActiveFloorChanged?.Invoke();
        PreviewPath();
    }

    void Update()
    {
        if (!enableTextureScroll) return;
        if (!isNavigating) return;
        if (line == null || line.material == null) return;

        textureOffset += textureScrollSpeed * Time.deltaTime;
        line.material.mainTextureOffset = new Vector2(textureOffset, 0f);
    }

    void AutoAssignIfNull()
    {
        if (floorsRoot == null)
        {
            var env = GameObject.Find("Environment");
            if (env != null) floorsRoot = env.transform;
        }

        if (mainCamera == null && Camera.main != null)
            mainCamera = Camera.main.transform;
    }

    void BuildFloorDropdownOptions()
    {
        floorDropdownOptions.Clear();
        floorDropdownOptions.Add("Tất cả các tầng");
        floorDropdownOptions.AddRange(floorNames);
    }

    // =========================================================
    // LOAD DATA
    // =========================================================
    void LoadFloorsAndWaypoints()
    {
        floors.Clear();
        floorPoints.Clear();
        floorLabels.Clear();
        floorNames.Clear();

        // only real points
        allPoints.Clear();
        allPointNames.Clear();
        allPointFloorIndex.Clear();

        SelectedFrom = -1;
        SelectedTo = -1;

        for (int i = 0; i < floorsRoot.childCount; i++)
        {
            Transform floor = floorsRoot.GetChild(i);
            floors.Add(floor);
            floorNames.Add(floor.name);

            var points = new List<Transform>();
            var labels = new List<GameObject>();

            Transform waypoints = floor.Find("Waypoints");
            if (waypoints != null)
            {
                var children = waypoints.GetComponentsInChildren<Transform>(true);

                foreach (var t in children)
                {
                    if (t == waypoints) continue;

                    points.Add(t);
                    labels.Add(CreateLabel(t));

                    allPoints.Add(t);
                    allPointNames.Add($"{floor.name} - {t.name}");
                    allPointFloorIndex.Add(i);
                }
            }

            floorPoints[i] = points;
            floorLabels[i] = labels;
        }
    }

    // =========================================================
    // FLOOR VISIBILITY
    // =========================================================
    void ApplyVisibleFloors()
    {
        for (int i = 0; i < floors.Count; i++)
            floors[i].gameObject.SetActive(visibleFloors.Contains(i));

        UpdateLabelsVisibility();
    }

    void UpdateLabelsVisibility()
    {
        foreach (var kv in floorLabels)
            foreach (var label in kv.Value)
                if (label != null) label.SetActive(false);

        foreach (var idx in visibleFloors)
        {
            if (!floorLabels.ContainsKey(idx)) continue;
            foreach (var label in floorLabels[idx])
                if (label != null) label.SetActive(true);
        }
    }

    public void SetFloor(int index)
    {
        if (ShowAllFloors) return;
        if (floors.Count == 0) return;

        index = Mathf.Clamp(index, 0, floors.Count - 1);
        if (ActiveFloorIndex == index && visibleFloors.Contains(index)) return;

        ActiveFloorIndex = index;

        visibleFloors.Clear();
        visibleFloors.Add(index);

        ApplyVisibleFloors();

        ActiveFloorChanged?.Invoke();
        PreviewPath();
    }

    public void SetFloorFromDropdown(int dropdownIndex)
    {
        dropdownIndex = Mathf.Clamp(dropdownIndex, 0, floorDropdownOptions.Count - 1);
        SelectedFloorDropdownIndex = dropdownIndex;

        if (dropdownIndex == 0)
        {
            SetShowAllFloors(true);
        }
        else
        {
            SetShowAllFloors(false);
            SetFloor(dropdownIndex - 1);
        }
    }

    public void SetFloorVisible(int index, bool visible)
    {
        if (visible) SetFloor(index);
    }

    // =========================================================
    // SELECTION (FROM/TO)
    // =========================================================
    void ClampSelections()
    {
        if (allPoints.Count == 0) return;

        if (SelectedFrom >= 0)
            SelectedFrom = Mathf.Clamp(SelectedFrom, 0, allPoints.Count - 1);

        if (SelectedTo >= 0)
            SelectedTo = Mathf.Clamp(SelectedTo, 0, allPoints.Count - 1);
    }

    public void SetFrom(int index)
    {
        if (allPoints.Count == 0) return;

        CancelNavigation(false);
        ApplyNavTransparency(false);

        SelectedFrom = Mathf.Clamp(index, 0, allPoints.Count - 1);

        if (!ShowAllFloors)
        {
            int floorIdx = allPointFloorIndex[SelectedFrom];
            if (floorIdx >= 0)
                SetFloor(floorIdx);
        }

        SelectionChanged?.Invoke();
        PreviewPath();
    }

    public void SetTo(int index)
    {
        if (allPoints.Count == 0) return;

        CancelNavigation(false);
        ApplyNavTransparency(false);

        SelectedTo = Mathf.Clamp(index, 0, allPoints.Count - 1);

        if (!ShowAllFloors)
        {
            int floorIdx = allPointFloorIndex[SelectedTo];
            if (floorIdx >= 0)
                SetFloor(floorIdx);
        }

        SelectionChanged?.Invoke();
        PreviewPath();
    }

    // =========================================================
    // PATH PREVIEW + NAVIGATION
    // =========================================================
    public void PreviewPath()
    {
        if (allPoints.Count == 0) return;
        if (isNavigating) return;

        if (SelectedFrom < 0 || SelectedTo < 0)
        {
            ClearPath();
            ClearPings();
            return;
        }

        if (SelectedFrom == SelectedTo)
        {
            ClearPath();
            ClearPings();
            return;
        }

        Vector3 start = allPoints[SelectedFrom].position;
        Vector3 end = allPoints[SelectedTo].position;

        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            line.positionCount = path.corners.Length;
            for (int i = 0; i < path.corners.Length; i++)
                line.SetPosition(i, path.corners[i] + Vector3.up * lineHeightOffset);

            PreviewPings();
        }
        else
        {
            ClearPath();
            ClearPings();
        }
    }

    public void StartNavigation()
    {
        if (isNavigating)
        {
            CancelNavigation(true);
            return;
        }

        if (allPoints.Count == 0) return;
        if (SelectedFrom < 0 || SelectedTo < 0) return;
        if (SelectedFrom == SelectedTo) return;

        // route becomes active and stays active after camera finishes
        IsRouteActive = true;
        NavigationStateChanged?.Invoke(true);

        navFromFloor = allPointFloorIndex[SelectedFrom];
        navToFloor = allPointFloorIndex[SelectedTo];
        navIsCrossFloor = IsCrossFloorRoute() || ShowAllFloors;

        ExitAllFloorsAndFocusFloor(navFromFloor);

        ShowAllFloors = false;
        ExitAllFloorsAndFocusFloor(navFromFloor);

        SpawnPings();

        Vector3 start = allPoints[SelectedFrom].position;
        Vector3 end = allPoints[SelectedTo].position;

        if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            Debug.LogError("NavigationController: NavMesh path failed");
            ClearPath();
            return;
        }

        navCorners = path.corners;
        totalDistance = ComputeTotalDistance(navCorners);
        revealDistance = 0f;

        isNavigating = true;

        DrawLinePoints(GetPartialPath(navCorners, revealDistance));

        var camController = mainCamera.GetComponent<CameraController>();
        if (camController == null)
        {
            Debug.LogError("NavigationController: CameraController not found.");
            CancelNavigation(true);
            return;
        }

        camController.SnapToBirdEye(allPoints[SelectedFrom], 55f, 75f);
        camController.MoveBirdEyeFromTo(
            allPoints[SelectedFrom],
            allPoints[SelectedTo],
            () =>
            {
                if (!isNavigating) return;

                revealDistance = totalDistance;
                DrawLinePoints(GetPartialPath(navCorners, revealDistance));

                isNavigating = false;

                // ensure both floors stay visible at end for cross-floor routes
                if (navIsCrossFloor && navFromFloor >= 0 && navToFloor >= 0)
                {
                    visibleFloors.Clear();
                    visibleFloors.Add(navFromFloor);
                    visibleFloors.Add(navToFloor);
                    ApplyVisibleFloors();
                }
            },
            pos =>
            {
                if (!isNavigating) return;

                ActivateFloorByY(pos.y);

                float distAlong = GetDistanceAlongPath(navCorners, pos);
                revealDistance = Mathf.Clamp(distAlong, 0f, totalDistance);

                DrawLinePoints(GetPartialPath(navCorners, revealDistance));
            }
        );
    }

    public void CancelNavigation(bool restorePreview = true)
    {
        var camController = mainCamera != null ? mainCamera.GetComponent<CameraController>() : null;
        if (camController != null)
            camController.CancelMove();

        isNavigating = false;
        navCorners = null;
        revealDistance = 0f;
        totalDistance = 0f;

        ClearPath();

        // We are not using transparency anymore, but keep restore for safety.
        ApplyNavTransparency(false);

        navFromFloor = -1;
        navToFloor = -1;
        navIsCrossFloor = false;

        IsRouteActive = false;
        NavigationStateChanged?.Invoke(false);

        if (restorePreview)
            PreviewPath();
    }

    public void CancelAndResetToPlaceholder()
    {
        CancelNavigation(false);

        SelectedFrom = -1;
        SelectedTo = -1;

        SelectionChanged?.Invoke();
        PreviewPath();
    }

    // =========================================================
    // DISTANCE HELPERS
    // =========================================================
    float GetDistanceAlongPath(Vector3[] corners, Vector3 worldPos)
    {
        if (corners == null || corners.Length < 2) return 0f;

        float bestDistanceAlong = 0f;
        float bestSqr = float.MaxValue;
        float cumulative = 0f;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 a = corners[i - 1];
            Vector3 b = corners[i];

            Vector3 ab = b - a;
            float abLen = ab.magnitude;
            if (abLen < 0.0001f) continue;

            Vector3 dir = ab / abLen;

            float t = Vector3.Dot(worldPos - a, dir);
            t = Mathf.Clamp(t, 0f, abLen);

            Vector3 proj = a + dir * t;

            float sqr = (worldPos - proj).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestDistanceAlong = cumulative + t;
            }

            cumulative += abLen;
        }

        return bestDistanceAlong;
    }

    void ClearPath()
    {
        if (line != null) line.positionCount = 0;
        ClearPings();
    }

    float ComputeTotalDistance(Vector3[] pts)
    {
        if (pts == null || pts.Length < 2) return 0f;

        float d = 0f;
        for (int i = 1; i < pts.Length; i++)
            d += Vector3.Distance(pts[i - 1], pts[i]);

        return d;
    }

    List<Vector3> GetPartialPath(Vector3[] pts, float distance)
    {
        var result = new List<Vector3>();
        if (pts == null || pts.Length == 0) return result;

        result.Add(pts[0]);
        float traveled = 0f;

        for (int i = 1; i < pts.Length; i++)
        {
            float seg = Vector3.Distance(pts[i - 1], pts[i]);

            if (traveled + seg <= distance)
            {
                result.Add(pts[i]);
                traveled += seg;
            }
            else
            {
                float remain = distance - traveled;
                float t = (seg <= 0.0001f) ? 1f : Mathf.Clamp01(remain / seg);
                Vector3 p = Vector3.Lerp(pts[i - 1], pts[i], t);
                result.Add(p);
                break;
            }
        }

        return result;
    }

    void DrawLinePoints(List<Vector3> pts)
    {
        if (line == null)
            return;

        if (pts == null || pts.Count == 0)
        {
            ClearPath();
            return;
        }

        if (pts.Count == 1)
        {
            Vector3 p = pts[0] + Vector3.up * lineHeightOffset;
            line.positionCount = 2;
            line.SetPosition(0, p);
            line.SetPosition(1, p + Vector3.up * 0.05f);
            return;
        }

        line.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++)
            line.SetPosition(i, pts[i] + Vector3.up * lineHeightOffset);
    }

    void SetupLineStyle()
    {
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.widthCurve = widthCurve;

        line.numCornerVertices = lineCornerVertices;
        line.numCapVertices = lineCapVertices;

        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Tile;

        if (lineGradient != null)
            line.colorGradient = lineGradient;
    }

    // =========================================================
    // PINGS
    // =========================================================
    void SpawnPings()
    {
        ClearPings();

        if (SelectedFrom < 0 || SelectedTo < 0) return;

        if (startPingPrefab != null)
        {
            startPingInstance = Instantiate(
                startPingPrefab,
                allPoints[SelectedFrom].position,
                Quaternion.identity,
                visualsRoot
            );
        }

        if (endPingPrefab != null)
        {
            endPingInstance = Instantiate(
                endPingPrefab,
                allPoints[SelectedTo].position,
                Quaternion.identity,
                visualsRoot
            );
        }
    }

    void PreviewPings()
    {
        if (SelectedFrom < 0 || SelectedTo < 0)
        {
            ClearPings();
            return;
        }

        if (startPingPrefab == null && endPingPrefab == null) return;
        if (visualsRoot == null) return;

        if (startPingInstance == null && startPingPrefab != null)
        {
            startPingInstance = Instantiate(
                startPingPrefab,
                allPoints[SelectedFrom].position,
                Quaternion.identity,
                visualsRoot
            );
        }
        else if (startPingInstance != null)
        {
            startPingInstance.transform.position = allPoints[SelectedFrom].position;
        }

        if (endPingInstance == null && endPingPrefab != null)
        {
            endPingInstance = Instantiate(
                endPingPrefab,
                allPoints[SelectedTo].position,
                Quaternion.identity,
                visualsRoot
            );
        }
        else if (endPingInstance != null)
        {
            endPingInstance.transform.position = allPoints[SelectedTo].position;
        }
    }

    void ClearPings()
    {
        if (startPingInstance != null) Destroy(startPingInstance);
        if (endPingInstance != null) Destroy(endPingInstance);

        startPingInstance = null;
        endPingInstance = null;
    }

    // =========================================================
    // LABELS
    // =========================================================
    GameObject CreateLabel(Transform wp)
    {
        GameObject go = new GameObject("Label_" + wp.name);
        go.transform.SetParent(wp);
        go.transform.localPosition = Vector3.up * labelHeight;

        var text = go.AddComponent<TextMeshPro>();
        text.text = wp.name;
        text.font = labelFont;
        text.fontSize = labelSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;

        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(0, 0, 0, 180);

        go.AddComponent<Billboard>();
        return go;
    }

    // =========================================================
    // FLOOR AUTO SWITCH DURING MOVEMENT
    // =========================================================
    void BuildFloorRanges()
    {
        floorRanges.Clear();
        float threshold = 1.7f;

        for (int i = 0; i < floors.Count; i++)
        {
            Transform floor = floors[i];
            float y = floor.position.y;

            Transform waypoints = floor.Find("Waypoints");
            if (waypoints != null)
            {
                var children = waypoints.GetComponentsInChildren<Transform>(true);
                if (children.Length > 1)
                    y = children[1].position.y;
            }

            floorRanges.Add(new FloorRange
            {
                floor = floor,
                minY = y - threshold,
                maxY = y + threshold
            });
        }
    }

    void ActivateFloorByY(float y)
    {
        if (ShowAllFloors) return;

        int bestIndex = -1;
        for (int i = 0; i < floorRanges.Count; i++)
        {
            if (y >= floorRanges[i].minY && y <= floorRanges[i].maxY)
            {
                bestIndex = i;
                break;
            }
        }

        if (bestIndex < 0) return;
        if (ActiveFloorIndex == bestIndex) return;

        // If cross-floor route is active, keep BOTH floors visible.
        if (IsRouteActive && navIsCrossFloor)
        {
            if (isNavigating)
            {
                // During camera move: switch visible floor BUT don't call PreviewPath
                ActiveFloorIndex = bestIndex;

                visibleFloors.Clear();
                visibleFloors.Add(bestIndex);

                ApplyVisibleFloors();
                ActiveFloorChanged?.Invoke();
            }
            else
            {
                // after movement: keep both floors visible, only update active index
                ActiveFloorIndex = bestIndex;
                ActiveFloorChanged?.Invoke();
            }
            return;
        }

        SetFloor(bestIndex);
    }

    void SetShowAllFloors(bool enabled)
    {
        ShowAllFloors = enabled;
        visibleFloors.Clear();

        if (enabled)
        {
            for (int i = 0; i < floors.Count; i++)
                visibleFloors.Add(i);
        }
        else
        {
            ActiveFloorIndex = Mathf.Clamp(ActiveFloorIndex, 0, floors.Count - 1);
            visibleFloors.Add(ActiveFloorIndex);
        }

        ApplyVisibleFloors();

        FloorsChanged?.Invoke();
        PreviewPath();
    }

    public void ExitAllFloorsAndFocusFloor(int floorIndex)
    {
        floorIndex = Mathf.Clamp(floorIndex, 0, floors.Count - 1);

        if (!ShowAllFloors)
        {
            SetFloor(floorIndex);
            return;
        }

        ShowAllFloors = false;

        ActiveFloorIndex = floorIndex;

        visibleFloors.Clear();
        visibleFloors.Add(ActiveFloorIndex);

        ApplyVisibleFloors();

        SelectedFloorDropdownIndex = floorIndex + 1;
        ActiveFloorChanged?.Invoke();
        FloorsChanged?.Invoke();
        PreviewPath();
    }

    // =========================================================
    // TRANSPARENCY (LEFT AS FALLBACK - NOT USED)
    // =========================================================
    bool IsCrossFloorRoute()
    {
        if (SelectedFrom < 0 || SelectedTo < 0) return false;

        int fromFloor = allPointFloorIndex[SelectedFrom];
        int toFloor = allPointFloorIndex[SelectedTo];

        if (fromFloor < 0 || toFloor < 0) return false;
        return fromFloor != toFloor;
    }

    void ApplyNavTransparency(bool enabled)
    {
        if (!makeFloorsTransparentInNav) return;
        if (floors == null || floors.Count == 0) return;

        if (navTransparentMaterial == null)
        {
            Debug.LogWarning("NavigationController: navTransparentMaterial is NULL.");
            return;
        }

        if (enabled)
        {
            SetMaterialAlpha(navTransparentMaterial, navAlpha);

            for (int i = 0; i < floors.Count; i++)
            {
                var renderers = floors[i].GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    if (r == null) continue;

                    // Skip TMP labels to avoid pink text materials
                    if (r.GetComponent<TextMeshPro>() != null) continue;
                    if (r.GetComponent<TMP_SubMesh>() != null) continue;

                    if (!originalMats.ContainsKey(r))
                        originalMats[r] = r.sharedMaterials;

                    var mats = new Material[r.sharedMaterials.Length];
                    for (int m = 0; m < mats.Length; m++)
                        mats[m] = navTransparentMaterial;

                    r.sharedMaterials = mats;
                }
            }
        }
        else
        {
            RestoreOriginalMaterials();
        }
    }

    void RestoreOriginalMaterials()
    {
        foreach (var kv in originalMats)
        {
            if (kv.Key == null) continue;
            kv.Key.sharedMaterials = kv.Value;
        }
        originalMats.Clear();
    }

    void SetMaterialAlpha(Material mat, float alpha)
    {
        if (mat == null) return;

        // URP Lit/Unlit: _BaseColor
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
        // Built-in fallback: _Color
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            c.a = alpha;
            mat.SetColor("_Color", c);
        }
    }
}
