// NavigationController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

// Bộ điều khiển điều hướng: quản lý tầng, điểm đến, vẽ đường dẫn (LineRenderer),
// hiển thị nhãn tên (TextMeshPro), xử lý thang máy, và hiệu ứng dẫn đường.
public class NavigationController : MonoBehaviour
{
    [Header("Cấu trúc tầng")]
    public Transform floorsRoot; // Transform gốc chứa các tầng trong scene

    [Header("Đường dẫn Line Renderer")]
    public LineRenderer line;        // LineRenderer dùng để vẽ đường đi
    public Material lineMaterial;    // Material cho đường vẽ

    [Header("Nhãn tên địa điểm")]
    public TMP_FontAsset labelFont;  // Font TextMeshPro cho nhãn
    public float labelHeight = 1f;   // Độ cao nhãn so với waypoint
    public float labelSize = 8f;     // Kích thước chữ cơ bản của nhãn

    [Header("Lớp (Layer) cho nhãn tên")]
    public string labelLayerName = "WaypointLabels"; // Layer dùng để đặt nhãn (tuỳ hệ thống camera/culling)

    [Header("Tự động co giãn kích thước nhãn")]
    public float minLabelSize = 0.8f;        // Scale nhỏ nhất của nhãn
    public float maxLabelSize = 2.2f;        // Scale lớn nhất của nhãn
    public float sizeAt1Meter = 1.4f;        // (Dự phòng) scale tham chiếu tại 1 mét
    public float scaleStartDistance = 2f;    // Khoảng cách bắt đầu nội suy scale
    public float scaleEndDistance = 30f;     // Khoảng cách kết thúc nội suy scale

    [Header("Camera")]
    public Transform mainCamera; // Transform camera chính (dùng để scale nhãn và điều khiển camera)

    [Header("Hiệu ứng biểu tượng (Ping)")]
    public GameObject startPingPrefab; // Prefab ping tại điểm bắt đầu
    public GameObject endPingPrefab;   // Prefab ping tại điểm kết thúc

    [Header("Kiểu dáng đường kẻ")]
    public float lineWidth = 0.25f; // Độ dày đường vẽ
    public int lineCornerVertices = 8; // Độ bo góc (số đỉnh) cho đường
    public int lineCapVertices = 8;    // Độ bo đầu mút (số đỉnh) cho đường
    public Gradient lineGradient;      // Gradient màu cho đường vẽ
    public AnimationCurve widthCurve = AnimationCurve.Linear(0, 1, 1, 1); // Curve độ dày theo chiều dài đường

    [Header("Tốc độ vẽ đường dẫn")]
    public float revealSpeed = 12f;        // Tốc độ "hiện dần" đường (mét/giây)
    public float lineHeightOffset = 0.05f; // Độ nâng đường lên để tránh z-fighting

    [Header("Thang máy (Điểm dừng tại mỗi tầng)")]
    public ElevatorShaft[] elevators; // Danh sách trục thang máy và các điểm dừng theo tầng
    [Header("Tốc độ di chuyển (giây)")]
    public float walkSpeed = 1.4f;             // Tốc độ đi bộ (m/s) để ước lượng thời gian
    public float elevatorSpeed = 2.5f;         // Tốc độ thang máy (m/s) để ước lượng thời gian
    public float elevatorAvgWaitSeconds = 5f;  // Thời gian chờ thang máy trung bình (giây)
    public bool forceVerticalAtShaftCenter = true; // Ép đoạn đi thang máy đi thẳng đứng qua tâm trục

    [Header("Tự động thiết lập thang máy")]
    public Transform elevatorsRoot;           // Transform gốc chứa cấu trúc thang máy trong scene
    public bool autoBuildElevatorsOnStart = true; // Tự động quét và dựng dữ liệu thang máy khi Start

    [Serializable]
    public class ElevatorStop
    {
        public int floorIndex;          // Chỉ số tầng (theo danh sách nội bộ đã sort)
        public Transform[] doorPoints;  // Các điểm cửa thang (có thể nhiều cửa)
    }

    [Serializable]
    public class ElevatorShaft
    {
        public string name;              // Tên trục thang máy
        public Transform shaftCenterXZ;  // Tâm trục theo XZ (dùng để ép đi thẳng đứng)
        public ElevatorStop[] stops;     // Các điểm dừng theo tầng
    }

    [Header("Cuộn cấu trúc bề mặt (Texture Scroll)")]
    public bool enableTextureScroll = false; // Bật cuộn texture trên đường vẽ
    public float textureScrollSpeed = 1f;    // Tốc độ cuộn texture
    private float textureOffset = 0f;        // Offset texture hiện tại

    [Header("Độ trong suốt khi dẫn đường")]
    public bool makeFloorsTransparentInNav = true; // Bật chế độ làm kiến trúc trong suốt khi dẫn đường
    [Range(0.05f, 1f)] public float navAlpha = 0.35f; // (Dự phòng) alpha tổng quát

    [Header("Phân chia độ trong suốt")]
    [Range(0.05f, 1f)] public float navFloorAlpha = 0.22f; // Alpha cho sàn/tầng
    [Range(0.05f, 1f)] public float navWallAlpha = 0.45f;  // Alpha cho tường
    [Range(0.05f, 1f)] public float navOtherAlpha = 0.55f; // Alpha cho các thành phần khác

    [Header("Tên các đối tượng trong kiến trúc")]
    public string floorRootName = "Floor"; // Tên node gốc của sàn (để phân loại alpha)
    public string wallRootName = "Wall";   // Tên node gốc của tường (để phân loại alpha)

    [Header("Hiển thị các vật dụng (Props)")]
    public string propsRootName = "Props";         // Tên node gốc chứa props trong mỗi tầng
    public bool hidePropsWhenTransparent = true;   // Ẩn props khi bật trong suốt

    // Danh sách cache Transform "Props" theo từng tầng (để bật/tắt nhanh)
    private readonly List<Transform> floorPropsRoots = new();

    // Cache: Renderer -> mảng material gốc (để khôi phục sau khi tắt trong suốt)
    private readonly Dictionary<Renderer, Material[]> originalMats = new();

    // Cache: Material gốc -> bản clone trong suốt (tái sử dụng, tránh tạo quá nhiều material)
    private readonly Dictionary<Material, Material> transparentCloneCache = new();

    // ----- Trạng thái public (dùng cho UI Toolkit) -----
    public IReadOnlyList<string> FloorNames => floorDropdownOptions;  // Danh sách tên tầng cho dropdown
    public IReadOnlyList<string> ActivePointNames => allPointNames;   // Danh sách tên các điểm (tất cả tầng)

    public int ActiveFloorIndex { get; private set; } = 0; // Tầng đang active (theo index nội bộ)

    // -1 nghĩa là chưa chọn
    public int SelectedFrom { get; private set; } = -1; // Điểm bắt đầu (index trong allPoints)
    public int SelectedTo { get; private set; } = -1;   // Điểm kết thúc (index trong allPoints)

    // True sau khi camera chạy xong, giữ nguyên cho tới khi người dùng huỷ
    public bool IsRouteActive { get; private set; } = false;

    // Các sự kiện để UI cập nhật theo trạng thái
    public event Action FloorsChanged;
    public event Action ActiveFloorChanged;
    public event Action SelectionChanged;
    public event Action<bool> NavigationStateChanged;

    // Danh sách tầng đã load (theo thứ tự sort bởi FloorId.id)
    private readonly List<Transform> floors = new();
    private readonly List<string> floorNames = new();

    // Mỗi tầng: danh sách waypoint + danh sách label đi kèm
    private readonly Dictionary<int, List<Transform>> floorPoints = new();
    private readonly Dictionary<int, List<GameObject>> floorLabels = new();

    // Tất cả waypoint trên tất cả tầng (chỉ "điểm thật")
    private readonly List<Transform> allPoints = new();
    private readonly List<string> allPointNames = new();
    private readonly List<int> allPointFloorIndex = new(); // Map point -> floor index nội bộ

    private NavMeshPath path; // Đối tượng lưu path tính bởi NavMesh

    // Các tầng đang hiển thị
    public IReadOnlyCollection<int> VisibleFloors => visibleFloors;
    private readonly HashSet<int> visibleFloors = new();

    public bool ShowAllFloors { get; private set; } = false; // Đang hiển thị tất cả tầng hay không

    // Danh sách option cho dropdown: "Tất cả các tầng" + tên từng tầng
    public IReadOnlyList<string> FloorDropdownOptions => floorDropdownOptions;
    private readonly List<string> floorDropdownOptions = new();
    public int SelectedFloorDropdownIndex { get; private set; } = 1; // index dropdown hiện tại

    // Dữ liệu khoảng Y của từng tầng để auto switch khi camera di chuyển
    private struct FloorRange
    {
        public Transform floor; // Transform tầng
        public float minY;      // ngưỡng Y thấp
        public float maxY;      // ngưỡng Y cao
    }
    private readonly List<FloorRange> floorRanges = new();

    // Trạng thái dẫn đường
    private GameObject startPingInstance; // Instance ping điểm bắt đầu
    private GameObject endPingInstance;   // Instance ping điểm kết thúc

    private Vector3[] navCorners;   // Mảng corners của đường dẫn cuối cùng
    private float revealDistance = 0f; // Độ dài đã "vẽ hiện" tới
    private float totalDistance = 0f;  // Tổng độ dài của đường dẫn
    private bool isNavigating = false; // Đang chạy dẫn đường/camera hay không

    // Theo dõi tầng của route để khi đi khác tầng thì giữ 2 tầng cùng hiển thị
    private int navFromFloor = -1;
    private int navToFloor = -1;
    private bool navIsCrossFloor = false;

    public Transform visualsRoot; // Nơi đặt các hiệu ứng (pings, v.v.)

    // ---------------------------------------------------------
    // CẢI TIẾN LABEL
    // ---------------------------------------------------------
    private readonly List<TextMeshPro> allLabelTmps = new(); // Cache các TMP label để scale theo khoảng cách
    private Material sharedLabelMaterial; // Material dùng chung cho nhãn (tối ưu và đồng nhất)

    // ---------------------------------------------------------
    // Hỗ trợ "xem trước" route khác tầng (trong suốt + có thể show all)
    // ---------------------------------------------------------
    private bool previewCrossFloorActive = false;   // Đang bật preview khác tầng hay không
    private bool previewForcedAllFloors = false;    // Preview có ép show all floors không
    private int previewRestoreFloorIndex = 0;       // Lưu tầng để restore khi tắt preview
    private int previewRestoreDropdownIndex = 1;    // Lưu dropdown để restore khi tắt preview

    // =========================================================
    // KHỞI TẠO HỆ THỐNG
    // =========================================================
    IEnumerator Start()
    {
        // Chờ 1 frame để các đối tượng trong scene sẵn sàng
        yield return null;

        // Tự động gán các tham chiếu nếu bị null
        AutoAssignIfNull();

        // Nếu không tìm thấy root tầng thì dừng
        if (floorsRoot == null)
        {
            Debug.LogError("NavigationController: floorsRoot is NULL (Environment not found).");
            yield break;
        }

        // Nếu không có camera chính thì dừng
        if (mainCamera == null)
        {
            Debug.LogError("NavigationController: mainCamera is NULL.");
            yield break;
        }

        // Nếu không có LineRenderer thì dừng
        if (line == null)
        {
            Debug.LogError("NavigationController: LineRenderer is NULL.");
            yield break;
        }

        // Tạo material dùng chung cho label nếu có thể
        EnsureSharedLabelMaterial();

        // Khởi tạo NavMeshPath để dùng lại khi tính đường
        path = new NavMeshPath();

        // Gán material cho line nếu người dùng có cung cấp
        if (lineMaterial != null)
            line.material = lineMaterial;

        // Bắt buộc LineRenderer phải có material
        if (line.material == null)
        {
            Debug.LogError("NavigationController: LineRenderer has no material. Assign a URP Unlit material.");
            yield break;
        }

        // Cấu hình LineRenderer cơ bản
        line.useWorldSpace = true;   // Dùng toạ độ thế giới
        line.sortingOrder = 999;     // Đưa lên trên để dễ nhìn
        line.positionCount = 0;      // Chưa vẽ gì lúc đầu

        // Load dữ liệu tầng + waypoints, tạo nhãn
        LoadFloorsAndWaypoints();

        // Nếu bật auto build elevator thì quét cấu trúc thang máy để dựng dữ liệu
        if (autoBuildElevatorsOnStart) AutoBuildElevators();

        // Xây dựng danh sách option cho dropdown tầng
        BuildFloorDropdownOptions();

        // Xây dựng khoảng Y cho từng tầng để auto switch theo camera
        BuildFloorRanges();

        // Thiết lập kiểu dáng đường kẻ
        SetupLineStyle();

        // Báo cho UI biết danh sách tầng đã thay đổi
        FloorsChanged?.Invoke();

        // Mặc định: hiển thị "Tất cả các tầng"
        ShowAllFloors = true;
        SelectedFloorDropdownIndex = 0; // "Tất cả các tầng" là index 0

        // Chọn tầng active mặc định: ưu tiên tầng có FloorId.id = 0, nếu không có thì dùng index 0
        int defaultIdx = FindFloorIndexById(0);
        if (defaultIdx < 0) defaultIdx = 0;
        ActiveFloorIndex = defaultIdx;

        // Hiển thị tất cả tầng
        visibleFloors.Clear();
        for (int i = 0; i < floors.Count; i++)
            visibleFloors.Add(i);

        // Áp dụng hiển thị tầng + label
        ApplyVisibleFloors();

        // Đảm bảo selection nằm trong phạm vi hợp lệ
        ClampSelections();

        // Báo cho UI biết tầng active đã sẵn sàng
        ActiveFloorChanged?.Invoke();
    }

    void Update()
    {
        // Nếu không bật scroll texture thì bỏ qua
        if (!enableTextureScroll) return;

        // Chỉ scroll khi đang dẫn đường (đang vẽ/đang chạy)
        if (!isNavigating) return;

        // Cần có line và material thì mới scroll được
        if (line == null || line.material == null) return;

        // Cộng dồn offset theo thời gian
        textureOffset += textureScrollSpeed * Time.deltaTime;

        // Dịch UV theo trục X để tạo cảm giác đường chạy
        line.material.mainTextureOffset = new Vector2(textureOffset, 0f);
    }

    void LateUpdate()
    {
        // Không có camera thì không scale nhãn
        if (mainCamera == null) return;

        // Duyệt tất cả nhãn và scale theo khoảng cách camera
        for (int i = 0; i < allLabelTmps.Count; i++)
        {
            var tmp = allLabelTmps[i];
            if (!tmp) continue;
            if (!tmp.gameObject.activeInHierarchy) continue;

            // Tính khoảng cách từ camera đến nhãn
            float d = Vector3.Distance(mainCamera.position, tmp.transform.position);

            // Tính t theo nội suy từ khoảng cách (start -> end)
            float t = Mathf.InverseLerp(scaleStartDistance, scaleEndDistance, d);

            // Tính scale s trong khoảng min -> max
            float s = Mathf.Lerp(minLabelSize, maxLabelSize, t);

            // Áp scale đồng đều
            tmp.transform.localScale = Vector3.one * s;
        }
    }

    void AutoAssignIfNull()
    {
        // Nếu floorsRoot null thì thử tìm GameObject tên "Environment"
        if (floorsRoot == null)
        {
            var env = GameObject.Find("Environment");
            if (env != null) floorsRoot = env.transform;
        }

        // Nếu mainCamera null thì lấy Camera.main
        if (mainCamera == null && Camera.main != null)
            mainCamera = Camera.main.transform;
    }

    void BuildFloorDropdownOptions()
    {
        // Xoá danh sách cũ
        floorDropdownOptions.Clear();

        // Option đầu tiên: tất cả tầng
        floorDropdownOptions.Add("Tất cả các tầng");

        // Thêm tên từng tầng
        floorDropdownOptions.AddRange(floorNames);
    }

    // =========================================================
    // TẢI DỮ LIỆU TẦNG VÀ ĐIỂM ĐẾN
    // =========================================================
    void LoadFloorsAndWaypoints()
    {
        // Xoá dữ liệu cũ
        floors.Clear();
        floorPoints.Clear();
        floorLabels.Clear();
        floorNames.Clear();

        floorPropsRoots.Clear();

        allPoints.Clear();
        allPointNames.Clear();
        allPointFloorIndex.Clear();

        allLabelTmps.Clear();

        // Reset lựa chọn
        SelectedFrom = -1;
        SelectedTo = -1;

        // Đọc FloorId và sort theo id ổn định (không phụ thuộc thứ tự trong hierarchy)
        var floorList = new List<(Transform tf, int id, string uiName)>();

        for (int i = 0; i < floorsRoot.childCount; i++)
        {
            Transform floor = floorsRoot.GetChild(i);
            var fid = floor.GetComponent<FloorId>();

            // Nếu có FloorId thì dùng fid.id, nếu không thì fallback theo i
            int stableId = (fid != null) ? fid.id : i;

            // Tên hiển thị UI: ưu tiên fid.displayName, nếu không có thì dùng floor.name
            string uiName = (fid != null && !string.IsNullOrEmpty(fid.displayName)) ? fid.displayName : floor.name;

            floorList.Add((floor, stableId, uiName));
        }

        // Sort tăng dần theo id
        floorList.Sort((a, b) => a.id.CompareTo(b.id));

        // Dựng list theo thứ tự đã sort
        for (int i = 0; i < floorList.Count; i++)
        {
            Transform floor = floorList[i].tf;

            floors.Add(floor);
            floorNames.Add(floorList[i].uiName);

            // Cache root props của tầng (nếu có)
            Transform propsRoot = floor.Find(propsRootName);
            floorPropsRoots.Add(propsRoot);

            var points = new List<Transform>();
            var labels = new List<GameObject>();

            // Tìm node Waypoints trong tầng
            Transform waypoints = floor.Find("Waypoints");
            if (waypoints != null)
            {
                // Lấy tất cả Transform con (kể cả inactive)
                var children = waypoints.GetComponentsInChildren<Transform>(true);

                foreach (var t in children)
                {
                    if (t == waypoints) continue; // bỏ qua chính node "Waypoints"

                    // Lưu waypoint
                    points.Add(t);

                    // Tạo nhãn hiển thị cho waypoint
                    labels.Add(CreateLabel(t));

                    // Lưu danh sách "tất cả điểm" để UI chọn
                    allPoints.Add(t);
                    allPointNames.Add($"{floor.name} - {t.name}");

                    // Lưu floor index nội bộ của điểm (chính là i sau khi sort)
                    allPointFloorIndex.Add(i);
                }
            }

            // Lưu vào dictionary theo floor index
            floorPoints[i] = points;
            floorLabels[i] = labels;
        }
    }

    // Hàm phụ: tìm index nội bộ của tầng dựa theo FloorId.id
    int FindFloorIndexById(int floorId)
    {
        for (int i = 0; i < floors.Count; i++)
        {
            var fid = floors[i].GetComponent<FloorId>();
            if (fid != null && fid.id == floorId)
                return i;
        }
        return -1;
    }

    // =========================================================
    // QUẢN LÝ HIỂN THỊ TẦNG
    // =========================================================
    void ApplyVisibleFloors()
    {
        // Bật/tắt GameObject của từng tầng dựa vào visibleFloors
        for (int i = 0; i < floors.Count; i++)
            floors[i].gameObject.SetActive(visibleFloors.Contains(i));

        // Cập nhật trạng thái nhãn tương ứng
        UpdateLabelsVisibility();
    }

    void UpdateLabelsVisibility()
    {
        // Tắt hết nhãn trước
        foreach (var kv in floorLabels)
            foreach (var label in kv.Value)
                if (label != null) label.SetActive(false);

        // Bật nhãn của các tầng đang hiển thị
        foreach (var idx in visibleFloors)
        {
            if (!floorLabels.ContainsKey(idx)) continue;
            foreach (var label in floorLabels[idx])
                if (label != null) label.SetActive(true);
        }
    }

    public void SetFloor(int index)
    {
        // Khoá chuyển tầng khi camera đang chạy dẫn đường
        if (isNavigating) return;

        if (floors.Count == 0) return;

        // Khi set 1 tầng cụ thể thì không còn chế độ show all
        ShowAllFloors = false;

        // Clamp index trong phạm vi hợp lệ
        index = Mathf.Clamp(index, 0, floors.Count - 1);

        // Nếu tầng không đổi và đang visible thì không làm gì
        if (ActiveFloorIndex == index && visibleFloors.Contains(index)) return;

        // Cập nhật tầng active
        ActiveFloorIndex = index;

        // Dropdown: +1 vì index 0 là "Tất cả các tầng"
        SelectedFloorDropdownIndex = ActiveFloorIndex + 1;

        // Chỉ hiển thị 1 tầng
        visibleFloors.Clear();
        visibleFloors.Add(index);

        ApplyVisibleFloors();

        // Khi chỉ xem 1 tầng ở chế độ thường thì tắt transparency
        ApplyNavTransparency(false);

        // Báo UI cập nhật
        ActiveFloorChanged?.Invoke();
        FloorsChanged?.Invoke();
    }

    public void SetFloorFromDropdown(int dropdownIndex)
    {
        // Log để debug UI
        Debug.Log($"[UI] dropdownIndex={dropdownIndex} option='{floorDropdownOptions[dropdownIndex]}'");

        // Khi camera đang chạy thì khoá thao tác
        if (isNavigating) return;

        // Clamp index dropdown
        dropdownIndex = Mathf.Clamp(dropdownIndex, 0, floorDropdownOptions.Count - 1);
        SelectedFloorDropdownIndex = dropdownIndex;

        // =========================
        // Khi route đang active (SAU KHI camera xong)
        // =========================
        if (IsRouteActive)
        {
            // Chỉ xử lý cho route khác tầng
            if (!navIsCrossFloor || navFromFloor < 0 || navToFloor < 0)
                return;

            if (dropdownIndex == 0)
            {
                // Chọn "Tất cả các tầng" => chỉ hiển thị 2 tầng liên quan route
                ShowAllFloors = false;

                visibleFloors.Clear();
                visibleFloors.Add(navFromFloor);
                visibleFloors.Add(navToFloor);
                ApplyVisibleFloors();

                // Route active => giữ transparency để nhìn xuyên
                ApplyNavTransparency(true);

                FloorsChanged?.Invoke();
                ActiveFloorChanged?.Invoke();
                return;
            }

            int floorIndex = dropdownIndex - 1;

            // Chỉ cho chọn 1 trong 2 tầng của route
            if (floorIndex == navFromFloor || floorIndex == navToFloor)
                FocusFloorForActiveRoute(floorIndex);

            return;
        }

        // =========================
        // Chế độ bình thường (route không active)
        // =========================
        if (dropdownIndex == 0)
        {
            // Show all floors, nhưng không bật transparency
            SetShowAllFloors(true);
        }
        else
        {
            // Chỉ show 1 tầng
            SetShowAllFloors(false);
            SetFloor(dropdownIndex - 1);
        }
    }

    public void SetFloorVisible(int index, bool visible)
    {
        // Hàm này hiện tại chỉ dùng để chọn 1 tầng (visible = true)
        if (visible) SetFloor(index);
    }

    // =========================================================
    // LỰA CHỌN ĐIỂM ĐI VÀ ĐIỂM ĐẾN
    // =========================================================
    void ClampSelections()
    {
        // Không có điểm thì bỏ qua
        if (allPoints.Count == 0) return;

        // Clamp SelectedFrom nếu đã chọn
        if (SelectedFrom >= 0)
            SelectedFrom = Mathf.Clamp(SelectedFrom, 0, allPoints.Count - 1);

        // Clamp SelectedTo nếu đã chọn
        if (SelectedTo >= 0)
            SelectedTo = Mathf.Clamp(SelectedTo, 0, allPoints.Count - 1);
    }

    public void SetFrom(int index)
    {
        // Khoá thao tác khi camera đang chạy
        if (isNavigating) return;

        if (allPoints.Count == 0) return;

        // Huỷ route hiện tại và dọn dẹp (không restore preview)
        CancelNavigation(false);

        // Nếu đang show all floors thì bật transparency khi chọn điểm
        if (ShowAllFloors) ApplyNavTransparency(true);
        else ApplyNavTransparency(false);

        // Lưu điểm bắt đầu
        SelectedFrom = Mathf.Clamp(index, 0, allPoints.Count - 1);

        // Báo UI cập nhật
        SelectionChanged?.Invoke();
    }

    public void SetTo(int index)
    {
        // Khoá thao tác khi camera đang chạy
        if (isNavigating) return;

        if (allPoints.Count == 0) return;

        // Huỷ route hiện tại và dọn dẹp (không restore preview)
        CancelNavigation(false);

        // Nếu đang show all floors thì bật transparency khi chọn điểm
        if (ShowAllFloors) ApplyNavTransparency(true);
        else ApplyNavTransparency(false);

        // Lưu điểm kết thúc
        SelectedTo = Mathf.Clamp(index, 0, allPoints.Count - 1);

        // Báo UI cập nhật
        SelectionChanged?.Invoke();
    }

    // =========================================================
    // NEW: chế độ xem trước route khác tầng (trong suốt + có thể show all)
    // =========================================================
    void SetPreviewCrossFloorMode(bool enabled)
    {
        // Nếu trạng thái không đổi thì thôi
        if (previewCrossFloorActive == enabled) return;
        previewCrossFloorActive = enabled;

        if (enabled)
        {
            // Preview route khác tầng => bật transparency
            ApplyNavTransparency(true);

            // Tuỳ chọn: ép show tất cả tầng trong lúc preview
            if (!ShowAllFloors)
            {
                previewForcedAllFloors = true;
                previewRestoreFloorIndex = ActiveFloorIndex;
                previewRestoreDropdownIndex = SelectedFloorDropdownIndex;

                ShowAllFloors = true;
                visibleFloors.Clear();
                for (int i = 0; i < floors.Count; i++) visibleFloors.Add(i);
                ApplyVisibleFloors();

                // Cập nhật dropdown thành "Tất cả các tầng"
                SelectedFloorDropdownIndex = 0;

                FloorsChanged?.Invoke();
                ActiveFloorChanged?.Invoke();
            }
        }
        else
        {
            // Nếu trước đó preview có ép show all thì restore lại
            if (previewForcedAllFloors)
            {
                previewForcedAllFloors = false;

                ShowAllFloors = false;
                ActiveFloorIndex = Mathf.Clamp(previewRestoreFloorIndex, 0, floors.Count - 1);
                visibleFloors.Clear();
                visibleFloors.Add(ActiveFloorIndex);
                ApplyVisibleFloors();

                SelectedFloorDropdownIndex = previewRestoreDropdownIndex;

                FloorsChanged?.Invoke();
                ActiveFloorChanged?.Invoke();
            }

            // Nếu không có route active thì tắt transparency (trả lại bình thường)
            if (!IsRouteActive)
                ApplyNavTransparency(false);
        }
    }

    // =========================================================
    // XEM TRƯỚC ĐƯỜNG ĐI VÀ DẪN ĐƯỜNG
    // =========================================================
    public void PreviewPath()
    {
        // Không có điểm thì thôi
        if (allPoints.Count == 0) return;

        // Đang chạy dẫn đường thì không preview
        if (isNavigating) return;

        // Chưa chọn đủ điểm thì xoá đường và ping
        if (SelectedFrom < 0 || SelectedTo < 0)
        {
            ClearPath();
            ClearPings();
            SetPreviewCrossFloorMode(false);
            return;
        }

        // Chọn cùng 1 điểm thì không cần vẽ
        if (SelectedFrom == SelectedTo)
        {
            ClearPath();
            ClearPings();
            SetPreviewCrossFloorMode(false);
            return;
        }

        // Nếu route khác tầng thì bật chế độ preview cross-floor
        bool cross = IsCrossFloorRoute();
        SetPreviewCrossFloorMode(cross);

        Vector3 start = allPoints[SelectedFrom].position;
        Vector3 end = allPoints[SelectedTo].position;

        int fromFloor = allPointFloorIndex[SelectedFrom];
        int toFloor = allPointFloorIndex[SelectedTo];

        // Thử dựng route tốt nhất (cầu thang hoặc thang máy)
        if (TryBuildBestRoute(start, fromFloor, end, toFloor, out var corners))
        {
            // Vẽ toàn bộ đường preview
            line.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
                line.SetPosition(i, corners[i] + Vector3.up * lineHeightOffset);

            // Tạo ping preview
            PreviewPings();
        }
        else
        {
            // Nếu không dựng được route thì dọn dẹp
            ClearPath();
            ClearPings();
            SetPreviewCrossFloorMode(false);
        }
    }

    public void StartNavigation()
    {
        // Nếu đang dẫn đường mà bấm lại => huỷ
        if (isNavigating)
        {
            CancelNavigation(true);
            return;
        }

        // Điều kiện tối thiểu để dẫn đường
        if (allPoints.Count == 0) return;
        if (SelectedFrom < 0 || SelectedTo < 0) return;
        if (SelectedFrom == SelectedTo) return;

        // Tắt chế độ preview trước khi vào navigation thật
        if (previewCrossFloorActive) SetPreviewCrossFloorMode(false);

        // Lưu tầng của điểm đi/đến
        navFromFloor = allPointFloorIndex[SelectedFrom];
        navToFloor = allPointFloorIndex[SelectedTo];
        navIsCrossFloor = IsCrossFloorRoute();

        // Nếu show all hoặc route khác tầng => bật transparency để nhìn xuyên
        if (ShowAllFloors || navIsCrossFloor) ApplyNavTransparency(true);
        else ApplyNavTransparency(false);

        // Chuyển sang chỉ focus tầng xuất phát (không cho người dùng đổi tầng lúc route active)
        ExitAllFloorsAndFocusFloor(navFromFloor);

        // Đánh dấu route active
        IsRouteActive = true;
        NavigationStateChanged?.Invoke(true);

        // Spawn ping điểm đi/đến
        SpawnPings();

        Vector3 start = allPoints[SelectedFrom].position;
        Vector3 end = allPoints[SelectedTo].position;

        int fromFloor = allPointFloorIndex[SelectedFrom];
        int toFloor = allPointFloorIndex[SelectedTo];

        // Dựng route tối ưu
        if (!TryBuildBestRoute(start, fromFloor, end, toFloor, out navCorners))
        {
            Debug.LogError("NavigationController: Route build failed (stairs + elevators).");
            ClearPath();
            return;
        }

        // Tính tổng độ dài để vẽ "hiện dần"
        totalDistance = ComputeTotalDistance(navCorners);
        revealDistance = 0f;

        // Bắt đầu trạng thái navigating
        isNavigating = true;

        // Vẽ phần đường ban đầu (rất ngắn)
        DrawLinePoints(GetPartialPath(navCorners, revealDistance));

        // Cần CameraController để điều khiển camera
        var camController = mainCamera.GetComponent<CameraController>();
        if (camController == null)
        {
            Debug.LogError("NavigationController: CameraController not found.");
            CancelNavigation(true);
            return;
        }

        // Chuyển camera sang góc nhìn bird-eye tại điểm xuất phát
        camController.SnapToBirdEye(allPoints[SelectedFrom], 55f, 75f);

        // Cho camera di chuyển dọc theo corners
        camController.MoveBirdEyeAlongCorners(
            navCorners,
            () =>
            {
                // Callback khi camera chạy xong
                if (!isNavigating) return;

                // Vẽ full đường
                revealDistance = totalDistance;
                DrawLinePoints(GetPartialPath(navCorners, revealDistance));

                // Kết thúc trạng thái navigating (camera dừng), nhưng route vẫn active
                isNavigating = false;
                NavigationStateChanged?.Invoke(false);

                // Nếu khác tầng thì hiển thị cả 2 tầng và giữ transparency
                if (navIsCrossFloor && navFromFloor >= 0 && navToFloor >= 0)
                {
                    visibleFloors.Clear();
                    visibleFloors.Add(navFromFloor);
                    visibleFloors.Add(navToFloor);
                    ApplyVisibleFloors();

                    ApplyNavTransparency(true);
                }
            },
            pos =>
            {
                // Callback mỗi bước camera di chuyển (pos là vị trí camera)
                if (!isNavigating) return;

                // Auto kích hoạt tầng dựa theo Y (chỉ khi không show all)
                ActivateFloorByY(pos.y);

                // Tính độ dài đã đi dọc theo path để "reveal" đường
                float distAlong = GetDistanceAlongPath(navCorners, pos);
                revealDistance = Mathf.Clamp(distAlong, 0f, totalDistance);

                // Vẽ đoạn đường đã reveal
                DrawLinePoints(GetPartialPath(navCorners, revealDistance));
            }
        );
    }

    public void CancelNavigation(bool restorePreview = true)
    {
        // Nếu có CameraController thì huỷ chuyển động camera
        var camController = mainCamera != null ? mainCamera.GetComponent<CameraController>() : null;
        if (camController != null)
            camController.CancelMove();

        // Reset trạng thái dẫn đường
        isNavigating = false;
        navCorners = null;
        revealDistance = 0f;
        totalDistance = 0f;

        // Xoá line và ping
        ClearPath();

        // Trả lại vật liệu bình thường (không trong suốt)
        ApplyNavTransparency(false);

        // Reset thông tin route khác tầng
        navFromFloor = -1;
        navToFloor = -1;
        navIsCrossFloor = false;

        // Route không còn active
        IsRouteActive = false;
        NavigationStateChanged?.Invoke(false);

        // Tắt chế độ preview cross-floor
        SetPreviewCrossFloorMode(false);
    }

    public void CancelAndResetToPlaceholder()
    {
        // Huỷ dẫn đường nhưng không bật preview
        CancelNavigation(false);

        // Reset lựa chọn
        SelectedFrom = -1;
        SelectedTo = -1;

        // Báo UI cập nhật lựa chọn
        SelectionChanged?.Invoke();
    }

    bool TryCalculateNavCorners(Vector3 from, Vector3 to, out Vector3[] corners)
    {
        // Hàm tiện ích: tính corners trên NavMesh giữa 2 điểm
        corners = null;

        // Tính path trên NavMesh
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)) return false;

        // Yêu cầu đường phải hoàn chỉnh
        if (path.status != NavMeshPathStatus.PathComplete) return false;

        corners = path.corners;

        // Cần ít nhất 2 điểm để tạo đường
        return corners != null && corners.Length >= 2;
    }

    float PolylineLength(IList<Vector3> pts)
    {
        // Tính tổng chiều dài polyline
        float d = 0f;
        for (int i = 1; i < pts.Count; i++)
            d += Vector3.Distance(pts[i - 1], pts[i]);
        return d;
    }

    bool TryGetElevatorDoors(ElevatorShaft elev, int floorIndex, out Transform[] doors)
    {
        // Tìm danh sách cửa thang máy của 1 shaft tại 1 tầng
        doors = null;
        if (elev == null || elev.stops == null) return false;

        for (int i = 0; i < elev.stops.Length; i++)
        {
            var s = elev.stops[i];
            if (s != null && s.floorIndex == floorIndex &&
                s.doorPoints != null && s.doorPoints.Length > 0)
            {
                doors = s.doorPoints;
                return true;
            }
        }
        return false;
    }

    List<Vector3> BuildElevatorRide(ElevatorShaft elev, Vector3 doorA, Vector3 doorB)
    {
        // Tạo danh sách điểm mô phỏng đoạn "đi thang máy" giữa 2 tầng
        var pts = new List<Vector3>(3);

        // Nếu ép đi thẳng đứng theo tâm trục XZ
        if (forceVerticalAtShaftCenter && elev != null && elev.shaftCenterXZ != null)
        {
            Vector3 shaft = elev.shaftCenterXZ.position;

            // Đi từ cửa A về tâm trục (cùng Y), rồi đi thẳng đứng lên/xuống tới Y cửa B, rồi ra cửa B
            Vector3 a0 = new Vector3(shaft.x, doorA.y, shaft.z);
            Vector3 b0 = new Vector3(shaft.x, doorB.y, shaft.z);

            pts.Add(doorA);
            if ((a0 - doorA).sqrMagnitude > 0.0001f) pts.Add(a0);
            pts.Add(b0);
            if ((doorB - b0).sqrMagnitude > 0.0001f) pts.Add(doorB);
            return pts;
        }

        // Nếu không ép theo tâm trục, mặc định: đi thẳng đứng tại XZ của cửa A
        pts.Add(doorA);
        Vector3 up = new Vector3(doorA.x, doorB.y, doorA.z);
        if ((up - doorA).sqrMagnitude > 0.0001f) pts.Add(up);
        if ((doorB - pts[pts.Count - 1]).sqrMagnitude > 0.0001f) pts.Add(doorB);
        return pts;
    }

    bool TryBuildBestRoute(Vector3 start, int startFloor, Vector3 end, int endFloor, out Vector3[] bestCorners)
    {
        // Dựng route tối ưu giữa 2 điểm:
        // - Nếu cùng tầng: ưu tiên stairsCorners (NavMesh path trực tiếp)
        // - Nếu khác tầng: so sánh thời gian ước lượng giữa đi cầu thang và đi thang máy

        bestCorners = null;

        Vector3[] stairsCorners = null;
        float stairsTime = float.PositiveInfinity;

        // Tính đường trực tiếp (xem như đi cầu thang/đi bộ)
        if (TryCalculateNavCorners(start, end, out stairsCorners))
        {
            float stairsLen = PolylineLength(stairsCorners);
            stairsTime = stairsLen / Mathf.Max(0.01f, walkSpeed);
        }

        // Nếu cùng tầng thì trả về đường trực tiếp nếu có
        if (startFloor == endFloor)
        {
            if (stairsCorners != null)
            {
                bestCorners = stairsCorners;
                return true;
            }
            return false;
        }

        // Khác tầng: tìm route thang máy tốt nhất
        float bestElevTime = float.PositiveInfinity;
        List<Vector3> bestElevPts = null;

        if (elevators != null)
        {
            Debug.Log($"[ELEV] TryBuildBestRoute startFloor={startFloor} endFloor={endFloor} elevators={elevators?.Length ?? 0}");

            for (int e = 0; e < elevators.Length; e++)
            {
                var elev = elevators[e];
                if (elev == null) continue;

                // Kiểm tra shaft này có cửa ở tầng start không
                if (!TryGetElevatorDoors(elev, startFloor, out var startDoors))
                {
                    Debug.LogWarning($"[ELEV] '{elev.name}' has NO doors on startFloor={startFloor}");
                    continue;
                }

                // Kiểm tra shaft này có cửa ở tầng end không
                if (!TryGetElevatorDoors(elev, endFloor, out var endDoors))
                {
                    Debug.LogWarning($"[ELEV] '{elev.name}' has NO doors on endFloor={endFloor}");
                    continue;
                }

                // Thử mọi cặp cửa A (tầng start) và cửa B (tầng end)
                for (int ai = 0; ai < startDoors.Length; ai++)
                {
                    var dA = startDoors[ai];
                    if (dA == null) continue;

                    Vector3 doorA = dA.position;

                    // SamplePosition để đảm bảo điểm cửa nằm trên NavMesh
                    if (!NavMesh.SamplePosition(doorA, out var hitA, 8f, NavMesh.AllAreas))
                    {
                        Debug.LogWarning($"[ELEV] SamplePosition FAIL doorA '{dA.name}' floor={startFloor} pos={dA.position}");
                        continue;
                    }
                    doorA = hitA.position;

                    // Path từ start tới cửa A
                    if (!TryCalculateNavCorners(start, doorA, out var aCorners))
                    {
                        Debug.LogWarning($"[ELEV] Path FAIL start->doorA doorA='{dA.name}' floor={startFloor} start={start} doorA={doorA}");
                        continue;
                    }

                    for (int bi = 0; bi < endDoors.Length; bi++)
                    {
                        var dB = endDoors[bi];
                        if (dB == null) continue;

                        Vector3 doorB = dB.position;

                        // SamplePosition để đảm bảo cửa B nằm trên NavMesh
                        if (!NavMesh.SamplePosition(doorB, out var hitB, 8f, NavMesh.AllAreas))
                        {
                            Debug.LogWarning($"[ELEV] SamplePosition FAIL doorB '{dB.name}' floor={endFloor} pos={dB.position}");
                            continue;
                        }
                        doorB = hitB.position;

                        // Path từ cửa B tới end
                        if (!TryCalculateNavCorners(doorB, end, out var bCorners))
                        {
                            Debug.LogWarning($"[ELEV] Path FAIL doorB->end doorB='{dB.name}' floor={endFloor} doorB={doorB} end={end}");
                            continue;
                        }

                        // Ghép route: start -> doorA -> ride -> doorB -> end
                        var pts = new List<Vector3>(aCorners.Length + bCorners.Length + 6);

                        for (int i = 0; i < aCorners.Length; i++) pts.Add(aCorners[i]);

                        var ride = BuildElevatorRide(elev, doorA, doorB);

                        // Tránh trùng điểm đầu khi nối
                        if (pts.Count > 0 && ride.Count > 0 && (pts[pts.Count - 1] - ride[0]).sqrMagnitude < 0.0001f)
                            ride.RemoveAt(0);
                        pts.AddRange(ride);

                        // Tránh trùng điểm đầu khi nối phần bCorners
                        int bStart = 0;
                        if (pts.Count > 0 && bCorners.Length > 0 && (pts[pts.Count - 1] - bCorners[0]).sqrMagnitude < 0.0001f)
                            bStart = 1;
                        for (int i = bStart; i < bCorners.Length; i++) pts.Add(bCorners[i]);

                        // Ước lượng thời gian:
                        // - walkTime: thời gian đi bộ tới cửa + đi bộ từ cửa tới đích
                        float walkLen = PolylineLength(aCorners) + PolylineLength(bCorners);
                        float walkTime = walkLen / Mathf.Max(0.01f, walkSpeed);

                        // - rideTime: thời gian đi thang máy theo độ cao chênh lệch
                        float verticalMeters = Mathf.Abs(doorB.y - doorA.y);
                        float rideTime = verticalMeters / Mathf.Max(0.01f, elevatorSpeed);

                        // - totalTime: walkTime + thời gian chờ thang + rideTime
                        float totalTime = walkTime + elevatorAvgWaitSeconds + rideTime;

                        // Giữ route tốt nhất (thời gian thấp nhất)
                        if (totalTime < bestElevTime)
                        {
                            bestElevTime = totalTime;
                            bestElevPts = pts;
                        }
                    }
                }
            }
        }

        // Nếu tìm được route thang máy thì ưu tiên trả về
        if (bestElevPts != null && bestElevPts.Count >= 2)
        {
            bestCorners = bestElevPts.ToArray();
            return true;
        }

        // Nếu không có thang máy, so sánh lại với đường trực tiếp nếu có
        Debug.Log($"[ELEV] stairsTime={stairsTime}, bestElevTime={bestElevTime}, hasStairsCorners={(stairsCorners != null)}, hasBestElevPts={(bestElevPts != null)}");

        if (stairsTime <= bestElevTime)
        {
            if (stairsCorners != null)
            {
                bestCorners = stairsCorners;
                return true;
            }
        }
        else
        {
            if (bestElevPts != null && bestElevPts.Count >= 2)
            {
                bestCorners = bestElevPts.ToArray();
                return true;
            }
        }

        // Fallback cuối cùng: trả về cái nào có
        if (stairsCorners != null) { bestCorners = stairsCorners; return true; }
        if (bestElevPts != null) { bestCorners = bestElevPts.ToArray(); return true; }

        return false;
    }

    // =========================================================
    // DISTANCE HELPERS
    // =========================================================
    float GetDistanceAlongPath(Vector3[] corners, Vector3 worldPos)
    {
        // Tính khoảng cách dọc theo polyline corners tại vị trí worldPos (chiếu lên từng đoạn)
        if (corners == null || corners.Length < 2) return 0f;

        float bestDistanceAlong = 0f;
        float bestSqr = float.MaxValue;
        float cumulative = 0f;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 a = corners[i - 1];
            Vector3 b = corners[i];

            Vector3 ab = b - a;
            float abLen = ab.magnitude;
            if (abLen < 0.0001f) continue;

            Vector3 dir = ab / abLen;

            // Tính t (khoảng cách) khi chiếu worldPos lên đoạn a->b
            float t = Vector3.Dot(worldPos - a, dir);
            t = Mathf.Clamp(t, 0f, abLen);

            Vector3 proj = a + dir * t;

            // Chọn điểm chiếu gần nhất để xác định đoạn phù hợp nhất
            float sqr = (worldPos - proj).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestDistanceAlong = cumulative + t;
            }

            cumulative += abLen;
        }

        return bestDistanceAlong;
    }

    void ClearPath()
    {
        // Xoá line và ping
        if (line != null) line.positionCount = 0;
        ClearPings();
    }

    float ComputeTotalDistance(Vector3[] pts)
    {
        // Tính tổng độ dài của mảng điểm
        if (pts == null || pts.Length < 2) return 0f;

        float d = 0f;
        for (int i = 1; i < pts.Length; i++)
            d += Vector3.Distance(pts[i - 1], pts[i]);

        return d;
    }

    List<Vector3> GetPartialPath(Vector3[] pts, float distance)
    {
        // Lấy danh sách điểm đại diện cho đoạn đường từ 0 -> distance
        var result = new List<Vector3>();
        if (pts == null || pts.Length == 0) return result;

        result.Add(pts[0]);
        float traveled = 0f;

        for (int i = 1; i < pts.Length; i++)
        {
            float seg = Vector3.Distance(pts[i - 1], pts[i]);

            // Nếu đi hết đoạn này vẫn chưa vượt distance thì lấy luôn điểm cuối đoạn
            if (traveled + seg <= distance)
            {
                result.Add(pts[i]);
                traveled += seg;
            }
            else
            {
                // Nếu vượt distance thì nội suy 1 điểm trên đoạn hiện tại
                float remain = distance - traveled;
                float t = (seg <= 0.0001f) ? 1f : Mathf.Clamp01(remain / seg);
                Vector3 p = Vector3.Lerp(pts[i - 1], pts[i], t);
                result.Add(p);
                break;
            }
        }

        return result;
    }

    void DrawLinePoints(List<Vector3> pts)
    {
        // Vẽ line dựa theo danh sách điểm
        if (line == null)
            return;

        // Không có điểm thì xoá
        if (pts == null || pts.Count == 0)
        {
            ClearPath();
            return;
        }

        // Chỉ có 1 điểm thì vẽ 1 đoạn rất ngắn để tránh LineRenderer lỗi/khó nhìn
        if (pts.Count == 1)
        {
            Vector3 p = pts[0] + Vector3.up * lineHeightOffset;
            line.positionCount = 2;
            line.SetPosition(0, p);
            line.SetPosition(1, p + Vector3.up * 0.05f);
            return;
        }

        // Vẽ đầy đủ
        line.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++)
            line.SetPosition(i, pts[i] + Vector3.up * lineHeightOffset);
    }

    void SetupLineStyle()
    {
        // Thiết lập độ dày và curve
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.widthCurve = widthCurve;

        // Thiết lập bo góc và bo đầu mút
        line.numCornerVertices = lineCornerVertices;
        line.numCapVertices = lineCapVertices;

        // Canh line theo view để dễ nhìn
        line.alignment = LineAlignment.View;

        // Lặp texture theo chiều dài line
        line.textureMode = LineTextureMode.Tile;

        // Áp gradient nếu có
        if (lineGradient != null)
            line.colorGradient = lineGradient;
    }

    // =========================================================
    // PINGS
    // =========================================================
    void SpawnPings()
    {
        // Xoá ping cũ
        ClearPings();

        // Chưa chọn đủ điểm thì thôi
        if (SelectedFrom < 0 || SelectedTo < 0) return;

        // Tạo ping điểm bắt đầu
        if (startPingPrefab != null)
        {
            startPingInstance = Instantiate(
                startPingPrefab,
                allPoints[SelectedFrom].position,
                Quaternion.identity,
                visualsRoot
            );
        }

        // Tạo ping điểm kết thúc
        if (endPingPrefab != null)
        {
            endPingInstance = Instantiate(
                endPingPrefab,
                allPoints[SelectedTo].position,
                Quaternion.identity,
                visualsRoot
            );
        }
    }

    void PreviewPings()
    {
        // Nếu thiếu điểm thì xoá ping
        if (SelectedFrom < 0 || SelectedTo < 0)
        {
            ClearPings();
            return;
        }

        // Nếu không có prefab thì thôi
        if (startPingPrefab == null && endPingPrefab == null) return;

        // Cần có visualsRoot để gắn ping vào
        if (visualsRoot == null) return;

        // Ping start: nếu chưa có thì tạo, nếu có rồi thì cập nhật vị trí
        if (startPingInstance == null && startPingPrefab != null)
        {
            startPingInstance = Instantiate(
                startPingPrefab,
                allPoints[SelectedFrom].position,
                Quaternion.identity,
                visualsRoot
            );
        }
        else if (startPingInstance != null)
        {
            startPingInstance.transform.position = allPoints[SelectedFrom].position;
        }

        // Ping end: nếu chưa có thì tạo, nếu có rồi thì cập nhật vị trí
        if (endPingInstance == null && endPingPrefab != null)
        {
            endPingInstance = Instantiate(
                endPingPrefab,
                allPoints[SelectedTo].position,
                Quaternion.identity,
                visualsRoot
            );
        }
        else if (endPingInstance != null)
        {
            endPingInstance.transform.position = allPoints[SelectedTo].position;
        }
    }

    void ClearPings()
    {
        // Xoá instance ping nếu tồn tại
        if (startPingInstance != null) Destroy(startPingInstance);
        if (endPingInstance != null) Destroy(endPingInstance);

        startPingInstance = null;
        endPingInstance = null;
    }

    // =========================================================
    // LABELS
    // =========================================================
    void EnsureSharedLabelMaterial()
    {
        // Nếu đã có material dùng chung thì thôi
        if (sharedLabelMaterial != null) return;

        // Cần có font để clone material
        if (labelFont == null) return;

        // Tạo material dùng chung từ material của font
        sharedLabelMaterial = new Material(labelFont.material);
        sharedLabelMaterial.name = "SharedLabelMaterial_NavLabels";

        // FaceDilate: làm chữ dày hơn một chút
        sharedLabelMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.06f);

        // Outline: viền chữ để nổi bật
        sharedLabelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.15f);
        sharedLabelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(255, 255, 255, 255));

        // Underlay: bóng/đổ nền để dễ đọc trên nền phức tạp
        sharedLabelMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
        sharedLabelMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
        sharedLabelMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color32(0, 0, 0, 160));
        sharedLabelMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.9f);
        sharedLabelMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -1.2f);
    }

    GameObject CreateLabel(Transform wp)
    {
        // Tạo GameObject con để làm nhãn cho waypoint
        GameObject go = new GameObject("Label_" + wp.name);

        // Cho nhãn làm con của waypoint để đi theo waypoint
        go.transform.SetParent(wp);

        // Đặt nhãn cao hơn waypoint theo trục Y
        go.transform.localPosition = Vector3.up * labelHeight;

        // Gán layer cho nhãn (nếu layer tồn tại)
        int labelLayer = LayerMask.NameToLayer(labelLayerName);
        if (labelLayer != -1) go.layer = labelLayer;

        // Thêm TextMeshPro để hiển thị chữ trong world-space
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = wp.name;     // Nội dung nhãn: tên waypoint
        tmp.font = labelFont;   // Font TMP

        // Dùng material chung để chữ đẹp và đồng nhất
        EnsureSharedLabelMaterial();
        if (sharedLabelMaterial != null)
            tmp.fontSharedMaterial = sharedLabelMaterial;

        // Thiết lập font size (nhân hệ số để hợp với world-space)
        tmp.fontSize = labelSize * 0.42f;

        // Màu chữ
        tmp.color = new Color32(46, 150, 50, 255);

        // Canh giữa
        tmp.alignment = TextAlignmentOptions.Center;

        // Không wrap chữ
        tmp.enableWordWrapping = false;

        // Cho phép rich text (nếu cần)
        tmp.richText = true;

        // Thêm padding để chữ ít bị cắt
        tmp.extraPadding = true;

        // Bật kerning (khoảng cách ký tự đẹp hơn)
        tmp.enableKerning = true;

        // In đậm
        tmp.fontStyle = FontStyles.Bold;

        // Không auto sizing (vì scale bằng transform)
        tmp.enableAutoSizing = false;
        tmp.fontSizeMin = labelSize * 0.35f;
        tmp.fontSizeMax = labelSize * 0.50f;

        // Khoảng cách ký tự và dòng (lineSpacing âm để giảm chiều cao dòng)
        tmp.characterSpacing = 0f;
        tmp.lineSpacing = -10f;

        // Thêm Billboard để nhãn luôn hướng về camera
        go.AddComponent<Billboard>();

        // Lưu TMP để LateUpdate scale theo khoảng cách
        allLabelTmps.Add(tmp);

        return go;
    }

    // =========================================================
    // FLOOR AUTO SWITCH DURING MOVEMENT
    // =========================================================
    void BuildFloorRanges()
    {
        // Xây khoảng Y cho mỗi tầng, dùng để auto switch khi camera di chuyển
        floorRanges.Clear();

        // Ngưỡng +- theo Y để xác định camera đang ở "tầng" nào
        float threshold = 1.7f;

        for (int i = 0; i < floors.Count; i++)
        {
            Transform floor = floors[i];
            float y = floor.position.y;

            // Nếu có Waypoints thì ưu tiên lấy Y theo waypoint con (ổn định hơn)
            Transform waypoints = floor.Find("Waypoints");
            if (waypoints != null)
            {
                var children = waypoints.GetComponentsInChildren<Transform>(true);
                if (children.Length > 1)
                    y = children[1].position.y;
            }

            floorRanges.Add(new FloorRange
            {
                floor = floor,
                minY = y - threshold,
                maxY = y + threshold
            });
        }
    }

    void ActivateFloorByY(float y)
    {
        // Nếu đang show all floors thì không auto switch
        if (ShowAllFloors) return;

        int bestIndex = -1;

        // Tìm tầng có khoảng Y chứa y
        for (int i = 0; i < floorRanges.Count; i++)
        {
            if (y >= floorRanges[i].minY && y <= floorRanges[i].maxY)
            {
                bestIndex = i;
                break;
            }
        }

        if (bestIndex < 0) return;
        if (ActiveFloorIndex == bestIndex) return;

        // Nếu route đang active và là route khác tầng
        if (IsRouteActive && navIsCrossFloor)
        {
            if (isNavigating)
            {
                // Khi camera đang chạy: chỉ cho phép chuyển sang tầng start hoặc tầng end
                if (bestIndex != navFromFloor && bestIndex != navToFloor)
                    return;

                ActiveFloorIndex = bestIndex;
                SelectedFloorDropdownIndex = bestIndex + 1;

                visibleFloors.Clear();
                visibleFloors.Add(bestIndex);

                ApplyVisibleFloors();
                ActiveFloorChanged?.Invoke();
                FloorsChanged?.Invoke();
            }
            else
            {
                // Khi camera đã dừng: chỉ cập nhật UI state (hiển thị có thể do dropdown xử lý)
                ActiveFloorIndex = bestIndex;
                SelectedFloorDropdownIndex = bestIndex + 1;
                ActiveFloorChanged?.Invoke();
                FloorsChanged?.Invoke();
            }
            return;
        }

        // Trường hợp bình thường: set floor
        SetFloor(bestIndex);
    }

    void SetShowAllFloors(bool enabled)
    {
        // Nếu route đang active thì không cho đổi show all
        if (IsRouteActive) return;

        ShowAllFloors = enabled;
        visibleFloors.Clear();

        if (enabled)
        {
            // Thêm tất cả tầng vào visible
            for (int i = 0; i < floors.Count; i++)
                visibleFloors.Add(i);
        }
        else
        {
            // Chỉ giữ tầng active
            ActiveFloorIndex = Mathf.Clamp(ActiveFloorIndex, 0, floors.Count - 1);
            visibleFloors.Add(ActiveFloorIndex);
        }

        // Áp dụng bật/tắt tầng
        ApplyVisibleFloors();

        // Báo UI cập nhật
        FloorsChanged?.Invoke();
    }

    public void ExitAllFloorsAndFocusFloor(int floorIndex)
    {
        // Nếu route đang active thì khoá thao tác này
        if (IsRouteActive) return;

        // Clamp index tầng
        floorIndex = Mathf.Clamp(floorIndex, 0, floors.Count - 1);

        // Nếu không show all thì chỉ cần set floor
        if (!ShowAllFloors)
        {
            SetFloor(floorIndex);
            return;
        }

        // Nếu đang show all, chuyển sang chỉ show 1 tầng
        ShowAllFloors = false;

        ActiveFloorIndex = floorIndex;

        visibleFloors.Clear();
        visibleFloors.Add(ActiveFloorIndex);

        ApplyVisibleFloors();

        // Dropdown: +1 vì index 0 là "Tất cả các tầng"
        SelectedFloorDropdownIndex = floorIndex + 1;

        ActiveFloorChanged?.Invoke();
        FloorsChanged?.Invoke();

        // Nếu route active và khác tầng thì giữ transparency, còn lại tắt
        if (IsRouteActive && navIsCrossFloor)
            ApplyNavTransparency(true);
        else
            ApplyNavTransparency(false);
    }

    // =========================================================
    // TRANSPARENCY (CLONE MATERIAL, GIỮ RGB/TEXTURE)
    // =========================================================
    bool IsCrossFloorRoute()
    {
        // Kiểm tra route có đi qua 2 tầng khác nhau hay không
        if (SelectedFrom < 0 || SelectedTo < 0) return false;

        int fromFloor = allPointFloorIndex[SelectedFrom];
        int toFloor = allPointFloorIndex[SelectedTo];

        if (fromFloor < 0 || toFloor < 0) return false;
        return fromFloor != toFloor;
    }

    void ApplyPropsVisibility(bool transparentMode)
    {
        // Nếu không bật tính năng ẩn props thì thôi
        if (!hidePropsWhenTransparent) return;

        // Khi transparentMode = true => ẩn props
        bool showProps = !transparentMode;

        for (int i = 0; i < floorPropsRoots.Count; i++)
        {
            var propsRoot = floorPropsRoots[i];
            if (propsRoot == null) continue;

            // Chỉ setActive khi khác trạng thái để tránh tốn chi phí
            if (propsRoot.gameObject.activeSelf != showProps)
                propsRoot.gameObject.SetActive(showProps);
        }
    }

    void ApplyNavTransparency(bool enabled)
    {
        // Nếu không bật transparency thì thôi
        if (!makeFloorsTransparentInNav) return;
        if (floors == null || floors.Count == 0) return;

        if (enabled)
        {
            // Duyệt tất cả tầng và renderer bên trong để thay vật liệu sang bản trong suốt
            for (int i = 0; i < floors.Count; i++)
            {
                var renderers = floors[i].GetComponentsInChildren<Renderer>(true);

                foreach (var r in renderers)
                {
                    if (r == null) continue;

                    // Bỏ qua renderer của TMP để tránh làm chữ bị trong suốt/đổi material
                    if (r.GetComponent<TextMeshPro>() != null) continue;
                    if (r.GetComponent<TMP_SubMesh>() != null) continue;
                    if (r.GetComponentInParent<TMP_Text>(true) != null) continue;

                    // Lưu vật liệu gốc để restore sau
                    if (!originalMats.ContainsKey(r))
                        originalMats[r] = r.sharedMaterials;

                    var src = r.sharedMaterials;
                    if (src == null || src.Length == 0) continue;

                    var dst = new Material[src.Length];

                    for (int m = 0; m < src.Length; m++)
                    {
                        var mat = src[m];
                        if (mat == null)
                        {
                            dst[m] = null;
                            continue;
                        }

                        // Chọn alpha theo loại đối tượng (sàn/tường/khác)
                        float a = AlphaFor(r);

                        // Lấy hoặc tạo clone trong suốt từ material gốc
                        dst[m] = GetOrCreateTransparentClone(mat, a);
                    }

                    // Gán material mới cho renderer
                    r.sharedMaterials = dst;
                }
            }
        }
        else
        {
            // Restore lại material gốc
            RestoreOriginalMaterials();
        }

        // Ẩn/hiện props theo trạng thái transparent
        ApplyPropsVisibility(enabled);
    }

    Material GetOrCreateTransparentClone(Material original, float alpha)
    {
        // Lấy hoặc tạo clone material để làm trong suốt (tái sử dụng qua cache)
        if (original == null) return null;

        if (!transparentCloneCache.TryGetValue(original, out var clone) || clone == null)
        {
            clone = new Material(original);
            clone.name = original.name + "_NavTransparent";
            transparentCloneCache[original] = clone;
        }

        // Thiết lập clone sang chế độ transparent và set alpha
        MakeMaterialTransparentURP(clone, alpha);
        return clone;
    }

    void MakeMaterialTransparentURP(Material mat, float alpha)
    {
        // Thiết lập material URP sang transparent và đặt alpha
        if (mat == null) return;

        // Ưu tiên URP Lit: _BaseColor, fallback _Color
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            c.a = alpha;
            mat.SetColor("_Color", c);
        }

        // _Surface = 1 => Transparent (trong URP Lit)
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);

        // Tag và renderQueue để Unity render như Transparent
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        // Blend mode alpha
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        // Tắt ghi Z để tránh artefact khi trong suốt
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

        // Bật keyword transparent, tắt opaque
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
    }

    void RestoreOriginalMaterials()
    {
        // Khôi phục material gốc cho mọi renderer đã thay
        foreach (var kv in originalMats)
        {
            if (kv.Key == null) continue;
            kv.Key.sharedMaterials = kv.Value;
        }
        originalMats.Clear();

        // Huỷ clone material đã tạo (tránh rò rỉ)
        foreach (var kv in transparentCloneCache)
            if (kv.Value != null) Destroy(kv.Value);
        transparentCloneCache.Clear();
    }

    float AlphaFor(Renderer r)
    {
        // Xác định alpha theo tên node trong hierarchy (Floor/Wall/Other)
        Transform t = r.transform;
        while (t != null)
        {
            if (t.name == floorRootName) return navFloorAlpha;
            if (t.name == wallRootName) return navWallAlpha;
            t = t.parent;
        }
        return navOtherAlpha;
    }

    void FocusFloorForActiveRoute(int floorIndex)
    {
        // Focus 1 tầng khi route đang active (cho UI chọn 1 trong 2 tầng)
        floorIndex = Mathf.Clamp(floorIndex, 0, floors.Count - 1);

        ShowAllFloors = false;
        ActiveFloorIndex = floorIndex;

        visibleFloors.Clear();
        visibleFloors.Add(floorIndex);
        ApplyVisibleFloors();

        // Route active => giữ transparency
        ApplyNavTransparency(true);

        SelectedFloorDropdownIndex = floorIndex + 1;

        ActiveFloorChanged?.Invoke();
        FloorsChanged?.Invoke();
    }

    // =========================================================
    // AUTO BUILD ELEVATORS (PATCHED: dùng ElevatorStopFloor.floorId)
    // =========================================================
    void AutoBuildElevators()
    {
        // Nếu không có root thang máy thì thôi
        if (elevatorsRoot == null) return;

        var shafts = new List<ElevatorShaft>();

        // Duyệt từng shaft trong elevatorsRoot
        foreach (Transform shaftTf in elevatorsRoot)
        {
            var shaft = new ElevatorShaft();
            shaft.name = shaftTf.name;
            shaft.shaftCenterXZ = shaftTf.Find("CenterXZ");

            // Node chứa các stops
            var stopsTf = shaftTf.Find("Stops");
            if (stopsTf == null) continue;

            var stopList = new List<ElevatorStop>();

            // Duyệt từng stop
            foreach (Transform stopTf in stopsTf)
            {
                // Stop nên có component ElevatorStopFloor để biết stop này thuộc floorId nào
                var stopFloor = stopTf.GetComponent<ElevatorStopFloor>();
                int floorIndex = -1;

                if (stopFloor != null)
                {
                    // Map floorId -> index nội bộ
                    floorIndex = FindFloorIndexById(stopFloor.floorId);
                }
                else
                {
                    // Fallback: thử parse số trong tên stop (setup cũ)
                    int parsedId = ParseFloorIdFromName(stopTf.name);
                    if (parsedId != int.MinValue)
                        floorIndex = FindFloorIndexById(parsedId);

                    if (floorIndex < 0)
                        Debug.LogWarning($"[ELEV] Stop '{stopTf.name}' is missing ElevatorStopFloor AND name doesn't map to a FloorId. Add ElevatorStopFloor to this stop.");
                }

                // Không map được tầng thì bỏ qua stop
                if (floorIndex < 0) continue;

                // Thu thập các door point bên trong stopTf
                var doors = new List<Transform>();
                foreach (Transform d in stopTf)
                    doors.Add(d);

                // Không có cửa thì bỏ qua
                if (doors.Count == 0) continue;

                // Thêm stop vào danh sách
                stopList.Add(new ElevatorStop
                {
                    floorIndex = floorIndex,
                    doorPoints = doors.ToArray()
                });
            }

            // Shaft phải có ít nhất 2 stop để đi giữa các tầng
            if (stopList.Count >= 2)
            {
                shaft.stops = stopList.ToArray();
                shafts.Add(shaft);
            }
        }

        // Gán danh sách shaft vào biến elevators
        elevators = shafts.ToArray();

        // Log debug
        Debug.Log($"[ELEV] built shafts={elevators?.Length ?? 0}");
        if (elevators != null)
        {
            foreach (var sh in elevators)
            {
                Debug.Log($"[ELEV] shaft={sh.name} stops={sh.stops?.Length ?? 0}");
                if (sh.stops != null)
                    foreach (var st in sh.stops)
                        Debug.Log($"[ELEV]  stop floorIndex={st.floorIndex} doors={st.doorPoints?.Length ?? 0}");
            }
        }
    }

    // Fallback: thử parse một số nguyên (có thể âm) xuất hiện trong chuỗi tên (ví dụ: "F-1", "Floor0", "-1")
    static int ParseFloorIdFromName(string name)
    {
        name = name.Trim();
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '-' || char.IsDigit(name[i]))
            {
                int j = i + 1;
                while (j < name.Length && char.IsDigit(name[j])) j++;

                var token = name.Substring(i, j - i);
                if (int.TryParse(token, out int v))
                    return v;

                i = j;
            }
        }
        return int.MinValue;
    }
}
