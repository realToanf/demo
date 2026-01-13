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

    [Header("Camera Zone Limits")]
    public BoxCollider cameraZone;        
    public float zonePadding = 0.0f;   
    public bool clampPivotToZone = true;    
    public bool clampHeightToZone = true;   

    [Header("Refocus")]
    public Transform refocusPose;

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
    public float touchPanMultiplier = 10f;
    public float touchZoomSensitivity = 0.036f;
    public float gestureThreshold = 5f;

    [Header("Pivot Target (Drag & Drop)")]
    public Transform pivotTarget;              // drag & drop
    public Vector3 pivotOffset = Vector3.zero; // optional

    [Header("Idle / Lock Behavior")]
    public float relockAfterIdleSeconds = 5f;  // after user stops interacting, lock back to pivot
    public bool enableIdleSpin = true;
    public float idleSpinDelay = 0.5f;         // after locked, how long before spin starts
    public float idleSpinSpeed = 8.0f;         // degrees/sec
    public float idleSpinRamp = 3.0f;          // blend in/out speed

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
        if (lockCamera) return;

        bool inputThisFrame = HasAnyInputThisFrame();

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
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
            // No input: if idle long enough, instantly re-lock to pivot target
            if (mode == CameraMode.BirdEye && !pivotLocked && pivotTarget != null)
            {
                float idleFor = Time.time - lastInputTime;
                if (idleFor >= relockAfterIdleSeconds)
                    InstantRelockToPivot();
            }
        }

        // While locked, keep following pivot target
        if (mode == CameraMode.BirdEye && pivotLocked && pivotTarget != null)
        {
            birdPivot = pivotTarget.position + pivotOffset;
            transform.position = birdPivot + orbitOffset;
            transform.LookAt(birdPivot);

            birdDist = orbitOffset.magnitude;
        }

        // Controls
        TouchControl();
        MouseKeyboardControl();

        EnforceZone();

        // Idle spin only when locked
        ApplyIdleSpin();
    }

    // ================= LOCK / RELOCK =================
    void ReleaseFromPivot()
    {
        pivotLocked = false;

        birdDist = Vector3.Distance(transform.position, birdPivot);
        orbitOffset = transform.position - birdPivot;

        // Stop spin immediately
        idleSpinWeight = 0f;
    }

    void InstantRelockToPivot()
    {
        if (pivotTarget == null) return;

        // Recenter pivot to target
        birdPivot = pivotTarget.position + pivotOffset;

        // "Appropriate point": keep the current orbitOffset (user's last panned offset),
        // but apply it to the target pivot instantly.
        transform.position = birdPivot + orbitOffset;
        transform.LookAt(birdPivot);

        birdDist = orbitOffset.magnitude;

        pivotLocked = true;

        // Keep idleSpinWeight at 0; it will ramp in after idleSpinDelay
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

    // ================= MODE =================
    void ToggleMode()
    {
        mode = mode == CameraMode.FPS ? CameraMode.BirdEye : CameraMode.FPS;

        if (mode == CameraMode.BirdEye)
        {
            if (pivotTarget != null)
            {
                birdPivot = pivotTarget.position + pivotOffset;
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
            idleSpinWeight = 0f;
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

            // PAN (unlocked only; first interaction releases)
            if (mouse.leftButton.isPressed)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

                Vector3 move = (-right * delta.x - forward * delta.y) * panSpeed;
                transform.position += move;

                // While unlocked, move pivot with camera for consistent orbit center feel
                if (!pivotLocked)
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

    // ================= TOUCH =================
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

                    if (!pivotLocked)
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
        Vector3 end = ProjectToNavMesh(to.position);

        if (!UnityEngine.AI.NavMesh.CalculatePath(start, end, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            Debug.LogError("NavMesh path failed");
            return;
        }

        moveRoutine = StartCoroutine(MoveRoutine(path.corners, onComplete, onStep));
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

        Vector3 startPoint = corners[0];
        Vector3 endPoint = corners[corners.Length - 1];
        Vector3 center = (startPoint + endPoint) * 0.5f;

        Vector3 pathDir = (endPoint - startPoint).normalized;

        Vector3 overviewOffset = -pathDir * 10f + Vector3.up * (height * 0.6f);
        Vector3 overviewPos = center + overviewOffset;

        Quaternion overviewRot = Quaternion.LookRotation(center - overviewPos, Vector3.up);

        while (Vector3.Distance(transform.position, overviewPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, overviewPos, (moveSpeed * 0.6f) * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, overviewRot, rotateSpeed * Time.deltaTime);
            yield return null;
        }

        onComplete?.Invoke();
        lockCamera = false;

        lastInputTime = Time.time;
        idleSpinWeight = 0f;

        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

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

        pivotTarget = target;
        birdPivot = target.position + pivotOffset;

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
        if (moveRoutine != null)
            CancelMove();

        mode = CameraMode.BirdEye;
        lockCamera = false;
        pivotLocked = false;

        transform.position = refocusPose.position;

        if (pivotTarget != null)
            birdPivot = pivotTarget.position + pivotOffset;

        if (clampPivotToZone)
            birdPivot = ClampToZone(birdPivot);

        transform.LookAt(birdPivot);

        orbitOffset = transform.position - birdPivot;
        birdDist = orbitOffset.magnitude;

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        EnforceZone();

        lastInputTime = Time.time;
        idleSpinWeight = 0f;
    }
}
