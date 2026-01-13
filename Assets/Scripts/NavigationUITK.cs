// NavigationUITK.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class NavigationUITK : MonoBehaviour
{
    public UIDocument uiDocument;
    public NavigationController nav;

    [Header("Optional: Refocus Button -> CameraController")]
    public CameraController cam; 

    DropdownField fromDropdown;
    DropdownField toDropdown;
    DropdownField floorDropdown;
    Button navigateBtn;

    // NEW
    Button refocusBtn;

    bool isNavigating = false;

    const string FROM_PLACEHOLDER = "Chọn điểm bắt đầu";
    const string TO_PLACEHOLDER   = "Chọn điểm đến";

    void OnEnable()
    {
        var root = uiDocument.rootVisualElement;

        fromDropdown  = root.Q<DropdownField>("fromDropdown");
        toDropdown    = root.Q<DropdownField>("toDropdown");
        floorDropdown = root.Q<DropdownField>("floorDropdown");
        navigateBtn   = root.Q<Button>("navigateBtn");

        // NEW
        refocusBtn    = root.Q<Button>("refocusBtn");

        if (fromDropdown == null || toDropdown == null || floorDropdown == null || navigateBtn == null)
        {
            Debug.LogError("NavigationUITK: Missing UXML elements. Check your name= fields.");
            return;
        }

        // UI -> controller
        floorDropdown.RegisterValueChangedCallback(_ => nav.SetFloorFromDropdown(floorDropdown.index));

        fromDropdown.RegisterValueChangedCallback(_ =>
        {
            nav.SetFrom(fromDropdown.index);
            RefreshNavigateButtonState();
        });

        toDropdown.RegisterValueChangedCallback(_ =>
        {
            nav.SetTo(toDropdown.index);
            RefreshNavigateButtonState();
        });

        navigateBtn.clicked += OnNavigateButtonClicked;

        // NEW
        if (refocusBtn != null)
            refocusBtn.clicked += OnRefocusClicked;

        // controller -> UI
        nav.FloorsChanged += RefreshFloors;
        nav.ActiveFloorChanged += RefreshActiveFloor;
        nav.SelectionChanged += RefreshSelections;
        nav.NavigationStateChanged += OnNavigationStateChanged;

        RefreshFloors();
        RefreshActiveFloor();
        RefreshSelections();

        // initial state
        OnNavigationStateChanged(false);
        RefreshNavigateButtonState();

        // NEW: set initial refocus button state
        RefreshRefocusButtonState();
    }

    void OnDisable()
    {
        if (nav == null) return;

        nav.FloorsChanged -= RefreshFloors;
        nav.ActiveFloorChanged -= RefreshActiveFloor;
        nav.SelectionChanged -= RefreshSelections;
        nav.NavigationStateChanged -= OnNavigationStateChanged;

        if (navigateBtn != null)
            navigateBtn.clicked -= OnNavigateButtonClicked;

        // NEW
        if (refocusBtn != null)
            refocusBtn.clicked -= OnRefocusClicked;
    }

    void Update()
    {
        // NEW: keep refocus availability updated
        RefreshRefocusButtonState();
    }

    void SetControlsLocked(bool locked)
    {
        if (fromDropdown != null)  fromDropdown.SetEnabled(!locked);
        if (toDropdown != null)    toDropdown.SetEnabled(!locked);
        if (floorDropdown != null) floorDropdown.SetEnabled(!locked);
        // If you have more UI controls, disable/enable them here as well.

        // NEW: refocus should also respect lock state
        if (refocusBtn != null)
            refocusBtn.SetEnabled(!locked);
    }

    void OnNavigateButtonClicked()
    {
        if (nav == null) return;

        // If route is active (even after camera finished), clicking cancels + resets placeholders
        if (nav.IsRouteActive || isNavigating)
        {
            nav.CancelAndResetToPlaceholder();
            return;
        }

        // Otherwise: start navigation
        nav.StartNavigation();

        // Optional immediate lock (controller will also fire NavigationStateChanged(true))
        SetControlsLocked(true);
    }

    void OnNavigationStateChanged(bool navigating)
    {
        isNavigating = navigating;
        if (navigateBtn == null || nav == null) return;

        bool locked = nav.IsRouteActive || navigating;
        SetControlsLocked(locked);

        if (locked)
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

        // NEW: navigation state affects refocus as well
        RefreshRefocusButtonState();
    }

    void RefreshNavigateButtonState()
    {
        if (navigateBtn == null || nav == null) return;

        if (nav.IsRouteActive || isNavigating)
        {
            navigateBtn.SetEnabled(true);
            return;
        }

        bool valid = nav.SelectedFrom >= 0 &&
                     nav.SelectedTo >= 0 &&
                     nav.SelectedFrom != nav.SelectedTo;

        navigateBtn.SetEnabled(valid);
    }

    // NEW
    void OnRefocusClicked()
    {
        if (cam == null) return;
        cam.RefocusNow();
    }

    // NEW
    void RefreshRefocusButtonState()
    {
        if (refocusBtn == null) return;

        bool locked = (nav != null) && (nav.IsRouteActive || isNavigating);
        refocusBtn.SetEnabled(!locked && cam != null);
    }

    void RefreshFloors()
    {
        var floors = nav.FloorNames;
        if (floors == null || floors.Count == 0) return;

        floorDropdown.choices = new List<string>(floors);

        int idx = Mathf.Clamp(nav.SelectedFloorDropdownIndex, 0, floors.Count - 1);
        floorDropdown.index = idx;
        floorDropdown.SetValueWithoutNotify(floors[idx]);
    }

    void RefreshActiveFloor()
    {
        var points = nav.ActivePointNames;
        if (points == null)
            points = new List<string>();

        fromDropdown.choices = new List<string>(points);
        toDropdown.choices   = new List<string>(points);

        RefreshSelections();
    }

    void RefreshSelections()
    {
        var points = nav.ActivePointNames;
        if (points == null || points.Count == 0)
        {
            fromDropdown.index = -1;
            fromDropdown.SetValueWithoutNotify(FROM_PLACEHOLDER);

            toDropdown.index = -1;
            toDropdown.SetValueWithoutNotify(TO_PLACEHOLDER);

            RefreshNavigateButtonState();
            RefreshRefocusButtonState();
            return;
        }

        // FROM
        if (nav.SelectedFrom < 0 || nav.SelectedFrom >= points.Count)
        {
            fromDropdown.SetValueWithoutNotify(FROM_PLACEHOLDER);
            fromDropdown.index = -1;
        }
        else
        {
            fromDropdown.index = nav.SelectedFrom;
            fromDropdown.SetValueWithoutNotify(points[nav.SelectedFrom]);
        }

        // TO
        if (nav.SelectedTo < 0 || nav.SelectedTo >= points.Count)
        {
            toDropdown.SetValueWithoutNotify(TO_PLACEHOLDER);
            toDropdown.index = -1;
        }
        else
        {
            toDropdown.index = nav.SelectedTo;
            toDropdown.SetValueWithoutNotify(points[nav.SelectedTo]);
        }

        RefreshNavigateButtonState();
        RefreshRefocusButtonState();
    }
}
