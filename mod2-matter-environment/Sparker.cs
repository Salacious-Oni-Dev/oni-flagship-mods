using System.Collections.Generic;
using KSerialization;
using TUNING;
using UnityEngine;

namespace Mod2MatterEnvironment
{
    /// <summary>
    /// THE SPARKER: a logic-driven ignition source.
    ///
    /// One tile, one logic input, a small power draw. While the input is green and the building
    /// is powered it sparks every sim tick (200 ms), and it sparks once more the moment it turns
    /// on, so a single short pulse still lights a fuel pocket. Each spark marks two cells through
    /// <see cref="OniFramework.Ignition.Spark"/>: the Sparker's own cell and the cell in front of
    /// it (above it as placed, or wherever the rotation points). A spark is a one-tick flag on
    /// the cell. Each activation (the rising edge) also puts 5 J into the Sparker's own cell,
    /// as Stationeers' igniter does; the every-tick sparks add nothing. Nothing burns from the
    /// Sparker alone: whether anything ignites is the sim's ignition test (3.1), which needs fuel
    /// and reachable oxygen.
    ///
    /// STATIONEERS: `Objects.Pipes.PipeIgniter.OnAtmosphericTick` sets `Sparked` on its pipe
    /// network's atmosphere AND adds `MoleEnergy(5.0)` to it, once per activation (it resets its
    /// own Activate to 0 every tick). Ours MATCHES the mechanism (a powered, logic-driven device
    /// that sets the Sparked flag) and the energy: 5 J once per activation, here on the rising
    /// edge, into the Sparker's own cell (`SimMessages.ModifyEnergy`, 0.005 kJ, source `Burner`,
    /// the id vanilla's rocket exhaust heats cells with, capped at the same 3200 K those calls
    /// use). Two differences from Stationeers remain, both by choice:
    ///   * world-mounted, not in a pipe;
    ///   * besides the activation spark, the Sparked flag is also set every tick while the input
    ///     stays green ("each pulse, or every tick while on"). Those ticks add no energy.
    ///
    /// UNWIRED IS OFF. `LogicOperationalController.unNetworkedValue` defaults to 1, so a vanilla
    /// machine with no wire runs; an igniter that sparks forever because nobody wired it would
    /// light every methane pocket it was ever built beside. Set to 0 here.
    ///
    /// ART: vanilla's Automated Notifier (`alarm_sensor_kanim`, LogicAlarmConfig), a 1x1 logic
    /// device whose on_pre / on_loop / on_pst / off set reads as "armed / idle". Donor kanim, on
    /// the public kanim-needed list until it has art of its own.
    /// </summary>
    public class SparkerConfig : IBuildingConfig
    {
        internal const string Id = "Sparker";

        /// <summary>The draw while sparking, about 10 W while on.</summary>
        internal const float Watts = 10f;

        /// <summary>The cell in front, before rotation: above the Sparker.</summary>
        internal static readonly CellOffset FrontOffset = new CellOffset(0, 1);

        internal const string DonorKanim = "alarm_sensor_kanim";

        public override BuildingDef CreateBuildingDef()
        {
            // Sized, built and placed like the donor's own building (LogicAlarmConfig): 1x1,
            // refined metal, anywhere -- floor, wall or ceiling, with R360 to point its front.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1, DonorKanim, 30, 30f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER0, MATERIALS.REFINED_METALS, 1600f,
                BuildLocationRule.Anywhere, BUILDINGS.DECOR.PENALTY.TIER0,
                NOISE_POLLUTION.NONE);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = Watts;
            def.PowerInputOffset = new CellOffset(0, 0);
            def.SelfHeatKilowattsWhenActive = 0f;
            def.ExhaustKilowattsWhenActive = 0f;
            def.Overheatable = false;
            def.Floodable = false;
            def.Entombable = false;
            def.PermittedRotations = PermittedRotations.R360;
            def.ViewMode = OverlayModes.Logic.ID;
            def.AudioCategory = "Metal";
            def.SceneLayer = Grid.SceneLayer.Building;

            def.LogicInputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.InputPort(LogicOperationalController.PORT_ID, new CellOffset(0, 0),
                    Sparker.PortName, Sparker.PortActive, Sparker.PortInactive,
                    show_wire_missing_icon: true),
            };

            GeneratedBuildings.RegisterWithOverlay(OverlayModes.Logic.HighlightItemIDs, Id);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            LogicOperationalController logic = go.AddOrGet<LogicOperationalController>();
            logic.unNetworkedValue = 0;
            go.AddOrGet<EnergyConsumer>();
            go.AddOrGet<SparkerComponent>();
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>The Sparker's behaviour. See <see cref="SparkerConfig"/>.</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class SparkerComponent : KMonoBehaviour, ISim200ms
    {
        /// <summary>The source name every spark carries, for Ignition's Sparked event.</summary>
        internal const string Source = "Sparker";

        /// <summary>PipeIgniter's MoleEnergy(5.0) per activation: 5 J, in the sim's kJ.</summary>
        private const float ActivationKilojoules = 0.005f;

        /// <summary>The cap vanilla's Burner-source calls (rocket exhaust) pass.</summary>
        private const float ActivationMaxTemperatureK = 3200f;

        private static readonly HashedString[] OnAnims = { "on_pre", "on_loop" };
        private static readonly HashedString[] OffAnims = { "on_pst", "off" };

        private static readonly EventSystem.IntraObjectHandler<SparkerComponent>
            OnOperationalChangedDelegate = new EventSystem.IntraObjectHandler<SparkerComponent>(
                (component, data) => component.OnOperationalChanged(data));

        /// <summary>Sparks sent since spawn (not saved), for a rig to assert on.</summary>
        public int Sparks { get; private set; }

        private Operational operational;
        private Rotatable rotatable;
        private KBatchedAnimController anim;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            operational = GetComponent<Operational>();
            rotatable = GetComponent<Rotatable>();
            anim = GetComponent<KBatchedAnimController>();
            Subscribe((int)GameHashes.OperationalChanged, OnOperationalChangedDelegate);
            bool on = operational.IsOperational;
            operational.SetActive(on);
            anim.Play(on ? OnAnims : OffAnims);
        }

        /// <summary>
        /// The rising edge, one activation: powered and green together. Sparks at once, so a pulse
        /// shorter than a sim tick still lights (a second spark in the same tick is harmless, the
        /// flag lasts one tick either way), and puts PipeIgniter's 5 J into the own cell, once.
        /// </summary>
        private void OnOperationalChanged(object data)
        {
            bool on = operational.IsOperational;
            operational.SetActive(on);
            anim.Play(on ? OnAnims : OffAnims);
            if (on)
            {
                int cell = Grid.PosToCell(this);
                if (Grid.IsValidCell(cell))
                {
                    SimMessages.ModifyEnergy(cell, ActivationKilojoules,
                        ActivationMaxTemperatureK, SimMessages.EnergySourceID.Burner);
                }
                SparkBoth();
            }
        }

        public void Sim200ms(float dt)
        {
            if (operational.IsOperational)
            {
                SparkBoth();
            }
        }

        private void SparkBoth()
        {
            int cell = Grid.PosToCell(this);
            if (!Grid.IsValidCell(cell))
            {
                return;
            }
            OniFramework.Ignition.Spark(cell, Source);
            CellOffset offset = rotatable != null
                ? rotatable.GetRotatedCellOffset(SparkerConfig.FrontOffset)
                : SparkerConfig.FrontOffset;
            int front = Grid.OffsetCell(cell, offset);
            if (Grid.IsValidCell(front))
            {
                OniFramework.Ignition.Spark(front, Source);
            }
            Sparks++;
        }
    }

    /// <summary>The Sparker's display strings and build-menu entry. Called from Mod.OnLoad.</summary>
    internal static class Sparker
    {
        internal const string PortName = "Spark";
        internal const string PortActive = "Green Signal: spark every tick, and once on turning on";
        internal const string PortInactive = "Red Signal or no wire: idle";

        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + SparkerConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Sparker");
            Strings.Add(prefix + "DESC",
                "Throws a spark into its own tile and the tile in front of it while its "
                + "automation signal is green.");
            Strings.Add(prefix + "EFFECT",
                "An igniter, nothing more. Each switch-on puts a 5 J spark into its tile, and it "
                + "burns nothing by itself: a spark "
                + "lights a cell only if that cell holds a fuel gas with oxygen within reach. "
                + "Sparks every tick while the signal is green, and once the moment it turns "
                + "green, so a short pulse is enough. With no wire it stays off.");

            ModUtil.AddBuildingToPlanScreen(new HashedString("Automation"), SparkerConfig.Id,
                BUILDINGS.PlanSubcategoryName.logicmanager.ToString(), LogicAlarmConfig.ID);
        }
    }
}
