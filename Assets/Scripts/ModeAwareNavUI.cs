using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Script quản lý giao diện UI thích ứng theo chế độ xem (Tổng quan hoặc Trong nhà)
// Gắn script này vào một GameObject có UIDocument để điều khiển UI Toolkit theo scene hiện tại
[RequireComponent(typeof(UIDocument))]
public class ModeAwareNavUI : MonoBehaviour
{
    [Header("Cấu hình tên Scene (phải khớp Build Settings)")]
    public string overviewSceneName = "OverviewScene"; // Tên scene chế độ tổng quan tòa nhà
    public string roomToRoomSceneName = "NavScene";    // Tên scene chế độ dẫn đường trong nhà

    [Header("Tên các phần tử trong file UXML")]
    public string switchBtnName = "switchSceneBtn"; // Tên nút chuyển scene trong UXML
    public string floorSectionName = "floorSection"; // Tên khối UI chọn tầng trong UXML
    public string fromLabelName = "fromLabel"; // Tên label "điểm bắt đầu" trong UXML
    public string toLabelName = "toLabel";     // Tên label "điểm đến" trong UXML

    Button _switchBtn;              // Tham chiếu tới nút chuyển scene
    VisualElement _floorSection;    // Tham chiếu tới vùng UI chọn tầng
    Label _fromLabel;               // Tham chiếu tới label "From"
    Label _toLabel;                 // Tham chiếu tới label "To"

    void OnEnable()
    {
        // Lấy rootVisualElement (gốc UI) từ UIDocument của GameObject này
        var root = GetComponent<UIDocument>().rootVisualElement;

        // Tìm các phần tử UI theo name (đúng với name/id trong file UXML)
        _switchBtn = root.Q<Button>(switchBtnName);                 // Tìm nút chuyển scene
        _floorSection = root.Q<VisualElement>(floorSectionName);    // Tìm khu vực chọn tầng
        _fromLabel = root.Q<Label>(fromLabelName);                  // Tìm label "Bắt đầu"
        _toLabel = root.Q<Label>(toLabelName);                      // Tìm label "Tới"

        // Gán sự kiện click cho nút (nếu tìm thấy)
        if (_switchBtn != null) _switchBtn.clicked += ToggleScene;

        // Áp UI phù hợp ngay khi bật script
        ApplyModeUI();

        // Lắng nghe sự kiện đổi scene (để cập nhật UI khi scene thay đổi)
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    void OnDisable()
    {
        // Gỡ sự kiện click để tránh bị gán trùng hoặc rò rỉ callback
        if (_switchBtn != null) _switchBtn.clicked -= ToggleScene;

        // Gỡ lắng nghe sự kiện đổi scene
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    // Khi scene active thay đổi thì cập nhật lại UI
    void OnActiveSceneChanged(Scene a, Scene b) => ApplyModeUI();

    // Cập nhật giao diện dựa trên scene hiện tại
    void ApplyModeUI()
    {
        // Lấy tên scene đang active
        string cur = SceneManager.GetActiveScene().name;

        // Kiểm tra xem có đang ở chế độ tổng quan không
        bool isOverview = (cur == overviewSceneName);

        // Ở chế độ Tổng quan (Overview) thì ẩn phần chọn tầng, còn trong nhà thì hiện
        if (_floorSection != null)
            _floorSection.style.display = isOverview ? DisplayStyle.None : DisplayStyle.Flex;

        // Đổi chữ "Bắt đầu" / "Tới" theo ngữ cảnh (tòa hoặc phòng)
        if (_fromLabel != null) _fromLabel.text = isOverview ? "Bắt đầu từ tòa" : "Bắt đầu từ phòng";
        if (_toLabel != null) _toLabel.text = isOverview ? "Tới tòa" : "Tới phòng";

        // Đổi chữ trên nút chuyển scene để người dùng biết sẽ chuyển sang chế độ nào
        if (_switchBtn != null)
            _switchBtn.text = isOverview ? "Chuyển sang chế độ trong nhà" : "Chuyển sang chế độ tổng quan";
    }

    // Chuyển đổi giữa 2 scene: tổng quan <-> trong nhà
    void ToggleScene()
    {
        // Lấy tên scene hiện tại
        string cur = SceneManager.GetActiveScene().name;

        // Nếu đang ở overview thì chuyển sang scene trong nhà, ngược lại thì chuyển sang overview
        if (cur == overviewSceneName)
            SceneManager.LoadScene(roomToRoomSceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(overviewSceneName, LoadSceneMode.Single);
    }
}
