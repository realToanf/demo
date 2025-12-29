using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class NavigationUITK : MonoBehaviour
{
    public UIDocument uiDocument;
    public NavigationController nav;

    DropdownField fromDropdown;
    DropdownField toDropdown;
    DropdownField floorDropdown;
    Button navigateBtn;
    bool isNavigating = false;

    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;

        fromDropdown  = root.Q<DropdownField>("fromDropdown");
        toDropdown    = root.Q<DropdownField>("toDropdown");
        floorDropdown = root.Q<DropdownField>("floorDropdown");
        navigateBtn   = root.Q<Button>("navigateBtn");

        // Defensive check (prevents null crashes)
        if (fromDropdown == null || toDropdown == null || floorDropdown == null || navigateBtn == null)
        {
            Debug.LogError("NavigationUITK: Missing UXML elements. Check your name= fields.");
            return;
        }

        // UI -> controller
        floorDropdown.RegisterValueChangedCallback(_ => nav.SetFloorFromDropdown(floorDropdown.index));
        fromDropdown.RegisterValueChangedCallback(_ => {nav.SetFrom(fromDropdown.index); RefreshNavigateButtonState();});
        toDropdown.RegisterValueChangedCallback(_ => {nav.SetTo(toDropdown.index); RefreshNavigateButtonState();});
        navigateBtn.clicked += nav.StartNavigation;

        // controller -> UI
        nav.FloorsChanged += RefreshFloors;
        nav.ActiveFloorChanged += RefreshActiveFloor;
        nav.SelectionChanged += RefreshSelections;
        nav.NavigationStateChanged += OnNavigationStateChanged;

        RefreshFloors();
        RefreshActiveFloor();
        RefreshSelections();
        OnNavigationStateChanged(false);
        RefreshNavigateButtonState();
    }

    void OnDisable()
    {
        if (nav == null) return;

        nav.FloorsChanged -= RefreshFloors;
        nav.ActiveFloorChanged -= RefreshActiveFloor;
        nav.SelectionChanged -= RefreshSelections;
        nav.NavigationStateChanged -= OnNavigationStateChanged;

        if (navigateBtn != null)
            navigateBtn.clicked -= nav.StartNavigation;
    }

    void OnNavigationStateChanged(bool navigating)
    {
        isNavigating = navigating;
        if (navigateBtn == null) return;

        if (navigating)
        {
            navigateBtn.text = "Hủy chỉ đường";
            navigateBtn.AddToClassList("cancel");
            navigateBtn.SetEnabled(true);
        }
        else
        {
            navigateBtn.text = "Bắt đầu đi";
            navigateBtn.RemoveFromClassList("cancel");
            RefreshNavigateButtonState();
        }
    }
    
    void RefreshNavigateButtonState()
    {
        if (navigateBtn == null || nav == null) return;

        if (isNavigating)
        {
            navigateBtn.SetEnabled(true);
            return;
        }

        bool valid = nav.SelectedFrom != nav.SelectedTo;
        navigateBtn.SetEnabled(valid);
    }

    void RefreshFloors()
    {
        var floors = nav.FloorNames;
        if (floors == null || floors.Count == 0) return;

        floorDropdown.choices = new List<string>(floors);

        // If your controller starts on "All floors", index should be 0
        int idx = Mathf.Clamp(floorDropdown.index, 0, floors.Count - 1);
        floorDropdown.index = idx;
        floorDropdown.SetValueWithoutNotify(floors[idx]);
    }

    void RefreshActiveFloor()
    {
        var points = nav.ActivePointNames;
        if (points == null || points.Count == 0)
        {
            fromDropdown.choices = new List<string>();
            toDropdown.choices = new List<string>();
            return;
        }

        fromDropdown.choices = new List<string>(points);
        toDropdown.choices   = new List<string>(points);

        RefreshSelections();
    }

    void RefreshSelections()
    {
        var points = nav.ActivePointNames;
        if (points == null || points.Count == 0) return;

        int from = Mathf.Clamp(nav.SelectedFrom, 0, points.Count - 1);
        int to   = Mathf.Clamp(nav.SelectedTo,   0, points.Count - 1);

        fromDropdown.index = from;
        fromDropdown.SetValueWithoutNotify(points[from]);

        toDropdown.index = to;
        toDropdown.SetValueWithoutNotify(points[to]);

        RefreshNavigateButtonState();
    }
}
