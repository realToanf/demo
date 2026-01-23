// Điều khiển camera ở chế độ BirdEye/FPS, xử lý input và giới hạn vùng.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class CameraController : MonoBehaviour
{
    public enum CameraMode { FPS, BirdEye }
    public CameraMode mode = CameraMode.BirdEye;
    public Camera cam;
    public LayerMask occluderMask;

    [Header("Cấu hình góc nhìn Tổng quan (Overview)")]
    public float overviewPitch = 65f;
    public float overviewYaw = 45f;
    public float boundsPadding = 1.2f;
    public float minHeightAboveRoute = 2f;
    public int maxOcclusionAdjustSteps = 8;
    public float occlusionHeightStep = 2f;

    [Header("Cấu hình góc nhìn Thứ nhất (FPS)")]
    public float fpsMoveSpeed = 7.5f;
    public float fpsLookSpeed = 6f;

    [Header("Giới hạn vùng Camera")]
    public BoxCollider cameraZone;
    public float zonePadding = 0.0f;
    public bool clampPivotToZone = true;
    public bool clampHeightToZone = true;

    [Header("Chặn nhập liệu khi di chuột qua UI")]
    public bool blockInputByUI = false;

    [Header("Điểm lấy nét lại (Refocus)")]
    public Transform refocusPose;
    public Transform idleSnapTarget;
    public Transform refocusLookTarget;

    [Header("Cấu hình góc nhìn Chim bay (BirdEye)")]
    public float panSpeed = 5f;
    public float rotateSpeed = 15f;
    public float zoomSpeed = 1f;
    public float minHeight = 2f;
    public float maxHeight = 90f;
    public float minPitch = 20f;
    public float maxPitch = 80f;
    public float height = 50f;
    public float moveSpeed = 8f;
    public float followHeightOffset = 20f;
    public float heightSmoothSpeed = 5f;

    [Header("Cấu hình Cảm ứng (Mobile)")]
    public float touchPanMultiplier = 10f;
    public float touchZoomSensitivity = 0.036f;
    public float gestureThreshold = 5f;

    [Header("Điểm xoay mục tiêu (Kéo & Thả)")]
    public Transform pivotTarget;              // đối tượng để camera xoay quanh
    public Vector3 pivotOffset = Vector3.zero; // độ dời (tùy chọn)

    [Header("Hành vi khi nhàn rỗi / Tự động khóa")]
    public float relockAfterIdleSeconds = 5f;  // tự động khóa lại sau X giây nhàn rỗi
    public bool enableIdleSpin = true;
    public float idleSpinDelay = 0.5f;         // thời gian chờ trước khi bắt đầu xoay
    public float idleSpinSpeed = 8.0f;          // tốc độ xoay (độ/giây)
    public float idleSpinRamp = 3.0f;           // tốc độ tăng/giảm dần

    bool lockCamera = false;

    float yaw;
    float pitch;

    float currentPitch = 45f;

    // Orbit state
    Vector3 birdPivot;
    float birdDist = 20f;
    Vector3 orbitOffset;

    // Idle state
    float lastInputTime;
    float idleSpinWeight;

    // Pivot lock state
    bool pivotLocked = false;
    bool followPivotTarget = true;

    // =========================
    // ADDED: manual (world-space) pivot lock override
    // - lets you lock to destination without changing pivotTarget (spin/refocus target)
    // =========================
    bool manualPivotLock = false;
    Vector3 manualPivot;

    float lastTouchDist;
    Vector2 lastTwoFingerCenter;

    Coroutine moveRoutine;
    UnityEngine.AI.NavMeshPath path;

    void Awake()
    {
        Application.targetFrameRate = 120;
        path = new UnityEngine.AI.NavMeshPath();
    }

    void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;

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

        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }

    void Update()
    {
        // If an automated move coroutine is driving the camera, let it run untouched.
        if (lockCamera)
            return;

        // We still want internal camera maintenance even when UI blocks input.
        bool inputBlocked = blockInputByUI;

        bool inputThisFrame = !inputBlocked && HasAnyInputThisFrame();

        if (!inputBlocked && Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            ToggleMode();
            inputThisFrame = true;
        }

        if (inputThisFrame)
        {
            lastInputTime = Time.time;

            // Any interaction releases from pivot lock immediately
            if (mode == CameraMode.BirdEye && pivotLocked)
                ReleaseFromPivot();
        }
        else
        {
            // If UI is blocking, DO NOT treat that as "user is idle"
            if (!inputBlocked)
            {
                // No input: if idle long enough, instantly re-lock to pivot target
                if (mode == CameraMode.BirdEye && !pivotLocked && pivotTarget != null)
                {
                    float idleFor = Time.time - lastInputTime;
                    if (idleFor >= relockAfterIdleSeconds)
                        InstantRelockToPivot();
                }
            }
            else
            {
                // Optional: keep lastInputTime "fresh" while hovering UI
                lastInputTime = Time.time;
            }
        }

        if (mode == CameraMode.BirdEye && pivotLocked)
        {
            if (manualPivotLock)
            {
                birdPivot = manualPivot;
            }
            else if (followPivotTarget && pivotTarget != null)
            {
                birdPivot = pivotTarget.position + pivotOffset;
            }

            transform.position = birdPivot + orbitOffset;
            transform.LookAt(birdPivot);

            birdDist = orbitOffset.magnitude;
        }


        // Controls should be blocked when UI is hovered
        if (!inputBlocked)
        {
            TouchControl();
            MouseKeyboardControl();
        }

        // Always enforce zone so camera doesn't accumulate illegal positions
        EnforceZone();

        // Only spin if not blocked (hovering UI shouldn't cause spin)
        if (!inputBlocked)
            ApplyIdleSpin();
    }

    // =========================================================
    // KHÓA / NHẢ KHÓA CAMERA VỚI ĐIỂM XOAY
    // =========================================================
    void ReleaseFromPivot()
    {
        pivotLocked = false;

        // =========================
        // ADDED: release manual destination lock on any user interaction
        // =========================
        manualPivotLock = false;

        birdDist = Vector3.Distance(transform.position, birdPivot);
        orbitOffset = transform.position - birdPivot;

        // Stop spin immediately
        idleSpinWeight = 0f;
    }

    void InstantRelockToPivot()
    {
        if (pivotTarget == null) return;

        // =========================
        // ADDED: relocking to pivotTarget clears manual destination lock
        // =========================
        manualPivotLock = false;

        birdPivot = pivotTarget.position + pivotOffset;
        followPivotTarget = true;

        // capture current orbit relative to the pivot
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        transform.LookAt(birdPivot);

        pivotLocked = true;
        idleSpinWeight = 0f;
        EnforceZone();
    }
    // =================================================

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

    void ApplyIdleSpin()
    {
        if (!enableIdleSpin) return;
        if (lockCamera) return;
        if (mode != CameraMode.BirdEye) return;
        if (!pivotLocked) return;

        float idleFor = Time.time - lastInputTime;
        bool shouldSpin = idleFor >= (relockAfterIdleSeconds + idleSpinDelay);

        float target = shouldSpin ? 1f : 0f;
        idleSpinWeight = Mathf.MoveTowards(idleSpinWeight, target, idleSpinRamp * Time.deltaTime);

        if (idleSpinWeight <= 0f) return;

        float angle = idleSpinSpeed * idleSpinWeight * Time.deltaTime;

        transform.RotateAround(birdPivot, Vector3.up, angle);
        transform.LookAt(birdPivot);

        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        EnforceZone();
    }

    // =========================================================
    // THAY ĐỔI CHẾ ĐỘ CAMERA
    // =========================================================
    void ToggleMode()
    {
        mode = mode == CameraMode.FPS ? CameraMode.BirdEye : CameraMode.FPS;

        if (mode == CameraMode.BirdEye)
        {
            // entering bird-eye: default back to pivotTarget-follow unless you explicitly set manualPivot elsewhere
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
            // Leaving BirdEye
            pivotLocked = false;
            manualPivotLock = false;
            idleSpinWeight = 0f;
        }
    }

    // =========================================================
    // ĐIỀU KHIỂN CHUỘT VÀ BÀN PHÍM (PC)
    // =========================================================
    void MouseKeyboardControl()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (mode == CameraMode.FPS)
        {
            Vector3 dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += transform.forward;
            if (kb.sKey.isPressed) dir -= transform.forward;
            if (kb.aKey.isPressed) dir -= transform.right;
            if (kb.dKey.isPressed) dir += transform.right;

            transform.position += dir * fpsMoveSpeed * Time.deltaTime;

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
            Vector2 delta = mouse.delta.ReadValue() * Time.deltaTime;

            // PAN (unlocked only; first interaction releases)
            if (mouse.leftButton.isPressed)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                Vector3 move = (-right * delta.x - forward * delta.y) * panSpeed;
                transform.position += move;
                birdPivot += move;

                orbitOffset = transform.position - birdPivot;
                birdDist = orbitOffset.magnitude;
            }

            // ROTATE
            if (mouse.rightButton.isPressed)
            {
                transform.RotateAround(birdPivot, Vector3.up, delta.x * rotateSpeed);

                currentPitch -= delta.y * rotateSpeed;
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                UpdateBirdCamera();
            }

            // ZOOM
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 dir = transform.forward;
                Vector3 newPos = transform.position + dir * scroll * zoomSpeed;

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
    // ĐIỀU KHIỂN CẢM ỨNG (MOBILE/TABLET)
    // =========================================================
    void TouchControl()
    {
        if (mode != CameraMode.BirdEye) return;

        var ts = Touchscreen.current;
        if (ts == null) return;

        var touches = ts.touches;

        int activeTouchCount = 0;
        for (int i = 0; i < touches.Count; i++)
            if (touches[i].isInProgress) activeTouchCount++;

        // 1) ONE FINGER ROTATE
        if (activeTouchCount == 1)
        {
            var touch = touches[0].isInProgress ? touches[0] : touches[1];

            if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector2 d = touch.delta.ReadValue();
                if (d.magnitude < 0.1f) return;

                transform.RotateAround(birdPivot, Vector3.up, d.x * rotateSpeed * Time.deltaTime);

                currentPitch -= d.y * rotateSpeed * Time.deltaTime;
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                UpdateBirdCamera();
            }
        }
        // 2) TWO FINGERS PAN + PINCH
        else if (activeTouchCount >= 2)
        {
            var touch0 = touches[0];
            var touch1 = touches[1];

            Vector2 p0 = touch0.position.ReadValue();
            Vector2 p1 = touch1.position.ReadValue();
            Vector2 currentCenter = (p0 + p1) * 0.5f;
            float currentDist = Vector2.Distance(p0, p1);

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

            // PAN
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

            lastTouchDist = currentDist;
            lastTwoFingerCenter = currentCenter;
        }
        else
        {
            if (lastTouchDist > 0)
            {
                lastTouchDist = 0;
                lastTwoFingerCenter = Vector2.zero;
            }
        }
    }

    void UpdateBirdCamera()
    {
        birdDist = Vector3.Distance(transform.position, birdPivot);

        Quaternion rot = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0);
        transform.position = birdPivot + rot * Vector3.back * birdDist;
        transform.LookAt(birdPivot);

        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;
        EnforceZone();
    }

    public void MoveBirdEyeAlongCorners(
        Vector3[] corners,
        Action onComplete = null,
        Action<Vector3> onStep = null
    )
    {
        if (corners == null || corners.Length < 2) return;

        if (moveRoutine != null)
            CancelMove();

        // Drive the same coroutine, but do NOT recalc NavMesh path here
        moveRoutine = StartCoroutine(MoveRoutine(corners, onComplete, onStep));
    }


    public void CancelMove()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        lockCamera = false;

        lastTouchDist = 0;
        lastTwoFingerCenter = Vector2.zero;

        lastInputTime = Time.time;
        idleSpinWeight = 0f;
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
            moveRoutine = null;
            yield break;
        }

        Vector3 liftTarget = corners[0] + Vector3.up * (height * 0.3f);
        Quaternion topDownRot = Quaternion.Euler(90f, 0f, 0f);

        while (Vector3.Distance(transform.position, liftTarget) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, liftTarget, moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, topDownRot, rotateSpeed * Time.deltaTime);
            yield return null;
        }

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 target = corners[i];

            while (Vector3.Distance(new Vector2(transform.position.x, transform.position.z),
                   new Vector2(target.x, target.z)) > 0.05f)
            {
                Vector3 desiredPos = Vector3.MoveTowards(
                    transform.position,
                    new Vector3(target.x, transform.position.y, target.z),
                    (moveSpeed * 0.6f) * Time.deltaTime
                );

                float desiredY = target.y + followHeightOffset;
                float newY = Mathf.Lerp(transform.position.y, desiredY, (heightSmoothSpeed * 0.6f) * Time.deltaTime);

                transform.position = new Vector3(desiredPos.x, newY, desiredPos.z);

                onStep?.Invoke(new Vector3(transform.position.x, target.y, transform.position.z));
                yield return null;
            }
        }

        // =========================
        // FIXED: overview interpolation was a no-op before
        // =========================
        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;

        PositionSmartOverview(corners);
        Vector3 toPos = transform.position;
        Quaternion toRot = transform.rotation;

        // revert so we can lerp from -> to
        transform.position = fromPos;
        transform.rotation = fromRot;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * (moveSpeed * 0.3f);
            float a = Mathf.Clamp01(t);

            transform.position = Vector3.Lerp(fromPos, toPos, a);
            transform.rotation = Quaternion.Slerp(fromRot, toRot, a);

            yield return null;
        }

        onComplete?.Invoke();
        lockCamera = false;

        lastInputTime = Time.time;
        idleSpinWeight = 0f;

        // Keep the route-center pivot that PositionSmartOverview() already set.
        // Stay locked so orbit/idle spin rotates around center-of-route.
        manualPivotLock = false;
        pivotLocked = true;
        followPivotTarget = false;

        // rebuild orbit state coherently
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;
        transform.LookAt(birdPivot);

        lastInputTime = Time.time;
        idleSpinWeight = 0f;

        moveRoutine = null;
    }

    Vector3 ProjectToNavMesh(Vector3 pos)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return pos;
    }

    public void SnapToBirdEye(Transform target, float snapHeight = 40f, float pitch = 75f)
    {
        if (target == null) return;

        mode = CameraMode.BirdEye;

        birdPivot = target.position + pivotOffset;
        followPivotTarget = true;

        // SnapToBirdEye is an explicit lock: do NOT use manual destination lock here
        manualPivotLock = false;

        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);

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

    Vector3 ClampToZone(Vector3 worldPos)
    {
        if (cameraZone == null) return worldPos;

        // Work in collider local space so rotation/scale of the zone is supported
        Transform zt = cameraZone.transform;

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

    void EnforceZone()
    {
        if (cameraZone == null) return;
        if (mode != CameraMode.BirdEye) return;

        // Clamp pivot first (optional)
        if (clampPivotToZone)
            birdPivot = ClampToZone(birdPivot);

        // Clamp camera position
        Vector3 clampedPos = ClampToZone(transform.position);
        if (clampedPos != transform.position)
        {
            transform.position = clampedPos;

            // Rebuild orbit values so the camera continues smoothly
            orbitOffset = transform.position - birdPivot;
            birdDist = orbitOffset.magnitude;

            // Keep looking at pivot
            transform.LookAt(birdPivot);
        }
    }

    public void RefocusNow()
    {
        if (refocusPose == null) return;
        if (moveRoutine != null) CancelMove();

        mode = CameraMode.BirdEye;
        lockCamera = false;

        // Refocus should not be affected by manual destination lock
        manualPivotLock = false;

        transform.position = refocusPose.position;

        Vector3 lookPoint =
            (refocusLookTarget != null) ? (refocusLookTarget.position + pivotOffset) :
            (idleSnapTarget != null) ? (idleSnapTarget.position + pivotOffset) :
            birdPivot;

        birdPivot = lookPoint;
        transform.LookAt(lookPoint);

        // keep orbit state coherent after refocus
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }

    void PositionSmartOverview(Vector3[] corners)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;
        if (corners == null || corners.Length == 0) return;

        // 1) Bounds around the route
        Bounds b = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            b.Encapsulate(corners[i]);

        Vector3 ext = b.extents * boundsPadding;

        // 2) Required height to fit route in FOV
        float vertFovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float horizFovRad = 2f * Mathf.Atan(Mathf.Tan(vertFovRad * 0.5f) * cam.aspect);

        float halfWidth = ext.x;
        float halfDepth = ext.z;

        float heightFromDepth = halfDepth / Mathf.Tan(vertFovRad * 0.5f);
        float heightFromWidth = halfWidth / Mathf.Tan(horizFovRad * 0.5f);

        float baseHeight = Mathf.Max(heightFromDepth, heightFromWidth, minHeightAboveRoute);

        Vector3 center = b.center;

        // 3) Place camera at pitched overview
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

        // 4) Occlusion correction (lift if walls block the route)
        for (int step = 0; step < maxOcclusionAdjustSteps; step++)
        {
            if (!IsRouteOccluded(corners))
                break;

            camPos += Vector3.up * occlusionHeightStep;
            transform.position = camPos;
            transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);
        }

        // If still occluded, try rotating around the route
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

            // Final fallback: pure top-down
            if (!foundAngle)
            {
                Vector3 topPos = center + Vector3.up * (baseHeight * 2f);
                transform.position = topPos;
                transform.rotation = Quaternion.Euler(89f, 0f, 0f);
            }
        }

        // keep bird-eye orbit in sync
        birdPivot = center;
        followPivotTarget = false;
        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;
    }

    bool IsRouteOccluded(Vector3[] corners)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return false;
        if (corners == null || corners.Length == 0) return false;

        Vector3 camPos = transform.position;

        const int samplesPerSegment = 2;

        for (int i = 0; i < corners.Length; i++)
        {
            // Direct point
            Vector3 p = corners[i] + Vector3.up * 0.2f;
            if (IsPointOccluded(camPos, p))
                return true;

            // Midpoints between segments
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

    bool IsPointOccluded(Vector3 camPos, Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - camPos;
        float dist = dir.magnitude;
        if (dist < 0.1f) return false;

        dir /= dist;

        if (Physics.Raycast(camPos, dir, out RaycastHit hit, dist, occluderMask, QueryTriggerInteraction.Ignore))
        {
            // hit something in occluderMask before the route point
            return true;
        }

        return false;
    }
}
