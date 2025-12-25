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

    [Header("Waypoint Labels")]
    public TMP_FontAsset labelFont;
    public float labelHeight = 1f;
    public float labelSize = 8f;

    public Transform mainCamera;

    List<Transform> floors = new();
    List<Transform> points = new();
    List<GameObject> labels = new();

    NavMeshPath path;

    private int selectedFrom = -1;
    private int selectedTo = -1;
    public Material lineMaterial;

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

        path = new NavMeshPath();

        // Setup line renderer material safely
        if (line == null)
        {
            Debug.LogError("NavigationUI: LineRenderer is NULL.");
            yield break;
        }

        if (lineMaterial != null)
        {
            line.material = lineMaterial;
        }
        else if (line.material == null)
        {
            Debug.LogError("NavigationUI: lineMaterial is NULL and LineRenderer has no material. Assign a URP Unlit material in Inspector.");
            yield break;
        }

        line.material.color = Color.green;
        line.useWorldSpace = true;
        line.sortingOrder = 999;
        line.positionCount = 0;

        LoadFloors();

        BuildFloorRanges();

        floorsDropdown.onValueChanged.AddListener(_ => UpdateFloor());
        fromDropdown.onValueChanged.AddListener(_ => { UpdateSelection(); PreviewPath(); });
        toDropdown.onValueChanged.AddListener(_ => { UpdateSelection(); PreviewPath(); });

        UpdateFloor();
    }


    void LoadWaypoints(Transform waypoints)
    {
        List<string> names = new();

        foreach (Transform t in waypoints)
        {
            points.Add(t);
            names.Add(t.name);
            labels.Add(CreateLabel(t));
        }

        fromDropdown.AddOptions(names);
        toDropdown.AddOptions(names);
    }

    void LoadFloors()
    {
        floors.Clear();
        points.Clear();
        labels.Clear();
        floorsDropdown.ClearOptions();
        fromDropdown.ClearOptions();
        toDropdown.ClearOptions();

        List<string> names = new();

        foreach (Transform t in floorsRoot)
        {
            floors.Add(t);
            names.Add(t.name);

            Transform waypoints = t.Find("Waypoints");
            if (waypoints != null)
                LoadWaypoints(waypoints);
        }

        floorsDropdown.AddOptions(names);
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

    void UpdateFloor()
    {
        int index = floorsDropdown.value;

        for (int i = 0; i < floors.Count; i++)
        {
            bool active = (i == index);
            floors[i].gameObject.SetActive(active);
        }

        UpdateLabelsVisibility();
        PreviewPath();
    }

    void UpdateSelection()
    {
        selectedFrom = fromDropdown.value;
        selectedTo = toDropdown.value;
    }

    void UpdateLabelsVisibility()
    {
        foreach (var label in labels)
        {
            label.SetActive(label.transform.root.gameObject.activeSelf);
        }
    }

    public void PreviewPath()
    {
        if (points.Count == 0) return;

        int from = fromDropdown.value;
        int to = toDropdown.value;

        if (from == to)
        {
            ClearPath();
            return;
        }

        Vector3 start = points[from].position;
        Vector3 end = points[to].position;

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

    // Call this from the UI Button
    public void StartNavigation()
    {
        if (points.Count == 0) return;

        selectedFrom = fromDropdown.value;
        selectedTo = toDropdown.value;

        if (selectedFrom == selectedTo) return;

            mainCamera.GetComponent<CameraController>()
        .MoveBirdEyeFromTo(points[selectedFrom], points[selectedTo], ClearPath, pos => ActivateFloorByY(pos.y));
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

        UpdateLabelsVisibility();
        Debug.Log($"ActivateFloorByY y={y}");
    }


    void BuildFloorRanges()
    {
        floorRanges.Clear();

        float threshold = 1.7f; // (3.45/2) ~ 1.725

        for (int i = 0; i < floors.Count; i++)
        {
            Transform floor = floors[i];

            float y = floors[i].position.y;

            Transform waypoints = floor.Find("Waypoints");
            if (waypoints != null && waypoints.childCount > 0)
            {
                y = waypoints.GetChild(0).position.y;
            }

            floorRanges.Add(new FloorRange
            {
                floor = floors[i],
                minY = y - threshold,
                maxY = y + threshold
            });

            Debug.Log($"Floor[{i}] {floors[i].name}: y={y}, range=({y - threshold} -> {y + threshold})");
        }
    }
}
