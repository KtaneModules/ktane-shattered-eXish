using UnityEngine;

public class CollisionDetect : MonoBehaviour
{
    public bool isColliding;

    private void OnTriggerStay(Collider col)
    {
        if (col.tag == "ShatteredObjects")
            isColliding = true;
    }

    private void OnTriggerExit(Collider col)
    {
        if (col.tag == "ShatteredObjects")
            isColliding = false;
    }
}
