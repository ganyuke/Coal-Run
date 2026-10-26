using System.Collections.Generic;
using CoalRun.Core;
using CoalRun.Stations;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoalRun.UI
{
    // The notebook on screen: a book resting at the bottom right with its top edge showing. Click it and it
    // slides up, open to the page that matters; click its top edge and it slides down. Past pages are ticked
    // above it, the current page is big, and a few empty boxes wait below for whatever comes next. The key
    // prompts are sticky notes stacked on top of it, so they show even while it's shut. Not a screen of its
    // own: the game runs on underneath. Between shifts it's the briefing and the report too: it opens bigger in
    // the middle of the screen, on a page with a stamped button.
    [DefaultExecutionOrder(-200)] // Before InputReader, so a click on the book isn't also a click in the room.
    public class NotebookView : MonoBehaviour
    {
        [SerializeField] private Notebook notebook;
        [SerializeField] private PlayerController player;
        [SerializeField] private RectTransform book;
        [SerializeField] private TMP_Text heading;
        [Tooltip("Rows are cloned from this and laid out down the page.")]
        [SerializeField]
        private NotebookRow rowTemplate;
        [SerializeField] private RectTransform pageRoot;
        [Tooltip("Cloned for each key prompt, stacked above the book.")]
        [SerializeField]
        private RectTransform stickyTemplate;
        [Header("Briefing and report")]
        [Tooltip("The page's contents while it's a briefing or a report, in place of the checklist.")]
        [SerializeField]
        private RectTransform sheet;
        [SerializeField] private TMP_Text sheetHeadline, sheetBody, buttonLabel;
        [Tooltip("Clicked to begin the shift or run it again.")]
        [SerializeField]
        private RectTransform button;
        [Tooltip("Black behind the book: it dims the room under a briefing or report, and is the fade out of the staff lift.")]
        [SerializeField]
        private UnityEngine.UI.Graphic dim;
        [SerializeField] private Vector2 sheetSize = new(520, 560);
        [SerializeField] private float sheetY = 80;
        [SerializeField] private float briefingDim = .55f, reportDim = .8f;
        [Header("Where the book sits")]
        [Tooltip("Vertical position when shut, with just the top edge showing.")]
        [SerializeField]
        private float shutY = -330;
        [SerializeField] private float openY = 20;
        [Tooltip("How far it pops up by itself when a new page is written while it's shut.")]
        [SerializeField]
        private float nudge = 70;
        [SerializeField] private float nudgeTime = 2.5f;
        [SerializeField] private float slideSpeed = 9;
        [Tooltip("The top part of the book that closes it again when clicked, in canvas units.")]
        [SerializeField]
        private float headerHeight = 64;
        [Header("The page")]
        [SerializeField] private int tickedShown = 3;
        [SerializeField] private int emptyShown = 2;
        [SerializeField] private float rowHeight = 46, currentHeight = 86;
        [SerializeField] private Color ink = new(.16f, .13f, .1f), fadedInk = new(.5f, .46f, .4f), urgentInk = new(.7f, .12f, .08f);

        // The cursor is on the book, so clicks belong to it and not the room.
        public static bool PointerOver { get; private set; }

        private readonly List<NotebookRow> _rows = new();
        private readonly List<RectTransform> _stickies = new();
        private readonly List<TMP_Text> _stickyText = new();
        private bool _open, _dirty = true;
        private float _nudgeUntil, _dimmed;
        private Vector2 _bookSize, _bookRest;
        private Phase _sheetFor = Phase.Shift;

        private void OnEnable()
        {
            notebook.OnChanged += MarkDirty;
            notebook.OnWritten += Written;
        }

        private void OnDisable()
        {
            notebook.OnChanged -= MarkDirty;
            notebook.OnWritten -= Written;
            PointerOver = false;
        }

        private void Awake()
        {
            _bookSize = book.sizeDelta;
            _bookRest = book.anchoredPosition;
        }

        private void MarkDirty() => _dirty = true;
        private void Written(Notebook.Page page) => _nudgeUntil = Time.time + nudgeTime;

        private void Update()
        {
            var game = ShiftManager.Instance;
            var mouse = Mouse.current;
            var sheetMode = game.Phase == Phase.Briefing || game.Ended;
            // Nothing to write in until there's a shift to write about; the staff lift's door closes on it.
            var shown = (sheetMode || game.Phase is Phase.Tutorial or Phase.Shift or Phase.Leaving or Phase.Replacement) && !game.Departing;

            // Dim the room behind a briefing or report; the fade out of the staff lift is black too.
            var dimTarget = !sheetMode ? 0 : game.Phase == Phase.Briefing ? briefingDim : reportDim;
            _dimmed = Mathf.MoveTowards(_dimmed, dimTarget, Time.unscaledDeltaTime * 1.5f);
            dim.color = new Color(0, 0, 0, Mathf.Max(_dimmed, game.Fade));

            book.gameObject.SetActive(shown);
            PointerOver = false;
            if (!shown) return;
            if (sheetMode && _sheetFor != game.Phase) Sheet(game);
            // Back to the checklist: its heading is the plain day.
            if (!sheetMode && _sheetFor != Phase.Shift) { _sheetFor = Phase.Shift; _dirty = true; }
            sheet.gameObject.SetActive(sheetMode);
            pageRoot.gameObject.SetActive(!sheetMode);

            if (mouse != null)
            {
                var screen = mouse.position.ReadValue();
                PointerOver = RectTransformUtility.RectangleContainsScreenPoint(book, screen);
                if (PointerOver && mouse.leftButton.wasPressedThisFrame)
                {
                    if (sheetMode)
                    {
                        if (RectTransformUtility.RectangleContainsScreenPoint(button, screen)) Press(game);
                    }
                    else
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(book, screen, null, out var local);
                        if (!_open) _open = true;
                        else if (local.y > book.rect.yMax - headerHeight) _open = false;
                    }
                }
            }

            // Sliding up from the bottom, or growing into the middle of the screen.
            var canvasWidth = ((RectTransform)book.parent).rect.width;
            var size = sheetMode ? sheetSize : _bookSize;
            var x = sheetMode ? -(canvasWidth - size.x) / 2 : _bookRest.x;
            var y = sheetMode ? sheetY : _open ? openY : Time.time < _nudgeUntil ? shutY + nudge : shutY;
            var k = 1 - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime);
            book.sizeDelta = Vector2.Lerp(book.sizeDelta, size, k);
            book.anchoredPosition = Vector2.Lerp(book.anchoredPosition, new Vector2(x, y), k);

            if (sheetMode) { for (var i = 0; i < _stickies.Count; i++) _stickies[i].gameObject.SetActive(false); return; }
            if (_dirty) Rebuild();
            Stickies();
        }

        // Begin the shift, or run it again: what the stamped button does, like Enter and R.
        private static void Press(ShiftManager game)
        {
            if (game.Phase == Phase.Briefing) game.BeginShift();
            else if (game.Ended) game.Restart();
        }

        // What's written on the page between shifts.
        private void Sheet(ShiftManager game)
        {
            _sheetFor = game.Phase;
            heading.text = $"DAY {game.Day.day:00} · {(game.Phase == Phase.Briefing ? "BRIEFING" : "REPORT")}";
            if (game.Phase == Phase.Briefing)
            {
                sheetHeadline.text = "Keep the building warm until the blizzard passes.";
                sheetBody.text =
                    "Haul coal from the chute to the furnace. Feed it, light it, and set the burn lever: the colder it gets outside, the harder it has to burn." +
                    $"\n\nElectrical output target\n<b>{game.Day.target:0} kWh</b>\n\nThe cart has {game.Cart.Wheels} wheel{(game.Cart.Wheels == 1 ? "" : "s")}.";
                buttonLabel.text = "Begin the shift   [Enter]";
                return;
            }
            var complete = game.Phase == Phase.Complete;
            sheetHeadline.text = complete ? "You kept them alive." : "No one is coming down.";
            sheetBody.text =
                (complete ? "The shift is over. The furnace is still your problem tomorrow." : "The offices froze first. There is nobody left to send a replacement.") +
                $"\n\nElectrical output\n<b>{game.Furnace.Output:0.0} / {game.Day.target:0} kWh</b>\n\nCoal delivered  <b>{game.Furnace.Delivered:0.0}</b>\nOperators lost  <b>{game.Deaths}</b>";
            buttonLabel.text = "Run the shift again   [R]";
        }

        // Ticked pages above, the open one big, then empty boxes for what's still to come.
        private void Rebuild()
        {
            _dirty = false;
            heading.text = $"DAY {ShiftManager.Instance.Day.day:00}";
            var pages = notebook.Pages;
            var current = notebook.Current;
            var ticked = new List<Notebook.Page>();
            foreach (var p in pages) if (p.Done) ticked.Add(p);
            if (ticked.Count > tickedShown) ticked.RemoveRange(0, ticked.Count - tickedShown);

            var used = 0;
            var y = 0f;
            foreach (var p in ticked) y = Row(ref used, y, p.Text, true, rowHeight, 20, fadedInk);
            if (current != null) y = Row(ref used, y, current.Text, false, currentHeight, 26, current.Urgent ? urgentInk : ink);
            for (var i = 0; i < emptyShown; i++) y = Row(ref used, y, "", false, rowHeight, 20, fadedInk);
            for (var i = used; i < _rows.Count; i++) _rows[i].gameObject.SetActive(false);
        }

        private float Row(ref int used, float y, string text, bool done, float height, float size, Color color)
        {
            if (used == _rows.Count)
            {
                var row = Instantiate(rowTemplate, pageRoot);
                _rows.Add(row);
            }
            var r = _rows[used++];
            r.gameObject.SetActive(true);
            r.Set(text, done, size, color);
            r.Rect.anchoredPosition = new Vector2(0, -y);
            r.Rect.sizeDelta = new Vector2(r.Rect.sizeDelta.x, height);
            return y + height;
        }

        // One sticky note a prompt, stacked up from the top edge of the book, each turned a little.
        private void Stickies()
        {
            var prompts = player.Prompts();
            while (_stickies.Count < prompts.Count)
            {
                var note = Instantiate(stickyTemplate, stickyTemplate.parent);
                note.localRotation = Quaternion.Euler(0, 0, (_stickies.Count % 2 == 0 ? 1.5f : -1.5f) * (1 + _stickies.Count % 3));
                _stickies.Add(note);
                _stickyText.Add(note.GetComponentInChildren<TMP_Text>());
            }
            for (var i = 0; i < _stickies.Count; i++)
            {
                var on = i < prompts.Count;
                _stickies[i].gameObject.SetActive(on);
                if (!on) continue;
                _stickyText[i].text = $"<b>[{prompts[i].key}]</b>  {prompts[i].label}";
                var size = _stickies[i].sizeDelta;
                _stickies[i].anchoredPosition = new Vector2(stickyTemplate.anchoredPosition.x - i * 6, stickyTemplate.anchoredPosition.y + i * (size.y + 4));
            }
        }
    }
}
