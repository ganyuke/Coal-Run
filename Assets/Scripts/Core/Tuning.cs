using UnityEngine;

namespace CoalRun.Core
{
    // Physics and rate numbers shared by every day. Starting values are the JS prototype's TUNE.
    // Edit Assets/Data/Tuning.asset in the Inspector; changes made during Play mode stick.
    [CreateAssetMenu(menuName = "Coal Run/Tuning")]
    public class Tuning : ScriptableObject
    {
        [Header("World")]
        public float step = 1f / 60;
        public float gravity = -18;
        public float walkSpeed = 5.1f;

        [Header("Reach")]
        [Tooltip("Furthest gap between the operator and the end of the cart that still lets them take hold.")]
        public float gripReach = .45f;
        [Tooltip("How far from a crank or lever handle the operator can stand and still work it.")]
        public Vector2 machineReach = new(1.4f, 1.4f);

        [Header("Cart")]
        public float cartMass = 16;
        public float coalMass = 1.7f;
        public float cartCapacity = 10;
        public float overfillCapacity = 12;
        [Tooltip("Haul force per m/s the cart is short of walking pace. Higher drags heavy loads faster.")]
        public float haulGain = 110;
        [Tooltip("The most the operator can pull or push the cart with.")]
        public float haulForce = 340;
        [Tooltip("Ground friction on the wheels. They slide without contact friction; this force stands in for rolling resistance.")]
        public float rollingFriction = .13f;
        [Tooltip("Friction of the steel skid under a missing wheel. It carries half the cart's weight.")]
        public float skidFriction = .8f;
        [Tooltip("Friction with no wheels, the whole cart dragging.")]
        public float dragFriction = 1.3f;
        [Tooltip("Hauling pace with one wheel and a dragging skid, as a fraction of the pace with two.")]
        public float oneWheelHaul = .5f;
        [Tooltip("Hauling pace with no wheels at all.")]
        public float noWheelHaul = .3f;

        [Header("Heave and ram")]
        [Tooltip("How fast Shift raises the skid end of an empty one-wheel cart, in degrees per second; a full one rises at under half that. Let go, it drops.")]
        public float heaveRate = 70;
        [Tooltip("Highest the operator can lift the skid end, in degrees. Above the spill angle, so holding Shift down spills.")]
        public float heaveMax = 30;
        [Tooltip("The most the operator can lift the skid end with. A full cart rises slowly.")]
        public float heaveForce = 300;
        [Tooltip("Hauling pace with the skid off the floor, as a fraction of the pace with two wheels.")]
        public float heaveHaul = .85f;
        [Tooltip("An all but empty cart spills when tilted past this, in degrees...")]
        public float spillAngleEmpty = 25;
        [Tooltip("...and one loaded to overfillCapacity, heaped above the rim, past this. In between it's in proportion.")]
        public float spillAngleFull = 6;
        [Tooltip("Coal per second that pours out for every 10 degrees past the spill angle.")]
        public float spillRate = 8;
        [Tooltip("The skid landing faster than this, in m/s, jolts coal out of the bin. Dropped from 4° it lands at about 1.4, from 10° about 2.3.")]
        public float joltSpeed = 1.4f;
        [Tooltip("Coal jolted out of a cart loaded to overfillCapacity per m/s over joltSpeed. In proportion to the load.")]
        public float joltSpill = 2;
        [Tooltip("A double-tap dash starts at this speed (or the walking speed you already had, if faster)...")]
        public float dashStartSpeed = 3;
        [Tooltip("...and speeds up to this by the end, so a ram from further back hits harder.")]
        public float dashSpeed = 10;
        public float dashTime = .3f;
        [Tooltip("Seconds before another dash or lurch.")]
        public float dashCooldown = .6f;
        [Tooltip("Spin a full-speed ram gives an overturned empty cart, in degrees per second, rolling it the way it was hit. Less when it's loaded.")]
        public float rightingSpin = 250;
        [Tooltip("Impulse a double tap gives a held cart, in N·s, shared with the operator. A full cart lurches less, and a dragging skid soon stops it.")]
        public float lurchImpulse = 100;
        [Tooltip("How hard a lurch bounces a one-wheel cart's skid up, in degrees per second of tilt, for an empty cart. It glides on its wheel until the skid lands; a full cart hardly leaves the floor.")]
        public float lurchHop = 190;

        [Header("Coal")]
        public float hopperCapacity = 30;
        public float handCapacity = 3;
        public float chuteRate = 5;
        public float shovelRate = 2.8f;
        [Tooltip("Fastest a shovelful can be thrown. Throws aimed further than this allows fall short.")]
        public float throwSpeed = 10;
        [Tooltip("How much the lumps of one throw scatter, as a fraction of the chute's scatter.")]
        public float throwSpread = .25f;
        public float pileVolume = .23f;
        public float columnWidth = .4f;

        [Header("Lift")]
        public float liftGear = .53f;
        public float liftKeyCrank = 6.5f;
        public float liftMaxSpeed = 8;
        public float liftAcceleration = 6;
        [Tooltip("Injury per m/s when the lift jams against the operator. 7 kills, and it heals at 0.8 a second.")]
        public float liftHit = 1;
        [Tooltip("Injury per second while the lift is cranked on against the operator.")]
        public float liftCrush = 3;
        [Tooltip("How hard the lift jamming against the cart knocks it, as a fraction of the lift's speed (see joltSpill).")]
        public float liftKnock = .5f;
        [Tooltip("Fastest the platform can come down through coal in the shaft, in m/s. It slows to a stop on the flattened layer.")]
        public float liftSquashSpeed = 1.2f;

        [Header("Levers")]
        [Tooltip("How far a floor lever tilts either side of upright, in degrees.")]
        public float leverAngle = 35;
        [Tooltip("How fast a spring-return lever rolls back, in full travels per second.")]
        public float leverReturn = .5f;
        [Tooltip("Walking speed while holding a lever or crank, as a fraction of walkSpeed.")]
        public float machineWalk = .35f;

        [Header("Tip cradle")]
        [Tooltip("How much chain the tip lever pays out at the end of its travel, in metres. Past about 1.6 the chain goes slack and the cart just hangs from the other one, about 57° over.")]
        public float tipPayout = 1.8f;
        [Tooltip("How fast the chain follows the lever, in m/s.")]
        public float payoutSpeed = 2.5f;
        [Tooltip("How far from the cradle's middle the cart can be parked and still be hooked on.")]
        public float cradleReach = 1.3f;
        [Tooltip("How far the hoist lifts the cart off the floor. Enough that a cart hanging from one chain clears the floor.")]
        public float hoistHeight = 1.3f;
        [Tooltip("How fast the hoist raises and lowers it, in m/s.")]
        public float hoistSpeed = 1;

        [Header("Furnace")]
        public float[] burnRates = { .072f, .144f, .204f, .276f, .348f };
        public float[] heldTemperatures = { 8, -4, -14, -26, -38 };
        public float heatLoss = .006f;
        public float heatCapacity = .28f;
        public float comfortable = 20;
        public float freeze = 0;
        [Tooltip("Seconds the building can sit at or below freezing before the operator freezes. Never shown; it drains back at the same rate once it's above.")]
        public float freezeGrace = 30;
        [Tooltip("Seconds of holding E at the firebox to light it.")]
        public float lightTime = 2;
        [Tooltip("Seconds a fresh fire takes to come up to the notch's full burn.")]
        public float catchTime = 5;
        [Tooltip("Seconds the embers of a fire with an empty hopper last before it goes out.")]
        public float emberTime = 10;

        [Header("Shift")]
        public float shiftLength = 600;
        public float replacementDelay = 7;
    }
}
