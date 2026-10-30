using CoalRun.Gameplay.Lift;
using UnityEngine;

namespace CoalRun.Gameplay.Player
{
    // The per-frame look of the operator: the walk bob, held wheel, flint and steel. (Part of PlayerController.)
    public partial class PlayerController
    {
        private bool _striking;
        private float _lastStrike;

        private Vector3 _headRest;

        private float _pace, _stride, _lean, _poseLean;
        private float _poseCrouch = 1;

        private void Update()
        {
            HeldWheel();
            HeldBody();
            Strike();
            HeldShovel();
            ScoopOutline();
            // Crouched to strike at the firebox, or into a dig; shivering in the cold, harder the nearer to freezing.
            var art = visual.transform;
            var crouch = _striking ? .82f : _poseCrouch;
            var shiver = _game.Ended ? 0 :
                _game.Freezing ? .015f + .035f * _game.Chill :
                _game.Temperature < 6 ? .008f : 0;
            art.localScale = new Vector3(1, Mathf.MoveTowards(art.localScale.y, crouch, Time.deltaTime * 2), 1);
            var (bob, lean) = Walk();
            // Leaned over from the feet, so they stay planted.
            lean = Mathf.LerpAngle(_lean, lean + _poseLean, 1 - Mathf.Exp(-14 * Time.deltaTime));
            _lean = lean;
            var turn = Quaternion.Euler(0, 0, -lean);
            var feet = new Vector3(0, -.66f, 0);
            art.localRotation = turn;
            art.localPosition =
                new Vector3(shiver * Mathf.Sin(Time.time * 55), -.66f * (1 - art.localScale.y) + bob, 0) + feet -
                turn * feet;
        }

        // A bob that comes round once a step and a lean into the walk, both fading in and out with the pace. The bob
        // follows the distance walked, so it keeps time with a heavy haul or a freezing rush. The head nods a beat
        // behind. Returns the body's lift and its lean in degrees, positive toward the right.
        private (float bob, float lean) Walk()
        {
            var vx = Body.linearVelocity.x;
            var walking = Body.simulated && !Climbing && !_flying && !_striking &&
                          Mathf.Abs(Body.linearVelocity.y) < 1.5f;
            var pace = walking ? Mathf.Clamp01(Mathf.Abs(vx) / _game.Tune.walkSpeed * 1.6f) : 0;
            _pace = Mathf.MoveTowards(_pace, pace, Time.deltaTime * 6);
            _stride += Mathf.Abs(vx) * Time.deltaTime;

            float Lift(float steps)
            {
                return Mathf.Abs(Mathf.Sin(steps * Mathf.PI)) * stepBob * _pace;
            }

            var step = _stride / stepLength;
            if (head) head.localPosition = _headRest + new Vector3(0, Lift(step - headLag) - Lift(step), 0);
            return (Lift(step), Mathf.Sign(vx) * walkLean * _pace);
        }

        // In the hands, or while a wheel is being fitted, offered up to its slot and jiggled about, less and
        // less, until it lines up.
        private void HeldWheel()
        {
            if (!heldWheel || _flying) return;
            var fit = _fit / _game.Tune.fitTime;
            var hand = (Vector3)HandPoint + (heldWheel.parent.position - transform.position);
            if (fit <= 0)
            {
                heldWheel.SetPositionAndRotation(hand, Quaternion.identity);
                return;
            }

            var cart = _game.Cart;
            var loose = 1 - fit;
            var jiggle = new Vector2(Mathf.Sin(Time.time * 13), .5f * Mathf.Cos(Time.time * 9)) * (.12f * loose);
            var e = Mathf.SmoothStep(0, 1, fit * 3);
            heldWheel.SetPositionAndRotation(Vector3.Lerp(hand, cart.WheelSlot(cart.Wheels) + jiggle, e),
                Quaternion.Euler(0, 0, 25 * loose * Mathf.Sin(Time.time * 7)));
        }

        // Flint and steel: a quick scrape every .3 s, each throwing a few sparks at the firebox.
        private void Strike()
        {
            if (striker) striker.gameObject.SetActive(_striking);
            if (!_striking) return;
            var phase = Time.time % .3f / .3f;
            if (striker)
            {
                striker.localPosition = new Vector3(Facing * .38f, -.05f, 0);
                // Wound back slowly, then struck down fast.
                var swing = phase < .7f ? Mathf.Lerp(-20, 50, phase / .7f) : Mathf.Lerp(50, -40, (phase - .7f) / .3f);
                striker.localRotation = Quaternion.Euler(0, 0, Facing * -swing);
            }

            if (!strikeSparks || Time.time - _lastStrike < .3f) return;
            _lastStrike = Time.time;
            var at = Body.position + new Vector2(Facing * .5f, -.2f);
            for (var i = 0; i < 6; i++)
                strikeSparks.Emit(
                    new ParticleSystem.EmitParams
                    {
                        position = at, velocity = new Vector2(Facing * Random.Range(1.5f, 4f), Random.Range(-.5f, 2.5f))
                    }, 1);
            _game.PlayCue("strike", .1f);
        }
    }
}