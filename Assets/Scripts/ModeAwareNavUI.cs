using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ModeAwareNavUI : MonoBehaviour
{
    [Header("Scene names (must match Build Settings)")]
    public string overviewSceneName = "OverviewScene";
    public string roomToRoomSceneName = "NavScene";

    [Header("UXML element names")]
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

        _switchBtn = root.Q<Button>(switchBtnName);
        _floorSection = root.Q<VisualElement>(floorSectionName);
        _fromLabel = root.Q<Label>(fromLabelName);
        _toLabel = root.Q<Label>(toLabelName);

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

    void ApplyModeUI()
    {
        string cur = SceneManager.GetActiveScene().name;
        bool isOverview = (cur == overviewSceneName);

        // Overview: hide floor controls
        if (_floorSection != null)
            _floorSection.style.display = isOverview ? DisplayStyle.None : DisplayStyle.Flex;

        // Change label text per mode (optional)
        if (_fromLabel != null) _fromLabel.text = isOverview ? "Bắt đầu từ tòa" : "Bắt đầu từ phòng";
        if (_toLabel != null) _toLabel.text = isOverview ? "Tới tòa" : "Tới phòng";

        // Button text
        if (_switchBtn != null)
            _switchBtn.text = isOverview ? "Chuyển sang chế độ trong nhà" : "Chuyển sang chế độ tổng quan";
    }

    void ToggleScene()
    {
        string cur = SceneManager.GetActiveScene().name;

        if (cur == overviewSceneName)
            SceneManager.LoadScene(roomToRoomSceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(overviewSceneName, LoadSceneMode.Single);
    }
}
