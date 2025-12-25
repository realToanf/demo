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
    public float minHeight = 5f;
    public float maxHeight = 60f;
    public float minPitch = 20f;
    public float maxPitch = 80f;

    public float height = 15f;
    public float moveSpeed = 3f;

    bool lockCamera = false;

    float yaw;
    float pitch;
    float currentPitch = 45f;
    Vector3 birdPivot;

    float lastTouchDist;

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
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            Debug.Log("Toggle mode");
            ToggleMode();
        }

#if UNITY_ANDROID || UNITY_IOS
        TouchControl();
#else
        MouseKeyboardControl();
#endif
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
                Debug.Log(scroll);
                Vector3 dir = transform.forward;
                Vector3 newPos = transform.position + dir * scroll * zoomSpeed;

                float height = newPos.y;
                if (height >= minHeight && height <= maxHeight)
                {
                    transform.position = newPos;
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

        // 1 finger PAN
        if (touches.Count == 1 && touches[0].isInProgress)
        {
            Vector2 delta = touches[0].delta.ReadValue();
            Vector3 right = transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

            Vector3 move = (-right * delta.x - forward * delta.y) * panSpeed * 50f;
            transform.position += move;
            birdPivot += move;
        }

        // 2 fingers ROTATE + ZOOM
        if (touches.Count >= 2 &&
            touches[0].isInProgress &&
            touches[1].isInProgress)
        {
            Vector2 p0 = touches[0].position.ReadValue();
            Vector2 p1 = touches[1].position.ReadValue();
            float dist = Vector2.Distance(p0, p1);

            if (lastTouchDist > 0)
            {
                float zoomDelta = dist - lastTouchDist;
                Vector3 newPos = transform.position + transform.forward * zoomDelta * zoomSpeed;
                if (newPos.y >= minHeight && newPos.y <= maxHeight)
                    transform.position = newPos;
            }
            lastTouchDist = dist;

            Vector2 avgDelta = (touches[0].delta.ReadValue() + touches[1].delta.ReadValue()) * 0.5f;
            transform.RotateAround(birdPivot, Vector3.up, avgDelta.x * rotateSpeed);

            currentPitch -= avgDelta.y * rotateSpeed;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

            UpdateBirdCamera();
        }

        if (touches.Count < 2)
            lastTouchDist = 0;
    }

    void UpdateBirdCamera()
    {
        float dist = Vector3.Distance(transform.position, birdPivot);
        Quaternion rot = Quaternion.Euler(currentPitch, transform.eulerAngles.y, 0);
        transform.position = birdPivot + rot * Vector3.back * dist;
        transform.LookAt(birdPivot);
    }

    public void MoveBirdEyeFromTo(Transform from, Transform to, Action onComplete = null, Action<Vector3> onStep = null)
    {
        if (from == null || to == null) return;

        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        Vector3 start = ProjectToNavMesh(from.position);
        Vector3 end   = ProjectToNavMesh(to.position);

        if (!UnityEngine.AI.NavMesh.CalculatePath(start, end, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            Debug.LogError("NavMesh path failed");
            return;
        }

        moveRoutine = StartCoroutine(MoveRoutine(path.corners, onComplete, onStep));
    }
    // =================================================

    IEnumerator MoveRoutine(Vector3[] corners, Action onComplete, Action<Vector3> onStep)
    {
        if (corners.Length < 2) yield break;

        // đặt camera lên cao, nhìn xuống
        transform.position = corners[0] + Vector3.up * height;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        onStep?.Invoke(corners[0]);

        for (int i = 1; i < corners.Length; i++)
        {
            onStep?.Invoke(corners[i]);

            Vector3 target = corners[i] + Vector3.up * height;

            while (Vector3.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    target,
                    moveSpeed * Time.deltaTime
                );

                onStep?.Invoke(transform.position - Vector3.up * height);

                // nhìn về hướng di chuyển (xoay Y nhẹ)
                Vector3 dir = corners[i] - transform.position;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Quaternion look = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.Euler(90f, look.eulerAngles.y, 0),
                        rotateSpeed * Time.deltaTime
                    );
                }

                yield return null;
            }
        }

        onComplete?.Invoke();
    }

    Vector3 ProjectToNavMesh(Vector3 pos)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return pos;
    }
}
