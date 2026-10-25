namespace CoalRun.Core
{
    // Room geometry in world units (1 unit is about a metre), copied from the JS prototype's ROOM.
    // The scene was laid out from these, and the code uses them for floors, reach and coal surfaces.
    // Moving a floor or station by hand in the scene means changing it here too.
    public static class Room
    {
        public const float Width = 32, Height = 18;
        public const float Lower = 2, Upper = 9.5f;
        public const float Edge = 22, Chute = 26, Tip = 17;
        // The grate in the upper floor over the furnace hopper.
        public const float HopperStart = 16.1f, HopperEnd = 18.1f;
        public const float LiftX = 2.25f, LiftWidth = 3.5f;
        public const float EntranceStart = 29.2f, EntranceRoof = 6.3f;
        // The control room sits on a slab over the staff lift, up a flight of stairs from the work floor.
        public const float BoothStart = 27.4f, BoothFloor = 6.6f, StairsFoot = 23.5f;
    }
}
