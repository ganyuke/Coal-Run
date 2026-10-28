using CoalRun.Core;
using UnityEngine;

namespace CoalRun.Stations
{
    // Day 2's beat: engineering's installer stands in front of the cart, in the way it has to be hauled, and hands over the
    // other axle (the cart already has both wheels). Push the cart into him and he is the first thing the new axle runs over;
    // haul it round him and he sees himself out by the staff lift. Every installer dies, so most players will find out which.
    public class AxleInstaller : MonoBehaviour
    {
        [Tooltip("The installer, a clone of the training helper. Hidden unless the day has an installer.")]
        [SerializeField]
        private GameObject installer;
        [SerializeField] private Speaker voice;
        [Tooltip("The cart has to be moving at least this fast into him to count.")]
        [SerializeField]
        private float hitSpeed = 1.2f;
        [SerializeField] private float hitReach = 1.35f;
        [SerializeField] private float exitSpeed = 2.2f;

        private ShiftManager _game;
        private bool _met, _left;
        private float _talkAt;

        private void Start()
        {
            _game = ShiftManager.Instance;
            if (!_game.Day.installer || _game.InTraining)
            {
                installer.SetActive(false);
                enabled = false;
                return;
            }
            _game.OnPhase += Phase;
        }

        private void OnDestroy()
        {
            if (_game) _game.OnPhase -= Phase;
        }

        private void Phase(Phase phase)
        {
            if (phase != Core.Phase.Shift || _met) return;
            _met = true;
            _game.Say(_game.Lines.training.installerArrives, 9, voice);
        }

        private void FixedUpdate()
        {
            if (!installer.activeSelf || !_met) return;
            var cart = _game.Cart.Body;
            var at = (Vector2)installer.transform.position;
            var gap = cart.position.x - at.x;
            if (_left)
            {
                installer.transform.position = Vector3.MoveTowards(installer.transform.position, new Vector3(Room.StaffLiftCenter, at.y, 0), exitSpeed * Time.fixedDeltaTime);
                if (Mathf.Abs(installer.transform.position.x - Room.StaffLiftCenter) < .1f) installer.SetActive(false);
                return;
            }
            if (Mathf.Abs(gap) < hitReach && Mathf.Abs(cart.position.y - at.y) < 1.2f && cart.linearVelocity.x * -Mathf.Sign(gap) > hitSpeed)
            {
                var art = installer.transform.GetChild(0).gameObject;
                _game.Bodies.Add(Corpse.Topple(art, "Dead installer", at, new Vector2(cart.linearVelocity.x, 2.5f)));
                installer.SetActive(false);
                _game.Sound("hurt");
                _game.Say("“Huh. It rolls.”", 4);
                return;
            }
            if (Mathf.Abs(gap) > 6)
            {
                _left = true;
                _game.Say(_game.Lines.training.installerRunOver, 5, voice);
            }
        }
    }
}
