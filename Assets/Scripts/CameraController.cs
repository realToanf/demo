using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class CameraController : MonoBehaviour
{
    public enum CameraMode { FPS, BirdEye }
    public CameraMode mode = CameraMode.BirdEye;

    [Header("FPS Settings")]
    public float fpsMoveSpeed = 7.5f;
    public float fpsLookSpeed = 6f;

    [Header("BirdEye Settings")]
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

    [Header("Touch Settings")]
    public float touchPanMultiplier = 10f; // Multiplier for touch pan sensitivity
    public float touchZoomSensitivity = 0.036f; // Sensitivity for pinch zoom
    public float gestureThreshold = 5f; // Minimum pixel movement to detect gesture

    bool lockCamera = false;

    float yaw;
    float pitch;
    float currentPitch = 45f;
    Vector3 birdPivot;

    float lastTouchDist;
    Vector2 lastTwoFingerCenter; // Track center point of two-finger gesture

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
        birdPivot = transform.position + transform.forward * 5f;
    }

    void Update()
    {
        if(lockCamera) return;

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            Debug.Log("Toggle mode");
            ToggleMode();
        }

        TouchControl();

        MouseKeyboardControl();
    }

    // ================= MODE =================
    void ToggleMode()
    {
        mode = mode == CameraMode.FPS ? CameraMode.BirdEye : CameraMode.FPS;

        if (mode == CameraMode.BirdEye)
        {
            birdPivot = transform.position + transform.forward * 5f;
            transform.LookAt(birdPivot);
        }
    }

    // ================= PC =================
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

            // PAN
            if (mouse.leftButton.isPressed)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                Vector3 move = (-right * delta.x - forward * delta.y) * panSpeed;
                transform.position += move;
                birdPivot += move;
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

                float height = newPos.y;
                if (height >= minHeight && height <= maxHeight)
                {
                    transform.position = newPos;
                    // Cập nhật birdPivot sau khi zoom để tránh camera nhảy lên cao khi xoay
                    birdPivot = transform.position + transform.forward * Vector3.Distance(transform.position, birdPivot);
                }
            }
        }
    }

    // ================= TOUCH =================
    void TouchControl()
    {
        if (mode != CameraMode.BirdEye) return;

        var ts = Touchscreen.current;
        if (ts == null) return;

        var touches = ts.touches;

        // Count active touches
        int activeTouchCount = 0;
        for (int i = 0; i < touches.Count; i++)
        {
            if (touches[i].isInProgress)
                activeTouchCount++;
        }

        // ========================================
        // 1️⃣ ONE FINGER - ROTATE CAMERA
        // ========================================
        if (activeTouchCount == 1)
        {
            // Find the active touch
            var touch = touches[0].isInProgress ? touches[0] : touches[1];
            
            // Only process if touch is actively moving
            if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector2 delta = touch.delta.ReadValue();
                
                // Ignore very small movements (noise filtering)
                if (delta.magnitude < 0.1f) return;

                // Horizontal rotation (yaw) around birdPivot
                transform.RotateAround(birdPivot, Vector3.up, delta.x * rotateSpeed * Time.deltaTime);

                // Vertical rotation (pitch)
                currentPitch -= delta.y * rotateSpeed * Time.deltaTime;
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

                UpdateBirdCamera();
            }
        }

        // ========================================
        // 2️⃣ TWO FINGERS - PAN + PINCH ZOOM
        // ========================================
        else if (activeTouchCount >= 2)
        {
            var touch0 = touches[0];
            var touch1 = touches[1];

            Vector2 p0 = touch0.position.ReadValue();
            Vector2 p1 = touch1.position.ReadValue();
            Vector2 currentCenter = (p0 + p1) * 0.5f;
            float currentDist = Vector2.Distance(p0, p1);

            // Initialize on first frame of two-finger gesture
            if (lastTouchDist <= 0)
            {
                lastTouchDist = currentDist;
                lastTwoFingerCenter = currentCenter;
                return; // Skip first frame to avoid jumps
            }

            // ========================================
            // PINCH ZOOM (based on distance change)
            // ========================================
            float distChange = currentDist - lastTouchDist;
            
            // Only zoom if distance change is significant
            if (Mathf.Abs(distChange) > gestureThreshold)
            {
                Vector3 zoomDir = transform.forward;
                Vector3 newPos = transform.position + zoomDir * distChange * touchZoomSensitivity;
                
                // Clamp height
                if (newPos.y >= minHeight && newPos.y <= maxHeight)
                {
                    transform.position = newPos;
                    
                    // Update birdPivot to maintain rotation center
                    float pivotDist = Vector3.Distance(transform.position, birdPivot);
                    birdPivot = transform.position + transform.forward * pivotDist;
                }
            }

            // ========================================
            // PAN (based on center movement)
            // ========================================
            // Only pan if both touches are actively moving
            if (touch0.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved ||
                touch1.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector2 centerDelta = currentCenter - lastTwoFingerCenter;
                
                // Only pan if movement is significant
                if (centerDelta.magnitude > 0.5f)
                {
                    Vector3 right = transform.right;
                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                    Vector3 move = (-right * centerDelta.x - forward * centerDelta.y) * touchPanMultiplier * Time.deltaTime;
                    transform.position += move;
                    birdPivot += move;
                }
            }

            // Update state for next frame
            lastTouchDist = currentDist;
            lastTwoFingerCenter = currentCenter;
        }

        // ========================================
        // 3️⃣ RESET STATE when fingers lifted
        // ========================================
        else
        {
            // Reset two-finger gesture state
            if (lastTouchDist > 0)
            {
                lastTouchDist = 0;
                lastTwoFingerCenter = Vector2.zero;
            }
        }
    }

    void UpdateBirdCamera()
    {
        float dist = Vector3.Distance(transform.position, birdPivot);
        Quaternion rot = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0);
        transform.position = birdPivot + rot * Vector3.back * dist;
        transform.LookAt(birdPivot);
    }

    public void MoveBirdEyeFromTo(
        Transform from,
        Transform to,
        Action onComplete = null,
        Action<Vector3> onStep = null
    )
    {   
        if (from == null || to == null) return;

        if (moveRoutine != null)
            CancelMove();

        Vector3 start = ProjectToNavMesh(from.position);
        Vector3 end   = ProjectToNavMesh(to.position);

        if (!UnityEngine.AI.NavMesh.CalculatePath(start, end, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            Debug.LogError("NavMesh path failed");
            return;
        }

        moveRoutine = StartCoroutine(
            MoveRoutine(path.corners, onComplete, onStep)
        );
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
    }
    // =================================================
    IEnumerator MoveRoutine(
        Vector3[] corners,
        Action onComplete,
        Action<Vector3> onStep
    )
    {
        lockCamera = true;

        // lưu trạng thái ban đầu
        Vector3 originPos = transform.position;
        Quaternion originRot = transform.rotation;

        if (corners.Length < 2)
        {
            lockCamera = false;
            moveRoutine = null;
            yield break;
        }

        // ======================
        // 1️⃣ BAY LÊN CAO
        // ======================
        Vector3 liftTarget = corners[0] + Vector3.up * (height * 0.3f);
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

        // ======================
        // 2️⃣ DI CHUYỂN THEO PATH
        // ======================
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 target = corners[i];

            while (Vector3.Distance(new Vector2(transform.position.x, transform.position.z),
           new Vector2(target.x, target.z)) > 0.05f)
            {
                // move XZ toward target
                Vector3 desiredPos = Vector3.MoveTowards(
                    transform.position,
                    new Vector3(target.x, transform.position.y, target.z),
                    (moveSpeed * 0.6f) * Time.deltaTime
                );

                // compute desired Y based on target.y + offset
                float desiredY = target.y + followHeightOffset;

                // smooth Y
                float newY = Mathf.Lerp(transform.position.y, desiredY, (heightSmoothSpeed * 0.6f) * Time.deltaTime);

                transform.position = new Vector3(desiredPos.x, newY, desiredPos.z);

                onStep?.Invoke(new Vector3(transform.position.x, target.y, transform.position.z));
                yield return null;
            }
        }

        // ======================
        // 3️⃣ VIEW TỔNG QUAN (CHÉO)
        // ======================
        Vector3 startPoint = corners[0];
        Vector3 endPoint   = corners[corners.Length - 1];

        // điểm giữa
        Vector3 center = (startPoint + endPoint) * 0.5f;

        // hướng từ start → end (ĐỔI TÊN)
        Vector3 pathDir = (endPoint - startPoint).normalized;

        // offset chéo
        Vector3 overviewOffset =
            -pathDir * 10f +
            Vector3.up * (height * 0.6f);

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
                (moveSpeed * 0.6f) * Time.deltaTime
            );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                overviewRot,
                rotateSpeed * Time.deltaTime
            );

            yield return null;
        }

        onComplete?.Invoke();
        lockCamera = false;

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
        if (target == null)
        {
            return;
        }

        mode = CameraMode.BirdEye;

        birdPivot = target.position;

        float yaw = transform.eulerAngles.y;

        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rot = Quaternion.Euler(currentPitch, yaw, 0f);
        transform.position = birdPivot + rot * Vector3.back * snapHeight;
        transform.LookAt(birdPivot);
    }
}
