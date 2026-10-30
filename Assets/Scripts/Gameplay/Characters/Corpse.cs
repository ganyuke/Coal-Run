using CoalRun.Core;
using UnityEngine.Rendering;
using UnityEngine;

namespace CoalRun.Gameplay.Characters
{
    // generator for the corpses created when players die. copies out the art, then dims and topples the body.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public static class Corpse
    {
        // Takes over `art` (a worker's SortingGroup root) and returns the body it now lies on. A buried one
        // sinks through coal piles to the floor, so the coal goes on covering it instead of lifting it out.
        public static Rigidbody2D Topple(Rigidbody2D prefab, GameObject art, string name, Vector2 at, Vector2 velocity, bool buried = false)
        {
            // Already leaning past its balance point, so over it goes, either way.
            var side = Random.value < .5f ? -1 : 1;
            var rb = Object.Instantiate(prefab, at, Quaternion.Euler(0, 0, side * 25));
            var body = rb.gameObject;
            body.name = name;
            art.transform.SetParent(body.transform, false);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;
            art.transform.localScale = Vector3.one;
            foreach (Transform t in art.transform)
                if (t.name is "Held wheel" or "Striker" or "Held shovel")
                    t.gameObject.SetActive(false);
            art.GetComponent<SortingGroup>().sortingOrder = 3;
            foreach (var r in art.GetComponentsInChildren<SpriteRenderer>()) r.color *= new Color(.65f, .65f, .65f);
            // The prefab starts inactive so CorpseBody.Awake sees the attached artwork.
            body.SetActive(true);
            rb.linearVelocity = velocity;
            rb.angularVelocity = side * 90;
            if (buried) rb.GetComponent<BoxCollider2D>().excludeLayers |= GameLayers.Mask(GameLayers.Pile);
            return rb;
        }
    }
}
