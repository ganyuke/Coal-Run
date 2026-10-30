using CoalRun.Core;
using CoalRun.Dialogue;
using CoalRun.Gameplay.Characters;
using CoalRun.Gameplay.Machinery;
using CoalRun.Gameplay.Player;
using UnityEngine.Rendering;
using UnityEngine;

namespace CoalRun.Gameplay.Tutorial
{
    // Day 1's training, played once (ShiftManager.Phase starts at Tutorial): the helper sends the new
    // operator up to pull the chute lever, which sticks partway; forced through, the backed-up coal lets
    // go all at once, buries the cart and kills him. Digging the cart out and getting 3 coal into the
    // hopper teaches the job, then the briefing comes up. Stages and wording as in the JS.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoalRun.Stations")]
    public class Tutorial : MonoBehaviour
    {
        public enum Stage
        {
            Arrival,
            Lever,
            Stuck,
            Avalanche,
            Dig,
            Deliver,
            Done
        }

        [Tooltip(
            "Comes down the staff lift with the operator and walks over to where it's placed, beside the cart under the chute. Drawn in front of the cart, then behind the coal piles once the avalanche starts, so they cover him. Its art becomes his body when the coal comes down.")]
        [SerializeField]
        private TutorialHelper helper;

        [SerializeField] private float helperWalk = 2.2f;

        [Tooltip("The helper's voice: his lines come out above his head.")] [SerializeField]
        private Speaker helperVoice;

        [Tooltip("His sorting order once the coal comes down: behind the coal piles (16).")] [SerializeField]
        private int buriedOrder = 15;

        [Tooltip("Sorting order while he rides down in the staff lift, behind its door.")] [SerializeField]
        private int liftOrder = -17;

        [SerializeField] private MachineInput machines;

        [Tooltip("Leaning on the furnace. Digging is locked until it's picked up (E).")] [SerializeField]
        private GameObject shovel;

        [SerializeField] private float shovelReach = 1;

        [Tooltip("Walking speed while the helper talks or walks over, so the player hears him out.")] [SerializeField]
        private float listeningWalk = .45f;

        [Tooltip("How far the chute lever goes before it sticks.")] [SerializeField]
        private float stickAt = .3f;

        [Tooltip("The coal backed up behind the stuck chute, and how fast it comes down once it lets go.")]
        [SerializeField]
        private float avalanche = 32;

        [SerializeField] private float avalancheRate = 18;

        [Tooltip("The backed-up coal bursts out of the chute sideways, toward the helper, at up to this speed.")]
        [SerializeField]
        private float avalancheThrow = 3.2f;

        private ShiftManager _game;

        private SortingGroup _helperGroup;

        // The helper's injury from falling lumps (enough knocks him down, as in the JS), seconds he's been
        // buried or knocked down, and how much he's still flinching from the last hit.
        private float _helperInjury, _helperBurial, _flinch;
        private int _helperOrder;
        private FloorLever _lever;
        private bool _nagged;

        private bool _shovelTaken, _saidOw;

        // Where the helper stands, and whether he's walking there.
        private Vector3 _spot;
        private float _greaseIn;
        private float _timer, _left, _accumulator, _deliveredBefore;
        private bool _walking;

        public Stage Current { get; private set; }

        // Until the avalanche is over, the chute only does what the training says.
        public bool HoldsChute => Current < Stage.Dig;

        public string Objective => _game.InTraining ? Text() : "";

        // Until the avalanche is over, the chute lever is the only thing that works.
        public bool Restricts => _game.InTraining && Current < Stage.Dig;
        public bool CanDig => !_game.InTraining || _shovelTaken;
        public float WalkFactor => _game.InTraining && (_walking || (helperVoice && helperVoice.IsBusy)) ? listeningWalk : 1;

        private void Start()
        {
            _game = ShiftManager.Instance;
            _lever = _game.Chute.Lever;
            if (!_game.InTraining)
            {
                Current = Stage.Done;
                if (helper) helper.gameObject.SetActive(false);
                if (shovel) shovel.SetActive(false);
                return;
            }

            // Parked under the chute until the coal has buried it.
            _game.Cart.Park(true);
            _lever.Limit = stickAt;
            // Riding down in the staff lift with the operator, behind its door until it opens.
            _spot = helper.transform.position;
            helper.transform.position = new Vector3(29.9f, _spot.y, _spot.z);
            _helperGroup = helper.Art.GetComponent<SortingGroup>();
            _helperOrder = _helperGroup.sortingOrder;
            _helperGroup.sortingOrder = liftOrder;
        }

        // He staggers back from each hit, and sinks a little lower the more he's taken.
        private void Update()
        {
            if (!helper || !helper.gameObject.activeSelf) return;
            _flinch = Mathf.Max(0, _flinch - Time.deltaTime * 3);
            var art = helper.Art;
            // A bob in his step while he walks over.
            art.localPosition = new Vector3(0, _walking ? Mathf.Abs(Mathf.Sin(Time.time * 9)) * .06f : 0, 0);
            var lean = Mathf.Sign(helper.transform.position.x - _game.Room.Chute) *
                       (_flinch * 14 + Mathf.Min(3, _helperInjury) * 4);
            art.localRotation = Quaternion.Euler(0, 0, -lean + Mathf.Sin(Time.time * 40) * 4 * _flinch);
        }

        private void FixedUpdate()
        {
            if (Current == Stage.Done) return;
            var dt = Time.fixedDeltaTime;
            if (_greaseIn > 0 && (_greaseIn -= dt) <= 0) _game.Say(_game.Lines.training.helperGrease, helperVoice);
            // The coal comes down whatever else happens, even with the operator dead.
            if (_left > 0)
            {
                var q = Mathf.Min(_left, avalancheRate * dt);
                _left -= q;
                _accumulator += q;
                while (_accumulator >= .5f || (_left <= 0 && _accumulator > 0))
                {
                    var lump = Mathf.Min(.5f, _accumulator);
                    // Aimed anywhere between straight down onto the cart and onto the helper's head.
                    _game.Chute.Dispense(lump,
                        Mathf.Sign(helper.transform.position.x - _game.Room.Chute) * Random.Range(0, avalancheThrow));
                    _accumulator -= lump;
                }

                _game.PlayCue("coal", .13f);
            }

            if (helper && helper.gameObject.activeSelf && (_helperInjury > 3 ||
                                                           _game.Coal.PileHeight(_game.Coal.Surfaces[0],
                                                               helper.transform.position.x) > 1.2f))
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
                    if (!_walking)
                    {
                        _helperGroup.sortingOrder = _helperOrder;
                        _walking = true;
                    }

                    helper.transform.position = Vector3.MoveTowards(helper.transform.position, _spot, helperWalk * dt);
                    if (helper.transform.position != _spot) break;
                    _walking = false;
                    Current = Stage.Lever;
                    _timer = 0;
                    _game.Say(_game.Lines.training.helperIntro, helperVoice);
                    break;
                case Stage.Lever when _lever.Value >= stickAt - .05f:
                    // It sticks, and lets one lump out.
                    Current = Stage.Stuck;
                    _lever.Limit = 1;
                    machines.Release();
                    // Right onto his head, clear of the cart.
                    _game.Chute.Dispense(.5f, (helper.transform.position.x - _game.Room.Chute) / .6f, 0);
                    _game.PlayCue("clunk");
                    _greaseIn = 1;
                    break;
                case Stage.Lever when _timer > 14 && !_nagged:
                    _nagged = true;
                    _game.Say(_game.Lines.training.helperReassure, helperVoice);
                    break;
                case Stage.Stuck when _lever.Value > .8f:
                    Current = Stage.Avalanche;
                    _left = avalanche;
                    _timer = 0;
                    helper.Art.GetComponent<SortingGroup>().sortingOrder = buriedOrder;
                    _game.PlayCue("crash");
                    break;
                // He always dies, even when the coal happens to fall around him.
                case Stage.Avalanche when _left <= 0 && _timer > 4.5f:
                    if (helper && helper.gameObject.activeSelf) KillHelper();
                    Current = Stage.Dig;
                    _game.Cart.Park(false);
                    _deliveredBefore = _game.Furnace.Delivered;
                    break;
                // Shoveling is the lesson, but a cart hauled out some other way mustn't stall the training.
                case Stage.Dig when _game.Player.Scooped >= 1.5f || _game.Cart.Body.position.x < _game.Room.Edge ||
                                    _game.Cart.Body.position.y > _game.Room.Upper:
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

        public bool Allows(Machine machine)
        {
            return !Restricts || machine == _lever;
        }

        // Straight to the briefing, as if it had been played (F8).
        public void Skip()
        {
            if (Current == Stage.Done || !_game.InTraining) return;
            Current = Stage.Done;
            _left = 0;
            if (helper) helper.gameObject.SetActive(false);
            if (shovel) shovel.SetActive(false);
            _shovelTaken = true;
            if (helperVoice) helperVoice.Forget();
            _game.Cart.Park(false);
            _lever.Limit = 1;
            _game.FinishTraining();
        }

        // A lump falling onto the helper's head and shoulders (see CoalField). Returns whether it hit him.
        public bool HitHelper(Vector2 lump, float amount)
        {
            if (!helper || !helper.gameObject.activeSelf) return false;
            var at = (Vector2)helper.transform.position;
            if (Mathf.Abs(lump.x - at.x) > .5f || Mathf.Abs(lump.y - at.y) > .9f) return false;
            _helperInjury += amount;
            _flinch = 1;
            _game.PlayCue("hurt", .4f, .5f);
            if (!_saidOw) _game.Say(_game.Lines.training.helperOw, helperVoice);
            _saidOw = true;
            return true;
        }

        public bool ShovelInReach(PlayerController player)
        {
            return _game.InTraining && Current >= Stage.Dig && shovel && shovel.activeSelf && player.Lane == 0 &&
                   Mathf.Abs(player.Position.x - shovel.transform.position.x) < shovelReach && player.Position.y < 4.3f;
        }

        public void TakeShovel()
        {
            shovel.SetActive(false);
            _shovelTaken = true;
            _game.PlayCue("clank");
        }

        private void KillHelper()
        {
            if (helper.IsDead) return;
            _game.Bodies.Add(helper.Kill());
            if (helperVoice) helperVoice.Forget();
            _greaseIn = 0;
            _game.Say(_game.Lines.training.helpDead);
        }

        private string Text()
        {
            var p = _game.Player;
            var cart = _game.Cart.Body.position;
            var t = _game.Lines.training;
            switch (Current)
            {
                case Stage.Arrival: return "";
                case Stage.Lever:
                    return p.Lane == 1 ? t.pullLever : t.goPullLever;
                case Stage.Stuck: return t.pullAgain;
                case Stage.Avalanche: return t.stayUp;
                case Stage.Dig:
                    return p.Lane == 1 ? t.goDownDig : _shovelTaken ? t.dig : t.grabShovel;
            }

            if (_game.Coal.CartBurial(_game.Cart) > .5f && cart.x > _game.Room.Edge) return t.keepDigging;
            if (cart.y < _game.Room.Upper - .5f) return t.haulUp;
            return _game.Cart.InCradle ? t.tip : t.hook;
        }
    }
}