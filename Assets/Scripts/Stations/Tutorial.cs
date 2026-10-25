using CoalRun.Core;
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

        [Tooltip("Stands by the cart under the chute. Its art becomes his body when the coal comes down.")]
        [SerializeField]
        private GameObject helper;
        [SerializeField] private MachineInput machines;
        [Tooltip("How far the chute lever goes before it sticks.")]
        [SerializeField]
        private float stickAt = .3f;
        [Tooltip("The coal backed up behind the stuck chute, and how fast it comes down once it lets go.")]
        [SerializeField]
        private float avalanche = 32;
        [SerializeField] private float avalancheRate = 18;

        public Stage Current { get; private set; }
        // Until the avalanche is over, the chute only does what the training says.
        public bool HoldsChute => Current < Stage.Dig;
        public string Objective => _game.InTraining ? Text() : "";

        private ShiftManager _game;
        private FloorLever _lever;
        private float _timer, _left, _accumulator, _helperBurial, _deliveredBefore;
        private bool _nagged;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _lever = _game.Chute.Lever;
            if (_game.Phase != Phase.Tutorial)
            {
                Current = Stage.Done;
                if (helper) helper.SetActive(false);
                return;
            }
            // Parked under the chute until the coal has buried it.
            _game.Cart.Park(true);
            _lever.Limit = stickAt;
            _game.Say("“Alright newbie, just get up there and pull the lever. I’ll handle everything after this.”", 8);
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
                    _game.Chute.Dispense(lump);
                    _accumulator -= lump;
                }
                _game.Sound("coal", .13f);
            }

            if (helper && helper.activeSelf && _game.Coal.PileHeight(_game.Coal.Surfaces[0], helper.transform.position.x) > 1.2f)
            {
                _helperBurial += dt;
                if (_helperBurial > .6f) KillHelper();
            }

            if (!_game.InTraining) return;
            _timer += dt;
            switch (Current)
            {
                case Stage.Arrival when _timer > 2:
                    Current = Stage.Lever;
                    break;
                case Stage.Lever when _lever.Value >= stickAt - .05f:
                    // It sticks, and lets one lump out.
                    Current = Stage.Stuck;
                    _lever.Limit = 1;
                    machines.Release();
                    _game.Chute.Dispense(.5f);
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

        private void KillHelper()
        {
            var at = (Vector2)helper.transform.position;
            var art = helper.transform.GetChild(0).gameObject;
            _game.Bodies.Add(Corpse.Topple(art, "Dead helper", at, Vector2.zero));
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
                case Stage.Arrival or Stage.Lever:
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
