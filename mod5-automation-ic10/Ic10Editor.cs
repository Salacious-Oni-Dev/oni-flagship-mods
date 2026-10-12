using System.Text;
using Ic10;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mod5AutomationIc10
{
    // The housing's program editor: the program is typed in ONI's own UI.
    //
    // Stationeers types a program into InputSourceCode, a window of 128 line fields numbered
    // from 0 ("{i}."), each held to 90 ASCII characters, the whole to 4096; Confirm hands the
    // text to the chip and closes, Cancel throws the typing away. It does not pause the game:
    // its PauseGameToggle has no caller. Here:
    //
    //   title bar     the housing's name, and a close button
    //   status        running / switched off / no power / the error, and "edited" when the
    //                 text is not what the chip runs
    //   check         what Apply would do: compile, or the first error (Ic10Check)
    //   code          one multi-line text field held to the same limits as it is typed
    //                 (Ic10Source.Limit), with a gutter of line numbers from 0
    //   sockets       what d0..d5 point at, read only: they are set on the side screen
    //   registers     r0..r15, sp, ra, live
    //   help          the reference line for the instruction on the caret's line
    //   Apply         compile and run the text; the window stays open
    //   Revert        back to what the chip runs
    //   Close / Esc   close; unapplied typing is thrown away, as Stationeers' Cancel does
    //
    // A KModalScreen, as ONI's own text dialogs are (FileNameDialog): it sits on
    // KScreenManager's stack above every other screen, takes every key binding while it is
    // up, stops the camera, and its dimmed background takes the clicks that would reach the
    // world. That replaced the immediate-mode windows' patches on KInputManager and their
    // key release. Unlike ONI's modals it does not
    // pause the game, so the registers can be watched as the program runs.
    //
    // Built in code, the font is the game's (Localization.FontAsset), which is not
    // monospaced.
    public sealed class Ic10Editor : KModalScreen
    {
        private const float WindowWidth = 1100f;
        private const float WindowHeight = 700f;
        private const float GutterWidth = 44f;
        private const float CodeSize = 15f;
        private const float RefreshSeconds = 0.25f;

        private static readonly Color WindowColour = new Color32(0xF2, 0xF2, 0xF4, 255);
        private static readonly Color HeaderColour = new Color32(0x7B, 0x3F, 0x62, 255);
        private static readonly Color CodeColour = new Color32(0x1F, 0x21, 0x28, 255);
        private static readonly Color CodeInk = new Color32(0xE4, 0xE6, 0xEB, 255);
        private static readonly Color GutterColour = new Color32(0x2A, 0x2D, 0x36, 255);
        private static readonly Color GutterInk = new Color32(0x7F, 0x85, 0x94, 255);
        private static readonly Color Selection = new Color32(0x4C, 0x7A, 0xC8, 120);
        private static readonly Color GoodColour = new Color32(0x2E, 0x7D, 0x32, 255);

        private static Ic10Editor instance;

        private Ic10HousingBuilding target;
        private KInputTextField field;
        private TextMeshProUGUI numbers;
        private TextMeshProUGUI title;
        private TextMeshProUGUI status;
        private TextMeshProUGUI check;
        private TextMeshProUGUI help;
        private TextMeshProUGUI size;
        private TextMeshProUGUI sockets;
        private TextMeshProUGUI registers;
        private UnityEngine.UI.Button apply;
        private Ic10Check checker;
        private bool numbersDirty = true;
        private float numberedWidth = -1f;
        private int helpLine = -1;
        private float nextRefresh;
        private readonly StringBuilder scratch = new StringBuilder();

        // The editor that is up, for tests; null when none is.
        public static Ic10Editor Instance
        {
            get { return instance; }
        }

        public static bool IsOpen
        {
            get { return instance != null && instance.IsActive(); }
        }

        // The housing whose editor is open.
        public static Ic10HousingBuilding Target
        {
            get { return instance != null ? instance.target : null; }
        }

        public KInputTextField Field
        {
            get { return field; }
        }

        public string StatusText
        {
            get { return status != null ? status.text : null; }
        }

        public string CheckText
        {
            get { return check != null ? check.text : null; }
        }

        public string HelpText
        {
            get { return help != null ? help.text : null; }
        }

        public string GutterText
        {
            get { return numbers != null ? numbers.text : null; }
        }

        public static void Open(Ic10HousingBuilding housing)
        {
            if (housing == null)
            {
                return;
            }
            if (instance != null)
            {
                instance.ShowFor(housing);
                return;
            }
            GameScreenManager screens = GameScreenManager.Instance;
            if (screens == null || screens.ssOverlayCanvas == null)
            {
                Mod5Log.Warn("editor: no overlay canvas to open on");
                return;
            }
            GameObject go = new GameObject("Mod5Ic10Editor", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(screens.ssOverlayCanvas.gameObject.transform, false);
            Ic10Ui.Fill(go.GetComponent<RectTransform>());
            Ic10Editor editor = go.AddComponent<Ic10Editor>();
            editor.pause = false;
            editor.BuildUi();
            instance = editor;
            // Awake runs OnPrefabInit (KModalScreen's dimmed background); Activate puts the
            // screen on KScreenManager's stack now rather than at Start, a frame later.
            go.SetActive(true);
            editor.Activate();
            editor.ShowFor(housing);
        }

        public static void CloseFor(Ic10HousingBuilding housing)
        {
            if (instance != null && instance.target == housing)
            {
                Close();
            }
        }

        public static void Close()
        {
            if (instance != null)
            {
                Ic10Editor closing = instance;
                instance = null;
                closing.Deactivate();
            }
        }

        private void ShowFor(Ic10HousingBuilding housing)
        {
            target = housing;
            title.text = "IC10: " + STRINGS.UI.StripLinkFormatting(housing.gameObject.GetProperName());
            Load(housing.Source);
            Refresh();
            field.Select();
            field.ActivateInputField();
        }

        // Puts a program in the field as the chip runs it: the limits applied, the carriage
        // returns Ic10Source.Clean added gone.
        private void Load(string source)
        {
            field.SetTextWithoutNotify(Ic10Source.Limit(source ?? string.Empty));
            field.stringPosition = 0;
            TextChanged();
        }

        protected override void OnDeactivate()
        {
            if (instance == this)
            {
                instance = null;
            }
            base.OnDeactivate();
        }

        // Every key is the editor's while it is up. KModalScreen's own OnKeyDown would answer
        // the pause key (the space bar) with the "Negative" sound on every space typed;
        // FileNameDialog overrides it the same way.
        public override void OnKeyDown(KButtonEvent e)
        {
            if (e.TryConsume(Action.Escape))
            {
                Close();
                return;
            }
            e.Consumed = true;
        }

        public override void OnKeyUp(KButtonEvent e)
        {
            e.Consumed = true;
        }

        private void Update()
        {
            // The housing was deconstructed, or the game it was in has ended.
            if (target == null)
            {
                Close();
                return;
            }
            int caret = field.isFocused ? field.stringPosition : -1;
            int line = caret < 0 ? -1 : LineOf(field.text, caret);
            if (line != helpLine)
            {
                helpLine = line;
                string text = line < 0 ? null : Ic10Reference.HelpFor(Ic10Reference.LineAt(field.text, caret));
                help.text = text ?? " ";
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }
        }

        // The gutter follows the code as the field scrolls it: the field moves its text by
        // its anchored position (TMP_InputField.AdjustTextPositionRelativeToViewport).
        private void LateUpdate()
        {
            if (field == null || numbers == null || field.textComponent == null)
            {
                return;
            }
            float width = field.textComponent.rectTransform.rect.width;
            if (numbersDirty || width != numberedWidth)
            {
                numbersDirty = false;
                numberedWidth = width;
                Renumber();
            }
            numbers.rectTransform.anchoredPosition = new Vector2(0f, field.textComponent.rectTransform.anchoredPosition.y);
        }

        // The gutter's numbers, one per line of the program from 0, as Stationeers numbers
        // its line fields. A multi-line field always wraps (TMP_InputField.SetTextComponentWrapMode),
        // so a line too long for the field takes two rows; its second row gets no number. The
        // rows come from the text's own layout: with rich text off, character k of the layout
        // is character k of the text.
        private void Renumber()
        {
            TMP_Text code = field.textComponent;
            string text = field.text;
            code.ForceMeshUpdate();
            TMP_TextInfo info = code.textInfo;
            int[] rowOf = new int[CountLines(text)];
            int line = 0;
            int lastRow = 0;
            for (int k = 0; k < info.characterCount && k < text.Length && line < rowOf.Length; k++)
            {
                if (k == 0 || text[k - 1] == '\n')
                {
                    rowOf[line++] = info.characterInfo[k].lineNumber;
                }
                lastRow = info.characterInfo[k].lineNumber;
            }
            // A last line with nothing on it has no character to place it: it is the row
            // after the last.
            for (; line < rowOf.Length; line++)
            {
                rowOf[line] = line == 0 ? 0 : ++lastRow;
            }
            scratch.Length = 0;
            int row = 0;
            for (int i = 0; i < rowOf.Length; i++)
            {
                for (; row < rowOf[i]; row++)
                {
                    scratch.Append('\n');
                }
                scratch.Append(i).Append(".\n");
                row++;
            }
            numbers.text = scratch.ToString();
        }

        private static int CountLines(string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines++;
                }
            }
            return lines;
        }

        private static int LineOf(string text, int position)
        {
            int line = 0;
            int end = Mathf.Min(position, text.Length);
            for (int i = 0; i < end; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }
            return line;
        }

        // Typing past Stationeers' limits is cut as it is typed; the caret stays after what
        // was kept. Limit works left to right, so what it keeps of the text before the caret
        // is where the caret goes.
        private void OnTyped(string typed)
        {
            string limited = Ic10Source.Limit(typed);
            if (limited != typed)
            {
                int caret = Mathf.Clamp(field.stringPosition, 0, typed.Length);
                int kept = Ic10Source.Limit(typed.Substring(0, caret)).Length;
                field.SetTextWithoutNotify(limited);
                field.stringPosition = Mathf.Min(kept, limited.Length);
            }
            TextChanged();
        }

        private void TextChanged()
        {
            string text = field.text;
            numbersDirty = true;
            bool fits = Ic10Source.Fits(text);
            size.text = Ic10Source.Size(text) + " / " + Ic10Source.MaxSize + (fits ? string.Empty : "  too long");
            size.color = fits ? Ic10Ui.TextColour : Ic10Ui.ErrorColour;
            // InputSourceCode.UpdateFileSize: over 4096 the submit button is disabled.
            apply.interactable = fits;
            helpLine = -1;
            Check();
            Refresh();
        }

        // What the text as it stands would do on Apply: compile, or stop at its first error.
        // Stationeers compiles on submit and reports the first error; so does this.
        private void Check()
        {
            if (checker == null)
            {
                checker = new Ic10Check(Ic10Devices.Enums, Ic10Housing.SocketCount);
            }
            string error = checker.FirstError(Ic10Source.Clean(field.text));
            check.text = error == null ? "Compiles." : "Will not compile: " + error;
            check.color = error == null ? GoodColour : Ic10Ui.ErrorColour;
        }

        private void OnApply()
        {
            if (target == null || !Ic10Source.Fits(field.text))
            {
                return;
            }
            target.SetSource(Ic10Source.Clean(field.text));
            Refresh();
        }

        private void OnRevert()
        {
            if (target != null)
            {
                Load(target.Source);
            }
        }

        private void Refresh()
        {
            if (target == null || status == null)
            {
                return;
            }
            bool edited = !string.Equals(Ic10Source.Clean(field.text), Ic10Source.Clean(target.Source ?? string.Empty), System.StringComparison.Ordinal);
            string error = target.ErrorText();
            Ic10Chip chip = target.Chip;
            Ic10Housing housing = target.Housing;
            if (error != null)
            {
                status.text = "Error: " + error;
                status.color = Ic10Ui.ErrorColour;
            }
            else
            {
                string state = chip == null || housing == null ? "No chip" : !housing.On ? "Switched off" : !housing.Powered ? "No power" : "Running";
                status.text = chip == null || housing == null || !housing.On || !housing.Powered
                    ? state
                    : state + ", next line " + chip.LineNumber + " of " + chip.LineCount;
                status.color = Ic10Ui.TextColour;
            }
            if (edited)
            {
                status.text += "    (edited: Apply to run it)";
            }

            scratch.Length = 0;
            for (int i = 0; i < Ic10Housing.SocketCount; i++)
            {
                int id = target.GetSocket(i);
                string name = id > 0 ? (Ic10Devices.Name(id) ?? "(gone)") : "none";
                string label = housing != null ? housing.GetDeviceLabel(i) : null;
                scratch.Append("<b>d").Append(i).Append("</b>");
                if (!string.IsNullOrEmpty(label))
                {
                    scratch.Append(' ').Append(label);
                }
                scratch.Append(": ").Append(STRINGS.UI.StripLinkFormatting(name)).Append('\n');
            }
            sockets.text = scratch.ToString();

            scratch.Length = 0;
            if (chip != null)
            {
                for (int i = 0; i < Ic10Chip.RegisterCount; i++)
                {
                    string name = i == Ic10Chip.StackPointerIndex ? "sp" : i == Ic10Chip.ReturnAddressIndex ? "ra" : "r" + i;
                    scratch.Append(i % 2 == 0 ? string.Empty : "<pos=50%>");
                    scratch.Append("<b>").Append(name).Append("</b> ").Append(chip.GetRegister(i).ToString("G9"));
                    if (i % 2 == 1)
                    {
                        scratch.Append('\n');
                    }
                }
            }
            registers.text = scratch.ToString();
        }

        // ---------------------------------------------------------------------------------
        // The widgets.
        // ---------------------------------------------------------------------------------

        private void BuildUi()
        {
            Image window = Ic10Ui.Panel(transform, "Window", WindowColour);
            RectTransform frame = window.rectTransform;
            frame.anchorMin = new Vector2(0.5f, 0.5f);
            frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            VerticalLayoutGroup column = window.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(0, 0, 0, 10);
            column.spacing = 4f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            // Title bar.
            Image header = Ic10Ui.Panel(window.transform, "Header", HeaderColour);
            LayoutElement headerSize = header.gameObject.AddComponent<LayoutElement>();
            headerSize.minHeight = 34f;
            headerSize.preferredHeight = 34f;
            // A layout group whose children are forced to expand reports itself as flexible to
            // its parent, so every row says flexible 0 itself and only Middle takes the spare
            // height (otherwise header and footer come out twice as tall).
            headerSize.flexibleHeight = 0f;
            HorizontalLayoutGroup headerRow = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerRow.padding = new RectOffset(12, 4, 3, 3);
            headerRow.childControlWidth = true;
            headerRow.childControlHeight = true;
            headerRow.childForceExpandWidth = false;
            headerRow.childForceExpandHeight = true;
            title = Ic10Ui.Text(header.transform, "Title", 18f, false);
            title.color = Color.white;
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            GameObject closeX = Ic10Ui.Button(header.transform, "CloseX", "X", 28f);
            Ic10Ui.Wire(closeX.transform, Close, "Close; what is not applied is thrown away (Esc)");

            GameObject lines = new GameObject("Lines", typeof(RectTransform));
            lines.transform.SetParent(window.transform, false);
            VerticalLayoutGroup linesColumn = lines.AddComponent<VerticalLayoutGroup>();
            linesColumn.padding = new RectOffset(12, 12, 2, 0);
            linesColumn.childControlWidth = true;
            linesColumn.childControlHeight = true;
            linesColumn.childForceExpandWidth = true;
            linesColumn.childForceExpandHeight = false;
            status = Line(lines.transform, "Status", 16f);
            check = Line(lines.transform, "Check", 15f);

            // Code on the left, sockets and registers on the right.
            GameObject middle = Row(window.transform, "Middle", 0f, 12);
            middle.GetComponent<LayoutElement>().flexibleHeight = 1f;
            middle.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            BuildCode(middle.transform);
            BuildSide(middle.transform);

            help = Line(Row(window.transform, "HelpRow", 24f, 12).transform, "Help", 14f);
            help.overflowMode = TextOverflowModes.Ellipsis;
            help.GetComponent<LayoutElement>().flexibleWidth = 1f;

            GameObject footer = Row(window.transform, "Footer", 32f, 12);
            footer.GetComponent<HorizontalLayoutGroup>().spacing = 6f;
            apply = Ic10Ui.Button(footer.transform, "Apply", "Apply", 96f).GetComponent<UnityEngine.UI.Button>();
            Ic10Ui.Wire(apply.transform, OnApply, "Compile the program and run it on the housing");
            Ic10Ui.Wire(Ic10Ui.Button(footer.transform, "Revert", "Revert", 96f).transform, OnRevert, "Back to the program the housing runs");
            Ic10Ui.Wire(Ic10Ui.Button(footer.transform, "Close", "Close", 96f).transform, Close, "Close; what is not applied is thrown away (Esc)");
            GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(footer.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
            size = Ic10Ui.Text(footer.transform, "Size", 14f, false);
            size.alignment = TextAlignmentOptions.MidlineRight;
        }

        private void BuildCode(Transform parent)
        {
            Image code = Ic10Ui.Panel(parent, "Code", CodeColour);
            code.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // The gutter: a masked strip whose numbers move with the field's text.
            Image gutter = Ic10Ui.Panel(code.transform, "Gutter", GutterColour);
            RectTransform gutterRect = gutter.rectTransform;
            gutterRect.anchorMin = new Vector2(0f, 0f);
            gutterRect.anchorMax = new Vector2(0f, 1f);
            gutterRect.pivot = new Vector2(0f, 0.5f);
            gutterRect.offsetMin = new Vector2(0f, 0f);
            gutterRect.offsetMax = new Vector2(GutterWidth, 0f);
            RectTransform gutterView = Viewport(gutter.transform, "Viewport", 2f);
            numbers = CodeLabel(gutterView, "Numbers", GutterInk);
            numbers.alignment = TextAlignmentOptions.TopRight;

            // The field, built inactive so its Awake and OnEnable find their parts.
            GameObject fieldGo = new GameObject("Field", typeof(RectTransform));
            fieldGo.SetActive(false);
            fieldGo.transform.SetParent(code.transform, false);
            RectTransform fieldRect = fieldGo.GetComponent<RectTransform>();
            fieldRect.anchorMin = Vector2.zero;
            fieldRect.anchorMax = Vector2.one;
            fieldRect.offsetMin = new Vector2(GutterWidth, 0f);
            fieldRect.offsetMax = Vector2.zero;
            Image fieldBack = fieldGo.AddComponent<Image>();
            fieldBack.color = CodeColour;
            RectTransform view = Viewport(fieldGo.transform, "Viewport", 8f);
            TextMeshProUGUI text = CodeLabel(view, "Text", CodeInk);
            text.alignment = TextAlignmentOptions.TopLeft;

            field = fieldGo.AddComponent<KInputTextField>();
            field.transition = Selectable.Transition.None;
            field.targetGraphic = fieldBack;
            field.textViewport = view;
            field.textComponent = text;
            if (Localization.FontAsset != null)
            {
                field.fontAsset = Localization.FontAsset;
            }
            field.pointSize = CodeSize;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.richText = false;
            field.restoreOriginalTextOnEscape = false;
            field.onFocusSelectAll = false;
            field.resetOnDeActivation = false;
            field.customCaretColor = true;
            field.caretColor = CodeInk;
            field.caretWidth = 2;
            field.selectionColor = Selection;
            field.scrollSensitivity = 3f;
            field.onValueChanged.AddListener(OnTyped);
            fieldGo.SetActive(true);
        }

        private void BuildSide(Transform parent)
        {
            GameObject side = new GameObject("Side", typeof(RectTransform));
            side.transform.SetParent(parent, false);
            LayoutElement sideSize = side.AddComponent<LayoutElement>();
            sideSize.minWidth = 300f;
            sideSize.preferredWidth = 300f;
            // Its column expands its children, so it would report itself flexible and split
            // the spare width with the code (first live run: the code got a third).
            sideSize.flexibleWidth = 0f;
            VerticalLayoutGroup stack = side.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 4f;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            TextMeshProUGUI heading = Ic10Ui.Text(side.transform, "SocketsTitle", 15f, false);
            heading.text = "Sockets (set on the side screen)";
            heading.fontStyle = FontStyles.Bold;
            sockets = Ic10Ui.Text(side.transform, "Sockets", 14f, false);
            sockets.alignment = TextAlignmentOptions.TopLeft;
            sockets.overflowMode = TextOverflowModes.Ellipsis;
            sockets.gameObject.AddComponent<LayoutElement>().minHeight = 6 * 20f;

            heading = Ic10Ui.Text(side.transform, "RegistersTitle", 15f, false);
            heading.text = "Registers";
            heading.fontStyle = FontStyles.Bold;
            registers = Ic10Ui.Text(side.transform, "Registers", 14f, false);
            registers.alignment = TextAlignmentOptions.TopLeft;
            registers.overflowMode = TextOverflowModes.Ellipsis;
            registers.gameObject.AddComponent<LayoutElement>().minHeight = 9 * 20f;
        }

        // A full-width row with side padding, laid out left to right.
        private static GameObject Row(Transform parent, string name, float height, int padding)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
            line.padding = new RectOffset(padding, padding, 0, 0);
            line.spacing = 10f;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = true;
            LayoutElement size = row.AddComponent<LayoutElement>();
            if (height > 0f)
            {
                size.minHeight = height;
                size.preferredHeight = height;
                size.flexibleHeight = 0f;
            }
            return row;
        }

        private static TextMeshProUGUI Line(Transform parent, string name, float fontSize)
        {
            TextMeshProUGUI text = Ic10Ui.Text(parent, name, fontSize, false);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement size = text.gameObject.AddComponent<LayoutElement>();
            size.minHeight = 22f;
            return text;
        }

        // A masked area inset from its parent by `inset` on the left and right and 6 at the
        // top and bottom; the gutter and the field use the same top inset, so their lines
        // line up.
        private static RectTransform Viewport(Transform parent, string name, float inset)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, 6f);
            rect.offsetMax = new Vector2(-inset, -6f);
            go.AddComponent<RectMask2D>();
            return rect;
        }

        private static TextMeshProUGUI CodeLabel(RectTransform parent, string name, Color colour)
        {
            TextMeshProUGUI text = Ic10Ui.Text(parent, name, CodeSize, false);
            text.color = colour;
            text.richText = false;
            Ic10Ui.Fill(text.rectTransform);
            return text;
        }
    }
}
