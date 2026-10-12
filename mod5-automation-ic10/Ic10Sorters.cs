using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Ic10;
using KSerialization;
using TUNING;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The IC Sorter: Stationeers' LogicSorter on ONI's conveyor rail.
    //
    // The building is ONI's conveyor filter (SolidFilterConfig: 3 by 1, rail in at the left,
    // out at the right, the second output in the middle, 120 W) with ElementFilter's one-tag
    // test replaced by the sorter's list of 32 filters (Ic10/Ic10Sorter.cs). An item that
    // matches leaves by the middle port (Stationeers' export 2), anything else by the right
    // (export 1). The list is set only by a chip, through the device's memory.
    //
    // ElementFilter.OnConduitTick, its solid half, is what OnConduitTick copies.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10SorterFilter : KMonoBehaviour, ISaveLoadable, ISecondaryOutput
    {
        [SerializeField]
        public ConduitPortInfo portInfo;

        [Serialize]
        private double[] cells;

        [Serialize]
        private int mode;

        [Serialize]
        private int importCount;

        [Serialize]
        private int exportCount;

        [Serialize]
        private int pendingLimit = -1;

        [Serialize]
        private double pendingLimitValue;

        private readonly Ic10Sorter sorter = new Ic10Sorter();
        private Operational operational;
        private Building building;
        private KSelectable selectable;
        private int inputCell = -1;
        private int outputCell = -1;
        private int filteredCell = -1;
        private FlowUtilityNetwork.NetworkItem itemFilter;
        private HandleVector<int>.Handle partitionerEntry;
        private Guid needsConduitStatusItemGuid;
        private Guid conduitBlockedStatusItemGuid;

        // The item last asked about at the input, so an item that waits is not counted in
        // ImportCount again on each tick it waits.
        private int lastHandle = -1;

        private static readonly Dictionary<Tag, int> PrefabHashes = new Dictionary<Tag, int>();
        private static readonly Dictionary<string, Tag> Tags = new Dictionary<string, Tag>();

        public Ic10Sorter Sorter
        {
            get { return sorter; }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            operational = GetComponent<Operational>();
            building = GetComponent<Building>();
            selectable = GetComponent<KSelectable>();
            sorter.Load(new Ic10SorterState
            {
                Cells = cells,
                Mode = mode,
                ImportCount = importCount,
                ExportCount = exportCount,
                PendingLimit = pendingLimit,
                PendingLimitValue = pendingLimitValue,
            });

            // ElementFilter.OnSpawn, the solid conduit half.
            inputCell = building.GetUtilityInputCell();
            outputCell = building.GetUtilityOutputCell();
            int cell = Grid.PosToCell(transform.GetPosition());
            filteredCell = Grid.OffsetCell(cell, building.GetRotatedOffset(portInfo.offset));
            itemFilter = new FlowUtilityNetwork.NetworkItem(portInfo.conduitType, Endpoint.Source, filteredCell, gameObject);
            SolidConduit.GetFlowManager().networkMgr.AddToNetworks(filteredCell, itemFilter, true);
            SolidConduit.GetFlowManager().AddConduitUpdater(OnConduitTick);
            UpdateConduitExistsStatus();
            UpdateConduitBlockedStatus();
            partitionerEntry = GameScenePartitioner.Instance.Add("Ic10SorterConduitExists", gameObject, filteredCell,
                GameScenePartitioner.Instance.solidConduitsLayer, data => UpdateConduitExistsStatus());
        }

        protected override void OnCleanUp()
        {
            // ElementFilter.OnCleanUp takes the network manager by conduit type; for a solid
            // port that is the solid flow manager's.
            SolidConduit.GetFlowManager().networkMgr.RemoveFromNetworks(filteredCell, itemFilter, true);
            SolidConduit.GetFlowManager().RemoveConduitUpdater(OnConduitTick);
            if (partitionerEntry.IsValid() && GameScenePartitioner.Instance != null)
            {
                GameScenePartitioner.Instance.Free(ref partitionerEntry);
            }
            base.OnCleanUp();
        }

        [OnSerializing]
        private void OnSerializing()
        {
            Ic10SorterState state = sorter.Save();
            cells = state.Cells;
            mode = state.Mode;
            importCount = state.ImportCount;
            exportCount = state.ExportCount;
            pendingLimit = state.PendingLimit;
            pendingLimitValue = state.PendingLimitValue;
        }

        // ElementFilter.OnConduitTick for a solid port, with the sorter deciding the cell.
        // LogicSorter.TryFilter runs DoFilter on every tick an item waits, and so does this.
        private void OnConduitTick(float dt)
        {
            bool moved = false;
            UpdateConduitBlockedStatus();
            if (operational.IsOperational)
            {
                SolidConduitFlow flow = SolidConduit.GetFlowManager();
                SolidConduitFlow.ConduitContents contents = flow.GetContents(inputCell);
                Pickupable pickupable = flow.GetPickupable(contents.pickupableHandle);
                if (pickupable != null)
                {
                    int handle = contents.pickupableHandle.index;
                    if (handle != lastHandle)
                    {
                        lastHandle = handle;
                        sorter.ImportCount++;
                    }
                    int target = sorter.Matches(Describe(pickupable)) ? filteredCell : outputCell;
                    Pickupable there = flow.GetPickupable(flow.GetContents(target).pickupableHandle);
                    if (pickupable.PrimaryElement.Mass > 0f && (there == null || there.PrimaryElement.Mass <= 0f))
                    {
                        Pickupable taken = flow.RemovePickupable(inputCell);
                        if (taken != null)
                        {
                            flow.AddPickupable(target, taken);
                            sorter.ExportCount++;
                            lastHandle = -1;
                            moved = true;
                        }
                    }
                }
                else
                {
                    flow.RemovePickupable(inputCell);
                }
            }
            operational.SetActive(moved);
        }

        private static Ic10SortItem Describe(Pickupable pickupable)
        {
            KPrefabID id = pickupable.KPrefabID;
            int hash;
            if (!PrefabHashes.TryGetValue(id.PrefabTag, out hash))
            {
                hash = Ic10Math.Hash(id.PrefabTag.Name);
                PrefabHashes[id.PrefabTag] = hash;
            }
            Func<string, bool> hasTag = name =>
            {
                Tag tag;
                if (!Tags.TryGetValue(name, out tag))
                {
                    tag = new Tag(name);
                    Tags[name] = tag;
                }
                return id.HasTag(tag);
            };
            return new Ic10SortItem
            {
                PrefabHash = hash,
                SortingClass = Ic10SortClasses.SortingClass(hasTag),
                SlotType = Ic10SortClasses.SlotType(hasTag),
                Quantity = Ic10SortClasses.Quantity(pickupable.TotalAmount),
            };
        }

        private void UpdateConduitExistsStatus()
        {
            bool connected = RequireOutputs.IsConnected(filteredCell, portInfo.conduitType);
            bool shown = needsConduitStatusItemGuid != Guid.Empty;
            if (connected == shown)
            {
                needsConduitStatusItemGuid = selectable.ToggleStatusItem(Db.Get().BuildingStatusItems.NeedSolidOut,
                    needsConduitStatusItemGuid, !connected);
            }
        }

        private void UpdateConduitBlockedStatus()
        {
            bool empty = SolidConduit.GetFlowManager().IsConduitEmpty(filteredCell);
            bool shown = conduitBlockedStatusItemGuid != Guid.Empty;
            if (empty == shown)
            {
                conduitBlockedStatusItemGuid = selectable.ToggleStatusItem(Db.Get().BuildingStatusItems.ConduitBlockedMultiples,
                    conduitBlockedStatusItemGuid, !empty);
            }
        }

        public bool HasSecondaryConduitType(ConduitType type)
        {
            return portInfo.conduitType == type;
        }

        public CellOffset GetSecondaryConduitOffset(ConduitType type)
        {
            return portInfo.offset;
        }
    }

    // SolidFilterConfig, with the sorter in place of ElementFilter and Filterable.
    public sealed class Ic10SorterConfig : IBuildingConfig
    {
        public const string Id = "Ic10Sorter";

        private readonly ConduitPortInfo secondaryPort = new ConduitPortInfo(ConduitType.Solid, new CellOffset(0, 0));

        public override BuildingDef CreateBuildingDef()
        {
            // PLACEHOLDER ART: a scanner box in the middle cell on the conveyor rail across the
            // 3x1, its lamp lit green while operational.
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 3, 1, "StructureLogicSorter_kanim", 30, 10f,
                TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.RAW_METALS, 1600f, BuildLocationRule.Anywhere,
                TUNING.BUILDINGS.DECOR.PENALTY.TIER0, NOISE_POLLUTION.NOISY.TIER1);
            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 120f;
            def.SelfHeatKilowattsWhenActive = 0f;
            def.ExhaustKilowattsWhenActive = 0f;
            def.InputConduitType = ConduitType.Solid;
            def.OutputConduitType = ConduitType.Solid;
            def.Floodable = false;
            def.ViewMode = OverlayModes.SolidConveyor.ID;
            def.AudioCategory = "Metal";
            def.UtilityInputOffset = new CellOffset(-1, 0);
            def.UtilityOutputOffset = new CellOffset(1, 0);
            def.PermittedRotations = PermittedRotations.R360;
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.SolidConveyorIDs, Id);
            return def;
        }

        private void AttachPort(GameObject go)
        {
            go.AddComponent<ConduitSecondaryOutput>().portInfo = secondaryPort;
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
        {
            base.DoPostConfigurePreview(def, go);
            AttachPort(go);
        }

        public override void DoPostConfigureUnderConstruction(GameObject go)
        {
            base.DoPostConfigureUnderConstruction(go);
            AttachPort(go);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);
            go.AddOrGet<Ic10SorterFilter>().portInfo = secondaryPort;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGetDef<PoweredActiveController.Def>().showWorkingStatus = true;
            go.AddOrGet<UserNameable>();
        }
    }

    internal static class Ic10SorterStrings
    {
        internal static void Install()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Ic10SorterConfig.Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "IC Sorter");
            Strings.Add(prefix + "DESC", "A conveyor filter an IC Housing programs.");
            Strings.Add(prefix + "EFFECT",
                "Sends items that pass its filters out of the middle port, and everything else out of the far end. "
                + "A program writes up to 32 filters into its memory: an item kind, a class, a slot type or an amount, "
                + "with a limit on how many may pass. Mode chooses whether an item must pass All of them, Any, or None. "
                + "With no filters everything goes out of the far end.");
            ModUtil.AddBuildingToPlanScreen(new HashedString("Conveyance"), Ic10SorterConfig.Id,
                TUNING.BUILDINGS.PlanSubcategoryName.conveyancestructures.ToString(), "SolidFilter");
        }
    }
}
