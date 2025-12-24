// using UnityEngine;

// [RequireComponent(typeof(LineRenderer))]
// public class PathVisualizer : MonoBehaviour
// {
//     public Transform from;
//     public Transform to;

//     public List<Transform> points;

//     LineRenderer line;
//     UnityEngine.AI.NavMeshPath path;

//     void Awake()
//     {
//         line = GetComponent<LineRenderer>();
//         path = new UnityEngine.AI.NavMeshPath();
//     }

//     void Update()
//     {
//         if (from == null || to == null) return;

//         RenderPath(from, to);
//     }

//     void SetFromPoint(Transform point)
//     {
//         from = point;
//     }

//     void SetToPoint(Transform point)
//     {
//         to = point;
//     }

//     void RenderPath(Transform pointA, Transform pointB)
//     {
//         if (pointA == null || pointB == null) return;

//         if (UnityEngine.AI.NavMesh.CalculatePath(pointA.position, pointB.position, UnityEngine.AI.NavMesh.AllAreas, path))
//         {
//             line.positionCount = path.corners.Length;
//             line.SetPositions(path.corners);
//         }
//     }
// }