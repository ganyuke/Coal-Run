using CoalRun.Core;
using CoalRun.Stations;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoalRun.UI
{
    // The notebook on screen: a slip hanging from the top right of the screen with the one task that matters now.
    // When it's done it's ticked and held a moment so you see it, then the next task takes its place. Not a screen
    // of its own: the game runs on underneath. Between shifts it's the briefing and the report: it opens bigger in
    // the middle of the screen, on a page with a stamped button.
    [DefaultExecutionOrder(-200)] // Before InputReader, so a click on the briefing isn't also a click in the room.
    public class NotebookView : MonoBehaviour
    {
        [SerializeField] private Notebook notebook;
        [SerializeField] private RectTransform book;
        [Tooltip("The heading and the rule under it, only on the briefing and the report.")]
        [SerializeField]
        private TMP_Text heading;
        [SerializeField] private GameObject rule;
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
        [SerializeField] private float rowHeight = 46, textSize = 22;
        [SerializeField] private Color ink = new(.16f, .13f, .1f), urgentInk = new(.7f, .12f, .08f);

        // The cursor is on the briefing or report, so clicks belong to it and not the room.
        public static bool PointerOver { get; private set; }

        private Notebook.Page _showing;
        private bool _shownLast, _drawnDone;
        private float _dimmed, _tickedAt = -1, _width;
        private Phase _sheetFor = Phase.Shift;

        private void OnDisable() => PointerOver = false;

        private void Awake() => _width = book.sizeDelta.x;

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
            // The report and the briefing rise up from below the screen, whichever way they come.
            if (shown && !_shownLast && sheetMode)
            {
                book.sizeDelta = sheetSize;
                book.anchoredPosition = new Vector2(-(canvas.width - sheetSize.x) / 2, -canvas.height - 20);
            }
            _shownLast = shown;
            book.gameObject.SetActive(shown);
            PointerOver = false;
            if (!shown) return;
            if (sheetMode && _sheetFor != game.Phase) Sheet(game);
            if (!sheetMode) _sheetFor = Phase.Shift;
            sheet.gameObject.SetActive(sheetMode);
            heading.gameObject.SetActive(sheetMode);
            rule.SetActive(sheetMode);
            pageRoot.gameObject.SetActive(!sheetMode);

            var mouse = Mouse.current;
            if (sheetMode && mouse != null)
            {
                var screen = mouse.position.ReadValue();
                PointerOver = RectTransformUtility.RectangleContainsScreenPoint(book, screen);
                if (PointerOver && mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(button, screen)) Press(game);
            }

            // Hanging from the top right, as tall as the task; or grown into the middle of the screen.
            var inset = (book.rect.height - ((RectTransform)pageRoot.parent).rect.height) / 2;
            var size = sheetMode ? sheetSize : new Vector2(_width, inset - pageRoot.anchoredPosition.y + row.Rect.sizeDelta.y + inset);
            var at = sheetMode ? new Vector2(-(canvas.width - size.x) / 2, -(canvas.height - sheetY - size.y)) : new Vector2(-30, 0);
            var k = 1 - Mathf.Exp(-slideSpeed * Time.unscaledDeltaTime);
            book.sizeDelta = Vector2.Lerp(book.sizeDelta, size, k);
            book.anchoredPosition = Vector2.Lerp(book.anchoredPosition, at, k);
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
            row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, Mathf.Max(rowHeight, row.TextHeight + 16));
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
    }
}
