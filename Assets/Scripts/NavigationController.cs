using System;
using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.AI;
using System.Collections.Generic;

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

    public Transform mainCamera;

    // ----- Public state (for UI Toolkit) -----
    public IReadOnlyList<string> FloorNames => floorDropdownOptions;

    // ✅ IMPORTANT: UI sees ALL points across ALL floors
    public IReadOnlyList<string> ActivePointNames => allPointNames;

    public int ActiveFloorIndex { get; private set; } = 0;
    public int SelectedFrom { get; private set; } = 0;
    public int SelectedTo { get; private set; } = 1;

    public event Action FloorsChanged;
    public event Action ActiveFloorChanged;
    public event Action SelectionChanged;

    // Floors

    private readonly List<Transform> floors = new();
    private readonly List<string> floorNames = new();

    // Per-floor points + labels
    private readonly Dictionary<int, List<Transform>> floorPoints = new();
    private readonly Dictionary<int, List<GameObject>> floorLabels = new();

    // ✅ All points across ALL floors (dropdown + navigation)
    private readonly List<Transform> allPoints = new();
    private readonly List<string> allPointNames = new();
    private readonly List<int> allPointFloorIndex = new(); // point -> floor

    private NavMeshPath path;

    // Visible floors (we will always keep ONLY 1 in this set)
    public IReadOnlyCollection<int> VisibleFloors => visibleFloors;
    private readonly HashSet<int> visibleFloors = new();

    public bool IsFloorVisible(int index) => visibleFloors.Contains(index);
    public bool ShowAllFloors { get; private set; } = false;
    public IReadOnlyList<string> FloorDropdownOptions => floorDropdownOptions;
    private readonly List<string> floorDropdownOptions = new();
    public int SelectedFloorDropdownIndex { get; private set; } = 1;


    // Floor ranges for auto switching during camera movement
    struct FloorRange
    {
        public Transform floor;
        public float minY;
        public float maxY;
    }

    private readonly List<FloorRange> floorRanges = new();

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

        if (lineMaterial != null) line.material = lineMaterial;
        if (line.material == null)
        {
            Debug.LogError("NavigationController: LineRenderer has no material. Assign a URP Unlit material.");
            yield break;
        }

        line.material.color = Color.green;
        line.useWorldSpace = true;
        line.sortingOrder = 999;
        line.positionCount = 0;

        // Load floors + ALL waypoints (including inactive ones)
        LoadFloorsAndWaypoints();
        BuildFloorDropdownOptions();
        BuildFloorRanges();

        FloorsChanged?.Invoke();

        // Default: show floor 0
        SetFloor(0);

        // Clamp selection defaults
        ClampSelections();

        ActiveFloorChanged?.Invoke();
        PreviewPath();
    }

    void BuildFloorDropdownOptions()
    {
        floorDropdownOptions.Clear();
        floorDropdownOptions.Add("Tất cả các tầng");
        floorDropdownOptions.AddRange(floorNames);
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

    // =========================================================
    // LOAD DATA
    // =========================================================
    void LoadFloorsAndWaypoints()
    {
        floors.Clear();
        floorPoints.Clear();
        floorLabels.Clear();
        floorNames.Clear();

        allPoints.Clear();
        allPointNames.Clear();
        allPointFloorIndex.Clear();

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
                // ✅ include inactive children too
                var children = waypoints.GetComponentsInChildren<Transform>(true);

                foreach (var t in children)
                {
                    if (t == waypoints) continue;

                    points.Add(t);
                    labels.Add(CreateLabel(t));

                    // ✅ global list for dropdown
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
        // hide all
        foreach (var kv in floorLabels)
            foreach (var label in kv.Value)
                if (label != null) label.SetActive(false);

        // show only visible floors
        foreach (var idx in visibleFloors)
        {
            if (!floorLabels.ContainsKey(idx)) continue;
            foreach (var label in floorLabels[idx])
                if (label != null) label.SetActive(true);
        }
    }

    // ✅ Single-floor mode API
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
            SetFloor(dropdownIndex - 1); // because floor list starts at index 0
        }
    }

    // (Optional, kept for compatibility, but not used in single-floor mode)
    public void SetFloorVisible(int index, bool visible)
    {
        // In your project you said you DON'T want multi visible floors,
        // so we just redirect to SetFloor(index) when visible is true.
        if (visible) SetFloor(index);
    }

    // =========================================================
    // SELECTION (FROM/TO)
    // =========================================================
    void ClampSelections()
    {
        if (allPoints.Count == 0) return;

        SelectedFrom = Mathf.Clamp(SelectedFrom, 0, allPoints.Count - 1);
        SelectedTo   = Mathf.Clamp(SelectedTo,   0, allPoints.Count - 1);

        if (SelectedFrom == SelectedTo && allPoints.Count > 1)
            SelectedTo = (SelectedFrom == 0) ? 1 : 0;
    }

    public void SetFrom(int index)
    {
        if (allPoints.Count == 0) return;

        SelectedFrom = Mathf.Clamp(index, 0, allPoints.Count - 1);

        if (SelectedFrom == SelectedTo && allPoints.Count > 1)
            SelectedTo = (SelectedFrom == 0) ? 1 : 0;

        // ✅ show floor of selected point
        if (!ShowAllFloors)
            SetFloor(allPointFloorIndex[SelectedFrom]);

        SelectionChanged?.Invoke();
        PreviewPath();
    }

    public void SetTo(int index)
    {
        if (allPoints.Count == 0) return;

        SelectedTo = Mathf.Clamp(index, 0, allPoints.Count - 1);

        if (SelectedTo == SelectedFrom && allPoints.Count > 1)
            SelectedFrom = (SelectedTo == 0) ? 1 : 0;

        // ✅ show floor of selected point
        if (!ShowAllFloors)
            SetFloor(allPointFloorIndex[SelectedTo]);

        SelectionChanged?.Invoke();
        PreviewPath();
    }

    // =========================================================
    // PATH PREVIEW + NAVIGATION
    // =========================================================
    public void PreviewPath()
    {
        if (allPoints.Count == 0) return;

        int from = SelectedFrom;
        int to = SelectedTo;

        if (from == to || from < 0 || to < 0 ||
            from >= allPoints.Count || to >= allPoints.Count)
        {
            ClearPath();
            return;
        }

        Vector3 start = allPoints[from].position;
        Vector3 end = allPoints[to].position;

        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            line.positionCount = path.corners.Length;
            line.SetPositions(path.corners);
        }
        else
        {
            ClearPath();
        }
    }

    public void StartNavigation()
    {
        if (allPoints.Count == 0) return;
        if (SelectedFrom == SelectedTo) return;

        ExitAllFloorsAndFocusFloor(allPointFloorIndex[SelectedFrom]);

        var camController = mainCamera.GetComponent<CameraController>();
        if (camController == null)
        {
            Debug.LogError("NavigationController: CameraController not found.");
            return;
        }

        camController.MoveBirdEyeFromTo(
            allPoints[SelectedFrom],
            allPoints[SelectedTo],
            null,
            pos => ActivateFloorByY(pos.y)
        );
    }

    void ClearPath() => line.positionCount = 0;

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
                    y = children[1].position.y; // first waypoint child
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

        // ActiveFloorChanged?.Invoke();
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

        // Turn off all floors mode
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
}
