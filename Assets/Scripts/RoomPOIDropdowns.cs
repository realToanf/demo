using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class RoomPOIDropdowns : MonoBehaviour
{
    [Header("Room / POIs")]
    [SerializeField] private Transform roomRoot;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool sortByName = true;

    [Header("UI")]
    [SerializeField] private TMP_Dropdown startDropdown;
    [SerializeField] private TMP_Dropdown endDropdown;

    private readonly List<Transform> poiTransforms = new List<Transform>();

    void Start()
    {
        PopulateFromRoom();
    }

    public void PopulateFromRoom()
    {
        if (!roomRoot || !startDropdown || !endDropdown) return;

        poiTransforms.Clear();

        // get all children
        var all = roomRoot.GetComponentsInChildren<Transform>(includeInactive);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == roomRoot) continue; // skip root
            poiTransforms.Add(t);
        }

        if (sortByName)
            poiTransforms.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

        var options = new List<TMP_Dropdown.OptionData>(poiTransforms.Count);
        for (int i = 0; i < poiTransforms.Count; i++)
            options.Add(new TMP_Dropdown.OptionData(poiTransforms[i].name));

        startDropdown.options = new List<TMP_Dropdown.OptionData>(options);
        endDropdown.options   = new List<TMP_Dropdown.OptionData>(options);

        startDropdown.SetValueWithoutNotify(0);
        endDropdown.SetValueWithoutNotify(Mathf.Min(1, poiTransforms.Count - 1));

        startDropdown.RefreshShownValue();
        endDropdown.RefreshShownValue();
    }

    public Transform GetStart()
    {
        if (poiTransforms.Count == 0) return null;
        int idx = Mathf.Clamp(startDropdown.value, 0, poiTransforms.Count - 1);
        return poiTransforms[idx];
    }

    public Transform GetEnd()
    {
        if (poiTransforms.Count == 0) return null;
        int idx = Mathf.Clamp(endDropdown.value, 0, poiTransforms.Count - 1);
        return poiTransforms[idx];
    }
}
