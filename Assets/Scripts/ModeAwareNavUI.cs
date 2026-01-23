using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Script quản lý giao diện UI thích ứng theo chế độ xem (Tổng quan hoặc Trong nhà)
[RequireComponent(typeof(UIDocument))]
public class ModeAwareNavUI : MonoBehaviour
{
    [Header("Cấu hình tên Scene (phải khớp Build Settings)")]
    public string overviewSceneName = "OverviewScene"; // Scene tổng quan tòa nhà
    public string roomToRoomSceneName = "NavScene";    // Scene dẫn đường trong nhà

    [Header("Tên các phần tử trong file UXML")]
    public string switchBtnName = "switchSceneBtn";
    public string floorSectionName = "floorSection";
    public string fromLabelName = "fromLabel";
    public string toLabelName = "toLabel";

    Button _switchBtn;
    VisualElement _floorSection;
    Label _fromLabel;
    Label _toLabel;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        // Tìm kiếm các phần tử UI trong root
        _switchBtn = root.Q<Button>(switchBtnName);
        _floorSection = root.Q<VisualElement>(floorSectionName);
        _fromLabel = root.Q<Label>(fromLabelName);
        _toLabel = root.Q<Label>(toLabelName);

        // Gán sự kiện cho nút bấm
        if (_switchBtn != null) _switchBtn.clicked += ToggleScene;

        ApplyModeUI();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    void OnDisable()
    {
        if (_switchBtn != null) _switchBtn.clicked -= ToggleScene;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    void OnActiveSceneChanged(Scene a, Scene b) => ApplyModeUI();

    // Cập nhật giao diện dựa trên Scene hiện tại
    void ApplyModeUI()
    {
        string cur = SceneManager.GetActiveScene().name;
        bool isOverview = (cur == overviewSceneName);

        // Ở chế độ Tổng quan (Overview), ẩn đi phần điều khiển tầng
        if (_floorSection != null)
            _floorSection.style.display = isOverview ? DisplayStyle.None : DisplayStyle.Flex;

        // Thay đổi nội dung chữ dựa theo ngữ cảnh
        if (_fromLabel != null) _fromLabel.text = isOverview ? "Bắt đầu từ tòa" : "Bắt đầu từ phòng";
        if (_toLabel != null) _toLabel.text = isOverview ? "Tới tòa" : "Tới phòng";

        // Cập nhật tên nút chuyển đổi
        if (_switchBtn != null)
            _switchBtn.text = isOverview ? "Chuyển sang chế độ trong nhà" : "Chuyển sang chế độ tổng quan";
    }

    // Hàm thực hiện việc chuyển đổi giữa các Scene
    void ToggleScene()
    {
        string cur = SceneManager.GetActiveScene().name;

        if (cur == overviewSceneName)
            SceneManager.LoadScene(roomToRoomSceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(overviewSceneName, LoadSceneMode.Single);
    }
}
