using System.Collections;
using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // Hurt, death, respawn and riding the staff lift. (Part of PlayerController.)
    public partial class PlayerController
    {
        // `cause` is what the death message says if this is what kills them. A steady hurt (being crushed)
        // doesn't restart the stagger every step.
        public void Hurt(float amount, string cause = "Crushed by falling coal", bool stagger = true)
        {
            Injury += amount;
            _hurtBy = cause;
            Stagger = stagger ? .35f : Mathf.Max(Stagger, .2f);
            _game.Sound("hurt", .4f);
        }

        private string _hurtBy = "Crushed by falling coal";

        // Coal over the operator's head for five seconds kills; so does enough coal falling on them.
        private void CheckBurial(float dt)
        {
            if (Lane != 0) return;
            var p = Body.position;
            var depth = _game.Coal.GroundAt(p.x, p.y) - (p.y - .66f);
            if (Holding && depth > .35f)
            {
                _gripBlocked = true;
                ReleaseGrip();
                _box.excludeLayers = GameLayers.Mask(GameLayers.Cart);
                _game.Say(_game.Lines.speech.tooMuchCoal, 3);
            }
            Burial = depth > 1.5f ? Burial + dt : Mathf.Max(0, Burial - dt * 2);
            if (Injury > 2.5f) Stagger = .8f;
            if (Burial > 5 || Injury > 7) _game.Die(Burial > 5 ? "Buried under coal" : _hurtBy);
        }

        // Drops whatever was in hand, leaves the body where it stood (see Corpse), and takes the operator out of the
        // world until the replacement arrives (see Respawn). Returns the body.
        public Rigidbody2D Kill()
        {
            ThrowHands();
            ReleaseGrip();
            machines.Release();
            StopAllCoroutines();
            if (Voice) Voice.Hush();
            _flying = Climbing = _striking = false;
            _fit = 0;
            Body.bodyType = RigidbodyType2D.Dynamic;
            if (CarryingWheel) ReleaseWheel(Vector2.zero);
            Arms(false);
            var body = Corpse.Topple(Instantiate(visual.gameObject), "Dead operator", Body.position, Body.linearVelocity);
            Hide();
            return body;
        }

        // Out of the world until Respawn: dead, or still riding down the staff lift.
        public void Hide()
        {
            Body.simulated = false;
            visual.gameObject.SetActive(false);
        }

        // Locked in place for the ride out of the staff lift, brought to the middle of the car facing the room.
        public void Stand(float x)
        {
            StartCoroutine(Glide(x));
            ReleaseGrip();
            Body.linearVelocity = Vector2.zero;
            Body.constraints = RigidbodyConstraints2D.FreezeAll;
            // Behind the door, which is about to close in front of them.
            if (visual) visual.sortingOrder = liftOrder;
        }

        private IEnumerator Glide(float x)
        {
            Facing = -1;
            var from = Body.position;
            for (float k = 0; k < 1; k += Time.deltaTime / .35f)
            {
                Body.position = new Vector2(Mathf.Lerp(from.x, x, Mathf.SmoothStep(0, 1, k)), from.y);
                yield return null;
            }
            Body.position = new Vector2(x, from.y);
        }

        // Riding down the staff lift: standing in the car behind the closed door, so they're already there
        // when it opens, but out of the world until Respawn.
        public void WaitInLift(Vector2 at)
        {
            Respawn(at);
            Body.simulated = false;
            if (visual) visual.sortingOrder = liftOrder;
        }

        public void Respawn(Vector2 at)
        {
            Body.simulated = true;
            visual.gameObject.SetActive(true);
            Facing = -1;
            ReleaseGrip();
            machines.Release();
            StopAllCoroutines();
            _flying = Climbing = false;
            Body.bodyType = RigidbodyType2D.Dynamic;
            if (CarryingWheel) ReleaseWheel(Vector2.zero);
            Hands = 0;
            Injury = Burial = Stagger = 0;
            Lane = 0;
            if (visual) visual.sortingOrder = frontOrder;
            Body.position = at;
            transform.position = at;
            Body.linearVelocity = Vector2.zero;
        }
    }
}
