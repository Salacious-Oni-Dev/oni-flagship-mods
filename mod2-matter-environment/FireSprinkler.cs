using System.Collections.Generic;
using KSerialization;
using OniFramework;
using STRINGS;
using TUNING;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// THE FIRE SPRINKLER: a plumbed ceiling head.
    ///
    /// <b>A 1x1 HEAD IN THE CEILING.</b> The head IS a ceiling tile: built like
    /// Klei's Hydroponic Farm (<c>HydroponicFarmConfig</c>: <c>BuildLocationRule.Tile</c>,
    /// <c>BuildingTemplates.CreateFoundationTileDef</c>, a liquid input in the tile at (0,0)), so it
    /// replaces a ceiling tile in place, and its cell is solid (<c>SimCellOccupier</c>). It sprays one
    /// cell: the open cell directly under it. A wider spray is several heads side by side.
    ///
    /// <b>ONE LOGIC PORT: THE FORCE-OPEN INPUT.</b> One cell carries one
    /// logic port (Klei's 1x1 tiles with logic, e.g. <c>FloorSwitchConfig</c>, have exactly one), so
    /// the 1x2's "spraying" output is gone and the input stays: it is the safety feature. Once IC10
    /// lands, one port can carry several outputs, and the spraying state can come back on it.
    ///
    /// <b>TRIGGER: A VIRTUAL GLASS BULB.</b> The head's tile is 50 kg of
    /// metal bonded into the ceiling and would track the ceiling, never the gas; a real bulb is a few
    /// grams in the gas. So the bulb is modelled, not built: the standard sprinkler response model
    /// (Heskestad &amp; Smith 1976, FMRC; the RTI of NFPA 13 and UL 199/1626),
    /// <c>dTb/dt = sqrt(u) / RTI * (Tgas - Tb)</c>, heated by the gas of the open cell under the
    /// head and never through the ceiling. RTI is 50 (m s)^0.5, the quick-response limit (NFPA 13
    /// "quick response", RTI &lt;= 50; residential heads are quick-response, UL 1626), and u is a
    /// fixed 1 m/s ceiling-jet speed (ONI's gas has no velocity), so the bulb's time constant is
    /// 50 s. Above the setpoint (default 340 K, about the common 68 C / 155 F rating, set in ONI's
    /// threshold side screen) the head opens. A green automation input forces it open whatever the
    /// bulb reads; red or no wire leaves it automatic, so an unwired head still protects its room.
    /// That is why this is not a <c>LogicOperationalController</c>, whose red would switch the bulb
    /// off.
    ///
    /// <b>CLOSE: THE ROOM'S MEAN, NOT THE BULB.</b> The bulb sits in the cell the
    /// spray falls through, so the head's own water would chill it and shut the head while the room is
    /// still hot (in one test: closed at bulb 297.7 K, room mean 343.6 K, hottest 502.2 K), and
    /// the row under the ceiling cools against the ceiling first (another: that row 325.5 K, the room
    /// 457.1 K mean, 804.3 K hottest). An open head closes only once the heat-capacity-weighted MEAN
    /// gas temperature of its room -- every open cell connected to the cell under the head, by a
    /// flood fill bounded at <see cref="MaxRoomCells"/> -- has read below the setpoint, with the
    /// input red, for <see cref="CloseHoldSeconds"/>.
    ///
    /// <b>RESET.</b> It sprays while triggered, and closes once its room's mean gas
    /// temperature has read below the setpoint, with the input red, for
    /// <see cref="CloseHoldSeconds"/> (see CLOSE above).
    ///
    /// <b>THE SPRAY.</b> 1.0 kg/s (a real residential head, K-factor 5.6 at 7 psi) of
    /// whatever liquid the pipe delivered (no filter), per head, drawn from a 10 kg
    /// insulated store and dropped into the open cell under the head as vanilla falling liquid (<c>FallingWater.AddParticle</c>). A particle carries the mass,
    /// temperature and germs it was drawn with and hands them to the sim where it lands
    /// (<c>AddRemoveSubstance</c>), so nothing is created or deleted. It exchanges no heat while it
    /// falls: the water cools a fire where it lands, pools and boils.
    ///
    /// <b>NO EXTINGUISH RULE.</b> This building never touches
    /// <c>Ignition</c>. Stationeers' water-charged extinguisher suppresses the cell and deletes 20 kJ
    /// (<c>FireExtinguisher.InternalsCanSuppressFire</c> counts water as inert); this does neither.
    /// A fire goes out here only because the water takes its cells and its heat.
    ///
    /// <b>POWER: NONE.</b> A sprinkler head is a passive valve, and one that needs power fails in
    /// exactly the fire that cut the power.
    ///
    /// <b>ART:</b> donor kanim <c>farmtilehydroponicrotating_kanim</c> (the Hydroponic Farm tile the
    /// footprint is copied from). On the public kanim-needed list until it has art of its own: a
    /// head that sits in the wall layer.
    /// </summary>
    public class FireSprinklerConfig : IBuildingConfig
    {
        internal const string Id = "FireSprinkler";

        internal const string DonorKanim = "farmtilehydroponicrotating_kanim";

        internal static readonly HashedString ForcePortId = "FireSprinklerForce";

        /// <summary>One liquid pipe packet. The store between the pipe and the head.</summary>
        internal const float StorageKg = 10f;

        public override BuildingDef CreateBuildingDef()
        {
            // A tile, as the Hydroponic Farm is (HydroponicFarmConfig: 1x1 BuildLocationRule.Tile,
            // CreateFoundationTileDef, a liquid input at (0,0)), and without its FlipV: a head only
            // hangs from a ceiling and only sprays down.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1, DonorKanim, 30, 10f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.REFINED_METALS, 1600f,
                BuildLocationRule.Tile, TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NONE);
            BuildingTemplates.CreateFoundationTileDef(def);

            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 0);
            def.RequiresPowerInput = false;
            def.Floodable = false;
            def.Overheatable = false;
            def.Entombable = false;
            def.UseStructureTemperature = false;
            def.BaseTimeUntilRepair = -1f;
            def.ConstructionOffsetFilter = BuildingDef.ConstructionOffsetFilter_OneDown;
            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "Metal";
            def.SceneLayer = Grid.SceneLayer.TileMain;

            // The one port a 1x1 cell can carry (FloorSwitchConfig puts its port at (0,0) the same
            // way). See the class comment for why it is the input.
            def.LogicInputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.InputPort(ForcePortId, new CellOffset(0, 0), FireSprinkler.ForcePortName,
                    FireSprinkler.ForcePortActive, FireSprinkler.ForcePortInactive),
            };

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs, Id);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            // The vent's plumbing (LiquidVentConfig): a liquid ConduitConsumer into a default
            // storage. Insulated here, so the water keeps the pipe's temperature until it sprays
            // and the head's own body never warms the store.
            Storage storage = BuildingTemplates.CreateDefaultStorage(go);
            storage.capacityKg = StorageKg;
            storage.showInUI = true;
            storage.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);

            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.ignoreMinMassCheck = true;
            consumer.capacityKG = StorageKg;
            consumer.forceAlwaysSatisfied = true;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Store;

            go.AddOrGet<CopyBuildingSettings>();

            // The tile half (HydroponicFarmConfig.ConfigureBuildingTemplate): the cell becomes the
            // head's construction material, and its temperature is the cell's.
            SimCellOccupier occupier = go.AddOrGet<SimCellOccupier>();
            occupier.doReplaceElement = true;
            occupier.notifyOnMelt = true;
            go.AddOrGet<TileTemperature>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            RequireInputs inputs = go.GetComponent<RequireInputs>();
            if (inputs != null)
            {
                // An empty pipe is a dry head, shown by its own status, not a disabled building.
                inputs.SetRequirements(power: false, conduit: false);
            }
            go.AddOrGet<FireSprinklerComponent>();
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>The sprinkler's behaviour. See <see cref="FireSprinklerConfig"/>.</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class FireSprinklerComponent : KMonoBehaviour, ISim200ms, IThresholdSwitch
    {
        /// <summary>The default setpoint, about 340 K.</summary>
        internal const float DefaultSetpointK = 340f;

        /// <summary>The side screen's range, K.</summary>
        internal const float MinSetpointK = 273.15f;
        internal const float MaxSetpointK = 1273.15f;

        /// <summary>Spray rate, kg/s. A residential head, K = 5.6 gpm/psi^0.5 at the 7 psi
        /// minimum: 14.8 gpm, 0.93 L/s.</summary>
        internal const float SprayKgPerSecond = 1f;

        /// <summary>How long the room's mean gas temperature must read below the setpoint, input red,
        /// before an open head closes: a few seconds.</summary>
        internal const float CloseHoldSeconds = 5f;

        /// <summary>The bulb's Response Time Index, (m s)^0.5: the quick-response limit (NFPA 13;
        /// UL 1626 residential heads are quick-response).</summary>
        internal const float BulbRti = 50f;

        /// <summary>The gas speed past the bulb, m/s: a fixed ceiling-jet speed, since ONI's gas has
        /// no velocity. With <see cref="BulbRti"/> the bulb's time constant is RTI / sqrt(u) = 50 s.</summary>
        internal const float BulbGasSpeed = 1f;

        /// <summary>The most cells the close rule's flood fill visits. A room larger than this is open
        /// space as far as one head is concerned, and the mean is over the cells nearest it.</summary>
        internal const int MaxRoomCells = 2048;

        private static readonly HashedString[] SprayStartAnims = { "working_pre", "working_loop" };

        private bool loggedHeadGeometry;

        [Serialize]
        private float setpointK = DefaultSetpointK;

        [Serialize]
        private bool spraying;

        [Serialize]
        private float belowForSeconds;

        /// <summary>The virtual bulb's temperature, K; saved, so a load does not reset it. 0 means
        /// "not yet set": the first tick starts it at the gas it hangs in.</summary>
        [Serialize]
        private float bulbK;

        /// <summary>Liquid sprayed since spawn, kg (not saved; for a rig).</summary>
        public double SprayedKg { get; private set; }

        /// <summary>The sensible heat that liquid carried out of the store, sum of m c T, kJ (not
        /// saved; for a rig).</summary>
        public double SprayedKJ { get; private set; }

        /// <summary>Whether the head is open.</summary>
        public bool Spraying => spraying;

        /// <summary>Whether the head is open with nothing in its store.</summary>
        public bool Dry { get; private set; }

        /// <summary>The virtual bulb's temperature, K (see the class comment).</summary>
        public float BulbK => bulbK;

        /// <summary>The bulb's highest reading since spawn, K (not saved; for a rig).</summary>
        public float BulbPeakK { get; private set; }

        /// <summary>Whether the bulb itself has opened the head since spawn (not saved; for a rig).
        /// A green input opening it does not count.</summary>
        public bool OpenedByBulb { get; private set; }

        /// <summary>The heat-capacity-weighted mean gas temperature of this head's room, K, as last
        /// read: what an open head's close is judged on. 0 until read (it is read while open).</summary>
        public float RoomMeanK { get; private set; }

        /// <summary>The hottest gas cell of this head's room, K, read with <see cref="RoomMeanK"/>
        /// (logged beside it, not judged).</summary>
        public float RoomHottestK { get; private set; }

        /// <summary>The automation input, as last read.</summary>
        public bool Forced { get; private set; }

        private Storage storage;
        private LogicPorts ports;
        private KBatchedAnimController anim;
        private KSelectable selectable;
        private System.Guid statusHandle;

        private static readonly EventSystem.IntraObjectHandler<FireSprinklerComponent>
            OnCopySettingsDelegate = new EventSystem.IntraObjectHandler<FireSprinklerComponent>(
                (component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            storage = GetComponent<Storage>();
            ports = GetComponent<LogicPorts>();
            anim = GetComponent<KBatchedAnimController>();
            selectable = GetComponent<KSelectable>();
            // A head saved open stays open: the bulb needs 1.6 s to refill its window after a load,
            // and a fire that was being sprayed at save time is still burning.
            PlayIdleOrSpray(force: true);
            RefreshStatus();
        }

        private void OnCopySettings(object data)
        {
            GameObject other = data as GameObject;
            FireSprinklerComponent source = other != null
                ? other.GetComponent<FireSprinklerComponent>()
                : null;
            if (source != null)
            {
                Threshold = source.Threshold;
            }
        }

        public void Sim200ms(float dt)
        {
            int head = Grid.PosToCell(this);
            int below = Grid.IsValidCell(head) ? Grid.CellBelow(head) : Grid.InvalidCell;
            if (!Grid.IsValidCell(below))
            {
                return;
            }

            // The virtual bulb: first-order toward the gas it hangs in, exact over the step.
            float gas = Grid.Temperature[below];
            if (!(bulbK > 0f))
            {
                bulbK = gas;
            }
            else if (gas > 0f)
            {
                float tau = BulbRti / Mathf.Sqrt(BulbGasSpeed);
                bulbK += (gas - bulbK) * (1f - Mathf.Exp(-dt / tau));
            }
            BulbPeakK = Mathf.Max(BulbPeakK, bulbK);

            Forced = ports != null && ports.GetInputValue(FireSprinklerConfig.ForcePortId) != 0;
            bool wasSpraying = spraying;
            if (!spraying)
            {
                // Closed: the bulb (or a green input) opens it.
                if (bulbK > setpointK || Forced)
                {
                    spraying = true;
                    belowForSeconds = 0f;
                    if (bulbK > setpointK)
                    {
                        OpenedByBulb = true;
                    }
                }
            }
            else if (Forced || ReadRoom(below) > setpointK)
            {
                // Open: held open by a green input or a hot room, whatever the bulb now reads.
                belowForSeconds = 0f;
            }
            else
            {
                belowForSeconds += dt;
                if (belowForSeconds >= CloseHoldSeconds)
                {
                    spraying = false;
                    belowForSeconds = 0f;
                }
            }

            Dry = false;
            if (spraying)
            {
                if (!loggedHeadGeometry)
                {
                    // Logged once so the drop's position stops being argued from screenshots:
                    // where the building thinks it is, which cell that is, and that cell's origin.
                    loggedHeadGeometry = true;
                    Grid.CellToXY(head, out int hx, out int hy);
                    Vector3 hpos = transform.GetPosition();
                    Vector3 corigin = Grid.CellToPos2D(head);
                    Debug.Log($"[Mod2MatterEnvironment] SPRINKLER head: pos ({hpos.x:F2},{hpos.y:F2})"
                        + $" -> cell {head} = ({hx},{hy}), origin ({corigin.x:F2},{corigin.y:F2})");
                }
                float sprayed = Spray(head, SprayKgPerSecond * dt);
                Dry = sprayed <= 0f;
            }

            PlayIdleOrSpray(force: spraying != wasSpraying);
            RefreshStatus();
        }

        private static readonly ushort[] roomSpecies = new ushort[GasMixtureFacade.MaxSpeciesPerCell];
        private static readonly float[] roomKg = new float[GasMixtureFacade.MaxSpeciesPerCell];
        private static readonly Queue<int> roomQueue = new Queue<int>();
        private static readonly HashSet<int> roomSeen = new HashSet<int>();

        /// <summary>
        /// Reads this head's room into <see cref="RoomMeanK"/> and <see cref="RoomHottestK"/> and
        /// returns the mean: a flood fill over the open (non-solid) cells connected to
        /// <paramref name="below"/>, in its world, at most <see cref="MaxRoomCells"/> of them. The mean
        /// is weighted by each cell's gas heat capacity, vanilla gas plus every mixture slot (a
        /// promoted room keeps its gas in the slots), so an empty or liquid cell weighs nothing.
        /// Read only while the head is open, which is the only time it decides anything.
        /// </summary>
        private float ReadRoom(int below)
        {
            roomQueue.Clear();
            roomSeen.Clear();
            roomQueue.Enqueue(below);
            roomSeen.Add(below);
            double hcSum = 0.0, hcT = 0.0;
            float hottest = 0f;
            while (roomQueue.Count > 0)
            {
                int c = roomQueue.Dequeue();
                float t = Grid.Temperature[c];
                double hc = 0.0;
                Element e = Grid.Element[c];
                if (e != null && e.IsGas && Grid.Mass[c] > 0f)
                {
                    hc += Grid.Mass[c] * (double)e.specificHeatCapacity;
                }
                int n = GasMixtureFacade.ReadComposition(c, roomSpecies, roomKg);
                for (int i = 0; i < n; i++)
                {
                    if (roomKg[i] > 0f && roomSpecies[i] < ElementLoader.elements.Count)
                    {
                        hc += roomKg[i] * (double)ElementLoader.elements[roomSpecies[i]].specificHeatCapacity;
                    }
                }
                if (hc > 0.0 && t > 0f)
                {
                    hcSum += hc;
                    hcT += hc * t;
                    hottest = Mathf.Max(hottest, t);
                }
                for (int d = 0; d < 4 && roomSeen.Count < MaxRoomCells; d++)
                {
                    int nb = d == 0 ? Grid.CellAbove(c) : d == 1 ? Grid.CellBelow(c)
                        : d == 2 ? Grid.CellLeft(c) : Grid.CellRight(c);
                    if (!Grid.IsValidCell(nb) || Grid.Solid[nb] || roomSeen.Contains(nb)
                        || !Grid.AreCellsInSameWorld(nb, below))
                    {
                        continue;
                    }
                    roomSeen.Add(nb);
                    roomQueue.Enqueue(nb);
                }
            }
            RoomMeanK = hcSum > 0.0 ? (float)(hcT / hcSum) : Grid.Temperature[below];
            RoomHottestK = hottest;
            return RoomMeanK;
        }

        /// <summary>
        /// Draws up to <paramref name="kg"/> from the store, each stored liquid in proportion to its
        /// share, and drops it as falling liquid into the open cell under the head. Returns the
        /// mass actually sprayed.
        /// </summary>
        private float Spray(int head, float kg)
        {
            if (storage == null || kg <= 0f)
            {
                return 0f;
            }
            int below = Grid.CellBelow(head);
            bool open = Grid.IsValidCell(below) && !Grid.Solid[below] && Grid.AreCellsInSameWorld(below, head);
            float stored = storage.MassStored();
            if (!open || stored <= 0f || FallingWater.instance == null)
            {
                return 0f;
            }
            float take = Mathf.Min(kg, stored);

            // One element at a time, each its share of the draw. The store is filled by one pipe,
            // so this is almost always a single element; a pipe that switched liquids sprays both.
            var tags = new List<Tag>();
            foreach (GameObject item in storage.items)
            {
                if (item == null)
                {
                    continue;
                }
                Tag tag = item.PrefabID();
                if (!tags.Contains(tag))
                {
                    tags.Add(tag);
                }
            }

            float sprayedTotal = 0f;
            foreach (Tag tag in tags)
            {
                float share = take * storage.GetMassAvailable(tag) / stored;
                if (share <= 0f)
                {
                    continue;
                }
                storage.ConsumeAndGetDisease(tag, share, out float consumed,
                    out Klei.SimUtil.DiseaseInfo disease, out float temperatureK);
                Element element = ElementLoader.GetElement(tag);
                if (consumed <= 0f || element == null || !(temperatureK > 0f))
                {
                    continue;
                }
                ushort elementIdx = (ushort)ElementLoader.GetElementIndex(element.id);
                // WHERE THE DROP IS BORN, which is not the same question as where it must be free
                // to fall. AddParticle's cell overload resolves a cell through Grid.CellToPos2D,
                // and that is the cell's BOTTOM-LEFT CORNER -- x = cellX, y = cellY, no half-cell
                // offset. Passing `below` therefore put every drop a whole
                // cell plus half a cell under the head, and half a cell to its left, which read on
                // screen as water appearing well below the sprinkler. Vanilla's own callers
                // (EnergyGenerator, BottleEmptier, WorldDamage) pass the building's OWN cell and so
                // take only the half, and so does this: the head's own cell, passed exactly the
                // way vanilla passes one. The drawn position is root_pos plus FallingWater's own
                // serialized initialOffset -- a prefab field invisible in code -- and vanilla's
                // callers look right precisely because they let that offset do the centring. A
                // half cell added here fights it rather than correcting it.
                FallingWater.instance.AddParticle(head, elementIdx, consumed, temperatureK,
                    disease.idx, disease.count, skip_sound: false, skip_decor: true,
                    debug_track: false, disable_randomness: true);
                sprayedTotal += consumed;
                SprayedKg += consumed;
                SprayedKJ += consumed * (double)element.specificHeatCapacity * temperatureK;
            }
            return sprayedTotal;
        }

        private void PlayIdleOrSpray(bool force)
        {
            if (anim == null || !force)
            {
                return;
            }
            // The donor kanim is a farm tile, so its anims may not carry these names; play only
            // what it has rather than warn every switch.
            if (spraying)
            {
                if (anim.HasAnimation(SprayStartAnims[0]))
                {
                    anim.Play(SprayStartAnims, KAnim.PlayMode.Loop);
                }
            }
            else
            {
                HashedString idle = storage != null && storage.MassStored() > 0f ? "on" : "off";
                if (anim.HasAnimation(idle))
                {
                    anim.Play(idle);
                }
            }
        }

        private void RefreshStatus()
        {
            if (selectable == null)
            {
                return;
            }
            StatusItem want = spraying
                ? (Dry ? FireSprinkler.DryStatus : FireSprinkler.SprayingStatus)
                : FireSprinkler.ArmedStatus;
            statusHandle = selectable.ReplaceStatusItem(statusHandle, want, this);
        }

        // ------------------------------------------------------------------ IThresholdSwitch
        // The Thermo Sensor's (TemperatureControlledSwitch) formatting and side screen, with the
        // direction fixed: a sprinkler that opens on cold is not one.

        public float Threshold
        {
            get => setpointK;
            set => setpointK = Mathf.Clamp(value, MinSetpointK, MaxSetpointK);
        }

        public bool ActivateAboveThreshold
        {
            get => true;
            set { }
        }

        public float CurrentValue => BulbK;

        public float RangeMin => MinSetpointK;

        public float RangeMax => MaxSetpointK;

        public LocString Title => UI.UISIDESCREENS.TEMPERATURESWITCHSIDESCREEN.TITLE;

        public LocString ThresholdValueName => UI.UISIDESCREENS.THRESHOLD_SWITCH_SIDESCREEN.TEMPERATURE;

        public string AboveToolTip => FireSprinkler.AboveTooltip;

        public string BelowToolTip => FireSprinkler.BelowTooltip;

        public ThresholdScreenLayoutType LayoutType => ThresholdScreenLayoutType.InputField;

        public int IncrementScale => 1;

        public NonLinearSlider.Range[] GetRanges => NonLinearSlider.GetDefaultRange(RangeMax);

        public float GetRangeMinInputField() => GameUtil.GetConvertedTemperature(RangeMin);

        public float GetRangeMaxInputField() => GameUtil.GetConvertedTemperature(RangeMax);

        public LocString ThresholdValueUnits()
        {
            switch (GameUtil.temperatureUnit)
            {
                case GameUtil.TemperatureUnit.Celsius:
                    return UI.UNITSUFFIXES.TEMPERATURE.CELSIUS;
                case GameUtil.TemperatureUnit.Fahrenheit:
                    return UI.UNITSUFFIXES.TEMPERATURE.FAHRENHEIT;
                default:
                    return UI.UNITSUFFIXES.TEMPERATURE.KELVIN;
            }
        }

        public string Format(float value, bool units) => GameUtil.GetFormattedTemperature(value,
            GameUtil.TimeSlice.None, GameUtil.TemperatureInterpretation.Absolute, units);

        public float ProcessedSliderValue(float input) => Mathf.Round(input);

        public float ProcessedInputValue(float input) => GameUtil.GetTemperatureConvertedToKelvin(input);
    }

    /// <summary>The sprinkler's strings, status items and build-menu entry. Called from
    /// Mod.OnLoad (strings, menu) and at the first spawn (status items need Db).</summary>
    internal static class FireSprinkler
    {
        internal const string ForcePortName = "Force Open";
        internal const string ForcePortActive = "Green Signal: spray, whatever the bulb reads";
        internal const string ForcePortInactive = "Red Signal or no wire: the heat bulb decides";

        internal const string AboveTooltip =
            "Its glass bulb opens it when the hot gas under the head has heated the bulb past {0}; it "
            + "closes once the room's mean gas temperature has been below {0} for a few seconds. (A "
            + "sprinkler only opens on heat; the direction is fixed.)";
        internal const string BelowTooltip = AboveTooltip;

        private static StatusItem armed;
        private static StatusItem sprayingItem;
        private static StatusItem dry;

        internal static StatusItem ArmedStatus => armed ?? (armed = Make("FIRESPRINKLERARMED",
            NotificationType.Neutral));

        internal static StatusItem SprayingStatus => sprayingItem ?? (sprayingItem = Make(
            "FIRESPRINKLERSPRAYING", NotificationType.Neutral));

        internal static StatusItem DryStatus => dry ?? (dry = Make("FIRESPRINKLERDRY",
            NotificationType.BadMinor));

        private static StatusItem Make(string id, NotificationType notification)
        {
            var item = new StatusItem(id, "BUILDING", "", StatusItem.IconType.Info, notification,
                false, OverlayModes.None.ID);
            item.resolveStringCallback = (str, data) =>
            {
                var head = data as FireSprinklerComponent;
                if (head == null)
                {
                    return str;
                }
                return str.Replace("{Bulb}", GameUtil.GetFormattedTemperature(head.BulbK))
                    .Replace("{Setpoint}", GameUtil.GetFormattedTemperature(head.Threshold));
            };
            return item;
        }

        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + FireSprinklerConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Fire Sprinkler");
            Strings.Add(prefix + "DESC",
                "A ceiling tile with a heat bulb. Hot gas from a fire opens it, and it rains whatever "
                + "its pipe carries into the cell below.");
            Strings.Add(prefix + "EFFECT",
                "Sprays 1 kg/s of the piped liquid into the cell below once the hot gas there has heated "
                + "its glass bulb past the setpoint (340 K unless changed; the bulb lags the gas by "
                + "about 50 s, as a real quick-response bulb does), and until the room's mean gas "
                + "temperature has been below the setpoint for a few seconds. A green automation signal forces "
                + "it open. Water puts "
                + "a fire out only by taking the burning cells and their heat. Ethanol will feed it.");

            string status = "STRINGS.BUILDING.STATUSITEMS.";
            Strings.Add(status + "FIRESPRINKLERARMED.NAME", "Armed: bulb {Bulb}, opens above {Setpoint}");
            Strings.Add(status + "FIRESPRINKLERARMED.TOOLTIP",
                "The glass bulb reads {Bulb}. The head opens when the bulb passes {Setpoint}; the hot gas "
                + "under the head heats it, with a lag.");
            Strings.Add(status + "FIRESPRINKLERSPRAYING.NAME", "Spraying");
            Strings.Add(status + "FIRESPRINKLERSPRAYING.TOOLTIP",
                "The head is open. It closes a few seconds after the room's mean gas temperature reads "
                + "below {Setpoint} with the automation input red.");
            Strings.Add(status + "FIRESPRINKLERDRY.NAME", "Open but dry");
            Strings.Add(status + "FIRESPRINKLERDRY.TOOLTIP",
                "The head is open and has nothing to spray. Connect a liquid pipe.");

            ModUtil.AddBuildingToPlanScreen(new HashedString("Plumbing"), FireSprinklerConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.pipes.ToString(), LiquidVentConfig.ID);
        }
    }
}
