using UnityEngine;
using TMPro;
using UnityEngine.AI;
using System.Collections.Generic;

public class NavigationUI : MonoBehaviour
{
    [Header("List")]
    public Transform floorsRoot;
    public Transform waypointsRoot;

    [Header("UI")]
    public TMP_Dropdown floorsDropdown;
    public TMP_Dropdown fromDropdown;
    public TMP_Dropdown toDropdown;

    [Header("Path")]
    public LineRenderer line;

    List<Transform> floors = new();
    List<Transform> points = new();

    NavMeshPath path;

    void Start()
    {
        path = new NavMeshPath();

        line.material = new Material(Shader.Find("Unlit/Color"));
        line.material.color = Color.green;
        line.useWorldSpace = true;
        line.sortingOrder = 999;
        line.alignment = LineAlignment.View;

        LoadFloors();
        LoadWaypoints();

        floorsDropdown.onValueChanged.AddListener(_ => UpdateFloor());

        fromDropdown.onValueChanged.AddListener(_ => UpdatePath());
        toDropdown.onValueChanged.AddListener(_ => UpdatePath());
        
        UpdateFloor();
    }

    void LoadWaypoints()
    {
        points.Clear();
        fromDropdown.ClearOptions();
        toDropdown.ClearOptions();

        List<string> names = new();

        foreach (Transform t in waypointsRoot)
        {
            points.Add(t);
            names.Add(t.name);
        }

        fromDropdown.AddOptions(names);
        toDropdown.AddOptions(names);
    }

    void LoadFloors()
    {
        floors.Clear();
        floorsDropdown.ClearOptions();

        List<string> names = new();

        foreach (Transform t in floorsRoot)
        {
            floors.Add(t);
            names.Add(t.name);
        }

        floorsDropdown.AddOptions(names);
    }
    
    void UpdateFloor()
    {
        int index = floorsDropdown.value;

        for (int i = 0; i < floors.Count; i++)
        {
            floors[i].gameObject.SetActive(i == index);
        }

        UpdatePath();
    }

    public void UpdatePath()
    {
        int from = fromDropdown.value;
        int to = toDropdown.value;

        if (from == to) return;

        Vector3 start = points[from].position;
        Vector3 end = points[to].position;
        
        Debug.Log($"Navigate from {start} to {end}");

        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path))
        {
            line.positionCount = path.corners.Length;
            line.SetPositions(path.corners);
        }
    }
}
