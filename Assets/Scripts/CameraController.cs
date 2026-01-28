// Điều khiển camera ở chế độ BirdEye/FPS, xử lý input, giới hạn vùng (zone) và các hành vi tự động (idle spin, relock).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class CameraController : MonoBehaviour
{
    // Hai chế độ camera: FPS (góc nhìn thứ nhất) và BirdEye (chim bay/orbit)
    public enum CameraMode { FPS, BirdEye }
    public CameraMode mode = CameraMode.BirdEye;

    // Camera tham chiếu (nếu null sẽ tự lấy Camera trên cùng GameObject)
    public Camera cam;

    // LayerMask dùng cho raycast kiểm tra vật cản che tuyến đường (route)
    public LayerMask occluderMask;

    // =========================
    // Cấu hình góc nhìn Tổng quan (Overview) sau khi camera chạy theo route
    // =========================
    [Header("Cấu hình góc nhìn Tổng quan (Overview)")]
    public float overviewPitch = 65f;              // góc pitch khi nhìn overview
    public float overviewYaw = 45f;                // góc yaw khi nhìn overview
    public float boundsPadding = 1.2f;             // nới bounds của route để chừa biên
    public float minHeightAboveRoute = 2f;         // độ cao tối thiểu phía trên route
    public int maxOcclusionAdjustSteps = 8;        // số bước thử nâng camera nếu bị che
    public float occlusionHeightStep = 2f;         // mỗi bước nâng thêm bao nhiêu

    // =========================
    // Cấu hình góc nhìn FPS
    // =========================
    [Header("Cấu hình góc nhìn Thứ nhất (FPS)")]
    public float fpsMoveSpeed = 7.5f;              // tốc độ di chuyển FPS
    public float fpsLookSpeed = 6f;                // tốc độ xoay nhìn FPS (chuột)

    // =========================
    // Giới hạn vùng Camera (zone clamp)
    // =========================
    [Header("Giới hạn vùng Camera")]
    public BoxCollider cameraZone;                 // vùng camera được phép hoạt động
    public float zonePadding = 0.0f;               // chừa biên trong vùng
    public bool clampPivotToZone = true;           // có clamp pivot (điểm xoay) không
    public bool clampHeightToZone = true;          // có clamp trục Y không

    // =========================
    // Chặn input khi hover UI (tự xử lý logic ở ngoài nếu cần)
    // =========================
    [Header("Chặn nhập liệu khi di chuột qua UI")]
    public bool blockInputByUI = false;

    // =========================
    // Refocus: đưa camera về pose/look mục tiêu
    // =========================
    [Header("Điểm lấy nét lại (Refocus)")]
    public Transform refocusPose;                  // vị trí/rotation chuẩn để refocus
    public Transform idleSnapTarget;               // fallback look target
    public Transform refocusLookTarget;            // target ưu tiên để LookAt khi refocus

    // =========================
    // Cấu hình BirdEye (orbit/pan/zoom/rotate)
    // =========================
    [Header("Cấu hình góc nhìn Chim bay (BirdEye)")]
    public float panSpeed = 5f;                    // tốc độ kéo/pan
    public float rotateSpeed = 15f;                // tốc độ xoay/orbit
    public float zoomSpeed = 1f;                   // tốc độ zoom bằng scroll
    public float minHeight = 2f;                   // giới hạn min Y khi zoom
    public float maxHeight = 90f;                  // giới hạn max Y khi zoom
    public float minPitch = 20f;                   // giới hạn pitch BirdEye
    public float maxPitch = 80f;                   // giới hạn pitch BirdEye
    public float height = 50f;                     // độ cao “mục tiêu” dùng cho move routine
    public float moveSpeed = 8f;                   // tốc độ di chuyển trong MoveRoutine
    public float followHeightOffset = 20f;         // offset Y khi camera follow theo corners
    public float heightSmoothSpeed = 5f;           // độ mượt khi lerp chiều cao theo route

    // =========================
    // Cảm ứng mobile
    // =========================
    [Header("Cấu hình Cảm ứng (Mobile)")]
    public float touchPanMultiplier = 10f;         // hệ số pan bằng 2 ngón
    public float touchZoomSensitivity = 0.036f;    // độ nhạy pinch zoom
    public float gestureThreshold = 5f;            // ngưỡng thay đổi khoảng cách để tính pinch

    // =========================
    // Pivot target: đối tượng camera xoay quanh (kéo & thả)
    // =========================
    [Header("Điểm xoay mục tiêu (Kéo & Thả)")]
    public Transform pivotTarget;                  // đối tượng để camera xoay quanh
    public Vector3 pivotOffset = Vector3.zero;     // offset thêm cho pivot

    // =========================
    // Idle behavior: tự relock + xoay khi nhàn rỗi
    // =========================
    [Header("Hành vi khi nhàn rỗi / Tự động khóa")]
    public float relockAfterIdleSeconds = 5f;      // sau X giây không input thì relock pivot
    public bool enableIdleSpin = true;             // cho phép auto spin khi idle
    public float idleSpinDelay = 0.5f;             // đợi thêm trước khi bắt đầu spin
    public float idleSpinSpeed = 8.0f;             // tốc độ spin (độ/giây)
    public float idleSpinRamp = 3.0f;              // tốc độ tăng/giảm dần weight spin

    // =========================
    // Cấu hình Cinematic smoothing (chỉ dùng cho animation pan-out/overview)
    // =========================
    [Header("Cinematic smoothing")]
    public float liftDuration = 0.8f;              // how long the initial lift takes
    public float overviewBlendDuration = 1.2f;     // how long to blend into the overview pose
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float posDampTime = 0.12f;              // SmoothDamp time (smaller = snappier)
    public float rotDampTime = 0.10f;              // rotation damping
    float _panDownStartTimer = float.NaN;
    float _panDownYVel = 0f;

    // Khi true: coroutine MoveRoutine đang “điều khiển” camera (khóa input)
    bool lockCamera = false;

    // Góc xoay FPS
    float yaw;
    float pitch;

    // Pitch dùng riêng cho BirdEye orbit
    float currentPitch = 45f;

    // =========================
    // Orbit state (BirdEye)
    // =========================
    Vector3 birdPivot;     // điểm pivot hiện tại camera đang orbit/LookAt
    float birdDist = 20f;  // khoảng cách camera -> pivot
    Vector3 orbitOffset;   // offset của camera so với pivot

    // =========================
    // Idle state
    // =========================
    float lastInputTime;   // thời điểm có input cuối cùng
    float idleSpinWeight;  // weight dùng để ramp spin mượt

    // =========================
    // Pivot lock state
    // =========================
    bool pivotLocked = false;        // camera đang “khóa” orbit quanh pivot?
    bool followPivotTarget = true;   // pivot có follow theo pivotTarget không?

    // =========================
    // Manual pivot lock (override pivot world-space)
    // Dùng khi muốn khóa pivot vào một điểm (vd destination) mà không cần đổi pivotTarget.
    // =========================
    bool manualPivotLock = false;
    Vector3 manualPivot;

    // Touch tracking cho pinch/pan
    float lastTouchDist;
    Vector2 lastTwoFingerCenter;

    // Coroutine di chuyển camera theo corners
    Coroutine moveRoutine;

    // NavMeshPath để dùng khi cần (ở script này đang tạo sẵn)
    UnityEngine.AI.NavMeshPath path;

    void Awake()
    {
        // Giới hạn FPS cao (tùy dự án)
        Application.targetFrameRate = 120;

        // Khởi tạo NavMeshPath
        path = new UnityEngine.AI.NavMeshPath();
    }

    void Start()
    {
        // Lưu góc hiện tại (dùng cho FPS look)
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;

        // Nếu có pivotTarget: mặc định BirdEye sẽ khóa pivot vào target
        if (pivotTarget != null)
        {
            birdPivot = pivotTarget.position + pivotOffset;
            followPivotTarget = true;
            pivotLocked = true;
        }
        else
        {
            // Không có pivot target thì đặt pivot tạm phía trước camera
            birdPivot = transform.position + transform.forward * 5f;
            pivotLocked = false;
        }

        // Khởi tạo orbitOffset và distance
        birdDist = Vector3.Distance(transform.position, birdPivot);
        orbitOffset = transform.position - birdPivot;

        // Khởi tạo idle
        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }

    void Update()
    {
        // Nếu đang chạy coroutine di chuyển camera (lockCamera), bỏ qua toàn bộ xử lý input thường
        if (lockCamera)
            return;

        // Nếu đang hover UI (hoặc bạn set cờ ở nơi khác), chặn input.
        // Nhưng vẫn cần “bảo trì” camera như enforce zone.
        bool inputBlocked = blockInputByUI;

        // Kiểm tra có input thực sự trong frame này không (chỉ tính khi không bị block)
        bool inputThisFrame = !inputBlocked && HasAnyInputThisFrame();

        // Bấm TAB để đổi mode (chỉ khi không bị block)
        if (!inputBlocked && Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            ToggleMode();
            inputThisFrame = true;
        }

        if (inputThisFrame)
        {
            // Có input: cập nhật thời gian và thả pivot lock nếu đang BirdEye + locked
            lastInputTime = Time.time;

            // Bất kỳ tương tác nào cũng “release” khỏi pivot lock (để pan/rotate tự do)
            if (mode == CameraMode.BirdEye && pivotLocked)
                ReleaseFromPivot();
        }
        else
        {
            // Không có input
            if (!inputBlocked)
            {
                // Nếu không bị block UI: có thể tính idle để relock lại
                if (mode == CameraMode.BirdEye && !pivotLocked && pivotTarget != null)
                {
                    float idleFor = Time.time - lastInputTime;
                    if (idleFor >= relockAfterIdleSeconds)
                        InstantRelockToPivot();
                }
            }
            else
            {
                // Nếu bị UI block: không nên coi user là idle => reset lastInputTime
                lastInputTime = Time.time;
            }
        }

        // Nếu BirdEye đang locked: cập nhật vị trí camera theo pivot (follow target hoặc manual)
        if (mode == CameraMode.BirdEye && pivotLocked)
        {
            if (manualPivotLock)
            {
                // Nếu manual lock: pivot là manualPivot
                birdPivot = manualPivot;
            }
            else if (followPivotTarget && pivotTarget != null)
            {
                // Nếu follow pivotTarget: pivot = target + offset
                birdPivot = pivotTarget.position + pivotOffset;
            }

            // Duy trì camera ở đúng offset so với pivot
            transform.position = birdPivot + orbitOffset;
            transform.LookAt(birdPivot);

            // Cập nhật lại dist cho nhất quán
            birdDist = orbitOffset.magnitude;
        }

        // Chỉ xử lý điều khiển khi không bị UI block
        if (!inputBlocked)
        {
            TouchControl();
            MouseKeyboardControl();
        }

        // Luôn enforce zone để tránh camera “trôi” ra khỏi vùng hợp lệ
        EnforceZone();

        // Chỉ spin khi không bị UI block (hover UI không tự xoay)
        if (!inputBlocked)
            ApplyIdleSpin();
    }

    // =========================================================
    // KHÓA / NHẢ KHÓA CAMERA VỚI PIVOT (BirdEye)
    // =========================================================
    void ReleaseFromPivot()
    {
        // Cho phép pan/rotate tự do => tắt pivot lock
        pivotLocked = false;

        // Thả luôn manual destination lock khi user tương tác
        manualPivotLock = false;

        // Rebuild orbit dựa trên pivot hiện tại (để chuyển trạng thái mượt)
        birdDist = Vector3.Distance(transform.position, birdPivot);
        orbitOffset = transform.position - birdPivot;

        // Dừng spin ngay
        idleSpinWeight = 0f;
    }

    void InstantRelockToPivot()
    {
        if (pivotTarget == null) return;

        // Khi relock về pivotTarget: clear manual lock
        manualPivotLock = false;

        // Pivot quay lại target
        birdPivot = pivotTarget.position + pivotOffset;
        followPivotTarget = true;

        // Giữ nguyên offset hiện tại để camera không “giật”
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        transform.LookAt(birdPivot);

        pivotLocked = true;
        idleSpinWeight = 0f;

        // Đảm bảo không vượt khỏi zone
        EnforceZone();
    }
    // =================================================

    // Kiểm tra có input nào trong frame (KB/Mouse/Touch) không
    bool HasAnyInputThisFrame()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        var ts = Touchscreen.current;

        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed) return true;
            if (kb.tabKey.wasPressedThisFrame) return true;
        }

        if (mouse != null)
        {
            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed) return true;
            if (Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f) return true;
        }

        if (ts != null)
        {
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (!touches[i].isInProgress) continue;
                var ph = touches[i].phase.ReadValue();
                if (ph == UnityEngine.InputSystem.TouchPhase.Began ||
                    ph == UnityEngine.InputSystem.TouchPhase.Moved ||
                    ph == UnityEngine.InputSystem.TouchPhase.Stationary)
                    return true;
            }
        }

        return false;
    }

    // Auto spin khi idle đủ lâu và camera đang pivotLocked trong BirdEye
    void ApplyIdleSpin()
    {
        if (!enableIdleSpin) return;
        if (lockCamera) return;
        if (mode != CameraMode.BirdEye) return;
        if (!pivotLocked) return;

        float idleFor = Time.time - lastInputTime;
        bool shouldSpin = idleFor >= (relockAfterIdleSeconds + idleSpinDelay);

        // Ramp weight để vào/ra spin mượt
        float target = shouldSpin ? 1f : 0f;
        idleSpinWeight = Mathf.MoveTowards(idleSpinWeight, target, idleSpinRamp * Time.deltaTime);

        if (idleSpinWeight <= 0f) return;

        float angle = idleSpinSpeed * idleSpinWeight * Time.deltaTime;

        // Xoay quanh pivot theo trục up
        transform.RotateAround(birdPivot, Vector3.up, angle);
        transform.LookAt(birdPivot);

        // Cập nhật orbitOffset để đồng bộ với vị trí mới
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        EnforceZone();
    }

    // =========================================================
    // THAY ĐỔI CHẾ ĐỘ CAMERA
    // =========================================================
    void ToggleMode()
    {
        // Toggle giữa FPS và BirdEye
        mode = mode == CameraMode.FPS ? CameraMode.BirdEye : CameraMode.FPS;

        if (mode == CameraMode.BirdEye)
        {
            // Vào BirdEye: mặc định follow pivotTarget (nếu có), không dùng manualPivotLock
            manualPivotLock = false;

            if (pivotTarget != null)
            {
                birdPivot = pivotTarget.position + pivotOffset;
                followPivotTarget = true;
                pivotLocked = true;
            }
            else
            {
                birdPivot = transform.position + transform.forward * 5f;
                pivotLocked = false;
            }

            birdDist = Vector3.Distance(transform.position, birdPivot);
            orbitOffset = transform.position - birdPivot;

            transform.LookAt(birdPivot);

            lastInputTime = Time.time;
            idleSpinWeight = 0f;
        }
        else
        {
            // Rời BirdEye: thả lock để FPS tự do
            pivotLocked = false;
            manualPivotLock = false;
            idleSpinWeight = 0f;
        }
    }

    // =========================================================
    // ĐIỀU KHIỂN CHUỘT + BÀN PHÍM (PC)
    // =========================================================
    void MouseKeyboardControl()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (mode == CameraMode.FPS)
        {
            // Di chuyển WASD trong FPS
            Vector3 dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += transform.forward;
            if (kb.sKey.isPressed) dir -= transform.forward;
            if (kb.aKey.isPressed) dir -= transform.right;
            if (kb.dKey.isPressed) dir += transform.right;

            transform.position += dir * fpsMoveSpeed * Time.deltaTime;

            // Giữ chuột phải để xoay nhìn
            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue() * fpsLookSpeed * Time.deltaTime;
                yaw += delta.x;
                pitch -= delta.y;
                pitch = Mathf.Clamp(pitch, -80f, 80f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            }
        }
        else
        {
            // BirdEye: delta chuột
            Vector2 delta = mouse.delta.ReadValue() * Time.deltaTime;

            // PAN: kéo bằng chuột trái (chỉ khi đã unlock; tương tác đầu tiên ở Update sẽ ReleaseFromPivot)
            if (mouse.leftButton.isPressed)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                // Kéo theo screen delta -> world move
                Vector3 move = (-right * delta.x - forward * delta.y) * panSpeed;
                transform.position += move;
                birdPivot += move;

                orbitOffset = transform.position - birdPivot;
                birdDist = orbitOffset.magnitude;
            }

            // ROTATE: giữ chuột phải để orbit quanh pivot + chỉnh pitch
            if (mouse.rightButton.isPressed)
            {
                transform.RotateAround(birdPivot, Vector3.up, delta.x * rotateSpeed);

                currentPitch -= delta.y * rotateSpeed;
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                UpdateBirdCamera();
            }

            // ZOOM: lăn bánh xe chuột
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 dir = transform.forward;
                Vector3 newPos = transform.position + dir * scroll * zoomSpeed;

                // Giới hạn theo Y
                float h = newPos.y;
                if (h >= minHeight && h <= maxHeight)
                {
                    transform.position = newPos;

                    orbitOffset = transform.position - birdPivot;
                    birdDist = orbitOffset.magnitude;
                }
            }
        }
    }

    // =========================================================
    // ĐIỀU KHIỂN CẢM ỨNG (MOBILE/TABLET) - chỉ BirdEye
    // =========================================================
    void TouchControl()
    {
        if (mode != CameraMode.BirdEye) return;

        var ts = Touchscreen.current;
        if (ts == null) return;

        var touches = ts.touches;

        // Đếm số touch đang active
        int activeTouchCount = 0;
        for (int i = 0; i < touches.Count; i++)
            if (touches[i].isInProgress) activeTouchCount++;

        // 1) 1 NGÓN: ROTATE
        if (activeTouchCount == 1)
        {
            // Lưu ý: chọn touch[0] hoặc touch[1] phòng trường hợp index 0 không inProgress
            var touch = touches[0].isInProgress ? touches[0] : touches[1];

            if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector2 d = touch.delta.ReadValue();
                if (d.magnitude < 0.1f) return;

                // Orbit yaw
                transform.RotateAround(birdPivot, Vector3.up, d.x * rotateSpeed * Time.deltaTime);

                // Pitch
                currentPitch -= d.y * rotateSpeed * Time.deltaTime;
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                UpdateBirdCamera();
            }
        }
        // 2) >=2 NGÓN: PAN + PINCH
        else if (activeTouchCount >= 2)
        {
            var touch0 = touches[0];
            var touch1 = touches[1];

            Vector2 p0 = touch0.position.ReadValue();
            Vector2 p1 = touch1.position.ReadValue();
            Vector2 currentCenter = (p0 + p1) * 0.5f;
            float currentDist = Vector2.Distance(p0, p1);

            // Frame đầu của gesture: khởi tạo
            if (lastTouchDist <= 0)
            {
                lastTouchDist = currentDist;
                lastTwoFingerCenter = currentCenter;
                return;
            }

            // PINCH ZOOM
            float distChange = currentDist - lastTouchDist;
            if (Mathf.Abs(distChange) > gestureThreshold)
            {
                Vector3 zoomDir = transform.forward;
                Vector3 newPos = transform.position + zoomDir * distChange * touchZoomSensitivity;

                if (newPos.y >= minHeight && newPos.y <= maxHeight)
                {
                    transform.position = newPos;

                    orbitOffset = transform.position - birdPivot;
                    birdDist = orbitOffset.magnitude;
                }
            }

            // PAN theo tâm 2 ngón
            if (touch0.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved ||
                touch1.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector2 centerDelta = currentCenter - lastTwoFingerCenter;
                if (centerDelta.magnitude > 0.5f)
                {
                    Vector3 right = transform.right;
                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                    Vector3 move = (-right * centerDelta.x - forward * centerDelta.y) * touchPanMultiplier * Time.deltaTime;
                    transform.position += move;
                    birdPivot += move;

                    orbitOffset = transform.position - birdPivot;
                    birdDist = orbitOffset.magnitude;
                }
            }

            // Cập nhật trạng thái gesture
            lastTouchDist = currentDist;
            lastTwoFingerCenter = currentCenter;
        }
        else
        {
            // Không có touch: reset tracking
            if (lastTouchDist > 0)
            {
                lastTouchDist = 0;
                lastTwoFingerCenter = Vector2.zero;
            }
        }
    }

    // Tính lại vị trí camera BirdEye dựa trên currentPitch + khoảng cách birdDist
    void UpdateBirdCamera()
    {
        birdDist = Vector3.Distance(transform.position, birdPivot);

        // Giữ yaw hiện tại theo transform.eulerAngles.y
        Quaternion rot = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0);
        transform.position = birdPivot + rot * Vector3.back * birdDist;
        transform.LookAt(birdPivot);

        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        EnforceZone();
    }

    // =========================================================
    // API: di chuyển camera BirdEye theo các điểm corners (đã có sẵn)
    // =========================================================
    public void MoveBirdEyeAlongCorners(
        Vector3[] corners,
        Action onComplete = null,
        Action<Vector3> onStep = null
    )
    {
        if (corners == null || corners.Length < 2) return;

        // Nếu đang có coroutine, hủy trước
        if (moveRoutine != null)
            CancelMove();

        // Chạy coroutine di chuyển theo corners (không recalc path ở đây)
        moveRoutine = StartCoroutine(MoveRoutine(corners, onComplete, onStep));
    }

    // Hủy di chuyển tự động
    public void CancelMove()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        lockCamera = false;

        // Reset tracking touch
        lastTouchDist = 0;
        lastTwoFingerCenter = Vector2.zero;

        // Reset idle
        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }

    // Coroutine di chuyển camera theo corners: lift lên, đi theo từng đoạn, rồi chuyển sang overview
    IEnumerator MoveRoutine(
        Vector3[] corners,
        Action onComplete,
        Action<Vector3> onStep
    )
    {
        // Bật lockCamera để Update không xử lý input trong lúc move
        lockCamera = true;

        _panDownStartTimer = float.NaN;
        _panDownYVel = 0f;

        if (corners.Length < 2)
        {
            lockCamera = false;
            moveRoutine = null;
            yield break;
        }

        // 1) Lift camera lên điểm đầu (tạo cảm giác “bay lên”)
        Vector3 liftTarget = corners[0] + Vector3.up * (height * 0.3f);
        Quaternion topDownRot = Quaternion.Euler(90f, 0f, 0f);

        {
            float tLift = 0f;
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;

            while (tLift < 1f)
            {
                tLift += Time.deltaTime / Mathf.Max(0.0001f, liftDuration);
                float a = Ease01(ease, tLift);

                transform.position = Vector3.Lerp(startPos, liftTarget, a);
                transform.rotation = Quaternion.Slerp(startRot, topDownRot, a);

                yield return null;
            }

            transform.position = liftTarget;
            transform.rotation = topDownRot;
        }

        // 2) Di chuyển theo từng corner (giữ XZ theo target, Y lerp theo followHeightOffset)
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 target = corners[i];

            while (Vector3.Distance(new Vector2(transform.position.x, transform.position.z),
                   new Vector2(target.x, target.z)) > 0.05f)
            {
                float rampMul = 1f;

                if (i == 1)
                {
                    // ramp over ~0.7s, starting at 15% speed
                    if (!float.IsNaN(_panDownStartTimer))
                        _panDownStartTimer += Time.deltaTime;
                    else
                        _panDownStartTimer = Time.deltaTime;

                    float ramp = Mathf.Clamp01(_panDownStartTimer / 2.0f);
                    rampMul = Mathf.Lerp(0.0f, 1f, ramp);
                }

                Vector3 desiredPos = Vector3.MoveTowards(
                    transform.position,
                    new Vector3(target.x, transform.position.y, target.z),
                    (moveSpeed * 0.6f) * Time.deltaTime
                );

                float desiredY = target.y + followHeightOffset;
                float heightLerpSpeed = (heightSmoothSpeed * 0.6f);

                float newY = Mathf.Lerp(transform.position.y, desiredY, heightLerpSpeed * Time.deltaTime);

                if (i == 1)
                {
                    float ySmoothTime = Mathf.Lerp(0.9f, 0.12f, rampMul);
                    float yMaxSpeed = Mathf.Lerp(0.5f, 999f, rampMul);
                    newY = Mathf.SmoothDamp(transform.position.y, desiredY, ref _panDownYVel, ySmoothTime, yMaxSpeed);
                }

                transform.position = new Vector3(desiredPos.x, newY, desiredPos.z);


                // Callback cho từng bước (trả về điểm trên route theo XZ hiện tại)
                onStep?.Invoke(new Vector3(transform.position.x, target.y, transform.position.z));
                yield return null;
            }
        }

        // 3) Chuyển qua overview “thông minh”
        // FIX: trước đây gọi PositionSmartOverview xong mà không lerp từ -> to (no-op). Nay lưu from/to rồi lerp.
        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;

        PositionSmartOverview(corners);  // hàm này sẽ set transform.position/rotation và set birdPivot = center
        Vector3 toPos = transform.position;
        Quaternion toRot = transform.rotation;

        // revert để nội suy mượt
        transform.position = fromPos;
        transform.rotation = fromRot;

        {
            float t = 0f;
            Vector3 vel = Vector3.zero;
            Vector3 angVel = Vector3.zero;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, overviewBlendDuration);
                float a = Ease01(ease, t);

                Vector3 blendPos = Vector3.Lerp(fromPos, toPos, a);
                Quaternion blendRot = Quaternion.Slerp(fromRot, toRot, a);

                transform.position = Vector3.SmoothDamp(transform.position, blendPos, ref vel, posDampTime);
                transform.rotation = SmoothDampRotation(transform.rotation, blendRot, ref angVel, rotDampTime);

                yield return null;
            }

            transform.position = toPos;
            transform.rotation = toRot;
        }

        // Callback complete
        onComplete?.Invoke();

        // 4) Mở lockCamera để user điều khiển lại
        lockCamera = false;

        // Reset idle
        lastInputTime = Time.time;
        idleSpinWeight = 0f;

        // Sau overview: giữ pivot = center-of-route (đã set trong PositionSmartOverview)
        // => khóa pivot để orbit/idle spin quay quanh center-of-route.
        manualPivotLock = false;
        pivotLocked = true;
        followPivotTarget = false;

        // Đồng bộ orbit state
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;
        transform.LookAt(birdPivot);

        lastInputTime = Time.time;
        idleSpinWeight = 0f;

        moveRoutine = null;
    }

    // Project một điểm xuống NavMesh (nếu cần dùng nơi khác)
    Vector3 ProjectToNavMesh(Vector3 pos)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return pos;
    }

    // Snap camera sang BirdEye và khóa pivot vào target
    public void SnapToBirdEye(Transform target, float snapHeight = 40f, float pitch = 75f)
    {
        if (target == null) return;

        mode = CameraMode.BirdEye;

        birdPivot = target.position + pivotOffset;
        followPivotTarget = true;

        // SnapToBirdEye là lock “chủ động” => không dùng manual pivot lock
        manualPivotLock = false;

        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Giữ yaw hiện tại
        float y = transform.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(currentPitch, y, 0f);

        birdDist = snapHeight;
        transform.position = birdPivot + rot * Vector3.back * birdDist;
        transform.LookAt(birdPivot);

        orbitOffset = transform.position - birdPivot;

        pivotLocked = true;

        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }

    // Clamp một điểm world vào trong BoxCollider zone (hỗ trợ zone có rotation/scale)
    Vector3 ClampToZone(Vector3 worldPos)
    {
        if (cameraZone == null) return worldPos;

        Transform zt = cameraZone.transform;

        // Đưa về local space của collider để clamp theo center/size chuẩn
        Vector3 local = zt.InverseTransformPoint(worldPos);
        Vector3 c = cameraZone.center;
        Vector3 e = cameraZone.size * 0.5f;

        float pad = zonePadding;

        float minX = c.x - e.x + pad;
        float maxX = c.x + e.x - pad;
        float minY = c.y - e.y + pad;
        float maxY = c.y + e.y - pad;
        float minZ = c.z - e.z + pad;
        float maxZ = c.z + e.z - pad;

        local.x = Mathf.Clamp(local.x, minX, maxX);
        local.z = Mathf.Clamp(local.z, minZ, maxZ);

        if (clampHeightToZone)
            local.y = Mathf.Clamp(local.y, minY, maxY);

        return zt.TransformPoint(local);
    }

    // Enforce zone cho BirdEye: clamp pivot (nếu bật) và clamp camera position
    void EnforceZone()
    {
        if (cameraZone == null) return;
        if (mode != CameraMode.BirdEye) return;

        // Clamp pivot trước (tùy chọn)
        if (clampPivotToZone)
            birdPivot = ClampToZone(birdPivot);

        // Clamp camera position
        Vector3 clampedPos = ClampToZone(transform.position);
        if (clampedPos != transform.position)
        {
            transform.position = clampedPos;

            // Rebuild orbit để không giật khi tiếp tục điều khiển
            orbitOffset = transform.position - birdPivot;
            birdDist = orbitOffset.magnitude;

            // Luôn nhìn vào pivot
            transform.LookAt(birdPivot);
        }
    }

    // Refocus: đưa camera về refocusPose, nhìn vào refocusLookTarget/idleSnapTarget/birdPivot
    public void RefocusNow()
    {
        if (refocusPose == null) return;
        if (moveRoutine != null) CancelMove();

        mode = CameraMode.BirdEye;
        lockCamera = false;

        // Refocus không bị ảnh hưởng bởi manual destination lock
        manualPivotLock = false;

        // Set vị trí theo pose
        transform.position = refocusPose.position;

        // Chọn điểm nhìn
        Vector3 lookPoint =
            (refocusLookTarget != null) ? (refocusLookTarget.position + pivotOffset) :
            (idleSnapTarget != null) ? (idleSnapTarget.position + pivotOffset) :
            birdPivot;

        birdPivot = lookPoint;
        transform.LookAt(lookPoint);

        // Đồng bộ orbit state
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        // Đồng bộ yaw/pitch từ transform hiện tại
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }

    // =========================================================
    // Tính “overview” thông minh để fit toàn bộ route trong FOV,
    // có xử lý occlusion (nâng lên / đổi yaw / fallback top-down).
    // =========================================================
    void PositionSmartOverview(Vector3[] corners)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;
        if (corners == null || corners.Length == 0) return;

        // 1) Tính Bounds của route
        Bounds b = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            b.Encapsulate(corners[i]);

        Vector3 ext = b.extents * boundsPadding;

        // 2) Tính độ cao cần thiết để fit route theo FOV (dọc/ngang)
        float vertFovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float horizFovRad = 2f * Mathf.Atan(Mathf.Tan(vertFovRad * 0.5f) * cam.aspect);

        float halfWidth = ext.x;
        float halfDepth = ext.z;

        float heightFromDepth = halfDepth / Mathf.Tan(vertFovRad * 0.5f);
        float heightFromWidth = halfWidth / Mathf.Tan(horizFovRad * 0.5f);

        float baseHeight = Mathf.Max(heightFromDepth, heightFromWidth, minHeightAboveRoute);

        Vector3 center = b.center;

        // 3) Đặt camera theo pitch/yaw overview
        float clampedPitch = Mathf.Clamp(overviewPitch, 1f, 89f);
        float pitchRad = clampedPitch * Mathf.Deg2Rad;

        Quaternion yawRot = Quaternion.Euler(0f, overviewYaw, 0f);
        Vector3 forwardDir = yawRot * Vector3.forward;

        float camDistance = baseHeight / Mathf.Sin(pitchRad);
        float horizontalDist = camDistance * Mathf.Cos(pitchRad);
        float verticalOffset = camDistance * Mathf.Sin(pitchRad);

        Vector3 camPos = center - forwardDir * horizontalDist + Vector3.up * verticalOffset;
        Quaternion camRot = Quaternion.LookRotation(center - camPos, Vector3.up);

        transform.position = camPos;
        transform.rotation = camRot;

        // 4) Nếu route bị che bởi occluderMask: nâng camera dần lên
        for (int step = 0; step < maxOcclusionAdjustSteps; step++)
        {
            if (!IsRouteOccluded(corners))
                break;

            camPos += Vector3.up * occlusionHeightStep;
            transform.position = camPos;
            transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);
        }

        // Nếu vẫn bị che: thử đổi yaw (quay quanh route) để tìm góc ít bị che
        if (IsRouteOccluded(corners))
        {
            float[] yawOffsets = { 45f, -45f, 90f, -90f, 135f, -135f };

            bool foundAngle = false;

            foreach (float offset in yawOffsets)
            {
                float newYaw = overviewYaw + offset;
                Quaternion yawRot2 = Quaternion.Euler(0f, newYaw, 0f);
                Vector3 newForward = yawRot2 * Vector3.forward;

                Vector3 newCamPos =
                    center - newForward * horizontalDist +
                    Vector3.up * verticalOffset;

                transform.position = newCamPos;
                transform.rotation = Quaternion.LookRotation(center - newCamPos, Vector3.up);

                if (!IsRouteOccluded(corners))
                {
                    foundAngle = true;
                    break;
                }
            }

            // Fallback cuối: nhìn gần top-down
            if (!foundAngle)
            {
                Vector3 topPos = center + Vector3.up * (baseHeight * 2f);
                transform.position = topPos;
                transform.rotation = Quaternion.Euler(89f, 0f, 0f);
            }
        }

        // Đồng bộ trạng thái BirdEye: pivot = center route, không follow pivotTarget
        birdPivot = center;
        followPivotTarget = false;
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;
    }

    // Kiểm tra route có bị che không bằng cách raycast từ camera tới các điểm trên route
    bool IsRouteOccluded(Vector3[] corners)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return false;
        if (corners == null || corners.Length == 0) return false;

        Vector3 camPos = transform.position;

        const int samplesPerSegment = 2;

        for (int i = 0; i < corners.Length; i++)
        {
            // 1) Check trực tiếp tại corner (nhích lên 0.2f để tránh ground)
            Vector3 p = corners[i] + Vector3.up * 0.2f;
            if (IsPointOccluded(camPos, p))
                return true;

            // 2) Check thêm điểm giữa các đoạn để tăng độ tin cậy
            if (i > 0)
            {
                Vector3 a = corners[i - 1];
                Vector3 b = corners[i];

                for (int s = 1; s <= samplesPerSegment; s++)
                {
                    float t = s / (float)(samplesPerSegment + 1);
                    Vector3 mid = Vector3.Lerp(a, b, t) + Vector3.up * 0.2f;

                    if (IsPointOccluded(camPos, mid))
                        return true;
                }
            }
        }

        return false;
    }

    // Raycast từ camera tới một điểm; nếu hit occluderMask trước điểm => bị che
    bool IsPointOccluded(Vector3 camPos, Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - camPos;
        float dist = dir.magnitude;
        if (dist < 0.1f) return false;

        dir /= dist;

        if (Physics.Raycast(camPos, dir, out RaycastHit hit, dist, occluderMask, QueryTriggerInteraction.Ignore))
        {
            // Có vật cản nằm trong occluderMask trước khi ray chạm tới worldPoint
            return true;
        }

        return false;
    }

    static float Ease01(AnimationCurve curve, float t)
    {
        t = Mathf.Clamp01(t);
        return curve != null ? curve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);
    }

    static Quaternion SmoothDampRotation(Quaternion current, Quaternion target, ref Vector3 angularVel, float smoothTime)
    {
        // Convert to euler and SmoothDampAngle each axis (simple + stable for cameras)
        Vector3 cur = current.eulerAngles;
        Vector3 tar = target.eulerAngles;

        cur.x = Mathf.SmoothDampAngle(cur.x, tar.x, ref angularVel.x, smoothTime);
        cur.y = Mathf.SmoothDampAngle(cur.y, tar.y, ref angularVel.y, smoothTime);
        cur.z = Mathf.SmoothDampAngle(cur.z, tar.z, ref angularVel.z, smoothTime);

        return Quaternion.Euler(cur);
    }
}
