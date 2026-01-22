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

    VisualElement rootVE;
    IPanel panel;

    VisualElement card;
    VisualElement cardHeader;
    VisualElement cardContent;
    VisualElement topbar;

    Button collapseBtn;
    bool isCollapsed = false;

    // Pointer gating state
    bool uiPointerDown = false;

    // Keep delegates so we can unsubscribe properly
    Action collapseBtnClickAction;

    // ---------- Instruction Modal ----------
    [Header("Instruction Modal")]
    VisualElement instructionOverlay;
    VisualElement instructionModal;
    Button instructionCloseBtn;
    Button helpBtn;
    Label instructionBody;

    bool isModalOpen = false;

    // Keep delegates for unsubscribe
    Action instructionCloseAction;
    Action helpBtnAction;

    // Cached callbacks
    EventCallback<PointerDownEvent> blockerDownCbAllow;
    EventCallback<PointerUpEvent> blockerUpCbAllow;
    EventCallback<PointerMoveEvent> blockerMoveCbAllow;
    EventCallback<WheelEvent> blockerWheelCbAllow;

    EventCallback<PointerDownEvent> blockerDownCbStop;
    EventCallback<PointerUpEvent> blockerUpCbStop;
    EventCallback<PointerMoveEvent> blockerMoveCbStop;
    EventCallback<WheelEvent> blockerWheelCbStop;

    // Global release callbacks (catch missed PointerUp)
    EventCallback<PointerUpEvent> globalPointerUpCb;
    EventCallback<PointerCancelEvent> globalPointerCancelCb;

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

        // ---------- Instruction Modal Init ----------
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
        if (topbar != null) topbar.pickingMode = PickingMode.Position;

        // ✅ Global safety net: if PointerUp gets eaten by dropdown popup/capture,
        // this still clears uiPointerDown.
        RegisterGlobalPointerRelease();

        // ✅ Immediate UI blocking (without breaking button clicks)
        RegisterImmediateUiBlocking();

        // --- Collapsible refs ---
        cardHeader  = card.Q<VisualElement>("cardHeader");
        cardContent = card.Q<VisualElement>("cardContent");
        collapseBtn = card.Q<Button>("collapseBtn");

        if (collapseBtn != null)
        {
            collapseBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            collapseBtnClickAction = () => SetCollapsed(!isCollapsed);
            collapseBtn.clicked += collapseBtnClickAction;
        }

        // Start collapsed
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

        UpdateBlockFromState();
    }

    void InitInstructionModal(VisualElement root)
    {
        instructionOverlay   = root.Q<VisualElement>("instructionOverlay");
        instructionModal     = root.Q<VisualElement>("instructionModal");
        instructionCloseBtn  = root.Q<Button>("instructionCloseBtn");
        helpBtn              = root.Q<Button>("helpBtn");
        instructionBody      = root.Q<Label>("instructionBody");

        if (instructionBody != null)
        {
            instructionBody.text =
                "• Nhấn nút kính lúp để hiển thị/ẩn menu chỉ đường\n" +
                "• Chọn “Bắt đầu từ phòng” và “Tới phòng”\n" +
                "• Nhấn “Bắt đầu đi” để xem đường đi\n" +
                "• Nhấn “Hủy chỉ đường” để ngừng vẽ đường đi\n" +
                "• Chọn tầng để xem các phòng\n" +
                "• “Quay lại góc nhìn ban đầu” để quay lại góc nhìn";
        }

        if (instructionOverlay == null)
        {
            Debug.LogWarning("NavigationUITK: instructionOverlay not found (instruction modal disabled).");
            return;
        }

        instructionOverlay.pickingMode = PickingMode.Position;

        instructionCloseAction = CloseInstructions;
        if (instructionCloseBtn != null)
            instructionCloseBtn.clicked += instructionCloseAction;

        if (helpBtn != null)
        {
            helpBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            helpBtnAction = OpenInstructions;
            helpBtn.clicked += helpBtnAction;
        }

        if (instructionModal != null)
            instructionModal.pickingMode = PickingMode.Position;

        instructionOverlay.AddToClassList("hidden");
        isModalOpen = false;
    }

    public void OpenInstructions()
    {
        if (instructionOverlay == null) return;

        isModalOpen = true;
        instructionOverlay.RemoveFromClassList("hidden");

        // modal is open -> block immediately
        UpdateBlockFromState();
    }

    public void CloseInstructions()
    {
        if (instructionOverlay == null) return;

        isModalOpen = false;
        instructionOverlay.AddToClassList("hidden");

        // closing modal -> unblock unless actively pressing UI
        UpdateBlockFromState();
    }

    void RegisterGlobalPointerRelease()
    {
        if (rootVE == null) return;

        globalPointerUpCb = OnAnyPointerUp_Global;
        globalPointerCancelCb = OnAnyPointerCancel_Global;

        // TrickleDown = we still get it even if something stops bubbling
        rootVE.RegisterCallback(globalPointerUpCb, TrickleDown.TrickleDown);
        rootVE.RegisterCallback(globalPointerCancelCb, TrickleDown.TrickleDown);
    }

    void OnAnyPointerUp_Global(PointerUpEvent e)
    {
        // ✅ Always release. Don't depend on pointerId (dropdown/popups can break it).
        uiPointerDown = false;
        UpdateBlockFromState();
    }

    void OnAnyPointerCancel_Global(PointerCancelEvent e)
    {
        uiPointerDown = false;
        UpdateBlockFromState();
    }

    void RegisterImmediateUiBlocking()
    {
        blockerDownCbAllow  = OnUiPointerDown_Allow;
        blockerUpCbAllow    = OnUiPointerUp_Allow;
        blockerMoveCbAllow  = OnUiPointerMove_Allow;
        blockerWheelCbAllow = OnUiWheel_Allow;

        blockerDownCbStop  = OnOverlayPointerDown_Stop;
        blockerUpCbStop    = OnOverlayPointerUp_Stop;
        blockerMoveCbStop  = OnOverlayPointerMove_Stop;
        blockerWheelCbStop = OnOverlayWheel_Stop;

        RegisterBlockerAllow(card);
        RegisterBlockerAllow(topbar);

        // Overlay: stop click-through ONLY on backdrop
        if (instructionOverlay != null)
        {
            instructionOverlay.RegisterCallback(blockerDownCbStop);
            instructionOverlay.RegisterCallback(blockerUpCbStop);
            instructionOverlay.RegisterCallback(blockerMoveCbStop);
            instructionOverlay.RegisterCallback(blockerWheelCbStop);
        }
    }

    void RegisterBlockerAllow(VisualElement ve)
    {
        if (ve == null) return;

        ve.pickingMode = PickingMode.Position;

        ve.RegisterCallback(blockerDownCbAllow, TrickleDown.TrickleDown);
        ve.RegisterCallback(blockerUpCbAllow, TrickleDown.TrickleDown);
        ve.RegisterCallback(blockerMoveCbAllow, TrickleDown.TrickleDown);
        ve.RegisterCallback(blockerWheelCbAllow, TrickleDown.TrickleDown);
    }

    // ---- Allowing callbacks (card/topbar) ----
    void OnUiPointerDown_Allow(PointerDownEvent e)
    {
        uiPointerDown = true;
        UpdateBlockFromState();
        // DO NOT stop propagation
    }

    void OnUiPointerUp_Allow(PointerUpEvent e)
    {
        uiPointerDown = false;
        UpdateBlockFromState();
        // DO NOT stop propagation
    }

    void OnUiPointerMove_Allow(PointerMoveEvent e)
    {
        // If dragging UI, keep blocked
        if (uiPointerDown)
            UpdateBlockFromState();
    }

    void OnUiWheel_Allow(WheelEvent e)
    {
        // Scroll counts as "interacting"
        if (cam != null) cam.blockInputByUI = true;
        // DO NOT stop propagation
    }

    // ---- Stop callbacks (overlay backdrop only) ----
    void OnOverlayPointerDown_Stop(PointerDownEvent e)
    {
        if (instructionOverlay == null) return;
        if (e.target != instructionOverlay) return; // allow modal button clicks

        uiPointerDown = true;
        UpdateBlockFromState();
        e.StopPropagation();
    }

    void OnOverlayPointerUp_Stop(PointerUpEvent e)
    {
        if (instructionOverlay == null) return;
        if (e.target != instructionOverlay) return;

        uiPointerDown = false;
        UpdateBlockFromState();
        e.StopPropagation();
    }

    void OnOverlayPointerMove_Stop(PointerMoveEvent e)
    {
        if (instructionOverlay == null) return;
        if (e.target != instructionOverlay) return;

        UpdateBlockFromState();
        e.StopPropagation();
    }

    void OnOverlayWheel_Stop(WheelEvent e)
    {
        if (cam != null) cam.blockInputByUI = true;
        // don't stop wheel
    }

    void UpdateBlockFromState()
    {
        if (cam == null) return;

        // ✅ Only two reasons to block:
        // - modal open
        // - actively pressing UI
        cam.blockInputByUI = isModalOpen || uiPointerDown;
    }

    void Update()
    {
        RefreshRefocusButtonState();

        // ✅ HARD WATCHDOG:
        // UI Toolkit can miss PointerUp (dropdown popup, capture, etc).
        // If no mouse buttons are actually pressed anymore, release the UI lock.
        if (!isModalOpen && uiPointerDown)
        {
            var mouse = Mouse.current;
            bool anyMouseDown =
                mouse != null &&
                (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed);

            var ts = Touchscreen.current;
            bool anyTouchDown = false;
            if (ts != null)
            {
                var touches = ts.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].isInProgress)
                    {
                        anyTouchDown = true;
                        break;
                    }
                }
            }

            if (!anyMouseDown && !anyTouchDown)
            {
                uiPointerDown = false;
                UpdateBlockFromState();
            }
        }
    }

    void OnDisable()
    {
        if (cam != null) cam.blockInputByUI = false;

        if (collapseBtn != null && collapseBtnClickAction != null)
            collapseBtn.clicked -= collapseBtnClickAction;

        if (instructionCloseBtn != null && instructionCloseAction != null)
            instructionCloseBtn.clicked -= instructionCloseAction;

        if (helpBtn != null && helpBtnAction != null)
            helpBtn.clicked -= helpBtnAction;

        if (card != null && blockerDownCbAllow != null)
        {
            card.UnregisterCallback(blockerDownCbAllow, TrickleDown.TrickleDown);
            card.UnregisterCallback(blockerUpCbAllow, TrickleDown.TrickleDown);
            card.UnregisterCallback(blockerMoveCbAllow, TrickleDown.TrickleDown);
            card.UnregisterCallback(blockerWheelCbAllow, TrickleDown.TrickleDown);
        }

        if (topbar != null && blockerDownCbAllow != null)
        {
            topbar.UnregisterCallback(blockerDownCbAllow, TrickleDown.TrickleDown);
            topbar.UnregisterCallback(blockerUpCbAllow, TrickleDown.TrickleDown);
            topbar.UnregisterCallback(blockerMoveCbAllow, TrickleDown.TrickleDown);
            topbar.UnregisterCallback(blockerWheelCbAllow, TrickleDown.TrickleDown);
        }

        if (instructionOverlay != null && blockerDownCbStop != null)
        {
            instructionOverlay.UnregisterCallback(blockerDownCbStop);
            instructionOverlay.UnregisterCallback(blockerUpCbStop);
            instructionOverlay.UnregisterCallback(blockerMoveCbStop);
            instructionOverlay.UnregisterCallback(blockerWheelCbStop);
        }

        if (rootVE != null && globalPointerUpCb != null)
        {
            rootVE.UnregisterCallback(globalPointerUpCb, TrickleDown.TrickleDown);
            rootVE.UnregisterCallback(globalPointerCancelCb, TrickleDown.TrickleDown);
        }

        if (nav != null)
        {
            nav.FloorsChanged -= RefreshFloors;
            nav.ActiveFloorChanged -= RefreshActiveFloor;
            nav.SelectionChanged -= RefreshSelections;
            nav.NavigationStateChanged -= OnNavigationStateChanged;
        }

        if (navigateBtn != null) navigateBtn.clicked -= OnNavigateButtonClicked;
        if (refocusBtn != null)  refocusBtn.clicked -= OnRefocusClicked;
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
}
