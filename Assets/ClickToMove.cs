using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem; // new Input System

public class ClickToMove : MonoBehaviour
{
    [SerializeField] private Camera cam; // assign in Inspector
    private NavMeshAgent agent;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        // fallback if camera not assigned
        if (cam == null)
            cam = Camera.main;
    }

    void Update()
    {
        // Check left mouse click using Input System
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Get click position
            Vector2 clickPos = Mouse.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(clickPos);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                agent.SetDestination(hit.point);
            }
        }
    }
}
