using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class NavigationUITKBridge : MonoBehaviour
{
    public NavigationUI nav;       // your existing NavigationUI script
    public UIDocument doc;         // your UI Document

    DropdownField fromDropdown;
    DropdownField toDropdown;
    DropdownField floorDropdown;
    Button navigateBtn;

    void Start()
    {
        if (!doc) doc = GetComponent<UIDocument>();

        var root = doc.rootVisualElement;

        fromDropdown = root.Q<DropdownField>("fromDropdown");
        toDropdown = root.Q<DropdownField>("toDropdown");
        floorDropdown = root.Q<DropdownField>("floorDropdown");
        navigateBtn = root.Q<Button>("navigateBtn");

        if (fromDropdown == null || toDropdown == null || floorDropdown == null || navigateBtn == null)
        {
            Debug.LogError("Bridge: UI element not found. Check UXML names.");
            return;
        }

        if (nav == null)
        {
            Debug.LogError("Bridge: NavigationUI not assigned.");
            return;
        }

        // Fill floors + rooms from your existing TMP dropdowns
        SyncFloors();
        SyncRooms();

        // Events
        floorDropdown.RegisterValueChangedCallback(evt =>
        {
            SetUGUIFloor(evt.newValue);
            SyncRooms();
            nav.PreviewPath();
        });

        fromDropdown.RegisterValueChangedCallback(evt =>
        {
            SetUGUIFrom(evt.newValue);
            nav.PreviewPath();
        });

        toDropdown.RegisterValueChangedCallback(evt =>
        {
            SetUGUITo(evt.newValue);
            nav.PreviewPath();
        });

        navigateBtn.clicked += () => nav.StartNavigation();
    }

    void SyncFloors()
    {
        var choices = new List<string>();
        foreach (var opt in nav.floorsDropdown.options)
            choices.Add(opt.text);

        floorDropdown.choices = choices;

        int idx = nav.floorsDropdown.value;
        if (idx >= 0 && idx < choices.Count)
            floorDropdown.value = choices[idx];
    }

    void SyncRooms()
    {
        var fromChoices = new List<string>();
        foreach (var opt in nav.fromDropdown.options)
            fromChoices.Add(opt.text);

        var toChoices = new List<string>();
        foreach (var opt in nav.toDropdown.options)
            toChoices.Add(opt.text);

        fromDropdown.choices = fromChoices;
        toDropdown.choices = toChoices;

        int f = nav.fromDropdown.value;
        int t = nav.toDropdown.value;

        if (f >= 0 && f < fromChoices.Count) fromDropdown.value = fromChoices[f];
        if (t >= 0 && t < toChoices.Count) toDropdown.value = toChoices[t];
    }

    void SetUGUIFloor(string floorName)
    {
        int idx = nav.floorsDropdown.options.FindIndex(o => o.text == floorName);
        if (idx >= 0)
        {
            nav.floorsDropdown.value = idx;
            nav.floorsDropdown.RefreshShownValue();
        }
    }

    void SetUGUIFrom(string roomName)
    {
        int idx = nav.fromDropdown.options.FindIndex(o => o.text == roomName);
        if (idx >= 0)
        {
            nav.fromDropdown.value = idx;
            nav.fromDropdown.RefreshShownValue();
        }
    }

    void SetUGUITo(string roomName)
    {
        int idx = nav.toDropdown.options.FindIndex(o => o.text == roomName);
        if (idx >= 0)
        {
            nav.toDropdown.value = idx;
            nav.toDropdown.RefreshShownValue();
        }
    }
}
