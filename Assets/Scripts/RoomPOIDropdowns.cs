using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

public class RoomPOIDropdowns : MonoBehaviour
{
    [Header("POI Source (optional)")]
    [Tooltip("If set, only POIs under this root will be included. If null, searches entire scene.")]
    [SerializeField] private Transform poiRoot;

    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool sortByName = true;

    [Header("UI")]
    [SerializeField] private TMP_Dropdown startDropdown;
    [SerializeField] private TMP_Dropdown endDropdown;

    // store the actual POIs (not transforms)
    private readonly List<RoomPOI> pois = new();

    void Start()
    {
        Populate();
    }

    void OnEnable()
    {
        // Important when UI panel is disabled at startup and enabled later
        Populate();
    }

    public void Populate()
    {
        if (!startDropdown || !endDropdown)
            return;

        pois.Clear();

        // Find POIs
        RoomPOI[] found;
        if (poiRoot != null)
        {
            found = poiRoot.GetComponentsInChildren<RoomPOI>(includeInactive);
        }
        else
        {
            // Unity 6 friendly scene-wide search
            found = UnityEngine.Object.FindObjectsByType<RoomPOI>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
        }

        if (found == null || found.Length == 0)
        {
            // Clear dropdowns so it's obvious
            startDropdown.ClearOptions();
            endDropdown.ClearOptions();
            return;
        }

        pois.AddRange(found);

        // Sort by displayName (fallback to object name)
        if (sortByName)
        {
            pois.Sort((a, b) => string.Compare(GetName(a), GetName(b), StringComparison.OrdinalIgnoreCase));
        }

        // Build dropdown options
        List<TMP_Dropdown.OptionData> options = new(pois.Count);
        for (int i = 0; i < pois.Count; i++)
        {
            options.Add(new TMP_Dropdown.OptionData(GetName(pois[i])));
        }

        startDropdown.ClearOptions();
        endDropdown.ClearOptions();
        startDropdown.AddOptions(options);
        endDropdown.AddOptions(options);

        // Default selections
        startDropdown.SetValueWithoutNotify(0);
        endDropdown.SetValueWithoutNotify(Mathf.Min(1, pois.Count - 1));
        startDropdown.RefreshShownValue();
        endDropdown.RefreshShownValue();
    }

    private string GetName(RoomPOI poi)
    {
        if (poi == null) return "(null)";
        if (!string.IsNullOrWhiteSpace(poi.displayName)) return poi.displayName;
        return poi.gameObject.name;
    }

    public Transform GetStart()
    {
        if (pois.Count == 0) return null;
        int idx = Mathf.Clamp(startDropdown.value, 0, pois.Count - 1);
        return pois[idx] != null ? pois[idx].transform : null;
    }

    public Transform GetEnd()
    {
        if (pois.Count == 0) return null;
        int idx = Mathf.Clamp(endDropdown.value, 0, pois.Count - 1);
        return pois[idx] != null ? pois[idx].transform : null;
    }
}
