using UnityEngine;

// Depth lanes and coal rules are collision layers. The layers are set up in
// Project Settings > Tags and Layers (slots 6–11). Only read these after the game has started (Awake or later).
public static class GameLayers
{
    public static readonly int Terrain = LayerMask.NameToLayer("Terrain");
    public static readonly int Player = LayerMask.NameToLayer("Player");
    public static readonly int Cart = LayerMask.NameToLayer("Cart");
    public static readonly int Coal = LayerMask.NameToLayer("Coal");
    public static readonly int Part = LayerMask.NameToLayer("Part");
    public static readonly int Pile = LayerMask.NameToLayer("Pile");

    public static int Mask(params int[] layers)
    {
        var mask = 0;
        foreach (var layer in layers) mask |= 1 << layer;
        return mask;
    }

    // Every pair of game layers is ignored except the ones listed. Notable gaps, same as the prototype:
    // coal lumps never hit each other, the player or the cart (the cart absorbs them by position), and
    // piles don't block the cart (burial slows it with forces instead).
    public static void ConfigureCollisions()
    {
        int[] all = { Terrain, Player, Cart, Coal, Part, Pile };
        foreach (var a in all)
            foreach (var b in all) Physics2D.IgnoreLayerCollision(a, b, true);
        Collide(Terrain, Player); Collide(Terrain, Cart); Collide(Terrain, Coal); Collide(Terrain, Part);
        Collide(Player, Cart); Collide(Player, Part); Collide(Player, Pile);
        Collide(Cart, Part);
        Collide(Coal, Pile); Collide(Part, Pile);
        return;

        void Collide(int a, int b) => Physics2D.IgnoreLayerCollision(a, b, false);
    }
}
