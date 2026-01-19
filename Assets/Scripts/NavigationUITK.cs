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
    const string TO_PLACEHOLDER   = "Chọn điểm đến";

    // --- UI refs ---
    VisualElement rootVE;
    IPanel panel;

    VisualElement card;
    VisualElement cardHeader;
    VisualElement cardContent;
    VisualElement topbar;

    Button collapseBtn;
    bool isCollapsed = false;

    // Hover flags (event-driven, reliable)
    bool overCard = false;
    bool overTopbar = false;

    IVisualElementScheduledItem uiPickScheduler;

    [Header("Debug")]
    public bool logPicked = false;

    // Keep delegates so we can unsubscribe properly
    EventCallback<ClickEvent> headerClickCb;
    Action collapseBtnClickAction;

    // ---------- Instruction Modal ----------
    [Header("Instruction Modal")]
    [SerializeField] bool showInstructionsOnlyOnce = true;

    VisualElement instructionOverlay;
    Button instructionCloseBtn;
    Button helpBtn; // optional

    bool isModalOpen = false;

    const string INSTR_SEEN_KEY = "INSTRUCTIONS_SEEN";

    // Keep delegates for unsubscribe
    Action instructionCloseAction;
    Action helpBtnAction;

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
        panel  = root.panel;

        // Prevent full-screen root from being picked everywhere.
        root.pickingMode = PickingMode.Ignore;

        // ---------- Instruction Modal Init (do early) ----------
        InitInstructionModal(root);

        // --- navCard ---
        card = root.Q<VisualElement>("navCard");
        if (card == null)
        {
            Debug.LogError("NavigationUITK: navCard not found in visual tree.");
            return;
        }
        card.pickingMode = PickingMode.Position;

        // --- topbar ---
        topbar = root.Q<VisualElement>("topbar");
        if (topbar == null)
        {
            Debug.LogWarning("NavigationUITK: topbar not found. Header won't block camera.");
        }
        else
        {
            topbar.pickingMode = PickingMode.Position;
        }

        ForcePickableTree(card);
        ForcePickableTree(topbar);

        RegisterHoverTracking();

        // Start scheduler AFTER we have rootVE.
        uiPickScheduler = rootVE.schedule.Execute(UpdateCameraUiBlock).Every(16);

        // --- Collapsible refs ---
        cardHeader  = card.Q<VisualElement>("cardHeader");
        cardContent = card.Q<VisualElement>("cardContent");
        collapseBtn = card.Q<Button>("collapseBtn");

        if (cardHeader != null && cardContent != null)
        {
            // Header click toggles
            headerClickCb = _ => SetCollapsed(!isCollapsed);
            cardHeader.RegisterCallback(headerClickCb);
        }

        if (collapseBtn != null)
        {
            // Stop bubbling so button click doesn't also trigger header click
            collapseBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            collapseBtnClickAction = () => SetCollapsed(!isCollapsed);
            collapseBtn.clicked += collapseBtnClickAction;
        }

        // ✅ Start collapsed
        SetCollapsed(true);

        // --- Other UI refs ---
        fromDropdown  = card.Q<DropdownField>("fromDropdown");
        toDropdown    = card.Q<DropdownField>("toDropdown");
        floorDropdown = card.Q<DropdownField>("floorDropdown");
        navigateBtn   = card.Q<Button>("navigateBtn");
        refocusBtn    = card.Q<Button>("refocusBtn");

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

    void InitInstructionModal(VisualElement root)
    {
        instructionOverlay = root.Q<VisualElement>("instructionOverlay");
        instructionCloseBtn = root.Q<Button>("instructionCloseBtn");
        helpBtn = root.Q<Button>("helpBtn"); // optional button in your UXML

        if (instructionOverlay == null)
        {
            // It's optional, but you asked for it. Log so you know you missed UXML.
            Debug.LogWarning("NavigationUITK: instructionOverlay not found (instruction modal disabled).");
            return;
        }

        // Root is PickingMode.Ignore => overlay MUST be pickable to intercept events.
        instructionOverlay.pickingMode = PickingMode.Position;

        // Extra safety: stop pointer events from going to UI behind
        instructionOverlay.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        instructionOverlay.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
        instructionOverlay.RegisterCallback<PointerMoveEvent>(e => e.StopPropagation());

        instructionCloseAction = CloseInstructions;
        if (instructionCloseBtn != null)
            instructionCloseBtn.clicked += instructionCloseAction;

        if (helpBtn != null)
        {
            helpBtnAction = OpenInstructions;
            helpBtn.clicked += helpBtnAction;
        }

        // Show on first run (optional)
        bool seen = PlayerPrefs.GetInt(INSTR_SEEN_KEY, 0) == 1;
        if (!showInstructionsOnlyOnce || !seen)
            OpenInstructions();
        else
            instructionOverlay.AddToClassList("hidden");
    }

    public void OpenInstructions()
    {
        if (instructionOverlay == null) return;

        isModalOpen = true;
        instructionOverlay.RemoveFromClassList("hidden");

        // Optional: if you want to lock dropdown interaction behind modal:
        // SetControlsLocked(true);
    }

    public void CloseInstructions()
    {
        if (instructionOverlay == null) return;

        isModalOpen = false;
        instructionOverlay.AddToClassList("hidden");

        PlayerPrefs.SetInt(INSTR_SEEN_KEY, 1);
        PlayerPrefs.Save();

        // Optional:
        // SetControlsLocked(isNavigating);
    }

    void RegisterHoverTracking()
    {
        if (card != null)
        {
            card.RegisterCallback<PointerEnterEvent>(_ => overCard = true, TrickleDown.TrickleDown);
            card.RegisterCallback<PointerLeaveEvent>(_ => overCard = false, TrickleDown.TrickleDown);
        }

        if (topbar != null)
        {
            topbar.RegisterCallback<PointerEnterEvent>(_ => overTopbar = true, TrickleDown.TrickleDown);
            topbar.RegisterCallback<PointerLeaveEvent>(_ => overTopbar = false, TrickleDown.TrickleDown);
        }
    }

    void OnDisable()
    {
        if (cam != null) cam.blockInputByUI = false;
        uiPickScheduler?.Pause();

        // Unregister UI callbacks cleanly
        if (cardHeader != null && headerClickCb != null)
            cardHeader.UnregisterCallback(headerClickCb);

        if (collapseBtn != null && collapseBtnClickAction != null)
            collapseBtn.clicked -= collapseBtnClickAction;

        if (instructionCloseBtn != null && instructionCloseAction != null)
            instructionCloseBtn.clicked -= instructionCloseAction;

        if (helpBtn != null && helpBtnAction != null)
            helpBtn.clicked -= helpBtnAction;

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
        if (fromDropdown != null)  fromDropdown.SetEnabled(!locked);
        if (toDropdown != null)    toDropdown.SetEnabled(!locked);
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
            cardContent.style.display = isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;

        if (card != null)
        {
            if (isCollapsed) card.AddToClassList("card--collapsed");
            else card.RemoveFromClassList("card--collapsed");
        }
    }

    void UpdateCameraUiBlock()
    {
        if (cam == null || uiDocument == null) return;

        // Modal open => ALWAYS block camera input
        if (isModalOpen)
        {
            cam.blockInputByUI = true;
            return;
        }

        if (panel == null)
        {
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            panel = root.panel;
            if (panel == null) return;
        }

        bool block = false;

        // A) Pointer capture = UI currently interacting (dropdown popup / scrolling / drag etc.)
        IEventHandler capturingHandler = panel.GetCapturingElement(PointerId.mousePointerId);
        if (capturingHandler != null)
            block = true;

        // B) Hover flags (topbar/card)
        if (!block && (overCard || overTopbar))
            block = true;

        // C) Fallback: use Pick only for dropdown popup detection
        VisualElement picked = null;
        if (!block || logPicked)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                Vector2 panelPos  = RuntimePanelUtils.ScreenToPanel(panel, screenPos);
                picked = panel.Pick(panelPos);

                if (!block && picked != null && LooksLikeDropdownPopup(picked))
                    block = true;
            }
        }

        cam.blockInputByUI = block;

        if (logPicked)
        {
            var capturingVE = capturingHandler as VisualElement;

            string pickedStr = picked != null ? $"{picked.name}/{picked.GetType().Name}" : "null";
            string capStr = capturingVE != null ? $"{capturingVE.name}/{capturingVE.GetType().Name}"
                                                : (capturingHandler != null ? capturingHandler.GetType().Name : "null");

            Debug.Log($"UIBlock: block={block} overCard={overCard} overTopbar={overTopbar} picked={pickedStr} capturing={capStr}");
        }
    }

    bool LooksLikeDropdownPopup(VisualElement ve)
    {
        int steps = 0;
        while (ve != null && steps++ < 16)
        {
            string n = ve.name ?? "";

            if (n.IndexOf("unity-dropdown", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("popup", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (ve.ClassListContains("unity-base-dropdown")) return true;
            if (ve.ClassListContains("unity-base-popup-field")) return true;
            if (ve.ClassListContains("unity-popup-window")) return true;
            if (ve.ClassListContains("unity-list-view")) return true;

            ve = ve.parent;
        }

        return false;
    }

    static void ForcePickableTree(VisualElement ve)
    {
        if (ve == null) return;

        ve.pickingMode = PickingMode.Position;

        foreach (var child in ve.Children())
            ForcePickableTree(child);
    }
}
