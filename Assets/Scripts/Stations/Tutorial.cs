using CoalRun.Core;
using UnityEngine.Rendering;
using UnityEngine;

namespace CoalRun.Stations
{
    // Day 1's training, played once (ShiftManager.Phase starts at Tutorial): the helper sends the new
    // operator up to pull the chute lever, which sticks partway; forced through, the backed-up coal lets
    // go all at once, buries the cart and kills him. Digging the cart out and getting 3 coal into the
    // hopper teaches the job, then the briefing comes up. Stages and wording as in the JS.
    public class Tutorial : MonoBehaviour
    {
        public enum Stage { Arrival, Lever, Stuck, Avalanche, Dig, Deliver, Done }

        [Tooltip("Comes down the staff lift with the operator and walks over to where it's placed, beside the cart under the chute. Drawn in front of the cart, then behind the coal piles once the avalanche starts, so they cover him. Its art becomes his body when the coal comes down.")]
        [SerializeField]
        private GameObject helper;
        [SerializeField] private float helperWalk = 2.2f;
        [Tooltip("His sorting order once the coal comes down: behind the coal piles (16).")]
        [SerializeField]
        private int buriedOrder = 15;
        [SerializeField] private MachineInput machines;
        [Tooltip("How far the chute lever goes before it sticks.")]
        [SerializeField]
        private float stickAt = .3f;
        [Tooltip("The coal backed up behind the stuck chute, and how fast it comes down once it lets go.")]
        [SerializeField]
        private float avalanche = 32;
        [SerializeField] private float avalancheRate = 18;
        [Tooltip("The backed-up coal bursts out of the chute sideways, toward the helper, at up to this speed.")]
        [SerializeField]
        private float avalancheThrow = 3.2f;

        public Stage Current { get; private set; }
        // Until the avalanche is over, the chute only does what the training says.
        public bool HoldsChute => Current < Stage.Dig;
        public string Objective => _game.InTraining ? Text() : "";

        private ShiftManager _game;
        private FloorLever _lever;
        private float _timer, _left, _accumulator, _deliveredBefore;
        // The helper's injury from falling lumps (enough knocks him down, as in the JS), seconds he's been
        // buried or knocked down, and how much he's still flinching from the last hit.
        private float _helperInjury, _helperBurial, _flinch;
        private bool _nagged;
        // Where the helper stands, and whether he's walking there.
        private Vector3 _spot;
        private bool _walking;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _lever = _game.Chute.Lever;
            if (!_game.InTraining)
            {
                Current = Stage.Done;
                if (helper) helper.SetActive(false);
                return;
            }
            // Parked under the chute until the coal has buried it.
            _game.Cart.Park(true);
            _lever.Limit = stickAt;
            // Out of sight in the staff lift until the door opens.
            _spot = helper.transform.position;
            helper.transform.position = new Vector3(29.9f, _spot.y, _spot.z);
            helper.SetActive(false);
        }

        private void FixedUpdate()
        {
            if (Current == Stage.Done) return;
            var dt = Time.fixedDeltaTime;
            // The coal comes down whatever else happens, even with the operator dead.
            if (_left > 0)
            {
                var q = Mathf.Min(_left, avalancheRate * dt);
                _left -= q;
                _accumulator += q;
                while (_accumulator >= .5f || _left <= 0 && _accumulator > 0)
                {
                    var lump = Mathf.Min(.5f, _accumulator);
                    // Aimed anywhere between straight down onto the cart and onto the helper's head.
                    _game.Chute.Dispense(lump, Mathf.Sign(helper.transform.position.x - Room.Chute) * Random.Range(0, avalancheThrow));
                    _accumulator -= lump;
                }
                _game.Sound("coal", .13f);
            }

            if (helper && helper.activeSelf && (_helperInjury > 3 || _game.Coal.PileHeight(_game.Coal.Surfaces[0], helper.transform.position.x) > 1.2f))
            {
                _helperBurial += dt;
                if (_helperBurial > .6f) KillHelper();
            }

            if (!_game.InTraining) return;
            _timer += dt;
            switch (Current)
            {
                // He steps out with the operator and walks over to the cart, then sends them up.
                case Stage.Arrival when _game.Phase == Phase.Tutorial:
                    if (!helper.activeSelf)
                    {
                        helper.SetActive(true);
                        _walking = true;
                    }
                    helper.transform.position = Vector3.MoveTowards(helper.transform.position, _spot, helperWalk * dt);
                    if (helper.transform.position != _spot) break;
                    _walking = false;
                    Current = Stage.Lever;
                    _timer = 0;
                    _game.Say("“Alright newbie, just get up there and pull the lever. I’ll handle everything after this.”", 8);
                    break;
                case Stage.Lever when _lever.Value >= stickAt - .05f:
                    // It sticks, and lets one lump out.
                    Current = Stage.Stuck;
                    _lever.Limit = 1;
                    machines.Release();
                    // Onto his head, so the first lump out makes him flinch.
                    _game.Chute.Dispense(.5f, Mathf.Sign(helper.transform.position.x - Room.Chute) * 1.7f);
                    _game.Sound("clunk");
                    _game.Say("“Gotta put in a little more elbow grease.”", 8);
                    break;
                case Stage.Lever when _timer > 14 && !_nagged:
                    _nagged = true;
                    _game.Say("“No, it’ll be fine. I’ve done this hundreds of times. Just do it so we can get this done and report back to those pencil-pushers upstairs.”", 9);
                    break;
                case Stage.Stuck when _lever.Value > .8f:
                    Current = Stage.Avalanche;
                    _left = avalanche;
                    _timer = 0;
                    helper.GetComponentInChildren<SortingGroup>().sortingOrder = buriedOrder;
                    _game.Sound("crash");
                    break;
                // He always dies, even when the coal happens to fall around him.
                case Stage.Avalanche when _left <= 0 && _timer > 4.5f:
                    if (helper && helper.activeSelf) KillHelper();
                    Current = Stage.Dig;
                    _game.Cart.Park(false);
                    _deliveredBefore = _game.Furnace.Delivered;
                    break;
                // Shoveling is the lesson, but a cart hauled out some other way mustn't stall the training.
                case Stage.Dig when _game.Player.Scooped >= 1.5f || _game.Cart.Body.position.x < Room.Edge || _game.Cart.Body.position.y > Room.Upper:
                    Current = Stage.Deliver;
                    break;
            }

            // Three coal in the hopper ends it, once they've let go of the tip lever.
            if (Current >= Stage.Dig && _game.Furnace.Delivered - _deliveredBefore >= 3 && !machines.Active)
            {
                Current = Stage.Done;
                _game.FinishTraining();
            }
        }

        // Straight to the briefing, as if it had been played (F8).
        public void Skip()
        {
            if (Current == Stage.Done || !_game.InTraining) return;
            Current = Stage.Done;
            _left = 0;
            if (helper) helper.SetActive(false);
            _game.Cart.Park(false);
            _lever.Limit = 1;
            _game.FinishTraining();
        }

        // A lump falling onto the helper's head and shoulders (see CoalField). Returns whether it hit him.
        public bool HitHelper(Vector2 lump, float amount)
        {
            if (!helper || !helper.activeSelf) return false;
            var at = (Vector2)helper.transform.position;
            if (Mathf.Abs(lump.x - at.x) > .5f || Mathf.Abs(lump.y - at.y) > .9f) return false;
            _helperInjury += amount;
            _flinch = 1;
            _game.Sound("hurt", .4f, .5f);
            return true;
        }

        // He staggers back from each hit, and sinks a little lower the more he's taken.
        private void Update()
        {
            if (!helper || !helper.activeSelf) return;
            _flinch = Mathf.Max(0, _flinch - Time.deltaTime * 3);
            var art = helper.transform.GetChild(0);
            // A bob in his step while he walks over.
            art.localPosition = new Vector3(0, _walking ? Mathf.Abs(Mathf.Sin(Time.time * 9)) * .06f : 0, 0);
            var lean = Mathf.Sign(helper.transform.position.x - Room.Chute) * (_flinch * 14 + Mathf.Min(3, _helperInjury) * 4);
            art.localRotation = Quaternion.Euler(0, 0, -lean + Mathf.Sin(Time.time * 40) * 4 * _flinch);
        }

        private void KillHelper()
        {
            var at = (Vector2)helper.transform.position;
            var art = helper.transform.GetChild(0).gameObject;
            _game.Bodies.Add(Corpse.Topple(art, "Dead helper", at, Vector2.zero, true));
            helper.SetActive(false);
            _game.Sound("hurt");
            _game.Say("“Well, the help is all dead. That’s awesome.”", 6);
        }

        private string Text()
        {
            var p = _game.Player;
            var cart = _game.Cart.Body.position;
            switch (Current)
            {
                case Stage.Arrival: return "";
                case Stage.Lever:
                    return p.Lane == 1 ? "Pull the chute lever over with the mouse." : "Go up to the control room (W at the stairs) and pull the chute lever.";
                case Stage.Stuck: return "Pull the lever again, all the way over.";
                case Stage.Avalanche: return "Stay in the control room.";
                case Stage.Dig:
                    return p.Lane == 1 ? "Go down and dig the cart out." : "Dig the cart out. Stand at the coal and hold F.";
            }
            if (_game.Coal.CartBurial(_game.Cart) > .5f && cart.x > Room.Edge) return "Keep digging until the cart is free.";
            if (cart.y < Room.Upper - .5f) return "Haul the cart onto the freight lift (hold Space and walk), then turn the crank to go up.";
            return _game.Cart.InCradle ? "Pull the tip lever to pour into the hopper." : "Push the cart to the tip cradle and hook it on with E.";
        }
    }
}
