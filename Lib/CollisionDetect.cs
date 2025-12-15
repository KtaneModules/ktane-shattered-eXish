using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CollisionDetect : MonoBehaviour
{
    public bool isColliding;

    readonly string[] collisionObjectNames = { "shatteredShardCollision", "shatteredBorderBottom", "shatteredBorderTop", "shatteredBorderLeft", "shatteredBorderRight" };

    List<Collider> currentlyHitColliders = new List<Collider>();

    private void OnTriggerEnter(Collider col)
    {
        if (collisionObjectNames.Contains(col.name))
        {
            isColliding = true;
            currentlyHitColliders.Add(col);
        }
    }

    private void OnTriggerExit(Collider col)
    {
        if (currentlyHitColliders.Contains(col))
        {
            currentlyHitColliders.Remove(col);
            if (currentlyHitColliders.Count == 0)
                isColliding = false;
        }
    }
}
