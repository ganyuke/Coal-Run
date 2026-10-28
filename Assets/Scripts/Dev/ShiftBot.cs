using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CoalRun.Core;
using CoalRun.Stations;
using UnityEngine.InputSystem;
using UnityEngine;

namespace CoalRun.Dev
{
    // A competent player for balance testing, after the JS prototype's route test ("survives a full
    // ten-minute shift"): fill the cart at the chute from the control room, haul it onto the freight lift,
    // crank up, set the burn lever from the forecast, tip the cart into the hopper, and back down. It plays
    // through the same Controls the keyboard makes and works the levers like the mouse does (Begin, Drag,
    // End), so it gets no help the player wouldn't. Each turn it looks at where the cart is and picks the
    // next leg, so a spill or a crash only costs it time. When it's truly stuck it puts the cart back at
    // the chute by hand and counts a rescue: a rescue is something to fix in the game or the bot.
    //
    // F9 (or ShiftBot.Run from the CLI) skips the training and starts it; F9 again stops it. It writes a
    // line per delivery and a sample every 5 s to Logs/ShiftBot.csv, and the summary to the console.
    [DefaultExecutionOrder(-90)] // After InputReader, before everything that reads its Controls.
    public class ShiftBot : MonoBehaviour
    {
        // The last finished run's summary, for the CLI.
        public static string Report { get; private set; } = "";
        public static bool Running => _bot;
        // Debugging: freeze the game at the first failure (or spill over HaltSpill) instead of rescuing, to look at it.
        public static bool Halt;
        public static float HaltSpill = 3;
        // The legs in progress, outermost first, for watching it from the CLI.
        public static string Doing => _bot ? string.Join(" > ", _bot._stack.Reverse().Select(e => e.GetType().Name.Split('>')[0].TrimStart('<'))) : "";

        private static ShiftBot _bot;
        private static readonly PropertyInfo CurrentProperty = typeof(InputReader).GetProperty(nameof(InputReader.Current));
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private ShiftManager _game;
        private InputReader _input;
        private FloorLever _chuteLever, _tipLever, _burnLever;
        private LiftCrank _wheel;
        private readonly Stack<IEnumerator> _stack = new();
        private Controls _c;
        // Set by Fail: the runner drops every leg in progress and rescues.
        private string _failure;
        // Held already when F9 started it.
        private bool _f9 = true;

        private readonly StringBuilder _log = new();
        private readonly List<string> _events = new();
        private float _nextSample, _minTemperature = float.PositiveInfinity, _frozenFor, _spilled, _lastLoose, _spillBurst, _lastCart;
        private bool _tipping, _endgame;
        public static bool Trace, Lurch = true;
        public static float LurchEvery = .7f, LurchMinDistance = 7, LurchMaxTilt = 6;
        private float _lurchAt;
        private readonly StringBuilder _trace = new();
        private int _traceStep;
        private float _looseAtFill;
        private float _haulTarget;
        private int _haulRepeat;
        private string _mark;
        private float _markAt;
        private int _deliveries, _rescues, _deaths;
        private float _lapStart, _lapTime;

        // Cart positions the route stops at: on the freight lift's platform, over the tip cradle, under the chute.
        private const float LiftStop = 2.7f, CradleStop = 15.4f, ChuteStop = Room.Chute;

        public static void Run(ShiftManager game, float speed = 1)
        {
            if (_bot) { _bot.Stop("stopped by hand"); return; }
            if (game.InTraining || game.Phase == Phase.Briefing) game.DebugCommand(8);
            _bot = game.gameObject.AddComponent<ShiftBot>();
            Time.timeScale = speed;
        }

        private void Start()
        {
            _game = ShiftManager.Instance;
            _input = (InputReader)typeof(PlayerController).GetField("input", Private)!.GetValue(_game.Player);
            _input.enabled = false;
            foreach (var m in Machine.All)
                switch (m.name)
                {
                    case "Chute lever": _chuteLever = (FloorLever)m; break;
                    case "Tip lever": _tipLever = (FloorLever)m; break;
                    case "Burn lever": _burnLever = (FloorLever)m; break;
                    case "Platform wheel": _wheel = (LiftCrank)m; break;
                }
            _log.AppendLine("clock,event,outside,inside,hopper,burn,notch,lit,cart coal,loose coal,chill,note");
            _lastLoose = _game.Coal.Loose;
            _stack.Push(Shift());
            _game.Say("Bot playing. F9 stops it.", 3);
        }

        private void Update()
        {
            // The InputReader is off, so F9 is read here.
            var down = Keyboard.current != null && Keyboard.current.f9Key.isPressed;
            if (down && !_f9) Stop("stopped by hand");
            _f9 = down;
        }

        private void FixedUpdate()
        {
            _c = new Controls { Aim = _game.Player.Position + Vector2.right * _game.Player.Facing };
            Measure();
            // Runs the nested legs like coroutines, one physics step per plain yield. A leg that keeps
            // finishing without taking a step would hang the editor, so that stops the bot instead.
            for (var guard = 0; _stack.Count > 0; guard++)
            {
                if (guard > 500) { Stop($"looping without a step in {Doing}"); return; }
                var top = _stack.Peek();
                var more = top.MoveNext();
                if (_failure != null && Halt)
                {
                    Stop("HALTED: " + _failure);
                    Time.timeScale = 0;
                    return;
                }
                if (_failure != null)
                {
                    _stack.Clear();
                    _stack.Push(Shift());
                    _stack.Push(Rescue(_failure));
                    _failure = null;
                    continue;
                }
                if (!more) { _stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { _stack.Push(nested); continue; }
                break;
            }
            if (!enabled) return;
            CurrentProperty.SetValue(_input, _c);
            if (_stack.Count == 0) Stop("done");
        }

        private void Stop(string why)
        {
            if (!enabled) return;
            enabled = false;
            _stack.Clear();
            foreach (var m in new Machine[] { _chuteLever, _tipLever, _burnLever, _wheel }) if (m) m.End();
            _input.enabled = true;
            Time.timeScale = 1;
            Summarize(why);
            _bot = null;
            Destroy(this);
        }

        // ---- The shift ----

        private IEnumerator Shift()
        {
            while (!_game.Ended)
            {
                // Out of the shift (a replacement on the way, or clocking out) there's nothing to drive.
                if (!_game.CanAct) { yield return null; continue; }
                if (_game.Phase == Phase.Leaving) { yield return ClockOut(); continue; }
                yield return Turn();
            }
        }

        // One leg, picked from where the cart is, like a person glancing at the room.
        private IEnumerator Turn()
        {
            var cart = _game.Cart;
            // A cart bounced up off the lift's end stop is airborne for a moment, not stuck.
            for (var i = 0; i < 6 && Mathf.Abs(cart.Body.linearVelocity.y) > 1; i++) yield return Wait(.2f);
            var p = cart.Body.position;
            var lift = _game.Lift;
            var onLift = p.x < 4.3f && Mathf.Abs(p.y - (lift.Y + .7f)) < .3f;
            var lower = !onLift && Mathf.Abs(p.y - (Room.Lower + .7f)) < .4f;
            var upper = !onLift && Mathf.Abs(p.y - (Room.Upper + .7f)) < .4f;
            var loaded = cart.Coal > LoadedAbove;

            if (_endgame && !cart.InCradle)
            {
                // Nothing more to haul: go down on the freight lift and wait by the staff lift for the whistle.
                Mark("down to clock out");
                if (_game.Player.Position.y > Room.Lower + 3) yield return Crank(-1);
                else yield return Walk(30.4f);
                yield return Wait(.5f);
                yield break;
            }
            if (cart.InCradle) { yield return Lower(); yield break; }
            if (cart.Overturned) { yield return Right(); yield break; }
            if (cart.Burial > StuckBurial || cart.Wheels == 0 || !(onLift || lower || upper)) { yield return Rescue($"cart stuck: burial {cart.Burial:0.0}, wheels {cart.Wheels}, at {p.x:0.0},{p.y:0.0}"); yield break; }
            // Passing the firebox on the lower floor, keep the fire going.
            if (!_game.Furnace.Lit && _game.Furnace.Hopper > .5f && _game.Player.Lane == 0 && _game.Player.Position.y < 4.3f) yield return Light();

            if (onLift)
            {
                var top = lift.Y > Room.Upper - .05f;
                var bottom = lift.Y < Room.Lower + .05f;
                if (loaded && !top) yield return Crank(1);
                else if (!loaded && !bottom) yield return Crank(-1);
                else if (top) yield return Deliver();
                else yield return Fill();
            }
            else if (upper) yield return loaded ? Deliver() : ToLift(Room.Upper);
            else if (!loaded) yield return Fill();
            else yield return ToLift(Room.Lower);
        }

        // Onto the platform, calling it to this floor first with the landing crank if it's at the other.
        private IEnumerator ToLift(float floor)
        {
            var lift = _game.Lift;
            if (Mathf.Abs(lift.Y - floor) > .01f)
            {
                Mark("call the lift");
                yield return HaulTo(7);
                yield return Walk(5.2f);
                var direction = floor > lift.Y ? 1 : -1;
                for (float t = 0; t < 10 && Mathf.Abs(lift.Y - floor) > .01f; t += Time.fixedDeltaTime)
                {
                    _c.CrankKey = direction;
                    yield return null;
                }
                if (Mathf.Abs(lift.Y - floor) > .01f) { yield return Fail($"couldn't call the lift to {floor}"); yield break; }
            }
            Mark("haul onto the lift");
            yield return HaulTo(LiftStop);
        }

        private IEnumerator Fill()
        {
            if (Mathf.Abs(_game.Cart.Body.position.x - ChuteStop) > .3f) { Mark("haul to chute"); yield return HaulTo(ChuteStop); yield break; }
            Mark("to the stairs");
            _looseAtFill = _game.Coal.Loose;
            yield return Walk(Room.StairsFoot);
            _c.Lane = 1;
            yield return null;
            while (_game.Player.Climbing) yield return null;
            if (_game.Player.Lane != 1) { yield return Fail("couldn't get up the stairs"); yield break; }
            yield return Walk(28.6f);
            Mark("pour");
            // The lever rolls back shut when let go and pours about chuteRate / leverReturn / 2 more on the way.
            var tail = _game.Tune.chuteRate / _game.Tune.leverReturn / 2;
            var want = LoadTarget - _game.Cart.Coal;
            var bunker = _game.Chute.Bunker;
            yield return Pull(_chuteLever, 1);
            for (float t = 0; t < 8 && bunker - _game.Chute.Bunker + tail < want; t += Time.fixedDeltaTime) yield return null;
            _chuteLever.End();
            yield return Wait(.3f);
            Mark("back down");
            yield return Walk(28.2f);
            _c.Lane = -1;
            yield return null;
            while (_game.Player.Climbing) yield return null;
            // Let the last of it land before hauling off.
            while (_game.Chute.Pull > 0) yield return null;
            yield return SweepChute();
            yield return Wait(.4f);
            Event("filled", $"cart {_game.Cart.Coal:0.0}, loose {_game.Coal.Loose:0.0}");
            if (Halt && _game.Coal.Loose - _looseAtFill > HaltSpill) { Stop("HALTED: coal missed the cart"); Time.timeScale = 0; }
        }

        private IEnumerator Deliver()
        {
            var cart = _game.Cart;
            if (Mathf.Abs(cart.Body.position.x - CradleStop) > .3f) { Mark("haul to the cradle"); yield return HaulTo(CradleStop); yield break; }
            Mark("hoist");
            yield return Wait(.3f);
            yield return Walk(CradleStop - 1.5f);
            _c.Interact = true;
            yield return null;
            for (float t = 0; t < 8 && _game.Cradle.Prompt != _game.Lines.prompts.lowerCart; t += Time.fixedDeltaTime) yield return null;
            if (_game.Cradle.Prompt != _game.Lines.prompts.lowerCart) { yield return Fail("the cradle didn't hoist the cart"); yield break; }

            Mark("burn lever");
            // The lever is only reached on a delivery, about a lap apart.
            var notch = ChooseNotch();
            if (notch != _game.Furnace.Notch)
            {
                yield return Walk(18.6f);
                yield return Pull(_burnLever, (float)notch / (_game.Tune.burnRates.Length - 1));
                _burnLever.End();
            }

            Mark("to the tip lever");
            yield return Walk(13.6f);
            Mark("tip");
            var before = _game.Furnace.Delivered;
            var load = cart.Coal;
            _tipping = true;
            yield return Pull(_tipLever, 1);
            for (float t = 0; t < 6 && cart.Coal > .05f; t += Time.fixedDeltaTime) yield return null;
            yield return Wait(.5f);
            _tipLever.End();
            for (float t = 0; t < 6 && Mathf.Abs(cart.Body.rotation) > 2; t += Time.fixedDeltaTime) yield return null;
            yield return Wait(.5f);
            _tipping = false;
            Mark("lower");
            _lapTime = _game.Clock - _lapStart;
            _deliveries++;
            Event("delivered", $"{_game.Furnace.Delivered - before:0.0} of {load:0.0}, lap {_game.Clock - _lapStart:0}s, notch {notch}");
            _lapStart = _game.Clock;
            yield return Lower();
            Mark("sweep");
            yield return SweepCradle();
        }

        // Which notch to burn at until the next delivery. Burning is output, so burn as much as the hopper can
        // afford: the most that still leaves enough coal to hold the building through the cold ahead (what the
        // storm needs minus what deliveries will bring), and never so little that the building drops below the
        // floor over the next lap (simulated with the game's own heat formula: it coasts, it has inertia).
        private int ChooseNotch()
        {
            var t = _game.Tune;
            var rates = t.burnRates;
            var now = _game.Clock;
            var end = _game.Tune.shiftLength;
            // What the building needs to stay at SafeTemperature, summed from now to each later moment,
            // less the coal deliveries bring meanwhile: the most the hopper can be short of at any point.
            var perSecond = _deliveries > 0 ? Mathf.Max(.08f, _game.Furnace.Delivered / Mathf.Max(60, now)) : .12f;
            // The lever is only reached again after about a lap.
            var lap = Mathf.Max(40, _lapTime);
            float reserve(float from)
            {
                float need = 0, worst = 0;
                for (var at = from; at < end; at += 1)
                {
                    need += Mathf.Max(0, t.heatLoss * (SafeTemperature - _game.Day.OutsideAt(at)));
                    worst = Mathf.Max(worst, need - perSecond * (at - from));
                }
                return worst;
            }
            // Too late for another load to be worth fetching: burn the hopper dry before the whistle, and stop hauling.
            if (now + lap + 8 > end)
            {
                _endgame = true;
                Event("endgame", $"hopper {_game.Furnace.Hopper:0.0}, burning it all");
                return rates.Length - 1;
            }
            var lowest = rates.Length - 1;
            for (var n = 0; n < rates.Length; n++)
            {
                var temperature = _game.Temperature;
                var ok = true;
                for (float at = 0; at < lap && ok; at += 1)
                {
                    temperature += (rates[n] - t.heatLoss * (temperature - _game.Day.OutsideAt(now + at))) / t.heatCapacity;
                    ok = temperature >= MinTemperature;
                }
                if (ok) { lowest = n; break; }
            }
            var chosen = lowest;
            var arriving = perSecond * lap;
            for (var n = rates.Length - 1; n > lowest; n--)
            {
                // The hopper when the next load lands, which has to cover what's still to come.
                var left = _game.Furnace.Hopper - rates[n] * lap + arriving;
                if (left >= reserve(now + lap) + ReserveMargin && _game.Furnace.Hopper - rates[n] * lap > 1) { chosen = n; break; }
            }
            Event("burn", $"notch {chosen} (lowest safe {lowest}), hopper {_game.Furnace.Hopper:0.0}, reserve {reserve(now + lap):0.0}, {perSecond:0.00} c/s coming");
            return chosen;
        }

        // How much coal is heaped on a floor between two x, and where its tallest column is (the middle of two
        // piles can be out of reach of both).
        private (float amount, float x) Heap(float floor, float from, float to)
        {
            var surface = _game.Coal.SurfaceAt((from + to) / 2, floor + .5f);
            var width = _game.Tune.columnWidth;
            float amount = 0, tallest = 0, at = 0;
            for (var i = 0; i < surface.Heights.Length; i++)
            {
                var x = surface.Start + (i + .5f) * width;
                if (x < from || x > to) continue;
                amount += surface.Heights[i] * width;
                if (surface.Heights[i] > tallest) { tallest = surface.Heights[i]; at = x; }
            }
            return (amount, at);
        }

        // What misses the grate when the cart tips piles up under the cradle, and what misses the cart at the chute
        // piles up beside it; either buries the next cart and blocks the floor. Shovel it into the open cart, the way
        // a player tops it up (what's swept up rides along with the next load), from whichever side the pile is on:
        // facing the cart, the cursor over it.
        private IEnumerator Sweep(float floor, float from, float to, Vector2 cart, int side, string what, float min)
        {
            var aim = cart + Vector2.up * 1.7f;
            for (var round = 0; round < 3 && Heap(floor, from, to).amount > min; round++)
            {
                var heap = Heap(floor, from, to);
                Event("  sweep " + what, $"round {round}: {heap.amount:0.00} at {heap.x:0.0}, operator {_game.Player.Position.x:0.00}");
                // Stand back from the pile on the side away from where they face, the cart being that way.
                yield return Walk(heap.x - Face(side, heap.x, cart.x) * .6f);
                float scooped = _game.Player.Scooped;
                for (float t = 0; t < 4 && Heap(floor, from, to).amount > min; t += Time.fixedDeltaTime)
                {
                    _c.Shovel = true;
                    _c.Aim = aim;
                    yield return null;
                }
                Event("  swept", $"scooped {_game.Player.Scooped - scooped:0.00}, left {Heap(floor, from, to).amount:0.00} at {Heap(floor, from, to).x:0.0}, operator {_game.Player.Position.x:0.00}");
                // Letting go throws what's on the shovel.
                _c.Aim = aim;
                yield return Wait(.5f);
            }
            var left = Heap(floor, from, to);
            if (left.amount > min) Event("sweep gave up", $"{left.amount:0.00} at {left.x:0.0} ({what})");
            if (left.amount > .4f) yield return Fail($"sweep can't clear {left.amount:0.00} at {left.x:0.0} ({what}), operator {_game.Player.Position}, hands {_game.Player.Hands:0.0}");
        }

        // Facing the cart from the pile: the side given, or (0) whichever way the cart lies.
        private static int Face(int side, float pile, float cart) => side != 0 ? side : pile < cart ? 1 : -1;

        private IEnumerator SweepCradle() => Sweep(Room.Upper, SweepFrom, Room.HopperStart, new Vector2(CradleStop, Room.Upper), 1, "cradle", SweepMin);

        private IEnumerator SweepChute() => Sweep(Room.Lower, ChuteStop - 1.6f, ChuteStop + 4, new Vector2(ChuteStop, Room.Lower), 0, "chute", .12f);

        private IEnumerator Lower()
        {
            yield return Walk(CradleStop - 1.5f);
            for (var i = 0; i < 4 && _game.Cart.InCradle; i++)
            {
                _c.Interact = true;
                yield return null;
                for (float t = 0; t < 6 && _game.Cart.InCradle; t += Time.fixedDeltaTime) yield return null;
            }
            if (_game.Cart.InCradle) yield return Fail("couldn't lower the cart from the cradle");
        }

        private IEnumerator Light()
        {
            yield return Walk(_game.Furnace.transform.position.x - .6f);
            for (float t = 0; t < _game.Tune.lightTime + 1 && !_game.Furnace.Lit; t += Time.fixedDeltaTime)
            {
                _c.InteractHeld = true;
                yield return null;
            }
            Event(_game.Furnace.Lit ? "lit" : "couldn't light", "");
        }

        private IEnumerator Right()
        {
            var p = _game.Cart.Body.position;
            yield return Walk(p.x + (p.x < 16 ? 1.5f : -1.5f));
            _c.Interact = true;
            yield return null;
            yield return Wait(1.5f);
            Event("righted the cart", "");
            if (_game.Cart.Overturned) yield return Rescue("couldn't right the cart");
        }

        private IEnumerator Crank(int direction)
        {
            var lift = _game.Lift;
            Mark(direction > 0 ? "crank up" : "crank down");
            // The platform's own wheel, at its left end. The cart stops right of it.
            yield return Walk(1.5f);
            var end = direction > 0 ? Room.Upper : Room.Lower;
            // Spun like the mouse spins it (clockwise is up): the cursor circles a fixed point, which gets
            // the platform to its top speed. The W/S keys are geared to under half of that.
            if (_wheel.CanOperate(_game.Player))
            {
                var centre = _wheel.HandlePosition;
                _wheel.Begin(centre + Vector2.right * SpinRadius);
                float angle = 0;
                for (float t = 0; t < 10 && Mathf.Abs(lift.Y - end) > .01f; t += Time.fixedDeltaTime)
                {
                    // Easing off for the last stretch: the platform stops dead at the ends of the shaft, and a loaded cart rammed into them spills.
                    angle -= direction * SpinRate * Mathf.Clamp(Mathf.Abs(lift.Y - end) / 2f, .2f, 1) * Time.fixedDeltaTime;
                    _wheel.Drag(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SpinRadius, Time.fixedDeltaTime);
                    yield return null;
                }
                _wheel.End();
            }
            else
                for (float t = 0; t < 10 && Mathf.Abs(lift.Y - end) > .01f; t += Time.fixedDeltaTime)
                {
                    _c.CrankKey = direction;
                    yield return null;
                }
            if (Mathf.Abs(lift.Y - end) > .01f) yield return Fail($"lift stuck at {lift.Y:0.0} going {(direction > 0 ? "up" : "down")}");
            yield return Wait(.1f);
        }

        private IEnumerator ClockOut()
        {
            if (_game.Player.Lane == 1)
            {
                yield return Walk(28.2f);
                _c.Lane = -1;
                yield return null;
                while (_game.Player.Climbing) yield return null;
            }
            // Just standing in the staff lift's car does it.
            yield return Walk(30.6f);
            yield return Wait(.5f);
        }

        // ---- Moves ----

        // Logs how long the previous stretch of the lap took, and starts timing the next one.
        private void Mark(string next)
        {
            if (_mark != null) Event("  " + _mark, $"{_game.Clock - _markAt:0.0}s");
            _mark = next;
            _markAt = _game.Clock;
        }

        private IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.fixedDeltaTime) yield return null;
        }

        // Walks to x on the floor they're on, through the cart if it's in the way.
        private IEnumerator Walk(float x)
        {
            var player = _game.Player;
            float stillFor = 0, last = player.Position.x;
            var digs = 0;
            for (float t = 0; t < 15; t += Time.fixedDeltaTime)
            {
                var d = x - player.Position.x;
                if (Mathf.Abs(d) < .15f) { yield return Wait(.1f); yield break; }
                _c.Move = Mathf.Sign(d);
                yield return null;
                // Not getting anywhere: dig through a pile in the way a few times, then fail rather than push on a wall for 15 s.
                stillFor = Mathf.Abs(player.Position.x - last) < .02f ? stillFor + Time.fixedDeltaTime : 0;
                last = player.Position.x;
                if (stillFor <= 1.5f) continue;
                if (digs++ >= 3) break;
                yield return DigThrough((int)Mathf.Sign(d));
                stillFor = 0;
            }
            yield return Fail($"walking stuck at {player.Position.x:0.0},{player.Position.y:0.0} going to {x:0.0}");
        }

        // On a pile too tall to step up onto: shovel the way ahead clear, throwing it on past where they are going so it lands
        // as a spread of low lumps they can walk over.
        private IEnumerator DigThrough(int direction)
        {
            var player = _game.Player;
            Event("dig through", $"stuck at {player.Position.x:0.0}, going {(direction > 0 ? "right" : "left")}");
            var aim = player.Position + new Vector2(direction * 4.5f, .6f);
            for (float t = 0; t < 2.5f; t += Time.fixedDeltaTime)
            {
                _c.Shovel = true;
                _c.Aim = aim;
                yield return null;
            }
            _c.Aim = aim;
            yield return Wait(.4f);
        }

        // Takes hold at the end of the cart facing the way it's going and pulls it there, as in the JS route test.
        private IEnumerator HaulTo(float x)
        {
            var cart = _game.Cart;
            var player = _game.Player;
            var tries = 0;
            // The same haul again and again is a loop (overshooting a stop, say), which isn't a failure to catch otherwise.
            _haulRepeat = Mathf.Approximately(_haulTarget, x) ? _haulRepeat + 1 : 0;
            _haulTarget = x;
            if (_haulRepeat > 4) { yield return Fail($"hauling to {x:0.0} over and over (cart at {cart.Body.position.x:0.00})"); yield break; }
            float stillFor = 0, startX = cart.Body.position.x;
            Event("haul", $"{startX:0.00} -> {x:0.0}, skid {cart.SkidSide}, operator {player.Position.x:0.00}, coal {cart.Coal:0.0}, tilt {cart.Body.rotation:0.0}");
            for (float t = 0; t < 40; t += Time.fixedDeltaTime)
            {
                var cx = cart.Body.position.x;
                var d = x - cx;
                // Always from the skid end of a one-wheel cart (pushing it when it goes the other way), the only
                // end Shift can heave from: heaved it rolls at 2+ m/s, dragged it crawls at 0.5.
                var end = cart.Wheels == 1 ? cart.SkidSide : (int)Mathf.Sign(d);
                if (cx + end * 1.5f < LeftWall) end = -end; // No room to stand there against the shaft wall.
                // Holding the wrong end only because there was no room at the other: let go once there is.
                if (player.Holding && cart.Wheels == 1 && Mathf.Sign(player.Position.x - cx) != cart.SkidSide && cx + cart.SkidSide * 1.5f >= LeftWall + .3f)
                {
                    _c.Grip = false;
                    yield return null;
                    continue;
                }
                if (!player.Holding && (Mathf.Abs(player.Position.x - cx) > 1.7f || Mathf.Sign(player.Position.x - cx) != end))
                    yield return Walk(cx + end * 1.5f);
                if (Mathf.Abs(d) < .3f)
                {
                    if (_trace.Length > 0) { Event("trace", _trace.ToString()); _trace.Clear(); }
                    _c.Grip = true;
                    yield return Wait(.2f);
                    yield break;
                }
                _c.Grip = true;
                // A kick along the way (a double tap while holding) whenever the cooldown allows, on the straights.
                if (Lurch && player.Holding && cart.Coal > LoadedAbove && -cart.Body.rotation < LurchMaxTilt && Mathf.Abs(d) > LurchMinDistance && (_lurchAt += Time.fixedDeltaTime) > LurchEvery && cart.Wheels == 1)
                {
                    _c.Dash = (int)Mathf.Sign(d);
                    _lurchAt = 0;
                }
                // Ease off into the stop: a heaved cart rolls on its wheel with nothing to brake it.
                _c.Move = Mathf.Sign(d) * Mathf.Clamp(Mathf.Abs(d) / 2f, .5f, 1);
                // Shift heaves a one-wheel cart's skid off the floor, from the skid end only. Only while it's
                // tilted well short of the spill angle, which shrinks as the load grows.
                _c.Heave = -cart.Body.rotation < SpillAngle(cart) - HeaveMargin;
                if (Trace && ++_traceStep % 15 == 0) _trace.Append($"{cart.Body.linearVelocity.x:0.0}/{-cart.Body.rotation:0}{(_c.Heave ? 'H' : '-')}{(cart.SkidClear ? 'c' : 'd')} ");
                yield return null;
                stillFor = Mathf.Abs(cart.Body.linearVelocity.x) < .1f ? stillFor + Time.fixedDeltaTime : 0;
                if (stillFor < 3) continue;
                // Not moving: kick it along with a few lurches if it's held, then let go and take hold again, a few times,
                // before giving up.
                stillFor = 0;
                if (++tries > 3) break;
                Event("cart won't move", $"at {cx:0.0}, burial {cart.Burial:0.00}, wheels {cart.Wheels}, holding {player.Holding}");
                for (var kick = 0; kick < 3 && player.Holding && cart.Burial < StuckBurial; kick++)
                {
                    _c.Grip = true;
                    _c.Dash = (int)Mathf.Sign(d);
                    yield return Wait(.7f);
                    if (Mathf.Abs(cart.Body.linearVelocity.x) > .3f) break;
                }
                yield return Wait(.3f);
            }
            yield return Fail($"couldn't haul the cart to {x:0.0} (it's at {cart.Body.position.x:0.0},{cart.Body.position.y:0.0})");
        }

        // A cart buried deeper than this can't be hauled at all (it doesn't move past 1.2); shallower, it's slow work, and a
        // lurch (a kick along the way) gets it through a heap at the lift's edge, so it is hauled rather than put back by hand.
        private const float StuckBurial = 1.0f;
        private const float HeaveMargin = 2.5f, LeftWall = .75f, SweepFrom = 12.5f, SpinRadius = .8f, SpinRate = 20, SweepMin = .1f;
        // Coal to load: the bin holds 10, and 12 before it spills over by itself.
        public static float LoadTarget = 11;
        // More than this on the cart is a load worth hauling; a few swept-up lumps are not.
        private const float LoadedAbove = 3;
        // Keep the building at least MinTemperature over the next lap (freezing is 0); the reserve is sized to hold SafeTemperature.
        public static float MinTemperature = 4, SafeTemperature = 4, ReserveMargin = 3;

        private float SpillAngle(Cart cart) => Mathf.Lerp(_game.Tune.spillAngleEmpty, _game.Tune.spillAngleFull, cart.Coal / _game.Tune.overfillCapacity);

        // Takes hold of a lever's knob and moves the cursor to where the knob sits at `value`.
        private IEnumerator Pull(FloorLever lever, float value)
        {
            for (float t = 0; t < 2 && !lever.CanOperate(_game.Player); t += Time.fixedDeltaTime) yield return null;
            if (!lever.CanOperate(_game.Player)) { yield return Fail($"can't reach the {lever.name.ToLowerInvariant()}: {lever.OutOfReach}"); yield break; }
            var stick = (Transform)typeof(FloorLever).GetField("stick", Private)!.GetValue(lever);
            Vector2 pivot = stick ? stick.position : lever.transform.position;
            var length = Vector2.Distance(lever.HandlePosition, pivot);
            var angle = Mathf.Lerp(_game.Tune.leverAngle, -_game.Tune.leverAngle, value) * Mathf.Deg2Rad;
            lever.Begin(lever.HandlePosition);
            lever.Drag(pivot + new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * length, Time.fixedDeltaTime);
            yield return null;
        }

        // Something went wrong the bot can't handle: note it, then put things back at the chute by hand.
        private IEnumerator Fail(string why)
        {
            _failure = why;
            yield break;
        }

        private IEnumerator Rescue(string why)
        {
            _rescues++;
            _tipping = false;
            Event("RESCUE", why);
            if (Halt) { Stop("HALTED: " + why); Time.timeScale = 0; yield break; }
            if (_rescues > 25) { Stop("gave up after 25 rescues"); yield break; }
            foreach (var m in new Machine[] { _chuteLever, _tipLever, _burnLever, _wheel }) m.End();
            var cart = _game.Cart;
            if (cart.InCradle) yield break; // Can't pull it off the chains by hand; Lower will be tried again.
            while (cart.LooseWheels.Count > 0) cart.RemoveWheel(cart.LooseWheels[0]);
            if (cart.Wheels == 0) cart.SetWheels(_game.Day.startingWheels);
            cart.Body.position = new Vector2(ChuteStop, Room.Lower + .7f);
            cart.Body.rotation = 0;
            cart.Body.linearVelocity = Vector2.zero;
            cart.Body.angularVelocity = 0;
            cart.transform.SetPositionAndRotation(cart.Body.position, Quaternion.identity);
            _game.Player.Respawn(new Vector2(ChuteStop + 1.4f, Room.Lower + .72f));
            yield return Wait(.5f);
        }

        // ---- The log ----

        private void Measure()
        {
            if (_game.Deaths != _deaths)
            {
                _deaths = _game.Deaths;
                Event("DEATH", $"{_game.LastDeath}, operator at {_game.Player.Position.x:0.0},{_game.Player.Position.y:0.0} while {Doing}");
            }
            if (!_game.ShiftRunning) return;
            _minTemperature = Mathf.Min(_minTemperature, _game.Temperature);
            if (_game.Freezing) _frozenFor += Time.fixedDeltaTime;
            var loose = _game.Coal.Loose;
            // Coal leaving the cart anywhere but the cradle is a spill (lumps in flight from the chute count as loose too, so loose alone says nothing).
            var drop = _lastCart - _game.Cart.Coal;
            _lastCart = _game.Cart.Coal;
            if (drop > 0 && !_tipping) { _spilled += drop; _spillBurst += drop; }
            _spillBurst = Mathf.MoveTowards(_spillBurst, 0, Time.fixedDeltaTime * .5f);
            if (Halt && _spillBurst > HaltSpill) { Event("SPILL", $"{_spillBurst:0.0} left the cart just now, cart {_game.Cart.Coal:0.0}, loose {loose:0.0}"); Stop("HALTED: spill"); Time.timeScale = 0; return; }
            _lastLoose = loose;
            if (_game.Clock < _nextSample) return;
            _nextSample = _game.Clock + 5;
            Line("sample", "");
        }

        private void Event(string what, string note)
        {
            _events.Add($"{(int)_game.Clock / 60}:{(int)_game.Clock % 60:00} {what} {note}");
            Line(what, note);
        }

        private void Line(string what, string note)
        {
            var g = _game;
            var f = g.Furnace;
            _log.AppendLine(string.Join(",", $"{g.Clock:0.0}", what, $"{g.Outside:0.0}", $"{g.Temperature:0.0}", $"{f.Hopper:0.0}", $"{f.BurnRate:0.000}", f.Notch,
                f.Lit ? 1 : 0, $"{g.Cart.Coal:0.0}", $"{g.Coal.Loose:0.0}", $"{g.Chill:0.00}", $"\"{note}\""));
        }

        private void Summarize(string why)
        {
            var g = _game;
            var s = new StringBuilder();
            s.AppendLine($"ShiftBot {why}: phase {g.Phase} at {(int)g.Clock / 60}:{(int)g.Clock % 60:00}, {_deliveries} deliveries, {g.Furnace.Delivered:0.0} coal delivered, {g.Furnace.Output:0.0} kWh (target {g.Day.target:0})");
            s.AppendLine($"Inside: lowest {_minTemperature:0.0}°, freezing for {_frozenFor:0}s. Deaths {g.Deaths}, rescues {_rescues}, spilled {_spilled:0.0} (loose now {g.Coal.Loose:0.0}). Coal error {g.TotalCoal - g.InitialCoal:0.0000}");
            foreach (var e in _events) s.AppendLine("  " + e);
            Report = s.ToString();
            Debug.Log(Report);
            var path = Path.Combine(Application.dataPath, "..", "Logs", "ShiftBot.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _log.ToString());
        }
    }
}
