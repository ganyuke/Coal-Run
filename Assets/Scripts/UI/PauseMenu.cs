using CoalRun.Core;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace CoalRun.UI
{
    // Esc stops the game and shows what the keys do; Esc again carries on, Q leaves (not in a web build). Both are also
    // stamped buttons you can click. The card is the scene's `Pause UI`: restyle it there, and change its words in
    // Lines.asset (Pause).
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] private InputReader input;

        [Tooltip("The whole card, hidden until paused.")] [SerializeField]
        private GameObject card;

        [SerializeField] private TMP_Text title, controls;

        [Tooltip("The two stamped buttons, copies of the report's: clicking them is Esc and Q.")] [SerializeField]
        private Button resumeButton, quitButton;

        [SerializeField] private TMP_Text resumeLabel, quitLabel;

        public static bool Paused { get; private set; }

        // The speed the game was going at before, so a bot running fast comes back at that speed.
        private float _speed = 1;

        private void Start()
        {
            card.SetActive(false);
            input.Pause += Toggle;
            input.Quit += Quit;
        }

        private void OnDestroy()
        {
            if (input)
            {
                input.Pause -= Toggle;
                input.Quit -= Quit;
            }

            if (Paused) Resume();
        }

#if !UNITY_EDITOR
        // Clicking away from the game (another tab, another window) pauses it, so the building doesn't freeze while nobody
        // is looking. Left out in the editor, where the window is often unfocused on purpose (driven from the CLI).
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && !Paused && input) Toggle();
        }
#endif

        private void Toggle()
        {
            if (Paused)
            {
                Resume();
                return;
            }

            var say = ShiftManager.Instance.Lines.pause;
            title.text = say.title;
            controls.text = say.controls;
            resumeLabel.text = say.resume;
            quitLabel.text = say.quit;
            // Nothing to quit to in a web page.
            quitButton.gameObject.SetActive(Application.platform != RuntimePlatform.WebGLPlayer);
            _speed = Time.timeScale;
            Time.timeScale = 0;
            AudioListener.pause = true;
            Paused = true;
            card.SetActive(true);
        }

        public void Resume()
        {
            Time.timeScale = _speed;
            AudioListener.pause = false;
            Paused = false;
            if (card) card.SetActive(false);
        }

        public void Quit()
        {
            if (!Paused || Application.platform == RuntimePlatform.WebGLPlayer) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}