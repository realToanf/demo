using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.AI;
using System.Collections.Generic;

public class NavigationUI : MonoBehaviour
{
    [Header("List")]
    public Transform floorsRoot;

    [Header("UI")]
    public TMP_Dropdown floorsDropdown;
    public TMP_Dropdown fromDropdown;
    public TMP_Dropdown toDropdown;

    [Header("Path")]
    public LineRenderer line;
    public Material lineMaterial;

    [Header("Waypoint Labels")]
    public TMP_FontAsset labelFont;
    public float labelHeight = 1f;
    public float labelSize = 8f;

    public Transform mainCamera;

    // Floors & Points
    private readonly List<Transform> floors = new();
    private readonly Dictionary<int, List<Transform>> floorPoints = new();
    private readonly Dictionary<int, List<GameObject>> floorLabels = new();

    // Active floor data
    private readonly List<Transform> activePoints = new();
    private readonly List<GameObject> activeLabels = new();

    private NavMeshPath path;

    private int selectedFrom = -1;
    private int selectedTo = -1;

    IEnumerator Start()
    {
        yield return null;

        AutoAssignIfNull();

        if (floorsRoot == null)
        {
            Debug.LogError("NavigationUI: floorsRoot is NULL (Environment not found).");
            yield break;
        }

        if (floorsDropdown == null || fromDropdown == null || toDropdown == null)
        {
            Debug.LogError("NavigationUI: Dropdown references are NULL.");
            yield break;
        }

        if (mainCamera == null)
        {
            Debug.LogError("NavigationUI: mainCamera is NULL.");
            yield break;
        }

        if (line == null)
        {
            Debug.LogError("NavigationUI: LineRenderer is NULL.");
            yield break;
        }

        path = new NavMeshPath();

        // Setup line renderer material safely
        if (lineMaterial != null) line.material = lineMaterial;
        if (line.material == null)
        {
            Debug.LogError("NavigationUI: LineRenderer has no material. Assign a URP Unlit material.");
            yield break;
        }

        line.material.color = Color.green;
        line.useWorldSpace = true;
        line.sortingOrder = 999;
        line.positionCount = 0;

        // ✅ Load floors + points (1 lần)
        LoadFloorsAndWaypoints();
        BuildFloorRanges();

        // listeners
        floorsDropdown.onValueChanged.AddListener(_ => UpdateFloor());
        fromDropdown.onValueChanged.AddListener(_ => { UpdateSelection(); PreviewPath(); });
        toDropdown.onValueChanged.AddListener(_ => { UpdateSelection(); PreviewPath(); });

        UpdateFloor();
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

    void LoadFloorsAndWaypoints()
    {
        floors.Clear();
        floorPoints.Clear();
        floorLabels.Clear();

        floorsDropdown.ClearOptions();
        fromDropdown.ClearOptions();
        toDropdown.ClearOptions();

        var floorNames = new List<string>();

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
                foreach (Transform wp in waypoints)
                {
                    points.Add(wp);
                    labels.Add(CreateLabel(wp));
                }
            }

            floorPoints[i] = points;
            floorLabels[i] = labels;
        }

        floorsDropdown.AddOptions(floorNames);
    }

    void UpdateFloor()
    {
        int index = floorsDropdown.value;

        for (int i = 0; i < floors.Count; i++)
            floors[i].gameObject.SetActive(i == index);

        RefreshActiveFloorPoints(index);
        PreviewPath();
    }

    void RefreshActiveFloorPoints(int floorIndex)
    {
        activePoints.Clear();
        activeLabels.Clear();

        fromDropdown.ClearOptions();
        toDropdown.ClearOptions();

        if (!floorPoints.ContainsKey(floorIndex))
            return;

        activePoints.AddRange(floorPoints[floorIndex]);
        activeLabels.AddRange(floorLabels[floorIndex]);

        var names = new List<string>();
        foreach (var p in activePoints) names.Add(p.name);

        fromDropdown.AddOptions(names);
        toDropdown.AddOptions(names);

        // default selections
        if (names.Count > 0)
        {
            fromDropdown.value = 0;
            toDropdown.value = Mathf.Min(1, names.Count - 1);
        }

        UpdateLabelsVisibility(floorIndex);
    }

    void UpdateLabelsVisibility(int activeFloorIndex)
    {
        // hide all
        foreach (var kv in floorLabels)
        {
            foreach (var label in kv.Value)
                if (label != null) label.SetActive(false);
        }

        // show active
        if (floorLabels.ContainsKey(activeFloorIndex))
        {
            foreach (var label in floorLabels[activeFloorIndex])
                if (label != null) label.SetActive(true);
        }
    }

    void UpdateSelection()
    {
        selectedFrom = fromDropdown.value;
        selectedTo = toDropdown.value;
    }

    public void PreviewPath()
    {
        if (activePoints.Count == 0) return;

        int from = fromDropdown.value;
        int to = toDropdown.value;

        if (from < 0 || to < 0 || from >= activePoints.Count || to >= activePoints.Count)
        {
            ClearPath();
            return;
        }

        if (from == to)
        {
            ClearPath();
            return;
        }

        Vector3 start = activePoints[from].position;
        Vector3 end = activePoints[to].position;

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
        if (activePoints.Count == 0) return;

        selectedFrom = fromDropdown.value;
        selectedTo = toDropdown.value;

        if (selectedFrom < 0 || selectedTo < 0) return;
        if (selectedFrom >= activePoints.Count || selectedTo >= activePoints.Count) return;
        if (selectedFrom == selectedTo) return;

        var camController = mainCamera.GetComponent<CameraController>();
        if (camController == null)
        {
            Debug.LogError("NavigationUI: CameraController not found.");
            return;
        }

        camController.MoveBirdEyeFromTo(
            activePoints[selectedFrom],
            activePoints[selectedTo],
            null,
            pos => ActivateFloorByY(pos.y)
        );
    }

    void ClearPath()
    {
        line.positionCount = 0;
    }

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

        // quick readability boost
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(0, 0, 0, 180);

        go.AddComponent<Billboard>();
        return go;
    }

    struct FloorRange
    {
        public Transform floor;
        public float minY;
        public float maxY;
    }

    List<FloorRange> floorRanges = new();

    void BuildFloorRanges()
    {
        floorRanges.Clear();

        float threshold = 1.7f;

        for (int i = 0; i < floors.Count; i++)
        {
            Transform floor = floors[i];

            float y = floor.position.y;

            Transform waypoints = floor.Find("Waypoints");
            if (waypoints != null && waypoints.childCount > 0)
                y = waypoints.GetChild(0).position.y;

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
        if (floorsDropdown.value == bestIndex) return;

        floorsDropdown.value = bestIndex;

        for (int i = 0; i < floors.Count; i++)
            floors[i].gameObject.SetActive(i == bestIndex);

        RefreshActiveFloorPoints(bestIndex);
    }
}
