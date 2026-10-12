using Ic10;
using KSerialization;
using STRINGS;
using TMPro;
using TUNING;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // Mod 5's logic bricks: Stationeers' logic memory, dial, switch, button and display as
    // ONI buildings. Each keeps one number. What a chip may read and write of it is decided in
    // the adapter (Ic10/Ic10OniBuilding.cs, MapBrick), where the offline tests reach it;
    // this file holds the number, saves it, and gives the player a way to change it.
    //
    // ART: every brick ships placeholder art of its own (Anim below).
    // Each subclass repeats the serialization attribute, as vanilla's Switch subclasses do
    // (LogicSwitch, CircuitSwitch); whether KSerialization needs it there was not read.
    [SerializationConfig(MemberSerialization.OptIn)]
    public abstract class Ic10Brick : KMonoBehaviour
    {
        [Serialize]
        protected double setting;

        [Serialize]
        protected int mode;

        public abstract Ic10OniBrickKind Kind { get; }

        public double Setting
        {
            get { return setting; }
        }

        public int Mode
        {
            get { return mode; }
        }

        public virtual void SetSetting(double value)
        {
            setting = value;
            Changed();
        }

        public virtual void SetMode(int value)
        {
            mode = value;
            Changed();
        }

        // The view the adapter maps. Press is null except on a button.
        public virtual Ic10OniBrick Describe()
        {
            return new Ic10OniBrick
            {
                Kind = Kind,
                GetSetting = () => setting,
                SetSetting = SetSetting,
                GetMode = () => mode,
                SetMode = SetMode,
            };
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Changed();
        }

        // "on" while the number is not zero.
        protected virtual void Changed()
        {
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            if (anim != null)
            {
                anim.Play(setting != 0.0 ? "on" : "off");
            }
        }
    }

    // LogicMemory. A chip writes it; the player has no control on it yet (Stationeers'
    // player turns it with a screwdriver, 1 or 100 a click).
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10MemoryBrick : Ic10Brick
    {
        public override Ic10OniBrickKind Kind
        {
            get { return Ic10OniBrickKind.Memory; }
        }
    }

    // LogicDial: the player turns it on a slider from 0 to Mode. Mode, the top of the range,
    // starts at 100 and is written by a chip; Stationeers' default is prefab data.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10DialBrick : Ic10Brick, ISingleSliderControl
    {
        public const string TitleKey = "STRINGS.UI.UISIDESCREENS.IC10DIAL.TITLE";
        public const string TooltipKey = "STRINGS.UI.UISIDESCREENS.IC10DIAL.TOOLTIP";

        public override Ic10OniBrickKind Kind
        {
            get { return Ic10OniBrickKind.Dial; }
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            mode = 100;
        }

        public string SliderTitleKey
        {
            get { return TitleKey; }
        }

        public string SliderUnits
        {
            get { return string.Empty; }
        }

        public int SliderDecimalPlaces(int index)
        {
            return 0;
        }

        public float GetSliderMin(int index)
        {
            return 0f;
        }

        public float GetSliderMax(int index)
        {
            return mode;
        }

        public float GetSliderValue(int index)
        {
            return (float)setting;
        }

        // The player's hand: LogicDial.InteractWith keeps it between 0 and Mode.
        public void SetSliderValue(float value, int index)
        {
            if (!float.IsNaN(value))
            {
                SetSetting((int)Mathf.Clamp(value, 0f, mode));
            }
        }

        public string GetSliderTooltipKey(int index)
        {
            return TooltipKey;
        }

        public string GetSliderTooltip(int index)
        {
            return Strings.Get(TooltipKey);
        }
    }

    // LogicSwitch: a lever the player throws at once, with no duplicant errand, as ONI's own
    // signal switch is thrown.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10SwitchBrick : Ic10Brick, IPlayerControlledToggle, ISim33ms
    {
        public const string TitleKey = "STRINGS.UI.UISIDESCREENS.IC10SWITCH.TITLE";

        public override Ic10OniBrickKind Kind
        {
            get { return Ic10OniBrickKind.Switch; }
        }

        public string SideScreenTitleKey
        {
            get { return TitleKey; }
        }

        public bool ToggleRequested { get; set; }

        public void ToggledByPlayer()
        {
            SetSetting(setting != 0.0 ? 0.0 : 1.0);
        }

        public bool ToggledOn()
        {
            return setting != 0.0;
        }

        // A click while the game is paused only sets ToggleRequested
        // (PlayerControlledToggleSideScreen.RequestToggle); the building acts on it at its
        // next tick, as LogicSwitch.Sim33ms does.
        public void Sim33ms(float dt)
        {
            if (ToggleRequested)
            {
                ToggledByPlayer();
                ToggleRequested = false;
                GetSelectable().SetStatusItem(Db.Get().StatusItemCategories.Main, null);
            }
        }

        public KSelectable GetSelectable()
        {
            return GetComponent<KSelectable>();
        }
    }

    // LogicButton: 1 from the press until 550 ms of game time have passed
    // (LogicButton.WaitThenStop), and it cannot be pressed again while it is down.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10ButtonBrick : Ic10Brick, ISidescreenButtonControl, ISim33ms
    {
        public const string ButtonKey = "STRINGS.UI.UISIDESCREENS.IC10BUTTON.BUTTON";
        public const string TooltipKey = "STRINGS.UI.UISIDESCREENS.IC10BUTTON.TOOLTIP";
        public const float HeldSeconds = 0.55f;

        private float held;

        public override Ic10OniBrickKind Kind
        {
            get { return Ic10OniBrickKind.Button; }
        }

        public override Ic10OniBrick Describe()
        {
            Ic10OniBrick brick = base.Describe();
            brick.SetSetting = null;
            brick.Press = Press;
            return brick;
        }

        protected override void OnSpawn()
        {
            // A button saved while down comes back up.
            setting = 0.0;
            base.OnSpawn();
        }

        public void Press()
        {
            if (setting == 0.0)
            {
                held = 0f;
                SetSetting(1.0);
            }
        }

        public void Sim33ms(float dt)
        {
            if (setting != 0.0)
            {
                held += dt;
                if (held >= HeldSeconds)
                {
                    SetSetting(0.0);
                }
            }
        }

        public string SidescreenButtonText
        {
            get { return Strings.Get(ButtonKey); }
        }

        public string SidescreenButtonTooltip
        {
            get { return Strings.Get(TooltipKey); }
        }

        // The interface gives this a default body, which the game's runtime cannot use from
        // a mod compiled for it; stated here instead.
        public string SidescreenTitle
        {
            get { return null; }
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride)
        {
        }

        public bool SidescreenEnabled()
        {
            return true;
        }

        public bool SidescreenButtonInteractable()
        {
            return setting == 0.0;
        }

        public void OnSidescreenButtonPressed()
        {
            Press();
        }

        public int HorizontalGroupID()
        {
            return -1;
        }

        public int ButtonSideScreenSortOrder()
        {
            return 20;
        }
    }

    // LogicDisplay: the number written to it, drawn over the building in the form Mode
    // chooses (Ic10Display.Format). Dark while switched off or unpowered, as
    // LogicDisplay.SetDisplay is. The player steps through the forms with a button.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10DisplayBrick : Ic10Brick, ISidescreenButtonControl, ISim200ms
    {
        public const string ButtonKey = "STRINGS.UI.UISIDESCREENS.IC10DISPLAY.BUTTON";
        public const string TooltipKey = "STRINGS.UI.UISIDESCREENS.IC10DISPLAY.TOOLTIP";

        public static readonly string[] ModeNames =
        {
            "Default", "Percent", "Power", "Kelvin", "Celsius", "Meters", "Credits", "Seconds", "Minutes",
            "Days", "String", "Fahrenheit", "Litres", "Mol", "Pa", "Newtons", "Degrees",
        };

        // Color, a ColorType number.
        [Serialize]
        private int color = Ic10Display.DefaultColor;

        private Operational operational;
        private TextMeshProUGUI label;
        private string shown;
        private string shownAnim;
        private int shownColor = -1;

        public int Color
        {
            get { return color; }
        }

        public void SetColor(int value)
        {
            color = value;
            Redraw();
        }

        public override Ic10OniBrick Describe()
        {
            Ic10OniBrick brick = base.Describe();
            brick.GetColor = () => color;
            brick.SetColor = SetColor;
            return brick;
        }

        public override Ic10OniBrickKind Kind
        {
            get { return Ic10OniBrickKind.Display; }
        }

        // The text it would show when lit.
        public string Text
        {
            get { return Ic10Display.Format(setting, mode); }
        }

        public bool Lit
        {
            get { return operational == null || operational.IsOperational; }
        }

        // What the label in the world actually holds, to report when a display shows nothing.
        public string LabelState
        {
            get
            {
                if (label == null)
                {
                    return "no label";
                }
                Vector3[] corners = new Vector3[4];
                label.rectTransform.GetWorldCorners(corners);
                return "lit " + Lit + ", text '" + label.text + "', font " + (label.font != null ? label.font.name : "null")
                    + ", canvas " + (label.canvas != null ? label.canvas.name : "none") + ", active " + label.isActiveAndEnabled
                    + ", world size " + (corners[2] - corners[0]) + ", text height " + label.textBounds.size.y * label.transform.lossyScale.y
                    + ", alpha " + label.color.a + ", anim " + shownAnim;
            }
        }

        // The text on the label, or null without one.
        public string LabelText
        {
            get { return label != null ? label.text : null; }
        }

        protected override void OnSpawn()
        {
            operational = GetComponent<Operational>();
            // On the world-space UI canvas, as vanilla's progress bars and harvest icons are
            // (ProgressBar.CreateProgressBar). A TextMeshPro mesh parented to the building drew
            // nothing: the base camera renders the default layer into Klei's multiple render
            // targets (CameraController, MultipleRenderTarget), and the text never reached the
            // screen though it was lit, sized and holding its text. It sits on the black panel of StructureConsoleLED5's model: the
            // panel's centre is 0.35 of a cell above the origin and it is 0.9 by 0.25 cells
            // (measured from the packed kanim). The text shrinks to fit it.
            GameObject canvas = GameScreenManager.Instance != null ? GameScreenManager.Instance.worldSpaceCanvas : null;
            if (canvas != null)
            {
                GameObject child = new GameObject("Ic10DisplayText", typeof(RectTransform));
                child.transform.SetParent(canvas.transform, false);
                // 100 canvas units to the cell, whatever the canvas's own scale.
                child.transform.localScale = Vector3.one * (0.01f / canvas.transform.lossyScale.x);
                child.transform.SetPosition(transform.GetPosition() + new Vector3(0f, 0.35f, 0f));
                label = child.AddComponent<TextMeshProUGUI>();
                if (Localization.FontAsset != null)
                {
                    label.font = Localization.FontAsset;
                }
                label.raycastTarget = false;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.enableAutoSizing = true;
                label.fontSizeMin = 5f;
                label.fontSizeMax = 220f;
                label.rectTransform.sizeDelta = new Vector2(80f, 20f);
                label.fontStyle = FontStyles.Bold;
                label.outlineWidth = 0.25f;
                label.outlineColor = new Color32(0, 0, 0, 255);
            }
            base.OnSpawn();
        }

        // The label lives on the shared canvas, not under the building.
        protected override void OnCleanUp()
        {
            if (label != null)
            {
                Destroy(label.gameObject);
                label = null;
            }
            base.OnCleanUp();
        }

        // Not the bricks' "on while the number is not zero": the model's on/off switch, as
        // SwitchOnOff.RefreshColorState paints it: red while switched off, plain green while
        // switched on without power (On), lit green while on and powered (OnPowered). Its
        // blinking error colours are not drawn: the display has no Error.
        protected override void Changed()
        {
            Redraw();
        }

        // Power and the enable button change without telling this component.
        public void Sim200ms(float dt)
        {
            Redraw();
            if (operational != null)
            {
                operational.SetActive(operational.IsOperational);
            }
        }

        private void Redraw()
        {
            if (label == null)
            {
                return;
            }
            bool lit = Lit;
            BuildingEnabledButton button = GetComponent<BuildingEnabledButton>();
            string state = button != null && !button.IsEnabled ? "off" : lit ? "on" : "on_unpowered";
            if (state != shownAnim)
            {
                shownAnim = state;
                KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
                if (anim != null)
                {
                    anim.Play(state);
                }
            }
            if (color != shownColor)
            {
                shownColor = color;
                float r, g, b;
                Ic10Display.Rgb(color, out r, out g, out b);
                // Lifted toward white and outlined: the colour as given read as dark green on the
                // black panel.
                label.color = UnityEngine.Color.Lerp(new UnityEngine.Color(r, g, b, 1f), UnityEngine.Color.white, 0.45f);
            }
            string text = lit ? Text : string.Empty;
            if (text != shown)
            {
                shown = text;
                label.text = text;
            }
        }

        public string SidescreenButtonText
        {
            get { return string.Format(Strings.Get(ButtonKey), ModeNames[Mathf.Clamp(mode, 0, ModeNames.Length - 1)]); }
        }

        public string SidescreenButtonTooltip
        {
            get { return Strings.Get(TooltipKey); }
        }

        // The interface gives this a default body, which the game's runtime cannot use from
        // a mod compiled for it; stated here instead.
        public string SidescreenTitle
        {
            get { return null; }
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride)
        {
        }

        public bool SidescreenEnabled()
        {
            return true;
        }

        public bool SidescreenButtonInteractable()
        {
            return true;
        }

        public void OnSidescreenButtonPressed()
        {
            SetMode((mode + 1) % Ic10Display.ModeCount);
        }

        public int HorizontalGroupID()
        {
            return -1;
        }

        public int ButtonSideScreenSortOrder()
        {
            return 20;
        }
    }

    // The five buildings. One shape for all: one cell, no automation port, buildable from the
    // start in Automation, shown in the data overlay. Only the display takes power.
    public abstract class Ic10BrickConfig : IBuildingConfig
    {
        protected abstract string BrickId { get; }

        // The building's kanim. Each brick and unit ships its own placeholder art, with the
        // "on" and "off" it plays. The default is vanilla's automation switch, which the display used until it
        // had art.
        protected virtual string Anim
        {
            get { return "switchdupecontrolledpower_kanim"; }
        }

        protected virtual float Watts
        {
            get { return 0f; }
        }

        // In cells. 2 only for the IC Logic Mirror, whose left cell is its input and right
        // cell its output; the origin of an even-width building
        // is its left cell.
        protected virtual int Width
        {
            get { return 1; }
        }

        protected abstract void AddBrick(GameObject go);

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(BrickId, Width, 1,
                Anim, 30, 30f, TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER0,
                MATERIALS.REFINED_METALS, 1600f, BuildLocationRule.Anywhere,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NONE);
            def.Overheatable = false;
            def.Floodable = false;
            def.Entombable = true;
            def.ViewMode = Ic10DataOverlay.ID;
            def.AudioCategory = "Metal";
            def.SceneLayer = Grid.SceneLayer.Building;
            if (Watts > 0f)
            {
                def.RequiresPowerInput = true;
                def.AddLogicPowerPort = false;
                def.EnergyConsumptionWhenActive = Watts;
                def.PowerInputOffset = new CellOffset(0, 0);
                def.SelfHeatKilowattsWhenActive = Watts / 1000f;
                def.ExhaustKilowattsWhenActive = 0f;
            }
            def.AddSearchTerms(SEARCH_TERMS.AUTOMATION);
            return def;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            AddBrick(go);
            go.AddOrGet<UserNameable>();
        }
    }

    public sealed class Ic10MemoryConfig : Ic10BrickConfig
    {
        public const string Id = "Ic10Memory";

        protected override string BrickId
        {
            get { return Id; }
        }

        protected override string Anim
        {
            get { return "StructureLogicMemory_kanim"; }
        }

        protected override void AddBrick(GameObject go)
        {
            go.AddOrGet<Ic10MemoryBrick>();
        }
    }

    public sealed class Ic10DialConfig : Ic10BrickConfig
    {
        public const string Id = "Ic10Dial";

        protected override string BrickId
        {
            get { return Id; }
        }

        protected override string Anim
        {
            get { return "StructureLogicDial_kanim"; }
        }

        protected override void AddBrick(GameObject go)
        {
            go.AddOrGet<Ic10DialBrick>();
        }
    }

    public sealed class Ic10SwitchConfig : Ic10BrickConfig
    {
        public const string Id = "Ic10Switch";

        protected override string BrickId
        {
            get { return Id; }
        }

        protected override string Anim
        {
            get { return "StructureLogicSwitch_kanim"; }
        }

        protected override void AddBrick(GameObject go)
        {
            go.AddOrGet<Ic10SwitchBrick>();
        }
    }

    public sealed class Ic10ButtonConfig : Ic10BrickConfig
    {
        public const string Id = "Ic10Button";

        protected override string BrickId
        {
            get { return Id; }
        }

        protected override string Anim
        {
            get { return "StructureLogicButton_kanim"; }
        }

        protected override void AddBrick(GameObject go)
        {
            go.AddOrGet<Ic10ButtonBrick>();
        }
    }

    public sealed class Ic10DisplayConfig : Ic10BrickConfig
    {
        public const string Id = "Ic10LogicDisplay";

        // Stationeers' Device.UsedPower default; the display's own figure is prefab data.
        public const float DisplayWatts = 10f;

        protected override string BrickId
        {
            get { return Id; }
        }

        protected override float Watts
        {
            get { return DisplayWatts; }
        }

        // Stationeers' LED Display (Small), the prefab LogicDisplay drives.
        protected override string Anim
        {
            get { return "StructureConsoleLED5_kanim"; }
        }

        protected override void AddBrick(GameObject go)
        {
            go.AddOrGet<Ic10DisplayBrick>();
        }
    }

    internal static class Ic10BrickStrings
    {
        internal static void Install()
        {
            Add(Ic10MemoryConfig.Id, "IC Memory",
                "Keeps one number.",
                "Holds a number an IC Housing writes with Setting, for another housing to read or for the same one to find again later.");
            Add(Ic10DialConfig.Id, "IC Dial",
                "A knob a program can read.",
                "Set by hand to a whole number from 0 to its range. A program reads it as Setting, and as Ratio, the share of the range. The range is Mode, 100 unless a program writes it.");
            Add(Ic10SwitchConfig.Id, "IC Switch",
                "A lever a program can read.",
                "On or off, thrown by hand. A program reads it as Setting, 1 or 0, and can throw it by writing Open.");
            Add(Ic10ButtonConfig.Id, "IC Button",
                "A push button a program can read.",
                "Reads as Setting 1 for about half a second after it is pressed, then 0. A program can press it by writing Activate.");
            Add(Ic10DisplayConfig.Id, "IC Display",
                "Shows a number.",
                "Shows the number a program writes to Setting. Mode chooses the form: a bare number, a percentage, watts, kelvin, litres and more. Needs power.");

            Strings.Add(Ic10DialBrick.TitleKey, "Dial");
            Strings.Add(Ic10DialBrick.TooltipKey, "The number a program reads from this dial as Setting");
            Strings.Add(Ic10SwitchBrick.TitleKey, "Switch");
            Strings.Add(Ic10ButtonBrick.ButtonKey, "Press");
            Strings.Add(Ic10ButtonBrick.TooltipKey, "A program reads Setting 1 for about half a second");
            Strings.Add(Ic10DisplayBrick.ButtonKey, "Form: {0}");
            Strings.Add(Ic10DisplayBrick.TooltipKey, "Step to the next form the number is shown in");
        }

        private static void Add(string id, string name, string desc, string effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", name);
            Strings.Add(prefix + "DESC", desc);
            Strings.Add(prefix + "EFFECT", effect);
            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"), id,
                TUNING.BUILDINGS.PlanSubcategoryName.logicmanager.ToString());
        }
    }
}
