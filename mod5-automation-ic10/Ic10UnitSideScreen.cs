using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Ic10;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mod5AutomationIc10
{
    // A logic unit's choices in ONI's own details panel.
    //
    // Stationeers sets a logic unit with the screwdriver on its buttons, as it sets a
    // housing's sockets, and the steps are its own:
    //
    //   a device     LogicReader.InteractWith -> Logicable.GetNextReadable(..., isForward:
    //                interaction.AltKey) -> _GetNext without allowNull: a plain press steps to
    //                the next building on the cable in name order and wraps, the modifier
    //                steps back and wraps, and neither ever gives none; from none the plain
    //                press gives the first and the modifier the last
    //   a logic type Logicable.GetNextReadableValue / GetNextWritableValue: through the
    //                LogicType list in order, wrapping, skipping what the chosen building
    //                cannot read (or write)
    //
    // Here each choice is a row: < and > step as above (Stationeers' two presses), and the
    // rows with a long list also have Pick, a list to choose from with a filter. A device row
    // also has Link and Clear (none, which Stationeers' unit buttons cannot give).
    // The PID controller's numbers are typed. The rows are made for the unit shown, from its
    // kind, and the device rows from its pins (Ic10Links.For), so the link tool and the panel
    // agree on names and colours.
    //
    // Registered as the housing's screen is (Ic10HousingSideScreen). Typing takes the keyboard
    // the way vanilla's TreeFilterableSideScreen search does: isEditing while a field has the
    // caret.
    public sealed class Ic10UnitSideScreen : SideScreenContent
    {
        private const float RowHeight = 26f;
        private const float SmallButton = 26f;
        private const float WideButton = 48f;
        private const float ListHeight = 300f;
        private const float RefreshSeconds = 0.25f;

        private Ic10UnitBuilding target;
        private TextMeshProUGUI status;
        private Transform rows;
        private GameObject chooser;
        private TextMeshProUGUI chooserTitle;
        private KInputTextField filter;
        private Transform chooserList;
        private float nextRefresh;
        private readonly List<KeyValuePair<TextMeshProUGUI, Func<string>>> labels = new List<KeyValuePair<TextMeshProUGUI, Func<string>>>();
        private readonly List<KeyValuePair<KInputTextField, Func<double>>> numbers = new List<KeyValuePair<KInputTextField, Func<double>>>();
        private readonly List<KeyValuePair<string, GameObject>> listItems = new List<KeyValuePair<string, GameObject>>();

        // The unit shown, and a row's text by its name, for tests.
        public Ic10UnitBuilding Target
        {
            get { return target; }
        }

        public string RowText(string row)
        {
            Transform found = rows != null ? rows.Find(row + "/Label") : null;
            TextMeshProUGUI text = found != null ? found.GetComponent<TextMeshProUGUI>() : null;
            return text != null ? text.text : null;
        }

        public bool Choosing
        {
            get { return chooser != null && chooser.activeSelf; }
        }

        public int ChoiceCount
        {
            get { return listItems.Count; }
        }

        public override bool IsValidForTarget(GameObject go)
        {
            return go != null && go.GetComponent<Ic10UnitBuilding>() != null;
        }

        public override string GetTitle()
        {
            return "Logic unit";
        }

        public override void SetTarget(GameObject go)
        {
            Ic10UnitBuilding next = go != null ? go.GetComponent<Ic10UnitBuilding>() : null;
            if (next != target || rows == null || rows.childCount == 0)
            {
                target = next;
                Rebuild();
            }
            Refresh();
        }

        public override void ClearTarget()
        {
            target = null;
            StopEditing();
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            BuildFrame();
            // SetTarget can come before the clone wakes.
            if (target != null)
            {
                Rebuild();
                Refresh();
            }
        }

        // ---------------------------------------------------------------------------------
        // The keyboard, while a field has the caret (TreeFilterableSideScreen's way).
        // ---------------------------------------------------------------------------------

        public override float GetSortKey()
        {
            return isEditing ? 50f : base.GetSortKey();
        }

        public override void OnKeyDown(KButtonEvent e)
        {
            if (!e.Consumed && isEditing)
            {
                e.Consumed = true;
            }
        }

        public override void OnKeyUp(KButtonEvent e)
        {
            if (!e.Consumed && isEditing)
            {
                e.Consumed = true;
            }
        }

        private void Editable(KInputTextField field)
        {
            field.onFocus = (System.Action)Delegate.Combine(field.onFocus, (System.Action)(() => isEditing = true));
            field.onEndEdit.AddListener(_ => StopEditing());
        }

        private void StopEditing()
        {
            if (isEditing && KScreenManager.Instance != null)
            {
                isEditing = false;
            }
        }

        // ---------------------------------------------------------------------------------
        // Refreshing.
        // ---------------------------------------------------------------------------------

        private void Update()
        {
            if (target == null || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        // Read fresh each time: a program, the link tool or the save can change a choice
        // under the screen.
        private void Refresh()
        {
            if (target == null || status == null || target.Unit == null)
            {
                return;
            }
            Ic10LogicUnit unit = target.Unit;
            string text = "Setting " + unit.Setting.ToString("G9", CultureInfo.InvariantCulture);
            if (unit.Error != 0)
            {
                text += "\nError: a choice below is missing or not on this unit's data cable";
            }
            if (target.Network == null)
            {
                text += "\nNo data cable under this unit: it can reach nothing.";
            }
            status.text = text;
            status.color = unit.Error != 0 ? Ic10Ui.ErrorColour : Ic10Ui.TextColour;
            foreach (KeyValuePair<TextMeshProUGUI, Func<string>> label in labels)
            {
                if (label.Key != null)
                {
                    label.Key.text = label.Value();
                }
            }
            foreach (KeyValuePair<KInputTextField, Func<double>> number in numbers)
            {
                if (number.Key != null && !number.Key.isFocused)
                {
                    number.Key.SetTextWithoutNotify(number.Value().ToString("G9", CultureInfo.InvariantCulture));
                }
            }
        }

        // ---------------------------------------------------------------------------------
        // Stepping.
        // ---------------------------------------------------------------------------------

        // Logicable._GetNext without allowNull, and _GetNextValue: through the list and
        // around, never to none; from a value not in the list (none, or a building gone from
        // the cable) the plain press gives the first and the modifier the last. An empty list
        // leaves the choice as it is.
        public static int StepWrap(List<int> values, int current, bool back)
        {
            int count = values == null ? 0 : values.Count;
            if (count == 0)
            {
                return current;
            }
            int at = values.IndexOf(current);
            if (at < 0)
            {
                return back ? values[count - 1] : values[0];
            }
            return values[((at + (back ? -1 : 1)) % count + count) % count];
        }

        private static List<int> Ids(List<KPrefabID> buildings)
        {
            List<int> ids = new List<int>(buildings.Count);
            foreach (KPrefabID building in buildings)
            {
                if (building != null)
                {
                    ids.Add(building.InstanceID);
                }
            }
            return ids;
        }

        private static List<int> Values(List<KeyValuePair<string, int>> options)
        {
            List<int> values = new List<int>(options.Count);
            foreach (KeyValuePair<string, int> option in options)
            {
                values.Add(option.Value);
            }
            return values;
        }

        // ---------------------------------------------------------------------------------
        // The rows, made for the unit shown.
        // ---------------------------------------------------------------------------------

        private void Rebuild()
        {
            HideChooser();
            labels.Clear();
            numbers.Clear();
            if (rows == null)
            {
                return;
            }
            for (int i = rows.childCount - 1; i >= 0; i--)
            {
                GameObject old = rows.GetChild(i).gameObject;
                old.SetActive(false);
                old.transform.SetParent(null, false);
                Destroy(old);
            }
            if (target == null || target.Unit == null)
            {
                return;
            }
            Ic10LogicUnit unit = target.Unit;

            Ic10LogicWriterSwitch writerSwitch = unit as Ic10LogicWriterSwitch;
            if (writerSwitch != null)
            {
                FlipRow(writerSwitch);
            }
            if (unit is Ic10LogicMirror)
            {
                NoteRow("Left cell: the cable to mirror from. Right cell: the cable it shows the building on.");
            }
            List<Ic10LinkSlot> pins = Ic10Links.For(target.gameObject);
            for (int i = 0; i < pins.Count; i++)
            {
                DeviceRow(i, pins[i]);
            }

            Ic10LogicReader reader = unit as Ic10LogicReader;
            if (reader != null)
            {
                OptionRow("Reads", "Reads", () => DeviceLogicTypes(reader.Target, false), () => reader.LogicType, v => reader.LogicType = v);
            }
            Ic10LogicSlotReader slotReader = unit as Ic10LogicSlotReader;
            if (slotReader != null)
            {
                SlotRow(() => slotReader.Slot, v => slotReader.Slot = v);
                OptionRow("Reads", "Reads", () => SlotTypes(Ic10Devices.Find(slotReader.Target), slotReader.Slot), () => slotReader.SlotType, v => slotReader.SlotType = v);
            }
            Ic10LogicBatchReader batchReader = unit as Ic10LogicBatchReader;
            if (batchReader != null)
            {
                OptionRow("Every", "Every", PrefabKinds, () => batchReader.Prefab, v => batchReader.Prefab = v);
                OptionRow("Reads", "Reads", () => LogicTypes(null, false), () => batchReader.LogicType, v => batchReader.LogicType = v);
                OptionRow("Method", "Combined by", () => Named(new[] { "Average", "Sum", "Minimum", "Maximum" }), () => batchReader.Method, v => batchReader.Method = v);
            }
            Ic10LogicWriter writer = unit as Ic10LogicWriter;
            if (writer != null)
            {
                Ic10LogicBatchWriter batchWriter = unit as Ic10LogicBatchWriter;
                if (batchWriter != null)
                {
                    OptionRow("Every", "To every", PrefabKinds, () => batchWriter.Prefab, v => batchWriter.Prefab = v);
                    OptionRow("Writes", "Writes", () => LogicTypes(null, true), () => writer.LogicType, v => writer.LogicType = v);
                }
                else
                {
                    OptionRow("Writes", "Writes", () => DeviceLogicTypes(writer.Target, true), () => writer.LogicType, v => writer.LogicType = v);
                }
            }
            if (writerSwitch != null)
            {
                OptionRow("Writes", "Writes", () => DeviceLogicTypes(writerSwitch.Target, true), () => writerSwitch.LogicType, v => writerSwitch.ChooseLogicType(v));
            }
            if (unit is Ic10LogicMathUnary)
            {
                OptionRow("Operation", "Operation", () => Named(Ic10LogicMathUnary.Modes), () => unit.Mode, v => unit.Mode = v);
            }
            Ic10LogicBatchSlotReader batchSlot = unit as Ic10LogicBatchSlotReader;
            if (batchSlot != null)
            {
                OptionRow("Every", "Every", PrefabKinds, () => batchSlot.Prefab, v => batchSlot.Prefab = v);
                SlotRow(() => batchSlot.Slot, v => batchSlot.Slot = v);
                OptionRow("Reads", "Reads", () => SlotTypes(null, 0), () => batchSlot.SlotType, v => batchSlot.SlotType = v);
                OptionRow("Method", "Combined by", () => Named(new[] { "Average", "Sum", "Minimum", "Maximum", "Count" }), () => batchSlot.Method, v => batchSlot.Method = v);
            }
            if (unit is Ic10LogicHashGen)
            {
                OptionRow("Hash", "Hash", HashChoices, () => (int)unit.Setting, v => unit.Setting = v);
            }
            if (unit is Ic10LogicMath)
            {
                OptionRow("Operation", "Operation", () => Named(Ic10LogicMath.Modes), () => unit.Mode, v => unit.Mode = v);
            }
            if (unit is Ic10LogicCompare)
            {
                OptionRow("Operation", "Operation", () => Named(Ic10LogicCompare.Modes), () => unit.Mode, v => unit.Mode = v);
            }
            if (unit is Ic10LogicMinMax)
            {
                // LogicMinMax's own numbers: 2 is the lesser, anything else the greater.
                OptionRow("Operation", "Gives the", () => Named(Ic10LogicMinMax.Modes), () => unit.Mode == 2 ? 1 : 0, v => unit.Mode = v == 1 ? 2 : 1);
            }
            Ic10LogicPid pid = unit as Ic10LogicPid;
            if (pid != null)
            {
                NumberRow("Setpoint", "Setpoint", () => pid.Setpoint, v => pid.Setpoint = v);
                NumberRow("Proportional", "Proportional gain", () => pid.ProportionalGain, v => pid.ProportionalGain = (float)v);
                NumberRow("Integral", "Integral gain", () => pid.IntegralGain, v => pid.IntegralGain = (float)v);
                NumberRow("Derivative", "Derivative gain", () => pid.DerivativeGain, v => pid.DerivativeGain = (float)v);
                NumberRow("Maximum", "Output maximum", () => pid.OutputMaximum, v => pid.OutputMaximum = (float)v);
                NumberRow("Minimum", "Output minimum", () => pid.OutputMinimum, v => pid.OutputMinimum = (float)v);
            }
        }

        private static string NameOf(int id)
        {
            if (id <= 0)
            {
                return "none";
            }
            string name = Ic10Devices.Name(id);
            return name == null ? "(gone)" : STRINGS.UI.StripLinkFormatting(name);
        }

        private TextMeshProUGUI Label(Transform row, Func<string> text)
        {
            TextMeshProUGUI label = Ic10Ui.Text(row, "Label", 15f, false);
            LayoutElement grow = label.gameObject.AddComponent<LayoutElement>();
            grow.flexibleWidth = 1f;
            grow.minWidth = 80f;
            label.overflowMode = TextOverflowModes.Ellipsis;
            labels.Add(new KeyValuePair<TextMeshProUGUI, Func<string>>(label, text));
            return label;
        }

        // A pin: < > step through the buildings on the cable (the units and bricks only, for
        // an input), Link, Clear.
        private void DeviceRow(int index, Ic10LinkSlot pin)
        {
            Func<int> current = () =>
            {
                List<Ic10LinkSlot> now = target != null ? Ic10Links.For(target.gameObject) : null;
                return now != null && index < now.Count ? now[index].Current : 0;
            };
            Transform row = Ic10Ui.Row(rows, pin.Label, RowHeight).transform;
            string name = pin.Label;
            Label(row, () => "<b>" + name + "</b>: " + NameOf(current()));
            bool unitsOnly = pin.UnitsOnly;
            Action<int> set = pin.Set;
            Ic10Ui.Wire(Ic10Ui.Button(row, "Back", "<", SmallButton).transform,
                () => Choose(set, StepWrap(Ids(target.Candidates(unitsOnly)), current(), true)),
                "Previous building on the cable, by name (Stationeers: screwdriver with the modifier)");
            Ic10Ui.Wire(Ic10Ui.Button(row, "Next", ">", SmallButton).transform,
                () => Choose(set, StepWrap(Ids(target.Candidates(unitsOnly)), current(), false)),
                "Next building on the cable, by name (Stationeers: screwdriver)");
            Ic10Ui.Wire(Ic10Ui.Button(row, "Link", "Link", WideButton).transform, () => OnLink(index), "Click the building in the world");
            Ic10Ui.Wire(Ic10Ui.Button(row, "Clear", "Clear", WideButton).transform, () => Choose(set, 0), "Point " + name + " at nothing");
        }

        private void OnLink(int index)
        {
            if (target == null)
            {
                return;
            }
            List<Ic10LinkSlot> pins = Ic10Links.For(target.gameObject);
            if (index < pins.Count)
            {
                Ic10LinkTool.Begin(target.gameObject, pins[index], Refresh);
            }
        }

        // A choice from a list: < > step through it and around, Pick shows it.
        private void OptionRow(string rowName, string title, Func<List<KeyValuePair<string, int>>> options, Func<int> current, Action<int> set)
        {
            Transform row = Ic10Ui.Row(rows, rowName, RowHeight).transform;
            Label(row, () => "<b>" + title + "</b>: " + NameIn(options(), current()));
            Ic10Ui.Wire(Ic10Ui.Button(row, "Back", "<", SmallButton).transform,
                () => Choose(set, StepWrap(Values(options()), current(), true)), "Previous (Stationeers: screwdriver with the modifier)");
            Ic10Ui.Wire(Ic10Ui.Button(row, "Next", ">", SmallButton).transform,
                () => Choose(set, StepWrap(Values(options()), current(), false)), "Next (Stationeers: screwdriver)");
            Ic10Ui.Wire(Ic10Ui.Button(row, "Pick", "Pick", WideButton).transform,
                () => ShowChooser(title, options(), set), "Choose from the list");
        }

        private static string NameIn(List<KeyValuePair<string, int>> options, int value)
        {
            foreach (KeyValuePair<string, int> option in options)
            {
                if (option.Value == value)
                {
                    return option.Key;
                }
            }
            return value == 0 ? "none" : value.ToString(CultureInfo.InvariantCulture);
        }

        private void SlotRow(Func<int> current, Action<int> set)
        {
            Transform row = Ic10Ui.Row(rows, "Slot", RowHeight).transform;
            Label(row, () => "<b>Slot</b>: " + (current() < 0 ? "none" : current().ToString(CultureInfo.InvariantCulture)));
            Ic10Ui.Wire(Ic10Ui.Button(row, "Back", "<", SmallButton).transform, () =>
            {
                if (current() > -1)
                {
                    Choose(set, current() - 1);
                }
            }, "Previous slot");
            Ic10Ui.Wire(Ic10Ui.Button(row, "Next", ">", SmallButton).transform, () => Choose(set, current() + 1), "Next slot");
        }

        // A number typed, taken when the field is left; text that is not a number puts the
        // value back.
        private void NumberRow(string rowName, string title, Func<double> current, Action<double> set)
        {
            Transform row = Ic10Ui.Row(rows, rowName, RowHeight).transform;
            TextMeshProUGUI label = Ic10Ui.Text(row, "Label", 15f, false);
            label.text = "<b>" + title + "</b>";
            LayoutElement grow = label.gameObject.AddComponent<LayoutElement>();
            grow.flexibleWidth = 1f;
            grow.minWidth = 80f;
            KInputTextField field = Ic10Ui.TextField(row, "Value", 120f, RowHeight);
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            Editable(field);
            field.onEndEdit.AddListener(text =>
            {
                double value;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    set(value);
                }
                field.SetTextWithoutNotify(current().ToString("G9", CultureInfo.InvariantCulture));
            });
            numbers.Add(new KeyValuePair<KInputTextField, Func<double>>(field, current));
        }

        // The writer switch's own switch (InteractableType.Activate in Stationeers).
        private void FlipRow(Ic10LogicWriterSwitch writerSwitch)
        {
            Transform row = Ic10Ui.Row(rows, "Switch", RowHeight).transform;
            Label(row, () => "<b>Switch</b>: " + (writerSwitch.Activate == 1 ? "on (writes 1)" : "off (writes 0)"));
            Ic10Ui.Wire(Ic10Ui.Button(row, "Flip", "Flip", WideButton).transform, () =>
            {
                writerSwitch.Flip();
                if (target != null)
                {
                    Game.Instance.userMenu.Refresh(target.gameObject);
                }
                Refresh();
            }, "Flip the switch: 1 or 0 is written to the chosen logic type of the chosen building");
        }

        private void NoteRow(string text)
        {
            TextMeshProUGUI note = Ic10Ui.Text(rows, "Note", 14f, true);
            note.text = text;
        }

        private void Choose(Action<int> set, int value)
        {
            if (target == null)
            {
                return;
            }
            set(value);
            Refresh();
        }

        // ---------------------------------------------------------------------------------
        // The lists.
        // ---------------------------------------------------------------------------------

        // A one-device unit's logic types. With no device chosen only None, as Stationeers'
        // GetNextReadableValue / GetNextWritableValue give None without a device.
        public static List<KeyValuePair<string, int>> DeviceLogicTypes(int target, bool write)
        {
            IIc10Device device = Ic10Devices.Find(target);
            if (device == null)
            {
                return new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("None", 0) };
            }
            return LogicTypes(device, write);
        }

        // The logic types in LogicType order (Logicable.LogicTypes), only those the building
        // can read or write; with no one building (a batch unit) all of them.
        private static List<KeyValuePair<string, int>> LogicTypes(IIc10Device device, bool write)
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in Ic10Devices.Enums.LogicType.Pairs)
            {
                if (device == null || (write ? device.CanLogicWrite(pair.Value) : device.CanLogicRead(pair.Value)))
                {
                    list.Add(pair);
                }
            }
            list.Sort((x, y) => x.Value.CompareTo(y.Value));
            return list;
        }

        private static List<KeyValuePair<string, int>> SlotTypes(IIc10Device device, int slot)
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in Ic10Devices.Enums.LogicSlotType.Pairs)
            {
                if (device == null || device.CanSlotRead(pair.Value, Mathf.Max(0, slot)))
                {
                    list.Add(pair);
                }
            }
            list.Sort((x, y) => x.Value.CompareTo(y.Value));
            return list;
        }

        private static List<KeyValuePair<string, int>> Named(string[] names)
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                list.Add(new KeyValuePair<string, int>(names[i], i));
            }
            return list;
        }

        // The kinds of building on this unit's cable, in the cable's name order
        // (Logicable._GetNextType: each prefab hash once, as first met).
        private List<KeyValuePair<string, int>> PrefabKinds()
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
            if (target == null)
            {
                return list;
            }
            HashSet<int> seen = new HashSet<int>();
            foreach (KPrefabID candidate in target.Candidates(false))
            {
                Building building = candidate != null ? candidate.GetComponent<Building>() : null;
                if (building == null)
                {
                    continue;
                }
                int hash = Ic10Math.Hash(candidate.PrefabTag.Name);
                if (seen.Add(hash))
                {
                    list.Add(new KeyValuePair<string, int>(STRINGS.UI.StripLinkFormatting(building.Def.Name), hash));
                }
            }
            return list;
        }

        // LogicHashGen's list: every buildable building and every element, by name; the
        // unit's Setting becomes the hash of the chosen one's id.
        private static List<KeyValuePair<string, int>> hashChoices;

        private static List<KeyValuePair<string, int>> HashChoices()
        {
            if (hashChoices != null)
            {
                return hashChoices;
            }
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
            foreach (BuildingDef def in Assets.BuildingDefs)
            {
                if (def != null && def.ShowInBuildMenu && !def.Deprecated)
                {
                    list.Add(new KeyValuePair<string, int>(STRINGS.UI.StripLinkFormatting(def.Name) + "  (" + def.PrefabID + ")", Ic10Math.Hash(def.PrefabID)));
                }
            }
            foreach (Element element in ElementLoader.elements)
            {
                if (element != null && !element.disabled)
                {
                    list.Add(new KeyValuePair<string, int>(STRINGS.UI.StripLinkFormatting(element.name) + "  (" + element.id + ")", Ic10Math.Hash(element.id.ToString())));
                }
            }
            list.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.CurrentCulture));
            hashChoices = list;
            return list;
        }

        // ---------------------------------------------------------------------------------
        // The chooser: a list in place of the rows, with a filter.
        // ---------------------------------------------------------------------------------

        private void ShowChooser(string title, List<KeyValuePair<string, int>> options, Action<int> set)
        {
            if (chooser == null)
            {
                return;
            }
            ClearList();
            chooserTitle.text = "<b>" + title + "</b>" + (options.Count == 0 ? ": nothing to choose" : string.Empty);
            foreach (KeyValuePair<string, int> option in options)
            {
                int value = option.Value;
                GameObject item = Ic10Ui.Button(chooserList, "Item", option.Key, 0f);
                item.GetComponent<LayoutElement>().minHeight = 24f;
                TextMeshProUGUI caption = item.GetComponentInChildren<TextMeshProUGUI>();
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                caption.margin = new Vector4(6f, 0f, 6f, 0f);
                caption.overflowMode = TextOverflowModes.Ellipsis;
                Ic10Ui.Wire(item.transform, () =>
                {
                    HideChooser();
                    Choose(set, value);
                }, null);
                listItems.Add(new KeyValuePair<string, GameObject>(option.Key, item));
            }
            filter.SetTextWithoutNotify(string.Empty);
            rows.gameObject.SetActive(false);
            chooser.SetActive(true);
        }

        private void HideChooser()
        {
            if (chooser == null)
            {
                return;
            }
            ClearList();
            chooser.SetActive(false);
            rows.gameObject.SetActive(true);
            StopEditing();
        }

        private void ClearList()
        {
            foreach (KeyValuePair<string, GameObject> item in listItems)
            {
                if (item.Value != null)
                {
                    Destroy(item.Value);
                }
            }
            listItems.Clear();
        }

        private void OnFilter(string text)
        {
            foreach (KeyValuePair<string, GameObject> item in listItems)
            {
                if (item.Value != null)
                {
                    item.Value.SetActive(string.IsNullOrEmpty(text) || item.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
        }

        // Picks the listed option whose name is given, as a click on it does: for tests.
        public bool PickListed(string name)
        {
            foreach (KeyValuePair<string, GameObject> item in listItems)
            {
                if (item.Key == name && item.Value != null)
                {
                    item.Value.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------------------------
        // The frame, on each clone: the rows are made per unit, so nothing is copied from
        // the prefab but its root.
        // ---------------------------------------------------------------------------------

        private void BuildFrame()
        {
            RectTransform root = gameObject.GetComponent<RectTransform>();
            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }
            root.sizeDelta = new Vector2(320f, 0f);
            Ic10Ui.Column(gameObject, 0, 4f).padding = new RectOffset(10, 10, 8, 8);
            ContentSizeFitter fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            status = Ic10Ui.Text(transform, "Status", 16f, true);
            status.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;

            GameObject rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(transform, false);
            Ic10Ui.Column(rowsGo, 0, 4f);
            rows = rowsGo.transform;

            chooser = new GameObject("Chooser", typeof(RectTransform));
            chooser.transform.SetParent(transform, false);
            Ic10Ui.Column(chooser, 0, 4f);
            chooserTitle = Ic10Ui.Text(chooser.transform, "Title", 15f, false);
            chooserTitle.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
            Transform filterRow = Ic10Ui.Row(chooser.transform, "FilterRow", RowHeight).transform;
            TextMeshProUGUI filterLabel = Ic10Ui.Text(filterRow, "Label", 14f, false);
            filterLabel.text = "Filter";
            filterLabel.gameObject.AddComponent<LayoutElement>().minWidth = 48f;
            filter = Ic10Ui.TextField(filterRow, "Filter", 0f, RowHeight);
            Editable(filter);
            filter.onValueChanged.AddListener(OnFilter);
            chooserList = Ic10Ui.ScrollList(chooser.transform, "List", ListHeight);
            GameObject cancel = Ic10Ui.Button(chooser.transform, "Cancel", "Cancel", 0f);
            // A button outside a row has no height of its own (first live run: 0 px tall).
            cancel.GetComponent<LayoutElement>().minHeight = RowHeight;
            Ic10Ui.Wire(cancel.transform, HideChooser, "Keep the choice as it is");
            chooser.SetActive(false);
        }

        // ---------------------------------------------------------------------------------
        // Registration.
        // ---------------------------------------------------------------------------------

        private static Ic10UnitSideScreen prefab;

        public static bool Registered { get; private set; }

        [HarmonyPatch(typeof(DetailsScreen), "OnPrefabInit")]
        public static class Install
        {
            public static void Postfix(DetailsScreen __instance)
            {
                try
                {
                    if (prefab == null)
                    {
                        GameObject go = new GameObject("Ic10UnitSideScreen", typeof(RectTransform));
                        go.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        prefab = go.AddComponent<Ic10UnitSideScreen>();
                        prefab.ContentContainer = go;
                    }
                    List<DetailsScreen.SideScreenRef> screens =
                        AccessTools.Field(typeof(DetailsScreen), "sideScreens")?.GetValue(__instance) as List<DetailsScreen.SideScreenRef>;
                    if (screens == null)
                    {
                        Mod5Log.Warn("unit side screen: DetailsScreen.sideScreens is not a List<SideScreenRef>; logic units cannot be set up");
                        return;
                    }
                    if (screens.Exists(s => s != null && s.screenPrefab is Ic10UnitSideScreen))
                    {
                        return;
                    }
                    screens.Add(new DetailsScreen.SideScreenRef
                    {
                        name = "Ic10UnitSideScreen",
                        screenPrefab = prefab,
                        offset = Vector2.zero,
                        tab = DetailsScreen.SidescreenTabTypes.Config,
                    });
                    Registered = true;
                }
                catch (Exception e)
                {
                    Mod5Log.Warn("unit side screen could not register: " + e.Message + "; logic units cannot be set up");
                }
            }
        }
    }
}
