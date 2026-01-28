// NavigationUITK.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

// Script UI Toolkit cho hệ thống dẫn đường:
// - Kết nối UI (Dropdown/Button) với NavigationController
// - Quản lý trạng thái nút "Bắt đầu đi" / "Hủy chỉ đường"
// - Hỗ trợ thu gọn/mở rộng thẻ điều khiển (navCard)
// - Hỗ trợ bảng hướng dẫn (instruction modal)
// - Chặn input camera khi người dùng đang tương tác UI (pointer gating)
public class NavigationUITK : MonoBehaviour
{
    [Header("Thành phần UI Document và Dẫn đường")]
    public UIDocument uiDocument; // UIDocument chứa UI Toolkit
    public NavigationController nav; // Controller xử lý logic dẫn đường

    [Header("Nút quay lại góc nhìn ban đầu (Tùy chọn)")]
    public CameraController cam; // CameraController để refocus và chặn input khi thao tác UI

    DropdownField fromDropdown;  // Dropdown chọn điểm bắt đầu
    DropdownField toDropdown;    // Dropdown chọn điểm đến
    DropdownField floorDropdown; // Dropdown chọn tầng (hoặc "Tất cả các tầng")
    Button navigateBtn;          // Nút bắt đầu/hủy dẫn đường
    Button refocusBtn;           // Nút quay lại góc nhìn ban đầu

    bool isNavigating = false; // Đang trong quá trình camera chạy dẫn đường hay không

    const string FROM_PLACEHOLDER = "Chọn điểm bắt đầu"; // Placeholder cho dropdown From
    const string TO_PLACEHOLDER   = "Chọn điểm đến";     // Placeholder cho dropdown To

    VisualElement rootVE; // Root của UI Toolkit
    IPanel panel;         // Panel của UI Toolkit (không dùng trực tiếp nhiều, nhưng cache lại)

    VisualElement card;        // Thẻ UI chính (navCard)
    VisualElement cardHeader;  // Header của thẻ
    VisualElement cardContent; // Content của thẻ (phần có các dropdown và nút)
    VisualElement topbar;      // Thanh topbar (nếu có)

    Button collapseBtn;   // Nút thu gọn/mở rộng
    bool isCollapsed = false; // Trạng thái thu gọn hiện tại

    // Trạng thái "đang nhấn/giữ" trên UI (để chặn input camera)
    bool uiPointerDown = false;

    // Theo dõi dropdown khi đang mở (để chặn input camera)
    bool anyDropdownOpen = false;

    // Giữ delegate để unsubscribe đúng cách
    Action collapseBtnClickAction;

    // ---------- Bảng hướng dẫn sử dụng ----------
    [Header("Bảng hướng dẫn")]
    VisualElement instructionOverlay; // Lớp phủ toàn màn hình (backdrop)
    VisualElement instructionModal;   // Khung modal hướng dẫn
    Button instructionCloseBtn;       // Nút đóng modal
    Button helpBtn;                   // Nút mở modal
    Label instructionBody;            // Nội dung hướng dẫn

    bool isModalOpen = false; // Modal đang mở hay không

    // Giữ delegate để unsubscribe
    Action instructionCloseAction;
    Action helpBtnAction;

    // Callback cache cho cơ chế chặn input (đăng ký/huỷ đăng ký dễ và sạch)
    EventCallback<PointerDownEvent> blockerDownCbAllow;
    EventCallback<PointerUpEvent> blockerUpCbAllow;
    EventCallback<PointerMoveEvent> blockerMoveCbAllow;
    EventCallback<WheelEvent> blockerWheelCbAllow;

    EventCallback<PointerDownEvent> blockerDownCbStop;
    EventCallback<PointerUpEvent> blockerUpCbStop;
    EventCallback<PointerMoveEvent> blockerMoveCbStop;
    EventCallback<WheelEvent> blockerWheelCbStop;

    // Callback toàn cục để "bắt" PointerUp/Cancel bị thất lạc (dropdown popup có thể ăn sự kiện)
    EventCallback<PointerUpEvent> globalPointerUpCb;
    EventCallback<PointerCancelEvent> globalPointerCancelCb;

    void Start()
    {
        // Bắt buộc phải có UIDocument
        if (uiDocument == null)
        {
            Debug.LogError("NavigationUITK: uiDocument is NOT assigned in the inspector.");
            return;
        }

        // Lấy root UI
        var root = uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("NavigationUITK: rootVisualElement is null.");
            return;
        }

        rootVE = root;
        panel  = root.panel;

        // Tránh root full-screen bị "bắt" click khắp nơi (chỉ bắt ở những phần tử cần)
        root.pickingMode = PickingMode.Ignore;

        // ---------- Khởi tạo Instruction Modal ----------
        InitInstructionModal(root);

        // --- Lấy navCard ---
        card = root.Q<VisualElement>("navCard");
        if (card == null)
        {
            Debug.LogError("NavigationUITK: navCard not found in visual tree.");
            return;
        }

        // navCard phải cho phép bắt pointer để chặn input camera
        card.pickingMode = PickingMode.Position;

        // --- Lấy topbar (nếu có) ---
        topbar = root.Q<VisualElement>("topbar");
        if (topbar != null) topbar.pickingMode = PickingMode.Position;

        // Đăng ký "lưới an toàn" toàn cục: nếu PointerUp bị nuốt vẫn reset được uiPointerDown
        RegisterGlobalPointerRelease();
        RegisterGlobalDropdownRelease();

        // Đăng ký chặn input camera ngay lập tức khi thao tác UI (nhưng vẫn cho button click hoạt động)
        RegisterImmediateUiBlocking();

        // --- Lấy các phần tử phục vụ thu gọn/mở rộng ---
        cardHeader  = card.Q<VisualElement>("cardHeader");
        cardContent = card.Q<VisualElement>("cardContent");
        collapseBtn = card.Q<Button>("collapseBtn");

        if (collapseBtn != null)
        {
            // Ngăn click ở nút thu gọn lan ra ngoài (tránh ảnh hưởng chặn input/logic khác)
            collapseBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            // Lưu action để unsubscribe
            collapseBtnClickAction = () => SetCollapsed(!isCollapsed);
            collapseBtn.clicked += collapseBtnClickAction;
        }

        // Mặc định bắt đầu ở trạng thái thu gọn
        SetCollapsed(true);

        // --- Lấy các phần tử UI chính ---
        fromDropdown  = card.Q<DropdownField>("fromDropdown");
        toDropdown    = card.Q<DropdownField>("toDropdown");
        floorDropdown = card.Q<DropdownField>("floorDropdown");
        navigateBtn   = card.Q<Button>("navigateBtn");
        refocusBtn    = card.Q<Button>("refocusBtn");

        // Kiểm tra thiếu phần tử bắt buộc thì báo lỗi và dừng
        if (fromDropdown == null || toDropdown == null || floorDropdown == null || navigateBtn == null)
        {
            if (fromDropdown == null)  Debug.LogError("NavigationUITK: Missing fromDropdown");
            if (toDropdown == null)    Debug.LogError("NavigationUITK: Missing toDropdown");
            if (floorDropdown == null) Debug.LogError("NavigationUITK: Missing floorDropdown");
            if (navigateBtn == null)   Debug.LogError("NavigationUITK: Missing navigateBtn");
            return;
        }

        // --- UI -> Controller ---
        // Khi đổi tầng từ dropdown => gọi nav.SetFloorFromDropdown theo index dropdown
        floorDropdown.RegisterValueChangedCallback(_ => nav.SetFloorFromDropdown(floorDropdown.index));

        // Theo dõi dropdown mở/đóng để chặn input camera
        RegisterDropdownTracking(fromDropdown);
        RegisterDropdownTracking(toDropdown);
        RegisterDropdownTracking(floorDropdown);

        // Khi đổi điểm bắt đầu => set From và cập nhật trạng thái nút navigate
        fromDropdown.RegisterValueChangedCallback(_ =>
        {
            nav.SetFrom(fromDropdown.index);
            RefreshNavigateButtonState();
        });

        // Khi đổi điểm đến => set To và cập nhật trạng thái nút navigate
        toDropdown.RegisterValueChangedCallback(_ =>
        {
            nav.SetTo(toDropdown.index);
            RefreshNavigateButtonState();
        });

        // Nút bắt đầu/hủy dẫn đường
        navigateBtn.clicked += OnNavigateButtonClicked;

        // Nút refocus (nếu có)
        if (refocusBtn != null)
            refocusBtn.clicked += OnRefocusClicked;

        // --- Controller -> UI ---
        // Khi controller thay đổi dữ liệu => UI tự refresh
        nav.FloorsChanged += RefreshFloors;
        nav.ActiveFloorChanged += RefreshActiveFloor;
        nav.SelectionChanged += RefreshSelections;
        nav.NavigationStateChanged += OnNavigationStateChanged;

        // Load dữ liệu ban đầu lên UI
        RefreshFloors();
        RefreshActiveFloor();
        RefreshSelections();

        // Đồng bộ trạng thái điều hướng lúc đầu
        OnNavigationStateChanged(false);
        RefreshNavigateButtonState();
        RefreshRefocusButtonState();

        // Đồng bộ trạng thái chặn input camera
        UpdateBlockFromState();
    }

    void InitInstructionModal(VisualElement root)
    {
        // Lấy các phần tử modal theo name trong UXML
        instructionOverlay   = root.Q<VisualElement>("instructionOverlay");
        instructionModal     = root.Q<VisualElement>("instructionModal");
        instructionCloseBtn  = root.Q<Button>("instructionCloseBtn");
        helpBtn              = root.Q<Button>("helpBtn");
        instructionBody      = root.Q<Label>("instructionBody");

        // Gán nội dung hướng dẫn (nếu có label)
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

        // Nếu không có overlay thì coi như không dùng modal hướng dẫn
        if (instructionOverlay == null)
        {
            Debug.LogWarning("NavigationUITK: instructionOverlay not found (instruction modal disabled).");
            return;
        }

        // Overlay cần bắt pointer để chặn click xuyên xuống scene
        instructionOverlay.pickingMode = PickingMode.Position;

        // Nút đóng modal
        instructionCloseAction = CloseInstructions;
        if (instructionCloseBtn != null)
            instructionCloseBtn.clicked += instructionCloseAction;

        // Nút help mở modal
        if (helpBtn != null)
        {
            // Ngăn click lan ra ngoài
            helpBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            helpBtnAction = OpenInstructions;
            helpBtn.clicked += helpBtnAction;
        }

        // Modal cũng nên bắt pointer (để nút bên trong hoạt động ổn định)
        if (instructionModal != null)
            instructionModal.pickingMode = PickingMode.Position;

        // Ẩn overlay lúc đầu
        instructionOverlay.AddToClassList("hidden");
        isModalOpen = false;
    }

    public void OpenInstructions()
    {
        // Không có overlay thì thôi
        if (instructionOverlay == null) return;

        // Mở modal
        isModalOpen = true;
        instructionOverlay.RemoveFromClassList("hidden");

        // Khi modal mở thì phải chặn input camera ngay
        UpdateBlockFromState();
    }

    public void CloseInstructions()
    {
        // Không có overlay thì thôi
        if (instructionOverlay == null) return;

        // Đóng modal
        isModalOpen = false;
        instructionOverlay.AddToClassList("hidden");

        // Khi đóng modal thì chỉ bỏ chặn nếu không đang nhấn UI
        UpdateBlockFromState();
    }

    void RegisterGlobalPointerRelease()
    {
        if (rootVE == null) return;

        // Lưu callback để có thể huỷ đúng cách
        globalPointerUpCb = OnAnyPointerUp_Global;
        globalPointerCancelCb = OnAnyPointerCancel_Global;

        // TrickleDown: vẫn nhận được callback dù ở bubble có StopPropagation
        rootVE.RegisterCallback(globalPointerUpCb, TrickleDown.TrickleDown);
        rootVE.RegisterCallback(globalPointerCancelCb, TrickleDown.TrickleDown);
    }

    void OnAnyPointerUp_Global(PointerUpEvent e)
    {
        // Luôn release (không phụ thuộc pointerId vì dropdown popup/capture có thể phá)
        uiPointerDown = false;
        UpdateBlockFromState();
    }

    void OnAnyPointerCancel_Global(PointerCancelEvent e)
    {
        // Khi pointer bị cancel cũng phải release
        uiPointerDown = false;
        UpdateBlockFromState();
    }

    void RegisterImmediateUiBlocking()
    {
        // Tạo callback cho vùng UI "cho phép click" (card/topbar)
        blockerDownCbAllow  = OnUiPointerDown_Allow;
        blockerUpCbAllow    = OnUiPointerUp_Allow;
        blockerMoveCbAllow  = OnUiPointerMove_Allow;
        blockerWheelCbAllow = OnUiWheel_Allow;

        // Tạo callback cho overlay backdrop (chặn click xuyên)
        blockerDownCbStop  = OnOverlayPointerDown_Stop;
        blockerUpCbStop    = OnOverlayPointerUp_Stop;
        blockerMoveCbStop  = OnOverlayPointerMove_Stop;
        blockerWheelCbStop = OnOverlayWheel_Stop;

        // Đăng ký cho card và topbar: vẫn cho thao tác UI bình thường, chỉ dùng để set uiPointerDown
        RegisterBlockerAllow(card);
        RegisterBlockerAllow(topbar);

        // Overlay: chỉ chặn click-through khi bấm vào "backdrop" (không chặn nút trong modal)
        if (instructionOverlay != null)
        {
            instructionOverlay.RegisterCallback(blockerDownCbStop);
            instructionOverlay.RegisterCallback(blockerUpCbStop);
            instructionOverlay.RegisterCallback(blockerMoveCbStop);
            instructionOverlay.RegisterCallback(blockerWheelCbStop);
        }
    }

    void RegisterDropdownTracking(DropdownField dropdown)
    {
        if (dropdown == null) return;

        dropdown.RegisterCallback<PointerDownEvent>(e =>
        {
            anyDropdownOpen = true;
            UpdateBlockFromState();
            // Don't StopPropagation; let the dropdown open normally
        }, TrickleDown.TrickleDown);

        // Selecting an item => dropdown closes
        dropdown.RegisterValueChangedCallback(_ =>
        {
            rootVE.schedule.Execute(() =>
            {
                anyDropdownOpen = false;
                UpdateBlockFromState();
            }).ExecuteLater(50);
        });
    }

    void CheckAllDropdownsClosed()
    {
        // Check if ANY of our tracked dropdowns currently has focus
        bool anyHasFocus = false;
        
        if (panel != null && panel.focusController != null)
        {
            var focused = panel.focusController.focusedElement as VisualElement;
            
            if (focused != null)
            {
                // Check if the focused element IS one of our dropdowns
                if (focused == fromDropdown || focused == toDropdown || focused == floorDropdown)
                {
                    anyHasFocus = true;
                }
                else
                {
                    // Check if focused element is a CHILD of any dropdown (the popup list)
                    var parent = focused;
                    while (parent != null)
                    {
                        if (parent == fromDropdown || parent == toDropdown || parent == floorDropdown)
                        {
                            anyHasFocus = true;
                            break;
                        }
                        parent = parent.parent;
                    }
                }
            }
        }
        
        anyDropdownOpen = anyHasFocus;
        UpdateBlockFromState();
        
        // If still open, schedule another check
        if (anyDropdownOpen)
        {
            rootVE.schedule.Execute(CheckAllDropdownsClosed).ExecuteLater(100);
        }
    }

    void RegisterBlockerAllow(VisualElement ve)
    {
        if (ve == null) return;

        // Đảm bảo element có thể bắt pointer
        ve.pickingMode = PickingMode.Position;

        // TrickleDown để bắt được cả khi các con xử lý sự kiện
        ve.RegisterCallback(blockerDownCbAllow, TrickleDown.TrickleDown);
        ve.RegisterCallback(blockerUpCbAllow, TrickleDown.TrickleDown);
        ve.RegisterCallback(blockerMoveCbAllow, TrickleDown.TrickleDown);
        ve.RegisterCallback(blockerWheelCbAllow, TrickleDown.TrickleDown);
    }

    // ---- Callback "ALLOW" (card/topbar) ----
    void OnUiPointerDown_Allow(PointerDownEvent e)
    {
        // Khi nhấn vào UI => chặn input camera
        uiPointerDown = true;
        UpdateBlockFromState();
        // Không stop propagation để button/dropdown vẫn hoạt động
    }

    void OnUiPointerUp_Allow(PointerUpEvent e)
    {
        // Nhả khỏi UI => bỏ chặn (nếu không có modal)
        uiPointerDown = false;
        UpdateBlockFromState();
        // Không stop propagation
    }

    void OnUiPointerMove_Allow(PointerMoveEvent e)
    {
        // Nếu đang kéo trên UI thì giữ trạng thái chặn
        if (uiPointerDown)
            UpdateBlockFromState();
    }

    void OnUiWheel_Allow(WheelEvent e)
    {
        // Lăn chuột trong UI cũng xem như đang tương tác => chặn input camera
        if (cam != null) cam.blockInputByUI = true;
        // Không stop propagation
    }

    // ---- Callback "STOP" (chỉ overlay backdrop) ----
    void OnOverlayPointerDown_Stop(PointerDownEvent e)
    {
        // Chỉ chặn nếu target đúng là overlay backdrop (để nút trong modal vẫn click được)
        if (instructionOverlay == null) return;
        if (e.target != instructionOverlay) return;

        uiPointerDown = true;
        UpdateBlockFromState();

        // StopPropagation để không click xuyên xuống scene phía sau
        e.StopPropagation();
    }

    void OnOverlayPointerUp_Stop(PointerUpEvent e)
    {
        if (instructionOverlay == null) return;
        if (e.target != instructionOverlay) return;

        uiPointerDown = false;
        UpdateBlockFromState();

        // StopPropagation để không click xuyên
        e.StopPropagation();
    }

    void OnOverlayPointerMove_Stop(PointerMoveEvent e)
    {
        if (instructionOverlay == null) return;
        if (e.target != instructionOverlay) return;

        // Khi đang rê trên backdrop thì vẫn chặn input camera
        UpdateBlockFromState();

        // StopPropagation để tránh ảnh hưởng tới scene bên dưới
        e.StopPropagation();
    }

    void OnOverlayWheel_Stop(WheelEvent e)
    {
        // Khi scroll trên overlay thì vẫn chặn camera
        if (cam != null) cam.blockInputByUI = true;

        // Không stop wheel để UI có thể xử lý scroll nếu cần
    }

    void UpdateBlockFromState()
    {
        if (cam == null) return;

        // Chỉ chặn input camera khi:
        // - Modal đang mở, hoặc
        // - Người dùng đang nhấn/giữ trên UI, hoặc
        // - Có dropdown đang mở
        cam.blockInputByUI = isModalOpen || uiPointerDown || anyDropdownOpen;
        Debug.Log($"BLOCK? {(cam!=null && cam.blockInputByUI)} | modal={isModalOpen} down={uiPointerDown} dd={anyDropdownOpen}");
    }

    void Update()
    {
        // Cập nhật trạng thái nút refocus liên tục (phụ thuộc isNavigating)
        RefreshRefocusButtonState();

        // WATCHDOG:
        // UI Toolkit có thể bị mất PointerUp (dropdown popup/capture, v.v.)
        // Nếu thực tế không còn nút chuột/touch nào đang nhấn thì release UI lock.
        // Không reset khi dropdown đang mở
        if (!isModalOpen && uiPointerDown && !anyDropdownOpen)
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

            // Nếu không còn input thật sự => reset uiPointerDown
            if (!anyMouseDown && !anyTouchDown)
            {
                uiPointerDown = false;
                UpdateBlockFromState();
            }
        }
    }

    void OnDisable()
    {
        // Khi disable script, đảm bảo không còn chặn camera
        if (cam != null) cam.blockInputByUI = false;

        // Gỡ sự kiện thu gọn
        if (collapseBtn != null && collapseBtnClickAction != null)
            collapseBtn.clicked -= collapseBtnClickAction;

        // Gỡ sự kiện đóng modal
        if (instructionCloseBtn != null && instructionCloseAction != null)
            instructionCloseBtn.clicked -= instructionCloseAction;

        // Gỡ sự kiện mở modal
        if (helpBtn != null && helpBtnAction != null)
            helpBtn.clicked -= helpBtnAction;

        // Huỷ đăng ký callback chặn input trên card
        if (card != null && blockerDownCbAllow != null)
        {
            card.UnregisterCallback(blockerDownCbAllow, TrickleDown.TrickleDown);
            card.UnregisterCallback(blockerUpCbAllow, TrickleDown.TrickleDown);
            card.UnregisterCallback(blockerMoveCbAllow, TrickleDown.TrickleDown);
            card.UnregisterCallback(blockerWheelCbAllow, TrickleDown.TrickleDown);
        }

        // Huỷ đăng ký callback chặn input trên topbar
        if (topbar != null && blockerDownCbAllow != null)
        {
            topbar.UnregisterCallback(blockerDownCbAllow, TrickleDown.TrickleDown);
            topbar.UnregisterCallback(blockerUpCbAllow, TrickleDown.TrickleDown);
            topbar.UnregisterCallback(blockerMoveCbAllow, TrickleDown.TrickleDown);
            topbar.UnregisterCallback(blockerWheelCbAllow, TrickleDown.TrickleDown);
        }

        // Huỷ đăng ký callback chặn input trên overlay
        if (instructionOverlay != null && blockerDownCbStop != null)
        {
            instructionOverlay.UnregisterCallback(blockerDownCbStop);
            instructionOverlay.UnregisterCallback(blockerUpCbStop);
            instructionOverlay.UnregisterCallback(blockerMoveCbStop);
            instructionOverlay.UnregisterCallback(blockerWheelCbStop);
        }

        // Huỷ lưới an toàn pointer up/cancel toàn cục
        if (rootVE != null && globalPointerUpCb != null)
        {
            rootVE.UnregisterCallback(globalPointerUpCb, TrickleDown.TrickleDown);
            rootVE.UnregisterCallback(globalPointerCancelCb, TrickleDown.TrickleDown);
        }

        // Huỷ sự kiện từ controller -> UI
        if (nav != null)
        {
            nav.FloorsChanged -= RefreshFloors;
            nav.ActiveFloorChanged -= RefreshActiveFloor;
            nav.SelectionChanged -= RefreshSelections;
            nav.NavigationStateChanged -= OnNavigationStateChanged;
        }

        // Huỷ sự kiện button
        if (navigateBtn != null) navigateBtn.clicked -= OnNavigateButtonClicked;
        if (refocusBtn != null)  refocusBtn.clicked -= OnRefocusClicked;
    }

    void SetControlsLocked(bool locked)
    {
        // Khoá/mở dropdown và nút refocus khi camera đang chạy
        if (fromDropdown != null)  fromDropdown.SetEnabled(!locked);
        if (toDropdown != null)    toDropdown.SetEnabled(!locked);
        if (floorDropdown != null) floorDropdown.SetEnabled(!locked);

        if (refocusBtn != null)
            refocusBtn.SetEnabled(!locked);
    }

    void OnNavigateButtonClicked()
    {
        if (nav == null) return;

        // Nếu route đang active => bấm sẽ huỷ route và reset placeholder
        if (nav.IsRouteActive)
        {
            nav.CancelAndResetToPlaceholder();
            return;
        }

        // Bắt đầu dẫn đường
        nav.StartNavigation();

        // Khi bấm bắt đầu, khoá điều khiển (đến khi camera chạy xong hoặc trạng thái thay đổi)
        SetControlsLocked(true);
    }

    void OnNavigationStateChanged(bool navigating)
    {
        // navigating = true khi camera đang chạy, false khi camera dừng
        isNavigating = navigating;

        if (navigateBtn == null || nav == null) return;

        // Khoá UI khi camera đang chạy
        SetControlsLocked(isNavigating);

        if (nav.IsRouteActive)
        {
            // Route đã active => nút chuyển sang "Hủy chỉ đường"
            navigateBtn.text = "Hủy chỉ đường";
            navigateBtn.AddToClassList("cancel");
            navigateBtn.SetEnabled(true);
        }
        else
        {
            // Route chưa active => nút là "Bắt đầu đi"
            navigateBtn.text = "Bắt đầu đi";
            navigateBtn.RemoveFromClassList("cancel");
            RefreshNavigateButtonState();
        }

        // Cập nhật nút refocus theo trạng thái mới
        RefreshRefocusButtonState();
    }

    void RefreshNavigateButtonState()
    {
        if (navigateBtn == null || nav == null) return;

        // Nếu route đang active thì luôn cho bấm để hủy
        if (nav.IsRouteActive)
        {
            navigateBtn.SetEnabled(true);
            return;
        }

        // Nếu camera đang chạy thì không cho bấm
        if (isNavigating)
        {
            navigateBtn.SetEnabled(false);
            return;
        }

        // Chỉ bật nút khi chọn đủ From/To và không trùng nhau
        bool valid = nav.SelectedFrom >= 0 &&
                     nav.SelectedTo >= 0 &&
                     nav.SelectedFrom != nav.SelectedTo;

        navigateBtn.SetEnabled(valid);
    }

    void OnRefocusClicked()
    {
        // Gọi camera refocus về góc nhìn ban đầu
        if (cam == null) return;
        cam.RefocusNow();
    }

    void RefreshRefocusButtonState()
    {
        // Nút refocus chỉ bật khi:
        // - không đang navigating
        // - có cam
        if (refocusBtn == null) return;
        refocusBtn.SetEnabled(!isNavigating && cam != null);
    }

    void RefreshFloors()
    {
        // Lấy danh sách tên tầng từ controller
        var floors = nav.FloorNames;
        if (floors == null || floors.Count == 0) return;

        // Cập nhật choices cho floorDropdown
        floorDropdown.choices = new List<string>(floors);

        // Set index hiện tại theo controller (đã clamp)
        int idx = Mathf.Clamp(nav.SelectedFloorDropdownIndex, 0, floors.Count - 1);
        floorDropdown.index = idx;

        // Set giá trị hiển thị mà không bắn event
        floorDropdown.SetValueWithoutNotify(floors[idx]);
    }

    void RefreshActiveFloor()
    {
        // Lấy danh sách tên điểm từ controller (thực tế là "tất cả điểm")
        var points = nav.ActivePointNames ?? new List<string>();

        // Cập nhật choices cho From/To
        fromDropdown.choices = new List<string>(points);
        toDropdown.choices   = new List<string>(points);

        // Refresh selection theo state controller
        RefreshSelections();
    }

    void RefreshSelections()
    {
        var points = nav.ActivePointNames;

        // Nếu không có điểm => reset dropdown về placeholder
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

        // -------- FROM --------
        if (nav.SelectedFrom < 0 || nav.SelectedFrom >= points.Count)
        {
            // Chưa chọn hoặc index sai => placeholder
            fromDropdown.SetValueWithoutNotify(FROM_PLACEHOLDER);
            fromDropdown.index = -1;
        }
        else
        {
            // Set index và text theo điểm đã chọn
            fromDropdown.index = nav.SelectedFrom;
            fromDropdown.SetValueWithoutNotify(points[nav.SelectedFrom]);
        }

        // -------- TO --------
        if (nav.SelectedTo < 0 || nav.SelectedTo >= points.Count)
        {
            // Chưa chọn hoặc index sai => placeholder
            toDropdown.SetValueWithoutNotify(TO_PLACEHOLDER);
            toDropdown.index = -1;
        }
        else
        {
            // Set index và text theo điểm đã chọn
            toDropdown.index = nav.SelectedTo;
            toDropdown.SetValueWithoutNotify(points[nav.SelectedTo]);
        }

        // Sau khi refresh selection thì refresh trạng thái nút
        RefreshNavigateButtonState();
        RefreshRefocusButtonState();
    }

    void SetCollapsed(bool collapsed)
    {
        // Lưu trạng thái
        isCollapsed = collapsed;

        // Ẩn/hiện phần nội dung
        if (cardContent != null)
            cardContent.style.display = isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;

        // Thêm/xoá class để CSS (USS) thay đổi style khi collapsed
        if (card != null)
        {
            if (isCollapsed) card.AddToClassList("card--collapsed");
            else card.RemoveFromClassList("card--collapsed");
        }
    }

    void RegisterGlobalDropdownRelease()
    {
        if (rootVE == null) return;

        rootVE.RegisterCallback<PointerDownEvent>(e =>
        {
            if (!anyDropdownOpen) return;

            var target = e.target as VisualElement;
            if (target == null) return;

            // Clicking on the dropdown field itself should NOT release the latch
            if (IsInside(target, fromDropdown) || IsInside(target, toDropdown) || IsInside(target, floorDropdown))
                return;

            // Otherwise: user clicked elsewhere => treat as dropdown closed
            anyDropdownOpen = false;
            UpdateBlockFromState();
        }, TrickleDown.TrickleDown);
    }

    bool IsInside(VisualElement target, VisualElement ancestor)
    {
        if (ancestor == null) return false;
        var p = target;
        while (p != null)
        {
            if (p == ancestor) return true;
            p = p.parent;
        }
        return false;
    }
}
