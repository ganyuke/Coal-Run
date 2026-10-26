using System;
using System.Collections.Generic;
using CoalRun.Core;
using CoalRun.Stations;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
        [Header("Sticky notes")]
        [Tooltip("Space between stacked notes, and how far each is shifted left of the one under it.")]
        [SerializeField]
        private float stickyGap = 2, stickyShift = 3;
        [Tooltip("The colour of a note by the key it's for, so the keys you can press are told apart at a glance. Anything else is the first colour.")]
        [SerializeField]
        private KeyColor[] keyColors =
        {
            new() { key = "E", color = new Color(1f, .91f, .45f) },
            new() { key = "Hold E", color = new Color(1f, .78f, .35f) },
            new() { key = "W", color = new Color(.62f, .85f, 1f) },
            new() { key = "S", color = new Color(.62f, .85f, 1f) },
            new() { key = "F", color = new Color(1f, .68f, .48f) },
            new() { key = "Space", color = new Color(.66f, .9f, .62f) },
            new() { key = "Shift", color = new Color(.8f, .71f, 1f) },
            new() { key = "Click", color = new Color(1f, .7f, .8f) },
            new() { key = "A/D", color = new Color(.58f, .92f, .88f) }
        };
        [Header("Where the book sits")]
        [Tooltip("Vertical position when shut: just the heading and the open page showing.")]
        [SerializeField]
        private float shutY = -244;
        [SerializeField] private float openY = 16;
        [Tooltip("When a page is written while it's shut, it hops up this far, once, to catch the eye. It never pops up and stays.")]
        [SerializeField]
        private float bounce = 14;
        [SerializeField] private float bounceTime = .45f;
        [SerializeField] private float slideSpeed = 9;
        [Tooltip("The book keeps to whichever side of the screen the operator isn't on.")]
        [SerializeField]
        private bool autoSide = true;
        [Tooltip("How close the operator can come to the book, in canvas units, before it moves across, and how long they must stay there.")]
        [SerializeField]
        private float sideMargin = 100, sideDelay = .35f;
        [Tooltip("Mouse travel, in pixels, before pressing the top edge becomes pulling the book up or down.")]
        [SerializeField]
        private float dragStart = 6;
        [Tooltip("The top part of the book that closes it again when clicked, in canvas units.")]
        [SerializeField]
        private float headerHeight = 64;
        [Header("The page")]
        [SerializeField] private int tickedShown = 3;
        [SerializeField] private int emptyShown = 2;
        [SerializeField] private float rowHeight = 38, currentHeight = 46;
        [SerializeField] private float tickedSize = 17, currentSize = 22;
        [SerializeField] private Color ink = new(.16f, .13f, .1f), fadedInk = new(.5f, .46f, .4f), urgentInk = new(.7f, .12f, .08f);

        [Serializable]
        public struct KeyColor
        {
            public string key;
            public Color color;
        }

        // The cursor is on the book, so clicks belong to it and not the room.
        public static bool PointerOver { get; private set; }

        private readonly List<NotebookRow> _rows = new();
        private readonly List<RectTransform> _stickies = new();
        private readonly List<TMP_Text> _stickyText = new();
        private readonly List<Image> _stickyImage = new();
        private bool _open, _dirty = true, _pressing, _dragging, _shownLast;
        private float _bounceAt = -10, _dimmed, _dragY, _pressY, _grab, _overlapFor;
        // Which side of the screen it's on: 1 right, -1 left.
        private int _side = 1;
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
        private void Written(Notebook.Page page)
        {
            _bounceAt = Time.time;
            ShiftManager.Instance.Sound("page", .3f);
        }

        private void Update()
        {
            var game = ShiftManager.Instance;
            var mouse = Mouse.current;
            // Between shifts, and once everything leading up to the report (the lift's door, a freezing) has played.
            var sheetMode = game.Phase == Phase.Briefing || game.Ended && game.ReportReady;
            // Nothing to write in until there's a shift to write about; the staff lift's door closes on it.
            var shown = (sheetMode || game.Phase is Phase.Tutorial or Phase.Shift or Phase.Leaving or Phase.Replacement) && !game.Departing;

            // Dim the room behind a briefing or report; the fade out of the staff lift is black too.
            var dimTarget = !sheetMode ? 0 : game.Phase == Phase.Briefing ? briefingDim : reportDim;
            _dimmed = Mathf.MoveTowards(_dimmed, dimTarget, Time.unscaledDeltaTime * 1.5f);
            dim.color = new Color(0, 0, 0, Mathf.Max(_dimmed, game.Fade));

            // The report and the briefing rise up from below the screen, whichever way they come.
            if (shown && !_shownLast && sheetMode)
            {
                var width = ((RectTransform)book.parent).rect.width;
                book.sizeDelta = sheetSize;
                book.anchoredPosition = new Vector2(-(width - sheetSize.x) / 2, -sheetSize.y - 20);
            }
            _shownLast = shown;
            book.gameObject.SetActive(shown);
            PointerOver = false;
            if (!shown) return;
            if (sheetMode && _sheetFor != game.Phase) Sheet(game);
            // Back to the checklist: its heading is the plain day.
            if (!sheetMode && _sheetFor != Phase.Shift) { _sheetFor = Phase.Shift; _dirty = true; }
            sheet.gameObject.SetActive(sheetMode);
            pageRoot.gameObject.SetActive(!sheetMode);

            var canvas = (RectTransform)book.parent;
            var canvasWidth = canvas.rect.width;
            if (mouse != null)
            {
                var screen = mouse.position.ReadValue();
                PointerOver = _pressing || RectTransformUtility.RectangleContainsScreenPoint(book, screen);
                if (sheetMode)
                {
                    if (PointerOver && mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(button, screen)) Press(game);
                }
                else
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var at);
                    var mouseY = at.y + canvas.rect.height / 2;
                    // Pressing the top edge (any of it while it's shut) either clicks it open and shut, or, moved
                    // far enough, pulls it up and down. Where it's let go is where it stays.
                    if (PointerOver && mouse.leftButton.wasPressedThisFrame)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(book, screen, null, out var local);
                        if (!_open || local.y > book.rect.yMax - headerHeight)
                        {
                            _pressing = true;
                            _dragging = false;
                            _pressY = screen.y;
                            _grab = book.anchoredPosition.y - mouseY;
                        }
                    }
                    if (_pressing)
                    {
                        if (mouse.leftButton.isPressed)
                        {
                            if (!_dragging && Mathf.Abs(screen.y - _pressY) > dragStart) _dragging = true;
                            if (_dragging) _dragY = Mathf.Clamp(mouseY + _grab, shutY, openY);
                        }
                        else
                        {
                            // Pulled past halfway it opens, pushed down past halfway it shuts: never left in between.
                            _open = _dragging ? _dragY > (shutY + openY) / 2 : !_open;
                            _pressing = _dragging = false;
                        }
                    }
                }
            }
            if (!sheetMode && Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) _open = !_open;

            Side(game, sheetMode, canvas);

            // Sliding up from the bottom, or growing into the middle of the screen.
            var size = sheetMode ? sheetSize : _bookSize;
            var margin = -_bookRest.x;
            var x = sheetMode ? -(canvasWidth - size.x) / 2 : _side > 0 ? -margin : -(canvasWidth - margin - size.x);
            // A new page hops the shut book up a little, once.
            var since = Time.time - _bounceAt;
            var hop = !_open && since < bounceTime ? bounce * Mathf.Sin(Mathf.PI * since / bounceTime) : 0;
            var y = sheetMode ? sheetY : _open ? openY : shutY + hop;
            var k = 1 - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime);
            book.sizeDelta = Vector2.Lerp(book.sizeDelta, size, k);
            var next = Vector2.Lerp(book.anchoredPosition, new Vector2(x, y), k);
            // Under the mouse while it's being pulled.
            if (_dragging) next.y = _dragY;
            book.anchoredPosition = next;

            if (sheetMode) { for (var i = 0; i < _stickies.Count; i++) _stickies[i].gameObject.SetActive(false); return; }
            if (_dirty) Rebuild();
            Stickies();
        }

        // Out of the operator's way: when they walk up to the book (on screen), it slides across to the other side
        // once they've stayed there a moment.
        private void Side(ShiftManager game, bool sheetMode, RectTransform canvas)
        {
            var cam = Camera.main;
            if (sheetMode || !autoSide || !cam || !player.Visible) { _overlapFor = 0; return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, cam.WorldToScreenPoint(player.Position), null, out var at);
            var operatorX = at.x + canvas.rect.width / 2;
            var operatorY = at.y + canvas.rect.height / 2;
            var margin = -_bookRest.x;
            var left = _side > 0 ? canvas.rect.width - margin - _bookSize.x : margin;
            var top = book.anchoredPosition.y + _bookSize.y;
            var near = operatorX > left - sideMargin && operatorX < left + _bookSize.x + sideMargin && operatorY < top + sideMargin;
            _overlapFor = near ? _overlapFor + Time.unscaledDeltaTime : 0;
            if (_overlapFor < sideDelay) return;
            _side = -_side;
            _overlapFor = 0;
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

            // The open page is always first, so it's where you saw it when the book was shut; the boxes for what's
            // still to come follow, and the ticked pages (newest first) are the log under them.
            var used = 0;
            var y = 0f;
            y = Row(ref used, y, current?.Text ?? "", false, currentHeight, currentSize, current is { Urgent: true } ? urgentInk : ink, true);
            for (var i = 0; i < emptyShown; i++) y = Row(ref used, y, "", false, rowHeight, tickedSize, fadedInk, false);
            for (var i = ticked.Count - 1; i >= 0; i--) y = Row(ref used, y, ticked[i].Text, true, rowHeight, tickedSize, fadedInk, false);
            for (var i = used; i < _rows.Count; i++) _rows[i].gameObject.SetActive(false);
        }

        private float Row(ref int used, float y, string text, bool done, float height, float size, Color color, bool fit)
        {
            if (used == _rows.Count)
            {
                var row = Instantiate(rowTemplate, pageRoot);
                _rows.Add(row);
            }
            var r = _rows[used++];
            r.gameObject.SetActive(true);
            r.Set(text, done, size, color);
            // The open page is as tall as what it says, however many lines that takes.
            if (fit) height = Mathf.Max(height, r.TextHeight + 16);
            r.Rect.anchoredPosition = new Vector2(0, -y);
            r.Rect.sizeDelta = new Vector2(r.Rect.sizeDelta.x, height);
            return y + height;
        }

        private Color KeyColorFor(string key)
        {
            foreach (var k in keyColors) if (k.key == key) return k.color;
            return keyColors.Length > 0 ? keyColors[0].color : Color.white;
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
                _stickyImage.Add(note.GetComponent<Image>());
            }
            for (var i = 0; i < _stickies.Count; i++)
            {
                var on = i < prompts.Count;
                _stickies[i].gameObject.SetActive(on);
                if (!on) continue;
                _stickyText[i].text = $"<b>[{prompts[i].key}]</b>  {prompts[i].label}";
                _stickyImage[i].color = KeyColorFor(prompts[i].key);
                var size = _stickies[i].sizeDelta;
                _stickies[i].anchoredPosition = new Vector2(stickyTemplate.anchoredPosition.x - i * stickyShift, stickyTemplate.anchoredPosition.y + i * (size.y + stickyGap));
            }
        }
    }
}
