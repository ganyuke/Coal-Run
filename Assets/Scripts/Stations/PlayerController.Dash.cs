using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // Dash, short dash and ram. (Part of PlayerController.)
    public partial class PlayerController
    {
        // A double tap. Free, the operator dashes and can ram the cart; holding it, they heave it along in
        // one lurch, which a full cart barely feels.
        private void Dash(int direction)
        {
            var t = _game.Tune;
            if (_dashCooldown > 0 || Lane != 0 || Machine || Mathf.Abs(Body.linearVelocity.y) > 1) return;
            _dashCooldown = t.dashCooldown;
            _dashDir = direction;
            Facing = direction;
            var cart = _game.Cart.Body;
            if (Holding)
            {
                // Shared between both bodies, since Hold keeps them together.
                var kick = new Vector2(direction * t.lurchImpulse / (cart.mass + Body.mass), 0);
                cart.linearVelocity += kick;
                Body.linearVelocity += kick;
                _game.Cart.Hop();
                _lurchFor = .3f;
                DashDust(8, .6f);
                _game.Sound("lurch", .2f);
                return;
            }
            _dashFor = t.dashTime;
            _dashStart = Mathf.Max(t.dashStartSpeed, Body.linearVelocity.x * direction);
            _ramming = true;
            _lastAhead = float.MaxValue;
            Body.linearVelocity = new Vector2(direction * _dashStart, Body.linearVelocity.y);
            DashDust(16, 1);
            _game.Sound("dash", .2f);
        }

        // Puffs of dust off the floor behind the feet, thrown back against the way we're going, `strength` times as hard.
        private void DashDust(int count, float strength)
        {
            if (!dashDust || Lane != 0) return;
            var feet = Body.position + new Vector2(-_dashDir * .1f, -.62f);
            for (var i = 0; i < count; i++)
                dashDust.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + new Vector2(Random.Range(-.12f, .12f), Random.Range(0f, .08f)),
                    velocity = new Vector2(-_dashDir * Random.Range(1f, 3.4f) * strength, Random.Range(.3f, 1.3f) * strength),
                    startSize = Random.Range(.16f, .32f) * (.6f + .4f * strength),
                    startLifetime = Random.Range(.45f, .85f)
                }, 1);
        }

        // Short dash: tapping the other way mid-dash stops it dead, so how soon you tap sets the distance.
        // Letting go doesn't: a double tap is usually quick, and every dash would come out short.
        private void DashCancel(Controls c)
        {
            if (c.Tap != -_dashDir) return;
            _dashFor = 0;
            Body.linearVelocity = new Vector2(0, Body.linearVelocity.y);
        }

        // Running into the end of the cart mid-dash: an inelastic hit through its centre of mass, so it
        // shoves the cart along without tipping it. An overturned cart is rolled over the way it was hit
        // instead, so ramming it from the right side stands it back up (and from the wrong side, turns it
        // further over). The operator bounces off and staggers.
        private void Ram(Vector2 p)
        {
            var cart = _game.Cart.Body;
            var over = _game.Cart.Overturned;
            float ahead = (cart.position.x - p.x) * _dashDir, reach = over ? _game.Cart.HalfWidth + .24f : Touching;
            var closing = ahead < _lastAhead;
            _lastAhead = ahead;
            // Further in than this the operator is already walking through the cart, not running into it.
            if (!closing || ahead > reach || ahead < reach - .45f || Mathf.Abs(cart.position.y - p.y) > 1.4f) return;
            float vp = Body.linearVelocity.x, vc = cart.linearVelocity.x;
            if ((vp - vc) * _dashDir <= 0) return;
            // A parked cart (the training) is a wall: the operator bounces off it, it doesn't budge.
            if (_game.Cart.Parked)
            {
                Body.position = new Vector2(cart.position.x - _dashDir * reach, p.y);
                Body.linearVelocity = new Vector2(-_dashDir * 1.5f, Body.linearVelocity.y);
                _dashFor = 0;
                _ramming = false;
                Stagger = .3f;
                _game.Sound("clank", .2f);
                return;
            }
            var shared = (Body.mass * vp + cart.mass * vc) / (Body.mass + cart.mass);
            cart.linearVelocity = new Vector2(shared, cart.linearVelocity.y);
            if (over)
            {
                // Hit high on its side, it rolls: clockwise when hit from the left. A small hop lets it
                // pivot over its corner instead of grinding on it.
                var hit = (vp - vc) * _dashDir / _game.Tune.dashSpeed * _game.Tune.cartMass / cart.mass;
                cart.angularVelocity = -_dashDir * _game.Tune.rightingSpin * hit;
                cart.linearVelocity += new Vector2(0, 2 * hit);
            }
            Body.position = new Vector2(cart.position.x - _dashDir * reach, p.y);
            Body.linearVelocity = new Vector2(shared - _dashDir * 1.5f, Body.linearVelocity.y);
            _dashFor = 0;
            _ramming = false;
            Stagger = .3f;
            _game.Sound("ram", .2f, cart.mass);
        }
    }
}
