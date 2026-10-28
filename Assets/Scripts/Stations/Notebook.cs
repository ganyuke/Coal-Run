using System;
using System.Collections.Generic;
using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // The operator's notebook: what needs doing right now, written down as the shift goes. It never has a
    // fixed list of pages. A page is written when the game needs something (the fire's out, the cart's empty)
    // and ticked when that need goes away. Pages are only ever added and ticked, never
    // removed or un-ticked: a need that comes back is written down again as a new page, so the notebook reads
    // as a log. The one that's open is the oldest unticked page, unless the fire's out, which takes over.
    // The training's objective lines are pages too.
    public class Notebook : MonoBehaviour
    {
        public class Page
        {
            // What the page is about, so the same need isn't written twice while it's still open.
            public string Key;
            public string Text;
            public bool Done;
            public bool Urgent;
            public float Clock;
        }

        [Tooltip("Seconds between looking at the room for something new to write down.")]
        [SerializeField]
        private float interval = .25f;
        [Tooltip("Coal on the cart that counts as a load worth hauling.")]
        [SerializeField]
        private float loaded = 5;
        [Tooltip("The burn lever is called for this many seconds ahead of the forecast.")]
        [SerializeField]
        private float lookAhead = 45;

        public List<Page> Pages { get; } = new();
        // Any page written, rewritten or ticked.
        public event Action OnChanged;
        // A new page, for the notebook to nudge itself up.
        public event Action<Page> OnWritten;

        private ShiftManager _game;
        private float _next;

        // An urgent page first, else the oldest still open; none when everything's done.
        public Page Current
        {
            get
            {
                Page open = null;
                foreach (var p in Pages)
                {
                    if (p.Done) continue;
                    if (p.Urgent) return p;
                    open ??= p;
                }
                return open;
            }
        }

        private void Start() => _game = ShiftManager.Instance;

        private void FixedUpdate()
        {
            _next -= Time.fixedDeltaTime;
            if (_next > 0 || _game.Ended) return;
            _next = interval;
            var needs = Needs();
            var changed = false;
            foreach (var page in Pages)
            {
                if (page.Done) continue;
                var need = needs.Find(n => n.Key == page.Key);
                if (need == null) { page.Done = true; changed = true; }
                else if (need.Text != page.Text) { page.Text = need.Text; changed = true; }
            }
            foreach (var need in needs)
            {
                if (Pages.Exists(p => !p.Done && p.Key == need.Key)) continue;
                need.Clock = _game.Clock;
                Pages.Add(need);
                OnWritten?.Invoke(need);
                changed = true;
            }
            if (changed) OnChanged?.Invoke();
        }

        // Everything that needs doing now, by where the game is. A page is ticked when its need leaves this list.
        private List<Page> Needs()
        {
            var list = new List<Page>();
            void Add(string key, string text, bool urgent = false) => list.Add(new Page { Key = key, Text = text, Urgent = urgent });

            // The training has its own objective line.
            if (_game.InTraining)
            {
                var objective = _game.Tutorial.Objective;
                if (objective != "") Add("training: " + objective, objective);
                return list;
            }
            if (_game.Phase is not (Phase.Shift or Phase.Leaving or Phase.Replacement)) return list;

            var furnace = _game.Furnace;
            var cart = _game.Cart;
            if (_game.Phase == Phase.Shift && !furnace.Lit)
                Add("fire", furnace.Hopper > .5f ? "The fire's out. Light it (hold E at the firebox)" : "The fire's out and the hopper's empty. Get coal to the furnace", true);

            if (cart.Overturned) Add("cart upright", "Stand the cart back up (E)");
            else if (cart.Wheels < _game.Day.startingWheels || cart.LooseWheels.Count > 0) Add("cart wheel", "Put the wheel back on (pick it up with E, then hold E at the cart)");
            if (cart.Burial > .8f) Add("cart buried", "Dig the cart out (hold F at the coal)");

            if (_game.Phase == Phase.Shift)
            {
                var upstairs = cart.Body.position.y > 6;
                var full = cart.Coal >= loaded;
                if (cart.InCradle) Add(full ? "tip" : "lower", full ? "Tip the cart into the hopper" : "Lower the cart (E)");
                else if (!upstairs) Add(full ? "haul" : "fill", full ? "Take the cart up on the freight lift" : "Fill the cart at the chute");
                else Add(full ? "hook" : "return", full ? "Hook the cart on at the tip cradle (E)" : "Take the empty cart back down");

                if (furnace.Lit)
                {
                    var held = _game.Tune.heldTemperatures[furnace.Notch];
                    var ahead = Mathf.Min(_game.Outside, _game.Day.OutsideAt(_game.Clock + lookAhead));
                    if (furnace.Notch < _game.Tune.burnRates.Length - 1 && held > ahead + .5f)
                        Add("burn", $"Turn the burn lever up: it's heading for {ahead:0}° outside and the fire only holds {held:0}°");
                }
            }

            if (_game.Phase == Phase.Leaving) Add("leave", "The shift is over. Stand in the staff lift");
            return list;
        }
    }
}
