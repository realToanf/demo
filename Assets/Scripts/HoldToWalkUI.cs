using UnityEngine;
using UnityEngine.EventSystems;

public class HoldToWalkUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public RouteFollower follower;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (follower == null) return;
        if (follower.route == null || !follower.route.HasRoute) return;

        // optional safety: snap to route when starting
        follower.ResetProgressToNearestPoint();
        follower.moving = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (follower != null) follower.moving = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (follower != null) follower.moving = false;
    }
}
