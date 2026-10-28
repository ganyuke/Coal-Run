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
    // The notebook on screen: a book hanging from the top right of the screen, shut to its heading and the page
    // that matters. Click it and it drops open; click its top edge and it folds back up. It stays put: the operator
    // never gets up that high. The current page is big, a few empty boxes wait under it for whatever comes next,
    // and past pages are ticked below those. The key prompts are sticky notes hanging under it, so they show even
    // while it's shut. Not a screen of its
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
        [Tooltip("How much of the book shows when it's shut, at least: it grows to fit the heading and the whole open page.")]
        [SerializeField]
        private float shutHeight = 120;
        [Tooltip("Gap between the top of the screen and the book.")]
        [SerializeField]
        private float topGap;
        [Tooltip("When a page is written while it's shut, it drops this much further, once, to catch the eye. It never drops open and stays.")]
        [SerializeField]
        private float bounce = 14;
        [SerializeField] private float bounceTime = .45f;
        [SerializeField] private float slideSpeed = 9;
        [Tooltip("Mouse travel, in pixels, before pressing the top edge becomes pulling the book open or shut.")]
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
        private float _bounceAt = -10, _dimmed, _dragHeight, _pressY, _grab;
        // How tall the shut book has to be to show the open page whole.
        private float _shutFit;
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
                var rect = ((RectTransform)book.parent).rect;
                book.sizeDelta = sheetSize;
                book.anchoredPosition = new Vector2(-(rect.width - sheetSize.x) / 2, -rect.height - 20);
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
            var canvasHeight = canvas.rect.height;
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
                    // How far down the screen the mouse is.
                    var mouseDown = canvasHeight / 2 - at.y;
                    // Pressing the top edge (any of it while it's shut) either clicks it open and shut, or, moved
                    // far enough, pulls it down and up. Where it's let go is where it stays.
                    if (PointerOver && mouse.leftButton.wasPressedThisFrame)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(book, screen, null, out var local);
                        if (!_open || local.y > book.rect.yMax - headerHeight)
                        {
                            _pressing = true;
                            _dragging = false;
                            _pressY = screen.y;
                            _grab = book.sizeDelta.y - mouseDown;
                        }
                    }
                    if (_pressing)
                    {
                        if (mouse.leftButton.isPressed)
                        {
                            if (!_dragging && Mathf.Abs(screen.y - _pressY) > dragStart) _dragging = true;
                            if (_dragging) _dragHeight = Mathf.Clamp(mouseDown + _grab, Mathf.Max(shutHeight, _shutFit), _bookSize.y);
                        }
                        else
                        {
                            // Pulled past halfway it opens, pushed back past halfway it shuts: never left in between.
                            _open = _dragging ? _dragHeight > (Mathf.Max(shutHeight, _shutFit) + _bookSize.y) / 2 : !_open;
                            _pressing = _dragging = false;
                        }
                    }
                }
            }
            if (!sheetMode && Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) _open = !_open;

            // Dropping open from the top, or growing into the middle of the screen. Shut, it's cut short: the page
            // masks whatever's below the fold.
            var since = Time.time - _bounceAt;
            // A new page drops the shut book a little further, once.
            var hop = !_open && since < bounceTime ? bounce * Mathf.Sin(Mathf.PI * since / bounceTime) : 0;
            var shut = Mathf.Max(shutHeight, _shutFit);
            var size = sheetMode ? sheetSize : new Vector2(_bookSize.x, _open ? _bookSize.y : shut + hop);
            var x = sheetMode ? -(canvasWidth - size.x) / 2 : _bookRest.x;
            var y = sheetMode ? -(canvasHeight - sheetY - size.y) : -topGap;
            var k = 1 - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime);
            var nextSize = Vector2.Lerp(book.sizeDelta, size, k);
            // Under the mouse while it's being pulled.
            if (_dragging) nextSize.y = _dragHeight;
            book.sizeDelta = nextSize;
            book.anchoredPosition = Vector2.Lerp(book.anchoredPosition, new Vector2(x, y), k);

            if (sheetMode) { for (var i = 0; i < _stickies.Count; i++) _stickies[i].gameObject.SetActive(false); return; }
            if (_dirty) Rebuild();
            Stickies();
        }

        // Begin the shift, or run it again: what the stamped button does, like Enter and R.
        private static void Press(ShiftManager game)
        {
            if (game.Phase == Phase.Briefing) game.BeginShift();
            else if (game.Ended) game.Continue();
        }

        // What's written on the page between shifts.
        private void Sheet(ShiftManager game)
        {
            _sheetFor = game.Phase;
            heading.text = $"DAY {game.Day.day:00} · {(game.Phase == Phase.Briefing ? "BRIEFING" : "REPORT")}";
            if (game.Phase == Phase.Briefing)
            {
                sheetHeadline.text = "Keep the building warm until the blizzard passes.";
                sheetBody.text = (string.IsNullOrEmpty(game.Day.memo) ? "" : $"<i>{game.Day.memo}</i>\n\n") +
                    "Haul coal from the chute to the furnace. Feed it, light it, and set the burn lever: the colder it gets outside, the harder it has to burn." +
                    $"\n\nElectrical output target\n<b>{game.Day.target:0} kWh</b>\n\nThe cart has {game.Cart.Wheels} wheel{(game.Cart.Wheels == 1 ? "" : "s")}.";
                buttonLabel.text = "Begin the shift   [Enter]";
                return;
            }
            var complete = game.Phase == Phase.Complete;
            sheetHeadline.text = complete ? "You kept them alive." : "No one is coming down.";
            sheetBody.text =
                (complete ? (game.Furnace.Output >= game.Day.target ? "Brass is pleased with the output." : "Brass is not pleased with the output.") + (game.HasNextDay ? " A memo is waiting for you in the morning." : " That is all the shifts there are, for now.") : "The offices froze first. There is nobody left to send a replacement.") +
                $"\n\nElectrical output\n<b>{game.Furnace.Output:0.0} / {game.Day.target:0} kWh</b>\n\nCoal delivered  <b>{game.Furnace.Delivered:0.0}</b>\nOperators lost  <b>{game.Deaths}</b>";
            buttonLabel.text = !complete ? "Run the shift again   [R]" : game.HasNextDay ? "Next day   [R]" : "Play again from Day 1   [R]";
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
            // Down to the end of the open page, from the top of the book: the page's inset, the heading, the page.
            var inset = (book.rect.height - ((RectTransform)pageRoot.parent).rect.height) / 2;
            _shutFit = inset - pageRoot.anchoredPosition.y + y + inset;
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

        // One sticky note a prompt, hanging down from the bottom edge of the book, each turned a little.
        private void Stickies()
        {
            var prompts = player.Prompts();
            while (_stickies.Count < prompts.Count)
            {
                var note = Instantiate(stickyTemplate, stickyTemplate.parent);
                note.localRotation = Quaternion.Euler(0, 0, (_stickies.Count % 2 == 0 ? 1.2f : -1.2f) * (1 + _stickies.Count % 2));
                _stickies.Add(note);
                // A long label shrinks to fit its note rather than wrapping out of it.
                var label = note.GetComponentInChildren<TMP_Text>();
                label.enableAutoSizing = true;
                label.fontSizeMax = label.fontSize;
                label.fontSizeMin = label.fontSize * .6f;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                _stickyText.Add(label);
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
                _stickies[i].anchoredPosition = new Vector2(stickyTemplate.anchoredPosition.x - i * stickyShift, stickyTemplate.anchoredPosition.y - i * (size.y + stickyGap + 5));
            }
        }
    }
}
