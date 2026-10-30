using System;
using System.Collections;
using CoalRun.Core;
using CoalRun.Gameplay.Characters;
using UnityEngine;

namespace CoalRun.Gameplay.Player
{
    // E: righting the cart, wheels and bodies carried, thrown and fitted. (Part of PlayerController.)
    public partial class PlayerController
    {
        private Rigidbody2D _carriedBody;
        private CorpseBody _carriedCorpse;
        private float _fit;
        private bool _flying;

        private Vector2 HandPoint => Body.position + new Vector2(Facing * .45f, .05f);

        // Interaction priority

        // E: one press, one action, picked by what the operator is standing next to: righting the cart, putting a carried wheel down, picking one up, hooking the cart onto the tip
        // cradle or lowering it, or clocking out at the staff lift.
        private void Interact()
        {
            if (Machine || Lane != 0) return;
            if (_game.Tutorial.ShovelInReach(this))
            {
                _game.Tutorial.TakeShovel();
                return;
            }

            if (_game.Tutorial.Restricts) return;
            if (CartOverturnedInReach())
            {
                RightCart();
                return;
            }

            if (_carriedBody)
            {
                ReleaseCarriedItem(Body.linearVelocity + new Vector2(Facing * 1.2f, .5f));
                return;
            }

            if (HandsFull)
            {
                // Away from the cart, E puts the wheel down; at the cart, holding it bolts it on (see Fit).
                if (!CartNear() || _game.Cart.Wheels >= 2)
                    ReleaseCarriedItem(Body.linearVelocity + new Vector2(Facing * 1.5f, 1));
                else if (_game.Cart.WheelBlocker != null) _game.Say(_game.Cart.WheelBlocker);
                return;
            }

            if (WheelInReach() is { } wheel)
            {
                PickUpWheel(wheel);
                return;
            }

            if (BodyInReach() is { } body)
            {
                PickUpBody(body);
                return;
            }

            if (CartNear() && _game.Cradle.Interact()) return;
        }

        private void RightCart()
        {
            var cart = _game.Cart;
            var cp = cart.Body.position;
            var floor = _game.Coal.SurfaceAt(cp.x, cp.y).Floor();
            // A cart at rest has landed, even when it came to rest hung up on something.
            if (cart.Body.linearVelocity.magnitude > (cp.y - floor > 1.8f ? .3f : 1))
            {
                _game.Say(_game.Lines.speech.cartLanding);
                return;
            }

            if (cart.Burial > .8f)
            {
                _game.Say(_game.Lines.speech.cartBuried);
                return;
            }

            ReleaseGrip();
            cart.Right();
        }

        // Shared carrying actions

        private void ReleaseCarriedItem(Vector2 velocity)
        {
            if (_carriedBody)
            {
                ReleaseBody(velocity);
                return;
            }

            HandsFull = false;
            if (heldWheel) heldWheel.gameObject.SetActive(false);
            _game.Cart.DropWheel(HandPoint, velocity);
            _game.PlayCue("clank");
        }

        // Thrown to pass through the cursor, like the shovel, but heavier: it falls short of far aims.
        private void ThrowCarriedItem(Vector2 aim)
        {
            if (Mathf.Abs(aim.x - Body.position.x) > .05f) Facing = (int)Mathf.Sign(aim.x - Body.position.x);
            ReleaseCarriedItem(ThrowVelocity(HandPoint, aim, _game.Tune.wheelThrowSpeed * (_carriedBody ? .85f : 1)));
        }

        // Wheels: find, pick up, fit and animate

        private Rigidbody2D WheelInReach()
        {
            Rigidbody2D best = null;
            var reach = _game.Tune.wheelReach;
            foreach (var w in _game.Cart.LooseWheels)
            {
                var d = Vector2.Distance(w.position, Body.position);
                if (d >= reach) continue;
                best = w;
                reach = d;
            }

            return best;
        }

        private void PickUpWheel(Rigidbody2D wheel)
        {
            ThrowHands();
            ReleaseGrip();
            Vector3 from = wheel.position;
            var spin = wheel.transform.rotation;
            _game.Cart.RemoveWheel(wheel);
            HandsFull = true;
            if (heldWheel) heldWheel.gameObject.SetActive(true);
            StartCoroutine(Fly(from, spin, () => HandPoint, .2f, null));
            _game.PlayCue("clank");
        }

        // Holding E at the cart bolts the carried wheel on; letting go starts over. The operator lifts the
        // end missing the wheel and works it about while the wheel is lined up on its slot (see Update),
        // then it goes on with a clunk and the cart is let down.
        private void Fit(Controls c, float dt)
        {
            var cart = _game.Cart;
            if (!HandsFull || _carriedBody || _flying || !c.InteractHeld || Lane != 0 || !CartNear() ||
                cart.WheelBlocker != null || CartOverturnedInReach())
            {
                _fit = 0;
                return;
            }

            _fit += dt;
            var t = _game.Tune;
            var k = _fit / t.fitTime;
            Facing = (int)Mathf.Sign(cart.WheelSlot(cart.Wheels).x - Body.position.x);
            // Up in the first quarter, then rocked about, settling as the wheel lines up.
            cart.HoldUp(cart.Wheels,
                t.fitLift * Mathf.SmoothStep(0, 1, k * 4) +
                Mathf.Sin(_fit * 11) * 2.5f * Mathf.Min(1, k * 4) * (1 - k));
            _game.PlayCue("wrench", .3f);
            if (_fit < t.fitTime) return;
            _fit = 0;
            StartCoroutine(Fly(heldWheel ? heldWheel.position : HandPoint,
                heldWheel ? heldWheel.rotation : Quaternion.identity, () => cart.WheelSlot(cart.Wheels), .1f, () =>
                {
                    HandsFull = false;
                    if (heldWheel) heldWheel.gameObject.SetActive(false);
                    cart.SetWheels(cart.Wheels + 1);
                }));
        }

        // Moves the held wheel from where it was to a point that may itself be moving (the hands, a cart
        // slot), eased in and out and turning upright on the way, so nothing jumps into place.
        private IEnumerator Fly(Vector3 from, Quaternion spin, Func<Vector2> to, float time, Action done)
        {
            _flying = true;
            for (float k = 0; k < 1; k += Time.deltaTime / time)
            {
                var e = k * k * (3 - 2 * k);
                if (heldWheel)
                    heldWheel.SetPositionAndRotation(Vector3.Lerp(from, to(), e),
                        Quaternion.Slerp(spin, Quaternion.identity, e));
                yield return null;
            }

            if (heldWheel) heldWheel.rotation = Quaternion.identity;
            _flying = false;
            done?.Invoke();
        }

        // Corpses: find, pick up, release and position

        private Rigidbody2D BodyInReach()
        {
            Rigidbody2D best = null;
            var reach = _game.Tune.wheelReach * 1.4f;
            foreach (var b in _game.Bodies)
            {
                if (!b || !b.gameObject.activeSelf || b == _carriedBody) continue;
                var d = Vector2.Distance(b.position, Body.position);
                if (d >= reach) continue;
                best = b;
                reach = d;
            }

            return best;
        }

        // Carried like a wheel (the same hands, the same throw), but slung over the shoulder, slower and shorter.
        private void PickUpBody(Rigidbody2D body)
        {
            ThrowHands();
            ReleaseGrip();
            _carriedBody = body;
            _carriedCorpse = body.GetComponent<CorpseBody>();
            HandsFull = true;
            if (heldWheel) heldWheel.gameObject.SetActive(false);
            body.simulated = false;
            body.linearVelocity = Vector2.zero;
            if (_carriedCorpse)
            {
                _carriedCorpse.Carried = true;
                _carriedCorpse.Lift(true);
            }

            _game.PlayCue("clank");
        }

        private void ReleaseBody(Vector2 velocity)
        {
            var body = _carriedBody;
            _carriedBody = null;
            HandsFull = false;
            // A body that isn't simulated has to be moved by its transform.
            body.transform.position = HandPoint + Vector2.up * .3f;
            body.simulated = true;
            body.linearVelocity = velocity;
            body.angularVelocity = -Facing * 220;
            if (_carriedCorpse)
            {
                _carriedCorpse.Carried = false;
                _carriedCorpse.Lift(false);
                _carriedCorpse.Throw(velocity.magnitude > 3 ? .9f : 0);
            }

            _carriedCorpse = null;
            _game.PlayCue("clank");
        }

        // The body hangs across the operator's shoulders while carried.
        private void HeldBody()
        {
            if (!_carriedBody) return;
            _carriedBody.transform.SetPositionAndRotation(Body.position + new Vector2(Facing * .1f, .35f),
                Quaternion.Euler(0, 0, -Facing * 78));
        }
    }
}