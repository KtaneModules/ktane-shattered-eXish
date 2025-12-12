using UnityEngine;

public class CollisionDetect : MonoBehaviour
{
    public bool isColliding;

    private void OnTriggerEnter(Collider col)
    {
        if (col.tag == "Pickup")
            isColliding = true;
    }

    private void OnTriggerExit(Collider col)
    {
        if (col.tag == "Pickup")
            isColliding = false;
    }
}
