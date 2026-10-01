using System.Collections.Generic;
using KSerialization;
using OniFramework;
using STRINGS;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// THE DISSOLVED GAS SENSOR: reads how much gas is dissolved in the liquid at its own cell and
    /// switches a logic wire on a threshold. It is the instrument that makes dissolved gas usable in an
    /// ordinary game, and without it dissolved gas is a quantity only a rig can see.
    ///
    /// WHY IT HAS TO EXIST. Carbonating a pond is a PROCESS: bubble carbon dioxide through cold
    /// water under pressure and the concentration climbs toward its Henry's-law ceiling over
    /// minutes. Something has to decide when it is done -- when to stop bubbling, when to start
    /// pumping the finished mixture out. Every other quantity ONI produces that a player must act
    /// on has a sensor for exactly this reason (Pressure, Temperature, Element, Germ), and
    /// dissolved gas did not.
    ///
    /// WHAT IT READS. <c>Solubility.ConcentrationGramsPerKg</c> at <c>Grid.PosToCell(this)</c>:
    /// grams of the selected gas per kilogram of the liquid in that one cell. The unit is the one
    /// the subject is quoted in everywhere else -- soda is 6-8 g/kg, water in equilibrium with
    /// ordinary room air about 0.0005 g/kg -- so a player reads the number rather than converting
    /// it.
    ///
    /// WHICH GAS, chosen with vanilla's own <c>Filterable</c> dropdown, the same control the
    /// Element Sensor uses, set to the GAS list because what is dissolved is a gas even though
    /// the sensor stands in a liquid. Five gases have a dissolved lane (<c>DissolvedGas</c>:
    /// carbon dioxide, oxygen, chlorine, hydrogen, methane); pick anything else and the sensor
    /// honestly reads zero rather than pretending.
    ///
    /// EIGHT SAMPLES, then a decision -- vanilla's own anti-chatter, copied from
    /// <c>LogicDiseaseSensor.Sim200ms</c> rather than invented: a threshold device that toggles on
    /// a single noisy reading makes a wire that clicks. At 200 ms a sample that is 1.6 s of
    /// averaging, which is nothing against a process that takes minutes.
    ///
    /// NOT POWERED, like every vanilla logic sensor: <c>AlwaysOperational</c>, no
    /// <c>RequiresPowerInput</c>. Sensors in this game are free to run; what they switch is not.
    /// </summary>
    public class DissolvedGasSensorConfig : IBuildingConfig
    {
        internal const string Id = "DissolvedGasSensor";

        /// <summary>Grams per kilogram. The slider's top end: comfortably above soda's 6-8 and
        /// above chlorine's ceiling in cold water, so no real process runs off the end of it.</summary>
        internal const float RangeMaxGPerKg = 20f;

        /// <summary>Where a new sensor starts: roughly half of soda strength, so the first thing
        /// a player does with it is move it.</summary>
        internal const float DefaultThresholdGPerKg = 4f;

        public override BuildingDef CreateBuildingDef()
        {
            // Vanilla's own world liquid sensor art and shape -- this is that instrument, reading
            // a different property of the same liquid.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1,
                "world_liquid_sensor_kanim", 30, 30f, TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER0,
                MATERIALS.REFINED_METALS, 1600f, BuildLocationRule.Anywhere,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NONE);

            def.Overheatable = false;
            def.Floodable = false;
            def.Entombable = true;
            def.ViewMode = OverlayModes.Logic.ID;
            def.AudioCategory = "Metal";
            def.SceneLayer = Grid.SceneLayer.Building;

            // Vanilla's logic sensors are free to run -- LogicDiseaseSensorConfig sets exactly
            // this. Nothing here draws power, so there is nothing for an Operational requirement
            // to gate on, and a sensor that needed one would report red in a working vessel.
            def.AlwaysOperational = true;
            def.LogicOutputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.OutputPort(LogicSwitch.PORT_ID, new CellOffset(0, 0),
                    "DISSOLVED GAS",
                    "Sends a GREEN SIGNAL while the dissolved concentration is on the chosen side "
                    + "of the threshold",
                    "Sends a RED SIGNAL while it is not",
                    show_wire_missing_icon: true),
            };

            SoundEventVolumeCache.instance.AddVolume("world_liquid_sensor_kanim", "PowerSwitch_on",
                NOISE_POLLUTION.NOISY.TIER3);
            SoundEventVolumeCache.instance.AddVolume("world_liquid_sensor_kanim", "PowerSwitch_off",
                NOISE_POLLUTION.NOISY.TIER3);
            GeneratedBuildings.RegisterWithOverlay(OverlayModes.Logic.HighlightItemIDs, Id);
            def.AddSearchTerms(SEARCH_TERMS.AUTOMATION);
            return def;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            // The gas dropdown. GAS rather than LIQUID: the sensor stands in water and reports on
            // what is dissolved IN it.
            go.AddOrGet<Filterable>().filterElementState = Filterable.ElementState.Gas;

            DissolvedGasSensor sensor = go.AddOrGet<DissolvedGasSensor>();
            sensor.Threshold = DefaultThresholdGPerKg;
            sensor.ActivateAboveThreshold = true;
            sensor.manuallyControlled = false;

            // STARTS OFF, unlike `Switch`'s own default of true. Vanilla's sensors inherit that
            // default and live with the 1.6 s it takes their sample window to correct it, because
            // a germ sensor briefly reading green costs nothing. This one gates a PUMP: at the
            // 10 kg/s a Liquid Pump moves, and at the speeds a rig runs at, a sensor that asserts
            // before it has measured anything empties part of the vessel it was installed to
            // protect. A sensor that has measured nothing should say nothing.
            sensor.defaultState = false;

            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>The sensor's behaviour. See <see cref="DissolvedGasSensorConfig"/>.</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class DissolvedGasSensor : Switch, ISaveLoadable, IThresholdSwitch, ISim200ms
    {
        /// <summary>Grams of dissolved gas per kilogram of liquid.</summary>
        [Serialize]
        [SerializeField]
        private float threshold = DissolvedGasSensorConfig.DefaultThresholdGPerKg;

        [Serialize]
        [SerializeField]
        private bool activateAboveThreshold = true;

        /// <summary>
        /// The gas being watched. Held here as well as on <c>Filterable</c> because the sensor is
        /// asked for its reading before the filter's own deserialisation has necessarily run, and
        /// a sensor that reads zero for one tick after a load would toggle its wire on load.
        /// </summary>
        [Serialize]
        [SerializeField]
        private int selectedGas = (int)SimHashes.CarbonDioxide;

        private const int WindowSize = 8;

        private readonly float[] samples = new float[WindowSize];

        private int sampleIdx;

        private bool sampled;

        private bool wasOn;

        private KBatchedAnimController animController;

        // The anim names `world_liquid_sensor_kanim` actually carries, read off vanilla's own
        // use of it in `LogicElementSensor.UpdateVisualState`: a transition, then a loop.
        private const string OnTransitionAnim = "on_pre";
        private const string OnLoopAnim = "on";
        private const string OffTransitionAnim = "on_pst";
        private const string OffLoopAnim = "off";

        public float Threshold
        {
            get { return threshold; }
            set { threshold = value; }
        }

        public bool ActivateAboveThreshold
        {
            get { return activateAboveThreshold; }
            set { activateAboveThreshold = value; }
        }

        /// <summary>The gas this sensor reports on.</summary>
        public SimHashes SelectedGas
        {
            get { return (SimHashes)selectedGas; }
            set { selectedGas = (int)value; }
        }

        /// <summary>
        /// The averaged reading, grams per kilogram. Before the first full window it is the
        /// average of what has been taken, so a sensor placed mid-run reports immediately rather
        /// than reading zero for 1.6 s.
        /// </summary>
        public float CurrentValue
        {
            get
            {
                int taken = sampled ? WindowSize : sampleIdx;
                if (taken <= 0)
                {
                    return 0f;
                }
                float sum = 0f;
                for (int i = 0; i < taken; i++)
                {
                    sum += samples[i];
                }
                return sum / taken;
            }
        }

        public float RangeMin
        {
            get { return 0f; }
        }

        public float RangeMax
        {
            get { return DissolvedGasSensorConfig.RangeMaxGPerKg; }
        }

        public LocString ThresholdValueName
        {
            get { return (LocString)"Dissolved concentration"; }
        }

        public string AboveToolTip
        {
            get { return "Send a green signal above {0}"; }
        }

        public string BelowToolTip
        {
            get { return "Send a green signal below {0}"; }
        }

        public ThresholdScreenLayoutType LayoutType
        {
            get { return ThresholdScreenLayoutType.SliderBar; }
        }

        public int IncrementScale
        {
            get { return 1; }
        }

        public NonLinearSlider.Range[] GetRanges
        {
            get { return NonLinearSlider.GetDefaultRange(RangeMax); }
        }

        public LocString Title
        {
            get { return (LocString)"Dissolved Gas Sensor"; }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            animController = GetComponent<KBatchedAnimController>();

            Filterable filterable = GetComponent<Filterable>();
            if (filterable != null)
            {
                // A sensor that has never been touched keeps its carbon dioxide default; one that
                // has been set, or loaded, keeps what it was set to.
                if (filterable.SelectedTag.IsValid
                    && ElementLoader.GetElement(filterable.SelectedTag) != null)
                {
                    selectedGas = (int)ElementLoader.GetElement(filterable.SelectedTag).id;
                }
                else
                {
                    filterable.SelectedTag = ElementLoader
                        .FindElementByHash((SimHashes)selectedGas).tag;
                }
                filterable.onFilterChanged += OnFilterChanged;
            }

            OnToggle += OnSwitchToggled;
            UpdateLogicCircuit();
            UpdateVisualState(force: true);
            wasOn = switchedOn;
        }

        private void OnFilterChanged(Tag tag)
        {
            Element element = ElementLoader.GetElement(tag);
            if (element == null)
            {
                return;
            }
            selectedGas = (int)element.id;
            // A new subject means the old samples describe nothing. Start the window again rather
            // than averaging one gas's concentration with another's.
            sampleIdx = 0;
            sampled = false;
        }

        public void Sim200ms(float dt)
        {
            int cell = Grid.PosToCell(this);
            samples[sampleIdx] = Grid.IsValidCell(cell)
                ? Solubility.ConcentrationGramsPerKg(cell, SelectedGas)
                : 0f;
            sampleIdx++;
            if (sampleIdx < WindowSize)
            {
                return;
            }
            sampleIdx = 0;
            sampled = true;

            float value = CurrentValue;
            bool wantOn = activateAboveThreshold ? value > threshold : value <= threshold;
            if (wantOn != IsSwitchedOn)
            {
                Toggle();
            }
        }

        private void OnSwitchToggled(bool toggledOn)
        {
            UpdateLogicCircuit();
            UpdateVisualState();
        }

        public float GetRangeMinInputField()
        {
            return RangeMin;
        }

        public float GetRangeMaxInputField()
        {
            return RangeMax;
        }

        public string Format(float value, bool units)
        {
            return value.ToString("0.##") + (units ? " g/kg" : string.Empty);
        }

        public float ProcessedSliderValue(float input)
        {
            return Mathf.Round(input * 100f) / 100f;
        }

        public float ProcessedInputValue(float input)
        {
            return input;
        }

        public LocString ThresholdValueUnits()
        {
            return (LocString)"g/kg";
        }

        private void UpdateLogicCircuit()
        {
            LogicPorts ports = GetComponent<LogicPorts>();
            if (ports != null)
            {
                ports.SendSignal(LogicSwitch.PORT_ID, switchedOn ? 1 : 0);
            }
        }

        private void UpdateVisualState(bool force = false)
        {
            if (animController == null || (wasOn == switchedOn && !force))
            {
                return;
            }
            wasOn = switchedOn;
            animController.Play(switchedOn ? OnTransitionAnim : OffTransitionAnim);
            animController.Queue(switchedOn ? OnLoopAnim : OffLoopAnim);
        }

        protected override void UpdateSwitchStatus()
        {
            StatusItem item = switchedOn
                ? Db.Get().BuildingStatusItems.LogicSensorStatusActive
                : Db.Get().BuildingStatusItems.LogicSensorStatusInactive;
            GetComponent<KSelectable>().SetStatusItem(Db.Get().StatusItemCategories.Power, item);
        }
    }

    /// <summary>Display strings and the build-menu entry for <see cref="DissolvedGasSensorConfig"/>.</summary>
    internal static class DissolvedGasSensorStrings
    {
        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS."
                + DissolvedGasSensorConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Dissolved Gas Sensor");
            Strings.Add(prefix + "DESC",
                "Measures how much gas is dissolved in the liquid it stands in, and sends a "
                + "signal on a threshold.");
            Strings.Add(prefix + "EFFECT",
                "Gas dissolved in water is invisible: a carbonated pond and a flat one look "
                + "exactly alike. This reads the concentration in grams per kilogram -- soda is "
                + "about 8 -- for any gas that dissolves, and switches a wire when the water "
                + "reaches the strength you asked for. Bubbling gas through cold water under "
                + "pressure is what puts it there; this is what tells you when to stop.");

            // Vanilla files every sensor under Automation / sensors, read out of
            // TUNING.BUILDINGS.PLANORDER rather than guessed.
            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"),
                DissolvedGasSensorConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.sensors.ToString());
        }
    }
}
