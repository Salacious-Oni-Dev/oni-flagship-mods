using KSerialization;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// STATIONEERS' ACTIVE VENT, AS ONE DEVICE WITH A MODE (`StructureActiveVent`,
    /// `Assets.Scripts.Objects.Pipes.ActiveVent`). Mode 0 is Outward, pipe to room; Mode 1 is
    /// Inward, room to pipe. The Air Intake is the same <see cref="AirVentComponent"/> fixed
    /// Inward and stays, as does the Air Diffuser (Stationeers' Passive Vent).
    ///
    /// It could not be one device before because a pipe port's direction was fixed by the def.
    /// Its one port is now an <see cref="ReversibleConduitPort"/>: an input
    /// while the vent is Outward, an output while it is Inward, so a vanilla run's flow graph turns
    /// round with it. On a run Mod 1 levels as one pressure direction never mattered.
    ///
    /// A MODE CHANGE RESETS THE SETPOINTS, as the Stationeers Active Vent's reset does on every
    /// `InteractableType.Mode` interaction (a logic write of `Mode` goes through the same
    /// `OnServer.Interact`): Outward is room 101.325 kPa / pipe 0, Inward is room 0 / pipe
    /// 50662.5 kPa. A new vent is Mode 0 with the Outward pair, which is what Stationeers'
    /// `OnAssignedReference` gives a new one.
    ///
    /// The rate is Mod 1's (<see cref="AirVentComponent"/>, five atmospheres a second), not the
    /// Stationeers prefab's 10 kPa a tick, by choice.
    ///
    /// IC10 (through <see cref="DataDevices"/>, so this mod never references Mod 5): `Mode`,
    /// `PressureExternal` and `PressureInternal`, in kPa as Stationeers' logic reads and writes
    /// take them, unclamped as there. `On` and `Power` come from Mod 5's reading of
    /// the vanilla components.
    /// </summary>
    public class ActiveVentConfig : IBuildingConfig
    {
        public const string Id = "ActiveVent";

        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Active Vent");
            Strings.Add(prefix + "DESC",
                "One vent that pumps either way: piped gas out into a room, or a room's air into "
                + "the pipe, each up to a pressure you set.");
            Strings.Add(prefix + "EFFECT",
                "Stationeers' Active Vent. Outward fills the room until it reaches its target, "
                + "inward draws the room down to its target, and its pipe port turns from an input "
                + "to an output with it. Switch the mode from its menu or from a program; switching "
                + "resets both targets the way Stationeers does.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("HVAC"), Id,
                BUILDINGS.PlanSubcategoryName.valves.ToString());
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 1, 1,
                "ventgas_kanim", 30, 30f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                MATERIALS.ALL_METALS, 1600f, BuildLocationRule.Anywhere,
                BUILDINGS.DECOR.PENALTY.TIER1, NOISE_POLLUTION.NOISY.TIER2);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = AirIntakeConfig.FullRateWatts;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0f;

            // NO DEF PORT. The ReversibleConduitPort added below is the port; a def port would be
            // registered by BuildingConduitEndpoints as a fixed Sink or Source beside it.
            def.Floodable = false;
            def.ViewMode = OverlayModes.GasConduits.ID;
            def.AudioCategory = "Metal";
            def.PermittedRotations = PermittedRotations.R360;
            def.PowerInputOffset = new CellOffset(0, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, Id);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefabTag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation),
                prefabTag);
            AirVentComponent vent = go.AddOrGet<AirVentComponent>();
            vent.Direction = VentDirection.Outward;
            // Stationeers' pressure per tick over the vent's own cells, and its run levelled.
            vent.StationeersRate = true;
            vent.externalPressurePa = ActiveVentMode.OutwardExternalPa;
            vent.internalPressurePa = ActiveVentMode.OutwardInternalPa;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGet<EnergyConsumer>();
            go.AddOrGetDef<ActiveController.Def>();

            ReversibleConduitPort port = go.AddOrGet<ReversibleConduitPort>();
            port.portInfo = new ConduitPortInfo(ConduitType.Gas, new CellOffset(0, 0));
            port.sourceByDefault = false;

            go.AddOrGet<ActiveVentMode>();
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayInFrontOfConduits);
        }
    }

    /// <summary>
    /// The Active Vent's saved mode, its menu button and its IC10 names. See
    /// <see cref="ActiveVentConfig"/>.
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class ActiveVentMode : KMonoBehaviour, IDataDeviceSource
    {
        public const int Outward = 0;
        public const int Inward = 1;

        // The Stationeers reset pairs, in pascals: Chemistry.OneAtmosphere / Zero, and Zero /
        // 50662.5 kPa (500 atmospheres, half of Stationeers' and our 60.795 MPa pipe rating).
        internal const float OutwardExternalPa = 101325f;
        internal const float OutwardInternalPa = 0f;
        internal const float InwardExternalPa = 0f;
        internal const float InwardInternalPa = 50662500f;

        [Serialize]
        private int mode = Outward;

        private AirVentComponent vent;
        private ReversibleConduitPort port;

        public int Mode => mode;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            vent = GetComponent<AirVentComponent>();
            port = GetComponent<ReversibleConduitPort>();
            Apply();
            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenu);
        }

        /// <summary>
        /// Sets the mode: anything but 0 is Inward, as Stationeers' vent direction reads a
        /// non-zero state. A change resets both setpoints (as Stationeers does); writing the
        /// mode it already has changes nothing.
        /// </summary>
        public void SetMode(int value)
        {
            int next = value == Outward ? Outward : Inward;
            if (next == mode)
            {
                return;
            }
            mode = next;
            if (vent != null)
            {
                vent.externalPressurePa = mode == Outward ? OutwardExternalPa : InwardExternalPa;
                vent.internalPressurePa = mode == Outward ? OutwardInternalPa : InwardInternalPa;
            }
            Apply();
            Game.Instance?.userMenu?.Refresh(gameObject);
        }

        private void Apply()
        {
            if (vent != null)
            {
                vent.Direction = mode == Outward ? VentDirection.Outward : VentDirection.Inward;
            }
            if (port != null)
            {
                port.SetSource(mode == Inward);
            }
        }

        private void OnRefreshUserMenu(object data)
        {
            bool outward = mode == Outward;
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                "action_switch_toggle",
                outward ? "Switch to Inward" : "Switch to Outward",
                () => SetMode(outward ? Inward : Outward),
                Action.NumActions, null, null, null,
                outward
                    ? "Now Outward: pipe into the room. Inward draws the room into the pipe."
                    : "Now Inward: room into the pipe. Outward fills the room from the pipe."));
        }

        public void DescribeDataDevice(DataDeviceMap map)
        {
            map.ReadWrite("Mode", () => mode, value => SetMode((int)value));
            map.ReadWrite("PressureExternal",
                () => vent != null ? vent.externalPressurePa / 1000.0 : 0.0,
                value =>
                {
                    if (vent != null)
                    {
                        vent.externalPressurePa = (float)(value * 1000.0);
                    }
                });
            map.ReadWrite("PressureInternal",
                () => vent != null ? vent.internalPressurePa / 1000.0 : 0.0,
                value =>
                {
                    if (vent != null)
                    {
                        vent.internalPressurePa = (float)(value * 1000.0);
                    }
                });
        }
    }
}
