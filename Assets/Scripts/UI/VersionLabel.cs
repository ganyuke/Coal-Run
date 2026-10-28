using TMPro;
using UnityEngine;

namespace CoalRun.UI
{
    // Writes the game's version (Player Settings, set by Tools/release.py) into its text, so a player can say which
    // build they played.
    [RequireComponent(typeof(TMP_Text))]
    public class VersionLabel : MonoBehaviour
    {
        private void Start() => GetComponent<TMP_Text>().text = "v" + Application.version;
    }
}
