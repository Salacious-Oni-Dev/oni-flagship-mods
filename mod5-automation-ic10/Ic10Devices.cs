using System.Collections.Generic;
using Ic10;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // A building, found by its KPrefabID.InstanceID, as an IC10 device. That id is what a
    // socket saves and what a script reads as ReferenceId.
    internal static class Ic10Devices
    {
        // Stationeers' tables plus ONI's logic types from 1024 up; one set for every chip.
        public static readonly Ic10EnumSet Enums = Ic10EnumSet.Oni();

        private struct Entry
        {
            public GameObject Go;
            public IIc10Device Device;
        }

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();
        private static Game _game;
        private static int _registryRevision = -1;

        // A logic-type name another mod declared that the tables lack: logged once each.
        private static readonly HashSet<string> UnknownLogged = new HashSet<string>();

        // The finished building with this id, or null. A building under construction is a
        // different prefab with no BuildingComplete, so it answers nothing, as a half-built
        // Stationeers device does.
        public static KPrefabID Building(int instanceId)
        {
            if (instanceId <= 0)
            {
                return null;
            }
            KPrefabID id = KPrefabIDTracker.Get().GetInstance(instanceId);
            if (id == null || id.GetComponent<BuildingComplete>() == null)
            {
                return null;
            }
            return id;
        }

        public static IIc10Device Find(int instanceId)
        {
            // A cache from the last loaded game holds only destroyed objects.
            // So does one described before a mod registered more.
            if (!ReferenceEquals(_game, Game.Instance) || _registryRevision != OniFramework.DataDevices.Revision)
            {
                Cache.Clear();
                _game = Game.Instance;
                _registryRevision = OniFramework.DataDevices.Revision;
            }

            KPrefabID id = Building(instanceId);
            if (id == null)
            {
                Cache.Remove(instanceId);
                return null;
            }

            // Another housing is a device in its own right: its Setting, and its chip's
            // stack through get and put.
            Ic10HousingBuilding housing = id.GetComponent<Ic10HousingBuilding>();
            if (housing != null)
            {
                return housing.Housing;
            }

            // A logic unit likewise: its Setting is what other units and chips read.
            Ic10UnitBuilding unit = id.GetComponent<Ic10UnitBuilding>();
            if (unit != null)
            {
                return unit.Unit;
            }

            Entry entry;
            if (Cache.TryGetValue(instanceId, out entry) && entry.Go == id.gameObject)
            {
                return entry.Device;
            }
            entry.Go = id.gameObject;
            Ic10OniBuilding building = Ic10OniBuilding.Create(Ic10OniViews.Build(id.gameObject), Enums);
            if (building.UnknownDeclared != null)
            {
                foreach (string name in building.UnknownDeclared)
                {
                    if (UnknownLogged.Add(name))
                    {
                        Mod5Log.Warn("a mod declared logic type \"" + name + "\" for " + id.PrefabTag.Name
                            + " through OniFramework.DataDevices; there is no such logic type, so it is skipped");
                    }
                }
            }
            entry.Device = building;
            Cache[instanceId] = entry;
            return entry.Device;
        }

        // Which finished buildings are devices: all of them except wires, pipes, tiles and
        // the back wall. A door is a foundation building and is kept.
        public static bool IsDevice(BuildingComplete building)
        {
            BuildingDef def = building.Def;
            // The data cable's own bridge is cable, not a device on it.
            if (def.isUtility || def.isKAnimTile || def.ObjectLayer == ObjectLayer.Backwall
                || def.ObjectLayer == Ic10DataGrid.BridgeLayer)
            {
                return false;
            }
            return !def.IsFoundation || building.GetComponent<Door>() != null;
        }

        public static string Name(int instanceId)
        {
            KPrefabID id = Building(instanceId);
            return id == null ? null : id.gameObject.GetProperName();
        }
    }

    // The housing as other chips see it. Ic10Housing answers Setting and LineNumber; this
    // adds what Device.CanLogicRead gives every Stationeers device (PrefabHash, NameHash,
    // ReferenceId), the housing's own switch, power and error light, and the game clock.
    internal sealed class OniHousing : Ic10Housing
    {
        private readonly Ic10HousingBuilding _owner;
        private readonly int _prefabHashType;
        private readonly int _nameHashType;
        private readonly int _referenceIdType;
        private readonly int _onType;
        private readonly int _powerType;
        private readonly int _errorType;
        private readonly int _cycleType;
        private readonly int _cycleTimeType;
        private readonly int _nightType;

        public int ReferenceId;

        public OniHousing(Ic10EnumSet enums, Ic10HousingBuilding owner) : base(enums)
        {
            _owner = owner;
            Ic10EnumTable t = enums.LogicType;
            _prefabHashType = t.ValueOf("PrefabHash");
            _nameHashType = t.ValueOf("NameHash");
            _referenceIdType = t.ValueOf("ReferenceId");
            _onType = t.ValueOf("On");
            _powerType = t.ValueOf("Power");
            _errorType = t.ValueOf("Error");
            // The game clock, so a chip reads it off db with no sensor.
            _cycleType = t.ValueOf("Cycle");
            _cycleTimeType = t.ValueOf("CycleTime");
            _nightType = t.ValueOf("Night");
        }

        public override bool CanLogicRead(int logicType)
        {
            return logicType == _prefabHashType || logicType == _nameHashType || logicType == _referenceIdType
                || logicType == _onType || logicType == _powerType || logicType == _errorType
                || logicType == _cycleType || logicType == _cycleTimeType || logicType == _nightType
                || base.CanLogicRead(logicType);
        }

        public override bool CanLogicWrite(int logicType)
        {
            return logicType == _onType || base.CanLogicWrite(logicType);
        }

        public override double GetLogicValue(int logicType)
        {
            if (logicType == _prefabHashType)
            {
                return PrefabHash;
            }
            if (logicType == _nameHashType)
            {
                return NameHash;
            }
            if (logicType == _referenceIdType)
            {
                return ReferenceId;
            }
            if (logicType == _onType)
            {
                return On ? 1.0 : 0.0;
            }
            if (logicType == _powerType)
            {
                return Powered ? 1.0 : 0.0;
            }
            if (logicType == _errorType)
            {
                return ErrorState;
            }
            if (logicType == _cycleType)
            {
                return Ic10OniReadings.Cycle();
            }
            if (logicType == _cycleTimeType)
            {
                return Ic10OniReadings.CycleTime();
            }
            if (logicType == _nightType)
            {
                return Ic10OniReadings.Night() ? 1.0 : 0.0;
            }
            return base.GetLogicValue(logicType);
        }

        public override void SetLogicValue(int logicType, double value)
        {
            if (logicType == _onType)
            {
                _owner.SetEnabled(Ic10MappedDevice.State(value));
                return;
            }
            base.SetLogicValue(logicType, value);
        }
    }
}
