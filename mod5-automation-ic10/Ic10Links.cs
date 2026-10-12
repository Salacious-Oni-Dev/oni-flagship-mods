using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // Choosing what a chip's pins point at by clicking the building in the world, and seeing
    // those choices as lines.
    //
    // Six device pins do not need to fit on a four-sided cell: in Stationeers the IC Housing has ONE data connection (CircuitHousing.InputNetwork1),
    // and d0..d5 are addresses on that network, stepped through with the screwdriver on six
    // buttons (CircuitHousing.InteractWith: InteractableType.Button1..6 ->
    // Logicable._TryGetNextLogicDevice over InputNetwork1DevicesSorted). The logic units are
    // the same: their inputs and outputs are buildings on their cable, never wires of their
    // own. Mod 5 keeps that model (one data cable). What it changes is how a
    // pin is chosen: Stationeers steps through a list one press at a time and ONI's placeholder
    // picker was a list of names, both of which leave the player matching names to buildings.
    //
    //   Ic10LinkTool   a tool in ONI's own tool system (InterfaceTool, as MoveToLocationTool):
    //                  "Link" beside a pin in either editor hides the editor, turns the data
    //                  overlay on (which lights the cable's devices) and waits for a click on a
    //                  building. A building on the cable becomes that pin; anything else is
    //                  refused with the game's "Negative" sound. Right-click or Escape cancels
    //                  (ToolMenu returns any non-default tool to SelectTool). The editor comes
    //                  back either way.
    //   Ic10LinkLines  a line from a chip to every building its pins point at, labelled with
    //                  the pin, while the chip's editor is open, while the link tool is out, or
    //                  in the data overlay while the chip is selected. Hovering any building in
    //                  the data overlay draws the lines of every chip pointing at it, so "who
    //                  writes this pump" has an answer on screen.
    //
    // The choice is still limited to the cable's buildings, as the list was; nothing about
    // what a pin may reach changes. PLACEHOLDER DRAWING: the lines and labels are
    // immediate-mode GUI.
    public sealed class Ic10LinkSlot
    {
        public string Label;
        public int Current;
        public bool UnitsOnly;
        public Action<int> Set;
        public int Index;
    }

    public static class Ic10Links
    {
        private static readonly HashSet<KMonoBehaviour> Chips = new HashSet<KMonoBehaviour>();
        private static Ic10LinkLines lines;

        // Called by every housing and logic unit as it spawns and as it goes.
        public static void Register(KMonoBehaviour chip)
        {
            Chips.Add(chip);
            if (lines == null)
            {
                lines = new GameObject("Mod5Ic10LinkLines").AddComponent<Ic10LinkLines>();
            }
        }

        public static void Unregister(KMonoBehaviour chip)
        {
            Chips.Remove(chip);
        }

        public static IEnumerable<KMonoBehaviour> All
        {
            get { return Chips; }
        }

        // The pins of a housing or a logic unit, in the order and with the names its editor
        // shows them; empty for anything else. The unit side screen's device rows are these pins.
        public static List<Ic10LinkSlot> For(GameObject go)
        {
            List<Ic10LinkSlot> slots = new List<Ic10LinkSlot>();
            if (go == null)
            {
                return slots;
            }
            Ic10HousingBuilding housing = go.GetComponent<Ic10HousingBuilding>();
            if (housing != null && housing.Housing != null)
            {
                for (int i = 0; i < Ic10.Ic10Housing.SocketCount; i++)
                {
                    int socket = i;
                    string label = housing.Housing.GetDeviceLabel(i);
                    Add(slots, "d" + i + (string.IsNullOrEmpty(label) ? string.Empty : " " + label),
                        housing.GetSocket(i), false, v => housing.SetSocket(socket, v));
                }
                return slots;
            }
            Ic10UnitBuilding building = go.GetComponent<Ic10UnitBuilding>();
            Ic10.Ic10LogicUnit unit = building != null ? building.Unit : null;
            if (unit == null)
            {
                return slots;
            }
            Ic10.Ic10LogicReader reader = unit as Ic10.Ic10LogicReader;
            if (reader != null)
            {
                Add(slots, "Device", reader.Target, false, v => reader.Target = v);
            }
            Ic10.Ic10LogicSlotReader slotReader = unit as Ic10.Ic10LogicSlotReader;
            if (slotReader != null)
            {
                Add(slots, "Device", slotReader.Target, false, v => slotReader.Target = v);
            }
            Ic10.Ic10LogicWriter writer = unit as Ic10.Ic10LogicWriter;
            if (writer != null)
            {
                Add(slots, "Input", writer.InputUnit, true, v => writer.InputUnit = v);
                if (!(unit is Ic10.Ic10LogicBatchWriter))
                {
                    Add(slots, "To", writer.Target, false, v => writer.Target = v);
                }
            }
            Ic10.Ic10LogicMirror mirror = unit as Ic10.Ic10LogicMirror;
            if (mirror != null)
            {
                Add(slots, "Mirrors", mirror.Target, false, v => mirror.Target = v);
            }
            Ic10.Ic10LogicWriterSwitch writerSwitch = unit as Ic10.Ic10LogicWriterSwitch;
            if (writerSwitch != null)
            {
                Add(slots, "To", writerSwitch.Target, false, v => writerSwitch.ChooseTarget(v));
            }
            Ic10.Ic10LogicProcessor processor = unit as Ic10.Ic10LogicProcessor;
            if (processor != null)
            {
                Add(slots, "Input 1", processor.Input1, true, v => processor.Input1 = v);
                Add(slots, "Input 2", processor.Input2, true, v => processor.Input2 = v);
            }
            Ic10.Ic10LogicMathUnary unary = unit as Ic10.Ic10LogicMathUnary;
            if (unary != null)
            {
                Add(slots, "Input", unary.Input1, true, v => unary.Input1 = v);
            }
            Ic10.Ic10LogicSelect select = unit as Ic10.Ic10LogicSelect;
            if (select != null)
            {
                Add(slots, "Input 1", select.Input1, true, v => select.Input1 = v);
                Add(slots, "Input 2", select.Input2, true, v => select.Input2 = v);
                Add(slots, "Select", select.Selector, true, v => select.Selector = v);
            }
            return slots;
        }

        private static void Add(List<Ic10LinkSlot> slots, string label, int current, bool unitsOnly, Action<int> set)
        {
            slots.Add(new Ic10LinkSlot { Label = label, Current = current, UnitsOnly = unitsOnly, Set = set, Index = slots.Count });
        }

        // What a pin may point at: the buildings on the chip's cable, as each editor's list.
        public static List<KPrefabID> Candidates(GameObject chip, Ic10LinkSlot slot)
        {
            Ic10HousingBuilding housing = chip != null ? chip.GetComponent<Ic10HousingBuilding>() : null;
            if (housing != null)
            {
                return housing.SocketCandidates();
            }
            Ic10UnitBuilding unit = chip != null ? chip.GetComponent<Ic10UnitBuilding>() : null;
            return unit != null ? unit.Candidates(slot.UnitsOnly) : new List<KPrefabID>();
        }

        // Six colours a pin keeps wherever it is drawn: d0 green, d1 blue, d2 orange, d3 pink,
        // d4 yellow, d5 cyan. The unit pins take them in their row order.
        private static readonly Color[] Colours =
        {
            new Color32(0x5D, 0xC1, 0x55, 255),
            new Color32(0x4A, 0x9B, 0xF0, 255),
            new Color32(0xFF, 0x8A, 0x3C, 255),
            new Color32(0xF0, 0x6E, 0xC8, 255),
            new Color32(0xF2, 0xD3, 0x4A, 255),
            new Color32(0x4F, 0xD8, 0xD8, 255),
        };

        public static Color ColourOf(int index)
        {
            return Colours[((index % Colours.Length) + Colours.Length) % Colours.Length];
        }

        // The middle of a building as drawn: its collider, else its position.
        public static Vector3 Centre(GameObject go)
        {
            KBoxCollider2D box = go.GetComponent<KBoxCollider2D>();
            return box != null ? box.bounds.center : go.transform.GetPosition();
        }

        // The building of a candidate list at a cell, if any covers it.
        public static KPrefabID At(List<KPrefabID> candidates, int cell)
        {
            if (candidates == null || !Grid.IsValidCell(cell))
            {
                return null;
            }
            foreach (KPrefabID candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }
                Building building = candidate.GetComponent<Building>();
                if (building != null ? building.PlacementCellsContainCell(cell) : Grid.PosToCell(candidate) == cell)
                {
                    return candidate;
                }
            }
            return null;
        }
    }

    public sealed class Ic10LinkTool : InterfaceTool
    {
        public static Ic10LinkTool Instance { get; private set; }

        private GameObject source;
        private Ic10LinkSlot slot;
        private List<KPrefabID> candidates;
        private System.Action after;

        public static bool Active
        {
            get { return Instance != null && PlayerController.Instance != null && PlayerController.Instance.ActiveTool == Instance; }
        }

        public GameObject Source
        {
            get { return source; }
        }

        public Ic10LinkSlot Slot
        {
            get { return slot; }
        }

        // Hand the cursor over to choosing one pin of a chip. `after` runs once the tool is put
        // away, linked or cancelled; the editors come back in it.
        public static void Begin(GameObject chip, Ic10LinkSlot slot, System.Action after)
        {
            if (PlayerController.Instance == null || chip == null || slot == null)
            {
                if (after != null)
                {
                    after();
                }
                return;
            }
            if (Instance == null)
            {
                GameObject go = new GameObject("Mod5Ic10LinkTool");
                go.SetActive(false);
                go.transform.SetParent(PlayerController.Instance.transform, false);
                Instance = go.AddComponent<Ic10LinkTool>();
            }
            Instance.source = chip;
            Instance.slot = slot;
            Instance.candidates = Ic10Links.Candidates(chip, slot);
            Instance.after = after;
            Instance.viewMode = Ic10DataOverlay.ID;
            PlayerController.Instance.ActivateTool(Instance);
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            cursor = Assets.GetTexture("cursor_hand");
            cursorOffset = Vector2.zero;
        }

        protected override void OnDeactivateTool(InterfaceTool new_tool)
        {
            base.OnDeactivateTool(new_tool);
            System.Action done = after;
            after = null;
            source = null;
            slot = null;
            candidates = null;
            if (done != null)
            {
                done();
            }
        }

        public override void OnLeftClickDown(Vector3 cursor_pos)
        {
            base.OnLeftClickDown(cursor_pos);
            if (TryLinkAt(Grid.PosToCell(cursor_pos)))
            {
                KMonoBehaviour.PlaySound(GlobalAssets.GetSound("HUD_Click"));
                SelectTool.Instance.Activate();
            }
            else
            {
                KMonoBehaviour.PlaySound(GlobalAssets.GetSound("Negative"));
            }
        }

        // The click without the mouse, for tests: links the building on the cable at
        // `cell` to the pin and says whether there was one.
        public bool TryLinkAt(int cell)
        {
            KPrefabID hit = Ic10Links.At(candidates, cell);
            if (hit == null || slot == null)
            {
                return false;
            }
            slot.Set(hit.InstanceID);
            slot.Current = hit.InstanceID;
            return true;
        }

        // The words beside the cursor: what a click here would do.
        private void OnGUI()
        {
            if (!Active || slot == null || Camera.main == null)
            {
                return;
            }
            int cell = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            KPrefabID hit = Ic10Links.At(candidates, cell);
            string text;
            if (hit != null)
            {
                text = "Link " + slot.Label + " to " + STRINGS.UI.StripLinkFormatting(hit.gameObject.GetProperName());
            }
            else if (Grid.IsValidCell(cell) && Grid.Objects[cell, (int)ObjectLayer.Building] != null)
            {
                text = "Not on this chip's data cable";
            }
            else
            {
                text = (candidates == null || candidates.Count == 0
                    ? "Nothing on this chip's data cable to link."
                    : "Click a building on the cable for " + slot.Label + ".")
                    + "  Right-click or Esc: cancel.";
            }
            Vector2 mouse = Event.current.mousePosition;
            Ic10LinkLines.Tag(new Vector2(mouse.x + 20f, mouse.y + 20f), false, text,
                hit != null ? Ic10Links.ColourOf(slot.Index) : Color.white);
        }
    }

    // The lines. Drawn in screen space with one white texture rotated along each segment, so no
    // material or shader of the game's is needed. Each link is an arc rising from the chip and
    // falling to its target, higher the longer it is, with its label on top: buildings in one
    // row with the chip (the usual layout on a straight cable) would otherwise put every line on
    // the same straight path, each hiding the one before (d1's line would cover d0's). A dark outline keeps a line visible over any art, and a label is
    // coloured text on a dark box, since a tinted GUI box darkens its own text.
    public sealed class Ic10LinkLines : MonoBehaviour
    {
        private const float Width = 3f;
        private const float Outline = 2f;
        private const int Segments = 20;
        private static GUIStyle tagStyle;
        private static float cellPixels = 40f;
        private readonly Dictionary<GameObject, string> labels = new Dictionary<GameObject, string>();
        private readonly Dictionary<GameObject, Color> colours = new Dictionary<GameObject, Color>();

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Camera.main == null)
            {
                return;
            }
            GUI.depth = 10;
            cellPixels = Mathf.Abs(ScreenOf(Vector3.up).y - ScreenOf(Vector3.zero).y);
            GameObject chip = null;
            if (Ic10LinkTool.Active)
            {
                chip = Ic10LinkTool.Instance.Source;
            }
            bool overlay = OverlayScreen.Instance != null && OverlayScreen.Instance.GetMode() == Ic10DataOverlay.ID;
            if (chip == null && overlay && SelectTool.Instance != null && SelectTool.Instance.selected != null)
            {
                chip = SelectTool.Instance.selected.gameObject;
            }
            if (chip != null)
            {
                DrawFrom(chip, null);
                if (Ic10LinkTool.Active && Ic10LinkTool.Instance.Slot != null)
                {
                    Vector2 mouse = Event.current.mousePosition;
                    Arc(ScreenOf(Ic10Links.Centre(chip)), mouse, Ic10Links.ColourOf(Ic10LinkTool.Instance.Slot.Index) * new Color(1f, 1f, 1f, 0.6f));
                }
            }
            // Who points at the building under the cursor, in the data overlay.
            KSelectable hover = overlay && SelectTool.Instance != null ? SelectTool.Instance.hover : null;
            if (hover != null && !Ic10LinkTool.Active)
            {
                foreach (KMonoBehaviour other in Ic10Links.All)
                {
                    if (other != null && other.gameObject != chip)
                    {
                        DrawFrom(other.gameObject, hover.gameObject);
                    }
                }
            }
        }

        // Every pin of `chip` that points somewhere, or only those pointing at `onlyTo`.
        private void DrawFrom(GameObject chip, GameObject onlyTo)
        {
            labels.Clear();
            colours.Clear();
            foreach (Ic10LinkSlot slot in Ic10Links.For(chip))
            {
                KPrefabID target = slot.Current > 0 ? Ic10Devices.Building(slot.Current) : null;
                if (target == null || (onlyTo != null && target.gameObject != onlyTo))
                {
                    continue;
                }
                GameObject to = target.gameObject;
                string label;
                labels[to] = labels.TryGetValue(to, out label) ? label + ", " + slot.Label : slot.Label;
                if (!colours.ContainsKey(to))
                {
                    colours[to] = Ic10Links.ColourOf(slot.Index);
                }
            }
            if (labels.Count == 0)
            {
                return;
            }
            Vector2 from = ScreenOf(Ic10Links.Centre(chip));
            foreach (KeyValuePair<GameObject, string> pair in labels)
            {
                Vector2 to = ScreenOf(Ic10Links.Centre(pair.Key));
                Color colour = colours[pair.Key];
                Vector2 top = Arc(from, to, colour);
                // On the top of the arc, which no other arc of this chip shares; "who points
                // here" names the chip as well.
                Tag(top, true, onlyTo == null ? pair.Value
                    : STRINGS.UI.StripLinkFormatting(chip.GetProperName()) + ": " + pair.Value, colour);
            }
        }

        private static Vector2 ScreenOf(Vector3 world)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        private static float Scale
        {
            get { return Mathf.Max(1f, Screen.height / 1080f); }
        }

        // A quadratic curve from a to b whose middle stands above the straight line by 0.3
        // of its length; returns that middle, where the label goes. Outline first, then colour.
        private static Vector2 Arc(Vector2 a, Vector2 b, Color colour)
        {
            // The top at least 1.2 cells above the ends, so a neighbour's label clears a one-cell
            // building at any zoom (a fixed 40 px sat on the sensor's antenna).
            float rise = Mathf.Max(Vector2.Distance(a, b) * 0.6f, 2.4f * cellPixels);
            Vector2 control = (a + b) * 0.5f + new Vector2(0f, -rise);
            Vector2[] points = new Vector2[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                points[i] = (1f - t) * (1f - t) * a + 2f * (1f - t) * t * control + t * t * b;
            }
            float width = Width * Scale;
            Color shadow = new Color(0f, 0f, 0f, 0.8f * colour.a);
            for (int i = 0; i < Segments; i++)
            {
                Line(points[i], points[i + 1], width + 2f * Outline * Scale, shadow);
            }
            for (int i = 0; i < Segments; i++)
            {
                Line(points[i], points[i + 1], width, colour);
            }
            return points[Segments / 2];
        }

        private static void Line(Vector2 a, Vector2 b, float width, Color colour)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f)
            {
                return;
            }
            Matrix4x4 matrix = GUI.matrix;
            Color before = GUI.color;
            GUI.color = colour;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            // Half a width longer at each end, so the segments of a curve meet without gaps.
            GUI.DrawTexture(new Rect(a.x - width * 0.5f, a.y - width * 0.5f, length + width, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = before;
        }

        // Coloured bold text on a dark box: centred above `at` when `above`, else with its top
        // left corner at `at` (the cursor label).
        public static void Tag(Vector2 at, bool above, string text, Color colour)
        {
            if (tagStyle == null)
            {
                tagStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                };
            }
            tagStyle.fontSize = Mathf.RoundToInt(14f * Scale);
            tagStyle.normal.textColor = new Color(colour.r, colour.g, colour.b, 1f);
            GUIContent content = new GUIContent(text);
            Vector2 size = tagStyle.CalcSize(content) + new Vector2(10f, 4f) * Scale;
            Rect rect = above
                ? new Rect(at.x - size.x * 0.5f, at.y - size.y - 4f * Scale, size.x, size.y)
                : new Rect(at.x, at.y, size.x, size.y);
            Color before = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
            GUI.Label(rect, content, tagStyle);
        }
    }
}
