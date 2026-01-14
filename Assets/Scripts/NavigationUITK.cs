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

    VisualElement cardHeader;
    VisualElement cardContent;
    Label chevron;
    bool isCollapsed = false;

    void Start()
    {
        if (uiDocument == null)
        {
            Debug.LogError("NavigationUITK: uiDocument is NOT assigned in the inspector.");
            return;
        }

        var root = uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("NavigationUITK: rootVisualElement is null.");
            return;
        }

        Debug.Log($"NavigationUITK: root child count = {root.childCount}");

        // Dump all named elements so we see what exists
        foreach (var e in root.Query<VisualElement>().ToList())
        {
            if (!string.IsNullOrEmpty(e.name))
                Debug.Log($"UI element: name={e.name}, type={e.GetType().Name}");
        }

        // --- scope everything under navCard ---
        var card = root.Q<VisualElement>("navCard");
        if (card == null)
        {
            Debug.LogError("NavigationUITK: navCard not found in visual tree.");
            return;
        }

        cardHeader  = card.Q<VisualElement>("cardHeader");
        cardContent = card.Q<VisualElement>("cardContent");
        chevron     = card.Q<Label>("chevron");

        if (cardHeader != null && cardContent != null)
        {
            SetCollapsed(false);

            cardHeader.RegisterCallback<ClickEvent>(_ =>
            {
                Debug.Log("NavigationUITK: cardHeader clicked");
                SetCollapsed(!isCollapsed);
            });
        }
        else
        {
            Debug.LogWarning(
                $"NavigationUITK: header found = {cardHeader != null}, content found = {cardContent != null}"
            );
        }

        fromDropdown  = card.Q<DropdownField>("fromDropdown");
        toDropdown    = card.Q<DropdownField>("toDropdown");
        floorDropdown = card.Q<DropdownField>("floorDropdown");
        navigateBtn   = card.Q<Button>("navigateBtn");
        refocusBtn    = card.Q<Button>("refocusBtn");

        Debug.Log(
            $"NavigationUITK: from={fromDropdown != null}, " +
            $"to={toDropdown != null}, " +
            $"floor={floorDropdown != null}, " +
            $"navBtn={navigateBtn != null}, " +
            $"refocus={refocusBtn != null}"
        );

        if (fromDropdown == null || toDropdown == null || floorDropdown == null || navigateBtn == null)
        {
            if (fromDropdown == null)  Debug.LogError("NavigationUITK: Missing fromDropdown");
            if (toDropdown == null)    Debug.LogError("NavigationUITK: Missing toDropdown");
            if (floorDropdown == null) Debug.LogError("NavigationUITK: Missing floorDropdown");
            if (navigateBtn == null)   Debug.LogError("NavigationUITK: Missing navigateBtn");
            return;
        }

        // --- UI -> controller ---
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

        if (refocusBtn != null)
            refocusBtn.clicked += OnRefocusClicked;

        // --- controller -> UI ---
        nav.FloorsChanged += RefreshFloors;
        nav.ActiveFloorChanged += RefreshActiveFloor;
        nav.SelectionChanged += RefreshSelections;
        nav.NavigationStateChanged += OnNavigationStateChanged;

        RefreshFloors();
        RefreshActiveFloor();
        RefreshSelections();

        OnNavigationStateChanged(false);
        RefreshNavigateButtonState();
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
        if (nav.IsRouteActive)
        {
            nav.CancelAndResetToPlaceholder();
            return;
        }

        // Otherwise: start navigation
        nav.StartNavigation();

        SetControlsLocked(true);
    }

    void OnNavigationStateChanged(bool navigating)
    {
        isNavigating = navigating;
        if (navigateBtn == null || nav == null) return;

        // Disable dropdowns ONLY during animation
        SetControlsLocked(isNavigating);

        // Button label depends on route state, not animation state
        if (nav.IsRouteActive)
        {
            navigateBtn.text = "Hủy chỉ đường";
            navigateBtn.AddToClassList("cancel");
            navigateBtn.SetEnabled(true); // always allow cancel when route active
        }
        else
        {
            navigateBtn.text = "Bắt đầu đi";
            navigateBtn.RemoveFromClassList("cancel");
            RefreshNavigateButtonState();
        }

        RefreshRefocusButtonState();
    }

    void RefreshNavigateButtonState()
    {
        if (navigateBtn == null || nav == null) return;

        // If a route is active, button must stay enabled to cancel
        if (nav.IsRouteActive)
        {
            navigateBtn.SetEnabled(true);
            return;
        }

        // If animating and no route yet, keep disabled (optional)
        if (isNavigating)
        {
            navigateBtn.SetEnabled(false);
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

        // Only locked during animation
        refocusBtn.SetEnabled(!isNavigating && cam != null);
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
    
    void SetCollapsed(bool collapsed)
    {
        isCollapsed = collapsed;

        if (cardContent != null)
        {
            cardContent.style.display = isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
        }
        if (chevron != null)
        {
            chevron.text = collapsed ? "▸" : "▾";
        }
    }
}
