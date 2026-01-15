// NavigationUITK.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

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

    Button refocusBtn;

    bool isNavigating = false;

    const string FROM_PLACEHOLDER = "Chọn điểm bắt đầu";
    const string TO_PLACEHOLDER = "Chọn điểm đến";

    // --- UI card refs ---
    VisualElement card;              // <-- IMPORTANT: field, not local
    VisualElement cardHeader;
    VisualElement cardContent;
    Label chevron;
    bool isCollapsed = false;

    IVisualElementScheduledItem uiPickScheduler;
    IPanel panel;
    VisualElement rootVE;

    // Optional debug toggle
    [Header("Debug")]
    public bool logPicked = false;

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

        rootVE = root;

        // IMPORTANT: root.panel can be null in Start in some setups.
        // We'll lazily initialize panel in UpdateCameraUiBlock().
        panel = root.panel;

        // Prevent full-screen root from being picked everywhere.
        root.pickingMode = PickingMode.Ignore;

        Debug.Log($"NavigationUITK: root child count = {root.childCount}");

        // --- scope everything under navCard ---
        card = root.Q<VisualElement>("navCard");
        if (card == null)
        {
            Debug.LogError("NavigationUITK: navCard not found in visual tree.");
            return;
        }

        // Ensure navCard itself is pickable
        card.pickingMode = PickingMode.Position;

        // Start scheduler AFTER we have rootVE.
        uiPickScheduler = rootVE.schedule.Execute(UpdateCameraUiBlock).Every(16);

        cardHeader = card.Q<VisualElement>("cardHeader");
        cardContent = card.Q<VisualElement>("cardContent");
        chevron = card.Q<Label>("chevron");

        if (cardHeader != null && cardContent != null)
        {
            SetCollapsed(false);

            cardHeader.RegisterCallback<ClickEvent>(_ =>
            {
                SetCollapsed(!isCollapsed);
            });
        }

        fromDropdown = card.Q<DropdownField>("fromDropdown");
        toDropdown = card.Q<DropdownField>("toDropdown");
        floorDropdown = card.Q<DropdownField>("floorDropdown");
        navigateBtn = card.Q<Button>("navigateBtn");
        refocusBtn = card.Q<Button>("refocusBtn");

        if (fromDropdown == null || toDropdown == null || floorDropdown == null || navigateBtn == null)
        {
            if (fromDropdown == null) Debug.LogError("NavigationUITK: Missing fromDropdown");
            if (toDropdown == null) Debug.LogError("NavigationUITK: Missing toDropdown");
            if (floorDropdown == null) Debug.LogError("NavigationUITK: Missing floorDropdown");
            if (navigateBtn == null) Debug.LogError("NavigationUITK: Missing navigateBtn");
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
        if (cam != null) cam.blockInputByUI = false;
        uiPickScheduler?.Pause();

        if (nav == null) return;

        nav.FloorsChanged -= RefreshFloors;
        nav.ActiveFloorChanged -= RefreshActiveFloor;
        nav.SelectionChanged -= RefreshSelections;
        nav.NavigationStateChanged -= OnNavigationStateChanged;

        if (navigateBtn != null)
            navigateBtn.clicked -= OnNavigateButtonClicked;

        if (refocusBtn != null)
            refocusBtn.clicked -= OnRefocusClicked;
    }

    void Update()
    {
        RefreshRefocusButtonState();
    }

    void SetControlsLocked(bool locked)
    {
        if (fromDropdown != null) fromDropdown.SetEnabled(!locked);
        if (toDropdown != null) toDropdown.SetEnabled(!locked);
        if (floorDropdown != null) floorDropdown.SetEnabled(!locked);

        if (refocusBtn != null)
            refocusBtn.SetEnabled(!locked);
    }

    void OnNavigateButtonClicked()
    {
        if (nav == null) return;

        if (nav.IsRouteActive)
        {
            nav.CancelAndResetToPlaceholder();
            return;
        }

        nav.StartNavigation();
        SetControlsLocked(true);
    }

    void OnNavigationStateChanged(bool navigating)
    {
        isNavigating = navigating;
        if (navigateBtn == null || nav == null) return;

        SetControlsLocked(isNavigating);

        if (nav.IsRouteActive)
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

        RefreshRefocusButtonState();
    }

    void RefreshNavigateButtonState()
    {
        if (navigateBtn == null || nav == null) return;

        if (nav.IsRouteActive)
        {
            navigateBtn.SetEnabled(true);
            return;
        }

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

    void OnRefocusClicked()
    {
        if (cam == null) return;
        cam.RefocusNow();
    }

    void RefreshRefocusButtonState()
    {
        if (refocusBtn == null) return;
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
        var points = nav.ActivePointNames ?? new List<string>();

        fromDropdown.choices = new List<string>(points);
        toDropdown.choices = new List<string>(points);

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
            cardContent.style.display = isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;

        if (chevron != null)
            chevron.text = collapsed ? "▸" : "▾";
    }

    void UpdateCameraUiBlock()
    {
        if (cam == null || uiDocument == null) return;

        // Lazy init panel
        if (panel == null)
        {
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            panel = root.panel;
            if (panel == null) return;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);

        VisualElement picked = panel.Pick(panelPos);

        bool block = false;

        // A) Pointer capture = UI currently interacting (dragging/scrolling/popup etc.)
        // NOTE: returns IEventHandler, not VisualElement.
        IEventHandler capturingHandler = panel.GetCapturingElement(PointerId.mousePointerId);
        if (capturingHandler != null)
            block = true;

        // B) Hovered element is inside navCard
        if (!block && picked != null && card != null)
        {
            var ve = picked;
            while (ve != null)
            {
                if (ve == card)
                {
                    block = true;
                    break;
                }
                ve = ve.parent;
            }
        }

        // C) Dropdown popup lists often live outside navCard hierarchy
        if (!block && picked != null)
        {
            if (LooksLikeDropdownPopup(picked))
                block = true;
        }

        cam.blockInputByUI = block;

        if (logPicked)
        {
            // Safe logging: capturing is IEventHandler, might be VisualElement
            var capturingVE = capturingHandler as VisualElement;

            if (picked != null || capturingHandler != null)
            {
                string pickedStr = picked != null ? $"{picked.name}/{picked.GetType().Name}" : "null";
                string capStr = capturingVE != null ? $"{capturingVE.name}/{capturingVE.GetType().Name}"
                                                    : (capturingHandler != null ? capturingHandler.GetType().Name : "null");

                Debug.Log($"UIBlock: block={block} picked={pickedStr} capturing={capStr}");
            }
        }
    }


    bool LooksLikeDropdownPopup(VisualElement ve)
    {
        // Walk upward a bit; popup list items are nested.
        int steps = 0;
        while (ve != null && steps++ < 12)
        {
            string n = ve.name ?? "";

            // Name heuristics
            if (n.IndexOf("unity-dropdown", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("popup", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // Class heuristics (depends on Unity version/theme)
            if (ve.ClassListContains("unity-base-dropdown")) return true;
            if (ve.ClassListContains("unity-base-popup-field")) return true;
            if (ve.ClassListContains("unity-popup-window")) return true;
            if (ve.ClassListContains("unity-list-view")) return true;

            ve = ve.parent;
        }

        return false;
    }
}
