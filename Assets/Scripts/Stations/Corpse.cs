using CoalRun.Core;
using UnityEngine.Rendering;
using UnityEngine;

namespace CoalRun.Stations
{
    // The dead stay where they fell for the rest of the shift: the art, dimmed, toppling over on a body
    // that lies on floors and coal piles and rides the lift, but touches nothing else, so it's never in the way.
    public static class Corpse
    {
        // Takes over `art` (a worker's SortingGroup root) and returns the body it now lies on. A buried one
        // sinks through coal piles to the floor, so the coal goes on covering it instead of lifting it out.
        public static Rigidbody2D Topple(GameObject art, string name, Vector2 at, Vector2 velocity, bool buried = false)
        {
            var body = new GameObject(name) { layer = GameLayers.Part };
            // Already leaning past its balance point, so over it goes, either way.
            var side = Random.value < .5f ? -1 : 1;
            body.transform.SetPositionAndRotation(at, Quaternion.Euler(0, 0, side * 25));
            art.transform.SetParent(body.transform, false);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;
            art.transform.localScale = Vector3.one;
            foreach (Transform t in art.transform)
                if (t.name is "Held wheel" or "Striker") t.gameObject.SetActive(false);
            art.GetComponent<SortingGroup>().sortingOrder = 3;
            foreach (var r in art.GetComponentsInChildren<SpriteRenderer>()) r.color *= new Color(.65f, .65f, .65f);
            var rb = body.AddComponent<Rigidbody2D>();
            rb.mass = 2;
            rb.linearVelocity = velocity;
            rb.angularVelocity = side * 90;
            var box = body.AddComponent<BoxCollider2D>();
            box.size = new Vector2(.48f, 1.33f);
            box.excludeLayers = buried ? GameLayers.Mask(GameLayers.Player, GameLayers.Cart, GameLayers.Part, GameLayers.Pile) : GameLayers.Mask(GameLayers.Player, GameLayers.Cart, GameLayers.Part);
            return rb;
        }
    }
}
