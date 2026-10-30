using System.Collections.Generic;
using CoalRun.Gameplay.Lift;
using CoalRun.Gameplay.Machinery;
using UnityEngine;

namespace CoalRun.Gameplay.Player
{
    // The key prompts for whatever is within reach. (Part of PlayerController.)
    public partial class PlayerController
    {
        private readonly List<(string key, string label)> _prompts = new();

        // Coal in the scoop box on either side: they turn to face the cursor when they dig.
        private bool PileInReach()
        {
            return ScoopTarget(1) != null || ScoopTarget(-1) != null;
        }

        // The cart is taken hold of from either end, not from inside its footprint. 1.24 is the distance
        // between centres when the operator's body touches the end of the bin.
        private bool CartInGrip()
        {
            Vector2 p = Body.position, cp = _game.Cart.Body.position;
            var dx = Mathf.Abs(p.x - cp.x);
            return dx > 1.05f && dx < 1.24f + _game.Tune.gripReach && Mathf.Abs(p.y - cp.y) < 1.4f;
        }

        // Up to two key prompts for whatever is within reach, most urgent first. The same list every time (the notebook
        // asks every frame), so don't keep it.
        public List<(string key, string label)> Prompts()
        {
            var list = _prompts;
            list.Clear();
            var say = _game.Lines.prompts;
            if (!_game.CanAct || Machine || Climbing) return list;
            if (Lane == 1)
            {
                if (AtDoor) list.Add(("S", say.downStairs));
                MachinePrompt(list);
                return list;
            }

            if (Holding)
            {
                // What the cart can be made to do while it's held: hauled, and (from the skid end of a one-wheel
                // cart) heaved up like a wheelbarrow.
                list.Add(("A/D", say.haul));
                if (_game.Cart.Wheels == 1 &&
                    Mathf.Approximately(Mathf.Sign(Body.position.x - _game.Cart.Body.position.x), _game.Cart.SkidSide))
                    list.Add(("Shift", say.heave));
                list.Add(("Space", say.letGo));
                return list;
            }

            if (_game.Tutorial.ShovelInReach(this)) list.Add(("E", say.pickShovel));
            if (_game.Tutorial.Restricts)
            {
                if (AtDoor) list.Add(("W", say.upStairs));
                return list;
            }

            if (_carriedBody)
            {
                list.Add(("E", say.putBody));
                list.Add(("Click", say.throwBody));
                return list;
            }

            if (HandsFull)
            {
                list.Add(CartNear() && _game.Cart.Wheels < 2 ? ("Hold E", say.fitWheel) : ("E", say.putWheel));
                list.Add(("Click", say.throwWheel));
                return list;
            }

            if (WheelInReach()) list.Add(("E", say.pickWheel));
            else if (BodyInReach()) list.Add(("E", say.pickBody));
            if (AtDoor) list.Add(("W", say.upStairs));
            if (_game.Furnace.CanLight(this)) list.Add(("Hold E", say.lightFire));
            if (CartNear() && _game.Cradle.Prompt != null) list.Add(("E", _game.Cradle.Prompt));
            if (CartOverturnedInReach()) list.Add(("E", say.rightCart));
            if (PileInReach() && _game.Tutorial.CanDig) list.Add(("F", say.shovel));
            if (!Holding && CartInGrip() && Mathf.Cos(_game.Cart.Angle) > .3f) list.Add(("Space", say.grip));
            MachinePrompt(list);
            if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
            return list;
        }

        private void MachinePrompt(List<(string key, string label)> list)
        {
            foreach (var m in Machine.All)
            {
                if (!_game.Tutorial.Allows(m)) continue;
                if (m is LiftCrank && m.CanOperate(this))
                {
                    list.Add(("W S", _game.Lines.prompts.crank));
                    return;
                }

                if (m is not FloorLever lever || !m.CanOperate(this)) continue;
                list.Add(("Mouse", lever.Prompt));
                return;
            }
        }
    }
}