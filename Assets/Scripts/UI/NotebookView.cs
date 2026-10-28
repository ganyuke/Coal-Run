using System;
using CoalRun.Core;
using CoalRun.Stations;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoalRun.UI
{
    // The notebook on screen: a slip hanging from the top right of the screen with the day and the one task that
    // matters now. When it's done it's ticked and held a moment so you see it, then the next task takes its place.
    // The key prompts are sticky notes hanging under the slip. Not a screen of its own: the game runs on underneath. Between shifts it's the briefing and the report: it opens bigger in
    // the middle of the screen, on a page with a stamped button.
    [DefaultExecutionOrder(-200)] // Before InputReader, so a click on the briefing isn't also a click in the room.
    public class NotebookView : MonoBehaviour
    {
        [SerializeField] private Notebook notebook;
        [SerializeField] private PlayerController player;
        [SerializeField] private RectTransform book;
        [Tooltip("The day, or on the briefing and the report what it is. Smaller over a task.")]
        [SerializeField]
        private TMP_Text heading;
        [SerializeField] private RectTransform rule;
        [Tooltip("The task's line.")]
        [SerializeField]
        private NotebookRow row;
        [SerializeField] private RectTransform pageRoot;
        [Header("Briefing and report")]
        [Tooltip("The page's contents while it's a briefing or a report, in place of the task.")]
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
        [Header("The task")]
        [Tooltip("Seconds a finished task stays up, ticked, before the next one takes its place.")]
        [SerializeField]
        private float tickHold = 1.2f;
        [SerializeField] private float slideSpeed = 9;
        [SerializeField] private float rowHeight = 36, textSize = 22;
        [Tooltip("The day's heading over a task, in place of the big one on the briefing and report.")]
        [SerializeField]
        private float headingSize = 14;
        [Tooltip("Paper left under the task.")]
        [SerializeField]
        private float bottomPadding = 8;
        [SerializeField] private Color ink = new(.16f, .13f, .1f), urgentInk = new(.7f, .12f, .08f);
        [Header("Sticky notes")]
        [Tooltip("The key prompt notes hanging under the slip (children of the Book, so they go with it), first prompt on top, laid out in the scene. There are never more than three prompts.")]
        [SerializeField]
        private Image[] stickies;
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

        [Serializable]
        public struct KeyColor
        {
            public string key;
            public Color color;
        }

        // The cursor is on the briefing or report, so clicks belong to it and not the room.
        public static bool PointerOver { get; private set; }

        private Notebook.Page _showing;
        private bool _shownLast, _drawnDone, _bigNow = true;
        private float _dimmed, _tickedAt = -1, _width, _bigHeading;
        // Where the heading, rule and task sit from the top of the page on the big sheet; over a task they're
        // pulled up to suit the smaller heading.
        private float _headingHeight, _ruleY, _rootY;
        private Phase _sheetFor = Phase.Shift;
        private static readonly System.Collections.Generic.List<(string key, string label)> NoPrompts = new();

        private void OnDisable() => PointerOver = false;

        private void Awake()
        {
            _width = book.sizeDelta.x;
            _bigHeading = heading.fontSize;
            _headingHeight = heading.rectTransform.sizeDelta.y;
            _ruleY = rule.anchoredPosition.y;
            _rootY = pageRoot.anchoredPosition.y;
        }

        private void Update()
        {
            var game = ShiftManager.Instance;
            // Between shifts, and once everything leading up to the report (the lift's door, a freezing) has played.
            var sheetMode = game.Phase == Phase.Briefing || game.Ended && game.ReportReady;
            if (!sheetMode) Follow();
            // Nothing to write in until there's a shift to write about; the staff lift's door closes on it.
            var playing = game.Phase is Phase.Tutorial or Phase.Shift or Phase.Leaving or Phase.Replacement;
            var shown = (sheetMode || playing && _showing != null) && !game.Departing;

            // Dim the room behind a briefing or report; the fade out of the staff lift is black too.
            var dimTarget = !sheetMode ? 0 : game.Phase == Phase.Briefing ? briefingDim : reportDim;
            _dimmed = Mathf.MoveTowards(_dimmed, dimTarget, Time.unscaledDeltaTime * 1.5f);
            dim.color = new Color(0, 0, 0, Mathf.Max(_dimmed, game.Fade));

            var canvas = ((RectTransform)book.parent).rect;
            // Hanging from the top right, as tall as the task; or grown into the middle of the screen.
            var inset = (book.rect.height - ((RectTransform)pageRoot.parent).rect.height) / 2;
            var size = sheetMode ? sheetSize : new Vector2(_width, inset - pageRoot.anchoredPosition.y + row.Rect.sizeDelta.y + bottomPadding + inset);
            var at = sheetMode ? new Vector2(-(canvas.width - size.x) / 2, -(canvas.height - sheetY - size.y)) : new Vector2(-30, 0);
            // Coming into view, it starts off screen at its full size: the report and the briefing rise up from below,
            // the task drops down from above.
            if (shown && !_shownLast)
            {
                book.sizeDelta = size;
                book.anchoredPosition = sheetMode ? new Vector2(at.x, -canvas.height - 20) : new Vector2(at.x, size.y + 20);
            }
            _shownLast = shown;
            book.gameObject.SetActive(shown);
            Stickies(playing && !sheetMode && !game.Departing);
            PointerOver = false;
            if (!shown) return;
            if (sheetMode && _sheetFor != game.Phase) Sheet(game);
            if (!sheetMode)
            {
                _sheetFor = Phase.Shift;
                if (_bigNow) Heading(false);
            }
            sheet.gameObject.SetActive(sheetMode);
            pageRoot.gameObject.SetActive(!sheetMode);

            var mouse = Mouse.current;
            if (sheetMode && mouse != null)
            {
                var screen = mouse.position.ReadValue();
                PointerOver = RectTransformUtility.RectangleContainsScreenPoint(book, screen);
                if (PointerOver && mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(button, screen)) Press(game);
            }

            var k = 1 - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime);
            book.sizeDelta = Vector2.Lerp(book.sizeDelta, size, k);
            book.anchoredPosition = Vector2.Lerp(book.anchoredPosition, at, k);

        }

        // The heading big for the briefing and report, or small over the task with everything under it pulled up.
        private void Heading(bool big)
        {
            _bigNow = big;
            var k = big ? 1 : headingSize / _bigHeading;
            heading.fontSize = big ? _bigHeading : headingSize;
            if (!big) heading.text = string.Format(ShiftManager.Instance.Lines.sheets.day, ShiftManager.Instance.Day.day);
            var h = heading.rectTransform;
            h.sizeDelta = new Vector2(h.sizeDelta.x, _headingHeight * k);
            var top = -h.anchoredPosition.y;
            rule.anchoredPosition = new Vector2(rule.anchoredPosition.x, -(top + (-_ruleY - top) * k));
            pageRoot.anchoredPosition = new Vector2(pageRoot.anchoredPosition.x, -(top + (-_rootY - top) * k));
        }

        // Which task is up: the notebook's current one, except that one just finished stays a moment, ticked.
        private void Follow()
        {
            var current = notebook.Current;
            if (_showing != current)
            {
                if (_showing is { Done: true })
                {
                    if (_tickedAt < 0) _tickedAt = Time.time;
                    if (Time.time - _tickedAt >= tickHold) Show(current);
                }
                else Show(current);
            }
            if (_showing == null || _showing.Done == _drawnDone) return;
            _drawnDone = _showing.Done;
            Draw();
        }

        private void Show(Notebook.Page page)
        {
            _showing = page;
            _tickedAt = -1;
            if (page == null) return;
            _drawnDone = page.Done;
            Draw();
            ShiftManager.Instance.Sound("page", .3f);
        }

        private void Draw()
        {
            row.gameObject.SetActive(true);
            row.Set(_showing.Text, _showing.Done, textSize, _showing.Urgent ? urgentInk : ink);
            row.Rect.anchoredPosition = Vector2.zero;
            row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, Mathf.Max(rowHeight, row.TextHeight + 4));
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
            Heading(true);
            var t = game.Lines.sheets;
            heading.text = $"{string.Format(t.day, game.Day.day)} · {(game.Phase == Phase.Briefing ? t.briefing : t.report)}";
            if (game.Phase == Phase.Briefing)
            {
                sheetHeadline.text = t.briefingHeadline;
                sheetBody.text = (string.IsNullOrEmpty(game.Day.memo) ? "" : $"<i>{game.Day.memo}</i>\n\n") + t.briefingBody +
                    $"\n\n{t.targetLabel}\n<b>{game.Day.target:0} kWh</b>\n\n{(game.Cart.Wheels == 1 ? t.oneWheel : string.Format(t.wheels, game.Cart.Wheels))}";
                buttonLabel.text = t.begin;
                return;
            }
            var complete = game.Phase == Phase.Complete;
            sheetHeadline.text = complete ? t.survived : t.lost;
            sheetBody.text =
                (complete ? (game.Furnace.Output >= game.Day.target ? t.pleased : t.notPleased) + " " + (game.HasNextDay ? t.memoWaiting : t.lastShift) : t.frozeOver) +
                $"\n\n{t.outputLabel}\n<b>{game.Furnace.Output:0.0} / {game.Day.target:0} kWh</b>\n\n{t.deliveredLabel}  <b>{game.Furnace.Delivered:0.0}</b>\n{t.lostLabel}  <b>{game.Deaths}</b>";
            buttonLabel.text = !complete ? t.again : game.HasNextDay ? t.nextDay : t.fromDay1;
        }

        private Color KeyColorFor(string key)
        {
            foreach (var k in keyColors) if (k.key == key) return k.color;
            return keyColors.Length > 0 ? keyColors[0].color : Color.white;
        }

        // The key prompts on the notes, in order; the notes left over are hidden, and all of them while `show` is off.
        private void Stickies(bool show)
        {
            var prompts = show ? player.Prompts() : NoPrompts;
            for (var i = 0; i < stickies.Length; i++)
            {
                var on = i < prompts.Count;
                stickies[i].gameObject.SetActive(on);
                if (!on) continue;
                stickies[i].GetComponentInChildren<TMP_Text>().text = $"<b>[{prompts[i].key}]</b>  {prompts[i].label}";
                stickies[i].color = KeyColorFor(prompts[i].key);
            }
        }
    }
}
