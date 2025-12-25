using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class CameraController : MonoBehaviour
{
    public enum CameraMode { FPS, BirdEye }
    public CameraMode mode = CameraMode.BirdEye;

    [Header("FPS Settings")]
    public float fpsMoveSpeed = 7.5f;
    public float fpsLookSpeed = 6f;

    [Header("BirdEye Settings")]
    public float rotateSpeed = 15f;
    public float minHeight = 2f;
    public float maxHeight = 90f;
    public float minPitch = 20f;
    public float maxPitch = 80f;

    [Header("BirdEye Feel")]
    public float panSensitivity = 0.001f;      // tune: 0.01 - 0.05
    public float zoomSensitivity = 30f;      // tune: 1 - 10
    public float zoomMinDist = 5f;
    public float zoomMaxDist = 250f;

    [Header("Zoom Stability")]
    public bool invertScrollZoom = false;     // nếu bạn thấy scroll ngược thì tick cái này
    public float maxScrollPerFrame = 240f;    // cap scroll spikes (trackpad/WebGL hay bắn mạnh)
    public float zoomDamping = 18f;           // 0 = instant, 12-25 = mượt
    public float pinchToDist = 0.0025f;       // pinch pixels -> distance scale

    [Header("BirdEye Pivot")]
    public LayerMask pivotRaycastMask = ~0;   // nếu scene có collider ground -> pivot chuẩn
    public float pivotRaycastMaxDistance = 500f;
    public float groundPlaneY = 0f;           // nếu không có collider, dùng plane y=0 (đổi nếu ground bạn không ở y=0)
    public float fallbackPivotDistance = 50f;

    [Header("Move Routine")]
    public float height = 50f;
    public float moveSpeed = 16f;

    bool lockCamera = false;

    // FPS
    float yaw;
    float pitch;

    // BirdEye orbit
    float birdYaw;
    float currentPitch = 45f;
    Vector3 birdPivot;

    // BirdEye distance (IMPORTANT: zoom chỉ chỉnh cái này)
    float birdDist;
    float targetBirdDist;

    // Touch zoom
    float lastTouchDist;

    Coroutine moveRoutine;
    UnityEngine.AI.NavMeshPath path;

    void Awake()
    {
#if UNITY_WEBGL
        Application.targetFrameRate = -1;
#else
        Application.targetFrameRate = 120;
#endif
        EnhancedTouchSupport.Enable();
        path = new UnityEngine.AI.NavMeshPath();
    }

    void OnDestroy()
    {
        EnhancedTouchSupport.Disable();
    }

    void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;

        if (mode == CameraMode.BirdEye)
        {
            birdPivot = GetDefaultBirdPivot();
            SyncBirdAnglesFromCurrentView();

            birdDist = Vector3.Distance(transform.position, birdPivot);
            targetBirdDist = birdDist;

            ClampTargetBirdDist();
            birdDist = targetBirdDist;

            UpdateBirdCamera(true);
        }
    }

    void Update()
    {
        if (lockCamera) return;

        // Toggle mode (TAB on desktop)
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            ToggleMode();
        }

        // UNIFIED INPUT: ưu tiên touch nếu có, không thì mouse
        if (mode == CameraMode.BirdEye && Touch.activeTouches.Count > 0)
            BirdEyeTouchControl();
        else
            MouseKeyboardControl();

        // nếu đang zoom smoothing thì tick update thêm cho mượt
        if (mode == CameraMode.BirdEye && Mathf.Abs(birdDist - targetBirdDist) > 0.0005f)
            UpdateBirdCamera(false);
    }

    // ================= MODE =================
    void ToggleMode()
    {
        mode = mode == CameraMode.FPS ? CameraMode.BirdEye : CameraMode.FPS;

        if (mode == CameraMode.BirdEye)
        {
            birdPivot = GetDefaultBirdPivot();
            SyncBirdAnglesFromCurrentView();

            birdDist = Vector3.Distance(transform.position, birdPivot);
            targetBirdDist = birdDist;

            ClampTargetBirdDist();
            birdDist = targetBirdDist;

            UpdateBirdCamera(true);
        }
        else
        {
            yaw = transform.eulerAngles.y;
            pitch = transform.eulerAngles.x;
        }
    }

    // ================= Pivot helper =================
    Vector3 GetDefaultBirdPivot()
    {
        // 1) nếu scene có ground collider -> raycast theo hướng nhìn
        if (Physics.Raycast(
                transform.position,
                transform.forward,
                out RaycastHit hit,
                pivotRaycastMaxDistance,
                pivotRaycastMask,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        // 2) fallback: intersect với mặt phẳng y = groundPlaneY
        Plane plane = new Plane(Vector3.up, new Vector3(0f, groundPlaneY, 0f));
        Ray ray = new Ray(transform.position, transform.forward);

        if (plane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);

        // 3) fallback cuối: đi theo forward phẳng (XZ)
        Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
        flatFwd.Normalize();

        Vector3 p = transform.position + flatFwd * fallbackPivotDistance;
        p.y = groundPlaneY;
        return p;
    }

    /// <summary>
    /// Sync birdYaw/currentPitch từ camera hiện tại để không bị snap khi bắt đầu rotate
    /// </summary>
    void SyncBirdAnglesFromCurrentView()
    {
        Vector3 dir = (birdPivot - transform.position);
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;

        dir.Normalize();

        birdYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        float pitchFromDir = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        currentPitch = Mathf.Clamp(-pitchFromDir, minPitch, maxPitch);
    }

    // ================= DIST CLAMP (the real fix) =================
    void ClampTargetBirdDist()
    {
        float sinPitch = Mathf.Sin(currentPitch * Mathf.Deg2Rad);
        if (sinPitch < 0.01f) sinPitch = 0.01f;

        // y = pivot.y + dist * sin(pitch)
        float minByHeight = (minHeight - birdPivot.y) / sinPitch;
        float maxByHeight = (maxHeight - birdPivot.y) / sinPitch;

        float minDist = Mathf.Max(zoomMinDist, minByHeight);
        float maxDist = Mathf.Min(zoomMaxDist, maxByHeight);

        // nếu pivot cao quá / data weird -> fallback
        if (maxDist < minDist)
        {
            minDist = zoomMinDist;
            maxDist = zoomMaxDist;
        }

        targetBirdDist = Mathf.Clamp(targetBirdDist, minDist, maxDist);
    }

    // ================= DESKTOP / MOUSE =================
    void MouseKeyboardControl()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null) return;

        Vector2 mouseDelta = mouse.delta.ReadValue(); // pixel/frame
        float dt = Time.deltaTime;

        if (mode == CameraMode.FPS)
        {
            if (kb == null) return;

            Vector3 dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += transform.forward;
            if (kb.sKey.isPressed) dir -= transform.forward;
            if (kb.aKey.isPressed) dir -= transform.right;
            if (kb.dKey.isPressed) dir += transform.right;

            transform.position += dir * fpsMoveSpeed * dt;

            if (mouse.rightButton.isPressed)
            {
                Vector2 look = mouseDelta * fpsLookSpeed * dt;
                yaw += look.x;
                pitch -= look.y;
                pitch = Mathf.Clamp(pitch, -80f, 80f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }
        else
        {
            // ========== BirdEye (Mouse) ==========

            // PAN (LMB drag)
            if (mouse.leftButton.isPressed)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                // scale theo height => cao pan nhanh hơn
                float heightScale = Mathf.Clamp(birdDist, 10f, 250f) * panSensitivity;

                Vector3 move = (-right * mouseDelta.x - forward * mouseDelta.y) * heightScale;

                transform.position += move;
                birdPivot += move;
            }

            // ROTATE (RMB drag) ✅ no snap
            if (mouse.rightButton.isPressed)
            {
                birdYaw += mouseDelta.x * rotateSpeed * dt;

                currentPitch -= mouseDelta.y * rotateSpeed * dt;
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                // pitch đổi => dist bị ảnh hưởng height constraint
                ClampTargetBirdDist();

                UpdateBirdCamera(false);
            }

            // ZOOM (scroll) ✅ stable
            float scroll = mouse.scroll.ReadValue().y;
            if (invertScrollZoom) scroll = -scroll;

            if (Mathf.Abs(scroll) > 0.01f)
            {
                scroll = Mathf.Clamp(scroll, -maxScrollPerFrame, maxScrollPerFrame);

                // normalize: 120 -> 1
                float scrollSteps = scroll / 120f;

                // mỗi step thay đổi theo phần trăm distance
                // zoomSensitivity = 30 => ~30% / step
                float percent = zoomSensitivity * 0.01f;

                // exp zoom
                float factor = Mathf.Pow(1f - percent, scrollSteps);

                targetBirdDist *= factor;

                ClampTargetBirdDist();
                UpdateBirdCamera(false);
            }
        }
    }

    // ================= TOUCH (BirdEye only) =================
    void BirdEyeTouchControl()
    {
        if (mode != CameraMode.BirdEye) return;

        var touches = Touch.activeTouches;
        if (touches.Count == 0) return;

        float dt = Time.deltaTime;

        // 1 finger PAN
        if (touches.Count == 1)
        {
            Vector2 delta = touches[0].delta;

            Vector3 right = transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

            float heightScale = Mathf.Clamp(transform.position.y, 5f, 200f) * panSensitivity;
            float touchBoost = 1.6f;

            Vector3 move = (-right * delta.x - forward * delta.y) * heightScale * touchBoost;

            transform.position += move;
            birdPivot += move;

            lastTouchDist = 0;
            return;
        }

        // 2 fingers ROTATE + ZOOM
        if (touches.Count >= 2)
        {
            var t0 = touches[0];
            var t1 = touches[1];

            Vector2 p0 = t0.screenPosition;
            Vector2 p1 = t1.screenPosition;
            float dist = Vector2.Distance(p0, p1);

            // ZOOM pinch (stable: chỉ chỉnh targetBirdDist)
            if (lastTouchDist > 0)
            {
                float pinchDelta = dist - lastTouchDist;

                float zoomFactor = Mathf.Max(0.25f, targetBirdDist * 0.02f);
                targetBirdDist -= pinchDelta * zoomSensitivity * zoomFactor * pinchToDist;

                ClampTargetBirdDist();
            }
            lastTouchDist = dist;

            // ROTATE + PITCH
            Vector2 avgDelta = (t0.delta + t1.delta) * 0.5f;

            float rotScale = 0.12f;
            birdYaw += avgDelta.x * rotateSpeed * rotScale * dt;

            currentPitch -= avgDelta.y * rotateSpeed * rotScale * dt;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

            ClampTargetBirdDist();
            UpdateBirdCamera(false);
        }
    }

    // ================= BirdEye Orbit Update =================
    void UpdateBirdCamera(bool instant)
    {
        ClampTargetBirdDist();

        if (instant || zoomDamping <= 0f)
        {
            birdDist = targetBirdDist;
        }
        else
        {
            float t = 1f - Mathf.Exp(-zoomDamping * Time.deltaTime);
            birdDist = Mathf.Lerp(birdDist, targetBirdDist, t);
        }

        Quaternion rot = Quaternion.Euler(currentPitch, birdYaw, 0f);
        transform.position = birdPivot + rot * Vector3.back * birdDist;
        transform.LookAt(birdPivot);
    }

    // ================= MOVE ROUTINE (mostly unchanged) =================
    public void MoveBirdEyeFromTo(
        Transform from,
        Transform to,
        Action onComplete = null,
        Action<Vector3> onStep = null
    )
    {
        if (from == null || to == null) return;

        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        Vector3 start = ProjectToNavMesh(from.position);
        Vector3 end = ProjectToNavMesh(to.position);

        if (!UnityEngine.AI.NavMesh.CalculatePath(start, end, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            Debug.LogError("NavMesh path failed");
            return;
        }

        moveRoutine = StartCoroutine(
            MoveRoutine(path.corners, onComplete, onStep)
        );
    }

    IEnumerator MoveRoutine(
        Vector3[] corners,
        Action onComplete,
        Action<Vector3> onStep
    )
    {
        lockCamera = true;

        if (corners.Length < 2)
        {
            lockCamera = false;
            yield break;
        }

        // 1) Lift up
        Vector3 liftTarget = corners[0] + Vector3.up * height;
        Quaternion topDownRot = Quaternion.Euler(90f, 0f, 0f);

        while (Vector3.Distance(transform.position, liftTarget) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                liftTarget,
                moveSpeed * Time.deltaTime
            );
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                topDownRot,
                rotateSpeed * Time.deltaTime
            );
            yield return null;
        }

        // 2) Move along path
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 target = corners[i] + Vector3.up * height;
            onStep?.Invoke(corners[i]);

            while (Vector3.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    target,
                    moveSpeed * Time.deltaTime
                );

                onStep?.Invoke(transform.position - Vector3.up * height);
                yield return null;
            }
        }

        // 3) Overview (diagonal)
        Vector3 startPoint = corners[0];
        Vector3 endPoint = corners[corners.Length - 1];

        Vector3 center = (startPoint + endPoint) * 0.5f;
        Vector3 pathDir = (endPoint - startPoint).normalized;

        Vector3 overviewOffset =
            -pathDir * 10f +
            Vector3.up * height;

        Vector3 overviewPos = center + overviewOffset;

        Quaternion overviewRot = Quaternion.LookRotation(
            center - overviewPos,
            Vector3.up
        );

        while (Vector3.Distance(transform.position, overviewPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                overviewPos,
                moveSpeed * Time.deltaTime
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                overviewRot,
                rotateSpeed * Time.deltaTime
            );

            yield return null;
        }

        // ✅ IMPORTANT: sync lại pivot/yaw/pitch/dist sau routine để zoom/rotate không bị “lạ”
        birdPivot = center;
        SyncBirdAnglesFromCurrentView();
        birdDist = Vector3.Distance(transform.position, birdPivot);
        targetBirdDist = birdDist;
        ClampTargetBirdDist();
        UpdateBirdCamera(true);

        onComplete?.Invoke();
        lockCamera = false;
    }

    Vector3 ProjectToNavMesh(Vector3 pos)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return pos;
    }
}
