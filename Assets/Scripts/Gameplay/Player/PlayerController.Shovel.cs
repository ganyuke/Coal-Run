using CoalRun.Core;
using CoalRun.Gameplay.Coal;
using CoalRun.Gameplay.Machinery;
using UnityEngine;

namespace CoalRun.Gameplay.Player
{
    // Digging, throwing and the shovel and arms drawn in the operator's hands. (Part of PlayerController.)
    public partial class PlayerController
    {
        private Sprite _armedBody;
        private int _digSide = 1;
        private bool _digging;

        private Vector2 _grip, _digAt;

        // Seconds the shovel has been out with nothing in reach to dig, and how it's turned (smoothed).
        private float _idleFor, _shovelAngle;
        private float _thrownAt = -1;

        private bool Digging(Controls c)
        {
            return c.Shovel && Lane == 0 && !Machine && !HandsFull && _game.Tutorial.CanDig;
        }

        // The cart pushes loose coal ahead of it. That's harder work than shoveling it out of the way:
        // the pile pushes back, and past 1.2 high it doesn't budge.
        private void Bulldoze(float dt, int direction)
        {
            var coal = _game.Coal;
            var p = _game.Cart.Body.position;
            var s = coal.SurfaceAt(p.x, p.y);
            int i = coal.Index(s, p.x + direction * 1f), j = i + direction;
            if (j < 0 || j >= s.Heights.Length || s.Heights[i] < .03f) return;
            _game.Cart.Body.AddForce(new Vector2(-direction * s.Heights[i] * 95, 0));
            if (s.Heights[i] > 1.2f) return;
            CoalField.Shove(s, i, j, dt * .9f);
        }

        // Holding F fills the shovel from the coal within reach (the deepest spot around the cart first),
        // then throws it the way the operator faces: into the cart when it's there, onto the floor otherwise.
        private void Shovel(Controls c, float dt)
        {
            var t = _game.Tune;
            if (!Digging(c))
            {
                _idleFor = 0;
                _digSide = Facing;
                ThrowHands();
                return;
            }

            if (Hands >= t.handCapacity - .001f)
            {
                ThrowHands();
                return;
            }

            if (ScoopTarget(Facing) is not { } target)
            {
                // Walking over scattered chunks, the pile keeps dropping out of reach for a moment: the shovel stays
                // out, on the side it was digging, and only gives up (and throws what's on it) once nothing has
                // turned up for a while. Held toward the cursor (they face it) while there's nothing to dig.
                _idleFor += dt;
                if (_idleFor <= t.scoopLinger) return;
                _digSide = Facing;
                ThrowHands();
                return;
            }

            _idleFor = 0;
            var (s, i) = target;
            var dx = s.Start + (i + .5f) * t.columnWidth - Body.position.x;
            // A pile right under their feet doesn't flip the shovel from one side to the other.
            if (Mathf.Abs(dx) > .3f) _digSide = (int)Mathf.Sign(dx);
            _digAt = new Vector2(Body.position.x + dx, s.Y + s.Heights[i]);
            var taken = _game.Coal.TakeFromPile(s, i, Mathf.Min(t.shovelRate * dt, t.handCapacity - Hands));
            Hands += taken;
            Scooped += taken;
            _game.PlayCue("shovel", .22f);
        }

        // The scoop box in the world, on the side they face.
        private Rect ScoopRect(int side)
        {
            var feet = Body.position + new Vector2(0, -.72f);
            var x = side > 0 ? feet.x + scoopBox.xMin : feet.x - scoopBox.xMax;
            return new Rect(x, feet.y + scoopBox.yMin, scoopBox.width, scoopBox.height);
        }

        // The tallest column of coal piled into the scoop box, on any surface: the floors or the lift.
        private (CoalField.Surface s, int i)? ScoopTarget(int side)
        {
            var box = ScoopRect(side);
            var width = _game.Tune.columnWidth;
            (CoalField.Surface, int)? best = null;
            var bestTop = float.MinValue;
            foreach (var s in _game.Coal.Surfaces)
            {
                var floor = s.Y;
                if (floor > box.yMax) continue;
                for (var i = 0; i < s.Heights.Length; i++)
                {
                    var x = s.Start + (i + .5f) * width;
                    var top = floor + s.Heights[i];
                    if (x < box.xMin || x > box.xMax || s.Heights[i] <= .001f || top < box.yMin ||
                        top <= bestTop) continue;
                    best = (s, i);
                    bestTop = top;
                }
            }

            return best;
        }

        private void ScoopOutline()
        {
            if (!scoopOutline) return;
            // hide scoop outline if the debug menu is not enabled
            scoopOutline.enabled = ShowDebug && _digging;
            if (!scoopOutline.enabled) return;
            var b = ScoopRect(Facing);
            scoopOutline.SetPosition(0, new Vector3(b.xMin, b.yMin));
            scoopOutline.SetPosition(1, new Vector3(b.xMin, b.yMax));
            scoopOutline.SetPosition(2, new Vector3(b.xMax, b.yMax));
            scoopOutline.SetPosition(3, new Vector3(b.xMax, b.yMin));
            var color = ScoopTarget(Facing) != null ? new Color(1, .85f, .3f, .7f) : new Color(1, 1, 1, .25f);
            scoopOutline.startColor = scoopOutline.endColor = color;
        }

        // Empties the shovel toward the cursor. The throw is aimed to pass through the cursor, so coal
        // lands where you point; it only goes in the cart if the lumps actually land in the bin.
        public void ThrowHands()
        {
            if (Hands <= 0) return;
            var t = _game.Tune;
            var aim = input.Current.Aim;
            if (Mathf.Abs(aim.x - Body.position.x) > .05f) Facing = (int)Mathf.Sign(aim.x - Body.position.x);
            var from = Body.position + new Vector2(Facing * .7f, .7f);
            var v = ThrowVelocity(from, aim, t.throwSpeed);
            _game.Coal.Emit(from.x, from.y, Hands, v.x, v.y, t.throwSpread);
            Hands = 0;
            _thrownAt = Time.time;
            _game.PlayCue("coal");
        }

        // Out while digging or carrying coal, held in both hands on the side they face. Each scoop is one stroke,
        // following how full the blade is: jabbed in low and forward, leaning into it, then levered back up as the
        // heap on the blade grows, coming round to point at the cursor so you can see where the throw will go. Then
        // swung across and up into the throw, leaning back, and down again.
        private void HeldShovel()
        {
            if (!heldShovel) return;
            var throwing = Time.time - _thrownAt;
            var shown = (_digging || Hands > 0 || throwing < .4f) && Lane == 0 && !HandsFull && !Machine &&
                        !_carriedBody;
            heldShovel.gameObject.SetActive(shown);
            Arms(shown);
            _poseLean = 0;
            _poseCrouch = 1;
            if (!shown) return;
            var f = Facing;
            var full = Hands / _game.Tune.handCapacity;
            // The top hand, in the art's space, and the shaft's angle from straight down (positive to the right).
            Vector2 grip;
            float angle;
            var stroke = _digging && full < .99f && _idleFor <= 0;
            if (stroke)
            {
                grip = new Vector2(f * Mathf.Lerp(.2f, .08f, full), Mathf.Lerp(-.12f, .02f, full));
                // Into the top of the heap it's digging from, and levered up from there.
                var toHeap = _digAt - (Vector2)visual.transform.TransformPoint(grip);
                var dig = f * Mathf.Clamp(Mathf.Atan2(f * toHeap.x, -toHeap.y) * Mathf.Rad2Deg, 20, 100);
                angle = Mathf.Lerp(dig, f * 40, full);
                _poseLean = f * Mathf.Lerp(10, 2, full);
                _poseCrouch = Mathf.Lerp(.93f, 1, full);
            }
            else
            {
                // Carried: across the body, blade out front and low.
                grip = new Vector2(f * .08f, 0);
                angle = f * 35;
            }

            var gripWorld = (Vector2)visual.transform.TransformPoint(grip);
            if (Hands > 0)
            {
                var toAim = input.Current.Aim - gripWorld;
                var aimed = Mathf.Clamp(Mathf.Atan2(toAim.x, -toAim.y) * Mathf.Rad2Deg, -120, 120);
                angle = Mathf.LerpAngle(angle, aimed, full * full);
            }

            // Working the blade into the heap: short pushes along the shaft, fading as it fills.
            if (stroke && Hands > 0) grip += Shaft(angle) * (.035f * Mathf.Sin(Time.time * 14) * (1 - full));
            if (throwing < .4f)
            {
                // Across and up in .12 s, back down over the rest.
                var swing = throwing < .12f
                    ? Mathf.SmoothStep(0, 1, throwing / .12f)
                    : 1 - Mathf.SmoothStep(0, 1, (throwing - .12f) / .28f);
                angle = Mathf.Lerp(angle, f * 145, swing);
                grip = Vector2.Lerp(grip, new Vector2(f * .05f, .15f), swing);
                _poseLean = Mathf.Lerp(_poseLean, -f * 6, swing);
            }

            // Eased, so a pile appearing and vanishing under the cursor as they walk doesn't snap it about.
            var ease = 1 - Mathf.Exp(-22 * Time.deltaTime);
            _shovelAngle = Mathf.LerpAngle(_shovelAngle, angle, ease);
            _grip = Vector2.Lerp(_grip, grip, ease);
            heldShovel.localPosition = _grip;
            heldShovel.localRotation = Quaternion.Euler(0, 0, _shovelAngle);
            // The back arm to the top of the handle, the front one further down it.
            Reach(backArm, new Vector2(f * backShoulder.x, backShoulder.y), _grip);
            Reach(frontArm, new Vector2(f * frontShoulder.x, frontShoulder.y),
                _grip + Shaft(_shovelAngle) * gripSpacing);
            if (!shovelLoad) return;
            shovelLoad.gameObject.SetActive(full > .01f);
            // On the blade's upper face.
            shovelLoad.localPosition = new Vector3((_shovelAngle >= 0 ? 1 : -1) * .06f * Mathf.Sqrt(full), -.86f, 0);
            shovelLoad.localScale = new Vector3(.18f + .16f * full, .12f + .14f * full, 1);
        }

        // Down the shaft from the top hand toward the blade, at `angle` degrees from straight down.
        private static Vector2 Shaft(float angle)
        {
            return new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), -Mathf.Cos(angle * Mathf.Deg2Rad));
        }

        // The arms out (and the body without its painted ones) while the shovel is, or back to hanging at the sides.
        private void Arms(bool on)
        {
            if (frontArm) frontArm.gameObject.SetActive(on);
            if (backArm) backArm.gameObject.SetActive(on);
            if (bodyArt && armlessBody) bodyArt.sprite = on ? armlessBody : _armedBody;
        }

        // Swings an arm from its shoulder to point at `hand`, stretched a little to meet it.
        private void Reach(Transform arm, Vector2 shoulder, Vector2 hand)
        {
            if (!arm) return;
            var d = hand - shoulder;
            arm.localPosition = shoulder;
            arm.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg);
            arm.localScale = new Vector3(1, Mathf.Clamp(d.magnitude / armLength, .6f, 1.4f), 1);
        }

        // The launch velocity that reaches `to` after a flight time that grows with distance, so near
        // throws are quick tosses and far ones lob. Capped at `max`, in which case it falls short.
        private static Vector2 ThrowVelocity(Vector2 from, Vector2 to, float max)
        {
            var d = to - from;
            var time = Mathf.Clamp(d.magnitude / 7, .25f, .7f);
            var v = d / time - .5f * Physics2D.gravity * time;
            return Vector2.ClampMagnitude(v, max);
        }
    }
}