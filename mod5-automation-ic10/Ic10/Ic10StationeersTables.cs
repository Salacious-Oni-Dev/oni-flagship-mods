// GENERATED from Stationeers' own tables (game build 0.2.6428.27798).
// Names and numeric values only: they are the script-facing interface IC10 programs are
// written against, so a compatible chip has to accept the same spellings and numbers.
// Not edited by hand.
using System.Collections.Generic;

namespace Ic10
{
    public static class Ic10StationeersTables
    {
        public static readonly KeyValuePair<string, int>[] LogicType =
        {
            P("None", 0), P("Power", 1), P("Open", 2), P("Mode", 3), P("Error", 4), P("Pressure", 5),
            P("Temperature", 6), P("PressureExternal", 7), P("PressureInternal", 8), P("Activate", 9),
            P("Lock", 10), P("Charge", 11), P("Setting", 12), P("Reagents", 13), P("RatioOxygen", 14),
            P("RatioCarbonDioxide", 15), P("RatioNitrogen", 16), P("RatioPollutant", 17),
            P("RatioMethane", 18), P("RatioWater", 19), P("Horizontal", 20), P("Vertical", 21),
            P("SolarAngle", 22), P("Maximum", 23), P("Ratio", 24), P("PowerPotential", 25),
            P("PowerActual", 26), P("Quantity", 27), P("On", 28), P("ImportQuantity", 29),
            P("ImportSlotOccupant", 30), P("ExportQuantity", 31), P("ExportSlotOccupant", 32),
            P("RequiredPower", 33), P("HorizontalRatio", 34), P("VerticalRatio", 35),
            P("PowerRequired", 36), P("Idle", 37), P("Color", 38), P("ElevatorSpeed", 39),
            P("ElevatorLevel", 40), P("RecipeHash", 41), P("ExportSlotHash", 42),
            P("ImportSlotHash", 43), P("PlantHealth1", 44), P("PlantHealth2", 45),
            P("PlantHealth3", 46), P("PlantHealth4", 47), P("PlantGrowth1", 48), P("PlantGrowth2", 49),
            P("PlantGrowth3", 50), P("PlantGrowth4", 51), P("PlantEfficiency1", 52),
            P("PlantEfficiency2", 53), P("PlantEfficiency3", 54), P("PlantEfficiency4", 55),
            P("PlantHash1", 56), P("PlantHash2", 57), P("PlantHash3", 58), P("PlantHash4", 59),
            P("RequestHash", 60), P("CompletionRatio", 61), P("ClearMemory", 62), P("ExportCount", 63),
            P("ImportCount", 64), P("PowerGeneration", 65), P("TotalMoles", 66), P("Volume", 67),
            P("Plant", 68), P("Harvest", 69), P("Output", 70), P("PressureSetting", 71),
            P("TemperatureSetting", 72), P("TemperatureExternal", 73), P("Filtration", 74),
            P("AirRelease", 75), P("PositionX", 76), P("PositionY", 77), P("PositionZ", 78),
            P("VelocityMagnitude", 79), P("VelocityRelativeX", 80), P("VelocityRelativeY", 81),
            P("VelocityRelativeZ", 82), P("RatioNitrousOxide", 83), P("PrefabHash", 84),
            P("ForceWrite", 85), P("SignalStrength", 86), P("SignalID", 87), P("TargetX", 88),
            P("TargetY", 89), P("TargetZ", 90), P("SettingInput", 91), P("SettingOutput", 92),
            P("CurrentResearchPodType", 93), P("ManualResearchRequiredPod", 94),
            P("MineablesInVicinity", 95), P("MineablesInQueue", 96), P("NextWeatherEventTime", 97),
            P("Combustion", 98), P("Fuel", 99), P("ReturnFuelCost", 100), P("CollectableGoods", 101),
            P("Time", 102), P("Bpm", 103), P("EnvironmentEfficiency", 104),
            P("WorkingGasEfficiency", 105), P("PressureInput", 106), P("TemperatureInput", 107),
            P("RatioOxygenInput", 108), P("RatioCarbonDioxideInput", 109),
            P("RatioNitrogenInput", 110), P("RatioPollutantInput", 111), P("RatioMethaneInput", 112),
            P("RatioWaterInput", 113), P("RatioNitrousOxideInput", 114), P("TotalMolesInput", 115),
            P("PressureInput2", 116), P("TemperatureInput2", 117), P("RatioOxygenInput2", 118),
            P("RatioCarbonDioxideInput2", 119), P("RatioNitrogenInput2", 120),
            P("RatioPollutantInput2", 121), P("RatioMethaneInput2", 122), P("RatioWaterInput2", 123),
            P("RatioNitrousOxideInput2", 124), P("TotalMolesInput2", 125), P("PressureOutput", 126),
            P("TemperatureOutput", 127), P("RatioOxygenOutput", 128),
            P("RatioCarbonDioxideOutput", 129), P("RatioNitrogenOutput", 130),
            P("RatioPollutantOutput", 131), P("RatioMethaneOutput", 132), P("RatioWaterOutput", 133),
            P("RatioNitrousOxideOutput", 134), P("TotalMolesOutput", 135), P("PressureOutput2", 136),
            P("TemperatureOutput2", 137), P("RatioOxygenOutput2", 138),
            P("RatioCarbonDioxideOutput2", 139), P("RatioNitrogenOutput2", 140),
            P("RatioPollutantOutput2", 141), P("RatioMethaneOutput2", 142),
            P("RatioWaterOutput2", 143), P("RatioNitrousOxideOutput2", 144),
            P("TotalMolesOutput2", 145), P("CombustionInput", 146), P("CombustionInput2", 147),
            P("CombustionOutput", 148), P("CombustionOutput2", 149),
            P("OperationalTemperatureEfficiency", 150), P("TemperatureDifferentialEfficiency", 151),
            P("PressureEfficiency", 152), P("CombustionLimiter", 153), P("Throttle", 154),
            P("Rpm", 155), P("Stress", 156), P("InterrogationProgress", 157), P("TargetPadIndex", 158),
            P("SizeX", 160), P("SizeY", 161), P("SizeZ", 162), P("MinimumWattsToContact", 163),
            P("WattsReachingContact", 164), P("Channel0", 165), P("Channel1", 166), P("Channel2", 167),
            P("Channel3", 168), P("Channel4", 169), P("Channel5", 170), P("Channel6", 171),
            P("Channel7", 172), P("LineNumber", 173), P("Flush", 174), P("SoundAlert", 175),
            P("SolarIrradiance", 176), P("RatioLiquidNitrogen", 177),
            P("RatioLiquidNitrogenInput", 178), P("RatioLiquidNitrogenInput2", 179),
            P("RatioLiquidNitrogenOutput", 180), P("RatioLiquidNitrogenOutput2", 181),
            P("VolumeOfLiquid", 182), P("RatioLiquidOxygen", 183), P("RatioLiquidOxygenInput", 184),
            P("RatioLiquidOxygenInput2", 185), P("RatioLiquidOxygenOutput", 186),
            P("RatioLiquidOxygenOutput2", 187), P("RatioLiquidMethane", 188),
            P("RatioLiquidMethaneInput", 189), P("RatioLiquidMethaneInput2", 190),
            P("RatioLiquidMethaneOutput", 191), P("RatioLiquidMethaneOutput2", 192),
            P("RatioSteam", 193), P("RatioSteamInput", 194), P("RatioSteamInput2", 195),
            P("RatioSteamOutput", 196), P("RatioSteamOutput2", 197), P("ContactTypeId", 198),
            P("RatioLiquidCarbonDioxide", 199), P("RatioLiquidCarbonDioxideInput", 200),
            P("RatioLiquidCarbonDioxideInput2", 201), P("RatioLiquidCarbonDioxideOutput", 202),
            P("RatioLiquidCarbonDioxideOutput2", 203), P("RatioLiquidPollutant", 204),
            P("RatioLiquidPollutantInput", 205), P("RatioLiquidPollutantInput2", 206),
            P("RatioLiquidPollutantOutput", 207), P("RatioLiquidPollutantOutput2", 208),
            P("RatioLiquidNitrousOxide", 209), P("RatioLiquidNitrousOxideInput", 210),
            P("RatioLiquidNitrousOxideInput2", 211), P("RatioLiquidNitrousOxideOutput", 212),
            P("RatioLiquidNitrousOxideOutput2", 213), P("Progress", 214), P("DestinationCode", 215),
            P("Acceleration", 216), P("ReferenceId", 217), P("AutoShutOff", 218), P("Mass", 219),
            P("DryMass", 220), P("Thrust", 221), P("Weight", 222), P("ThrustToWeight", 223),
            P("TimeToDestination", 224), P("BurnTimeRemaining", 225), P("AutoLand", 226),
            P("ForwardX", 227), P("ForwardY", 228), P("ForwardZ", 229), P("Orientation", 230),
            P("VelocityX", 231), P("VelocityY", 232), P("VelocityZ", 233), P("PassedMoles", 234),
            P("ExhaustVelocity", 235), P("FlightControlRule", 236), P("ReEntryAltitude", 237),
            P("Apex", 238), P("EntityState", 239), P("DrillCondition", 240), P("Index", 241),
            P("CelestialHash", 242), P("AlignmentError", 243), P("DistanceAu", 244),
            P("OrbitPeriod", 245), P("Inclination", 246), P("Eccentricity", 247),
            P("SemiMajorAxis", 248), P("DistanceKm", 249), P("CelestialParentHash", 250),
            P("TrueAnomaly", 251), P("RatioHydrogen", 252), P("RatioLiquidHydrogen", 253),
            P("RatioPollutedWater", 254), P("Discover", 255), P("Chart", 256), P("Survey", 257),
            P("NavPoints", 258), P("ChartedNavPoints", 259), P("Sites", 260), P("CurrentCode", 261),
            P("Density", 262), P("Richness", 263), P("Size", 264), P("TotalQuantity", 265),
            P("MinedQuantity", 266), P("BestContactFilter", 267), P("NameHash", 268),
            P("Altitude", 269), P("TargetSlotIndex", 270), P("TargetPrefabHash", 271),
            P("Extended", 272), P("NetworkFault", 273), P("ProportionalGain", 274),
            P("IntegralGain", 275), P("DerivativeGain", 276), P("Minimum", 277), P("Setpoint", 278),
            P("Reset", 279), P("StackSize", 280), P("NextWeatherHash", 281),
            P("ContactSlotIndex", 282), P("RatioHydrazine", 283), P("RatioLiquidHydrazine", 284),
            P("RatioLiquidAlcohol", 285), P("RatioHelium", 286), P("RatioLiquidSodiumChloride", 287),
            P("RatioSilanol", 288), P("RatioLiquidSilanol", 289), P("RatioHydrochloricAcid", 290),
            P("RatioLiquidHydrochloricAcid", 291), P("RatioOzone", 292), P("RatioLiquidOzone", 293),
            P("RatioHydrogenInput", 294), P("RatioHydrogenInput2", 295), P("RatioHydrogenOutput", 296),
            P("RatioHydrogenOutput2", 297), P("RatioLiquidHydrogenInput", 298),
            P("RatioLiquidHydrogenInput2", 299), P("RatioLiquidHydrogenOutput", 300),
            P("RatioLiquidHydrogenOutput2", 301), P("RatioPollutedWaterInput", 302),
            P("RatioPollutedWaterInput2", 303), P("RatioPollutedWaterOutput", 304),
            P("RatioPollutedWaterOutput2", 305), P("RatioHydrazineInput", 306),
            P("RatioHydrazineInput2", 307), P("RatioHydrazineOutput", 308),
            P("RatioHydrazineOutput2", 309), P("RatioLiquidHydrazineInput", 310),
            P("RatioLiquidHydrazineInput2", 311), P("RatioLiquidHydrazineOutput", 312),
            P("RatioLiquidHydrazineOutput2", 313), P("RatioLiquidAlcoholInput", 314),
            P("RatioLiquidAlcoholInput2", 315), P("RatioLiquidAlcoholOutput", 316),
            P("RatioLiquidAlcoholOutput2", 317), P("RatioHeliumInput", 318),
            P("RatioHeliumInput2", 319), P("RatioHeliumOutput", 320), P("RatioHeliumOutput2", 321),
            P("RatioLiquidSodiumChlorideInput", 322), P("RatioLiquidSodiumChlorideInput2", 323),
            P("RatioLiquidSodiumChlorideOutput", 324), P("RatioLiquidSodiumChlorideOutput2", 325),
            P("RatioSilanolInput", 326), P("RatioSilanolInput2", 327), P("RatioSilanolOutput", 328),
            P("RatioSilanolOutput2", 329), P("RatioLiquidSilanolInput", 330),
            P("RatioLiquidSilanolInput2", 331), P("RatioLiquidSilanolOutput", 332),
            P("RatioLiquidSilanolOutput2", 333), P("RatioHydrochloricAcidInput", 334),
            P("RatioHydrochloricAcidInput2", 335), P("RatioHydrochloricAcidOutput", 336),
            P("RatioHydrochloricAcidOutput2", 337), P("RatioLiquidHydrochloricAcidInput", 338),
            P("RatioLiquidHydrochloricAcidInput2", 339), P("RatioLiquidHydrochloricAcidOutput", 340),
            P("RatioLiquidHydrochloricAcidOutput2", 341), P("RatioOzoneInput", 342),
            P("RatioOzoneInput2", 343), P("RatioOzoneOutput", 344), P("RatioOzoneOutput2", 345),
            P("RatioLiquidOzoneInput", 346), P("RatioLiquidOzoneInput2", 347),
            P("RatioLiquidOzoneOutput", 348), P("RatioLiquidOzoneOutput2", 349),
            P("HealthDamage", 350), P("StunDamage", 351), P("CurrentNodeType", 352),
            P("TargetNodeType", 353), P("EnergyConvected", 354), P("EnergyRadiated", 355),
            P("Dispense", 356), P("DispenseSlot", 357), P("Gravity", 358),
        };

        public static readonly KeyValuePair<string, int>[] LogicSlotType =
        {
            P("None", 0), P("Occupied", 1), P("OccupantHash", 2), P("Quantity", 3), P("Damage", 4),
            P("Efficiency", 5), P("Health", 6), P("Growth", 7), P("Pressure", 8), P("Temperature", 9),
            P("Charge", 10), P("ChargeRatio", 11), P("Class", 12), P("PressureWaste", 13),
            P("PressureAir", 14), P("MaxQuantity", 15), P("Mature", 16), P("PrefabHash", 17),
            P("Seeding", 18), P("LineNumber", 19), P("Volume", 20), P("Open", 21), P("On", 22),
            P("Lock", 23), P("SortingClass", 24), P("FilterType", 25), P("ReferenceId", 26),
            P("HarvestedHash", 27), P("Mode", 28), P("MaturityRatio", 29), P("SeedingRatio", 30),
            P("FreeSlots", 31), P("TotalSlots", 32),
        };

        public static readonly KeyValuePair<string, int>[] LogicReagentMode =
        {
            P("Contents", 0), P("Required", 1), P("Recipe", 2), P("TotalContents", 3),
        };

        public static readonly KeyValuePair<string, int>[] LogicBatchMethod =
        {
            P("Average", 0), P("Sum", 1), P("Minimum", 2), P("Maximum", 3), P("Count", 4),
        };

        // The basic enums ProgrammableChip registers for the sorter and the printer, in its
        // order (ProgrammableChip's enum list; SortingClass, Slot.Class, SorterInstruction,
        // PrinterInstruction, ConditionOperation). ConditionOperation is registered with no
        // prefix, so a script spells its names bare: `Equals`, `Greater`, `Less`, `NotEquals`.
        public static readonly KeyValuePair<string, int>[] ConditionOperation =
        {
            P("Equals", 0), P("Greater", 1), P("Less", 2), P("NotEquals", 3),
        };

        public static readonly KeyValuePair<string, int>[] SortingClass =
        {
            P("Default", 0), P("Kits", 1), P("Tools", 2), P("Resources", 3), P("Food", 4), P("Clothing", 5), P("Appliances", 6), P("Atmospherics", 7), P("Storage", 8), P("Ores", 9), P("Ices", 10),
        };

        public static readonly KeyValuePair<string, int>[] SlotClass =
        {
            P("None", 0), P("Helmet", 1), P("Suit", 2), P("Back", 3), P("GasFilter", 4), P("GasCanister", 5), P("Motherboard", 6), P("Circuitboard", 7), P("DataDisk", 8), P("Organ", 9), P("Ore", 10), P("Plant", 11), P("Uniform", 12), P("Entity", 13), P("Battery", 14), P("Egg", 15), P("Belt", 16), P("Tool", 17), P("Appliance", 18), P("Ingot", 19), P("Torpedo", 20), P("Cartridge", 21), P("AccessCard", 22), P("Magazine", 23), P("Circuit", 24), P("Bottle", 25), P("ProgrammableChip", 26), P("Glasses", 27), P("CreditCard", 28), P("DirtCanister", 29), P("SensorProcessingUnit", 30), P("LiquidCanister", 31), P("LiquidBottle", 32), P("Wreckage", 33), P("SoundCartridge", 34), P("DrillHead", 35), P("ScanningHead", 36), P("Flare", 37), P("Blocked", 38), P("SuitMod", 39), P("Crate", 40), P("Portables", 41), P("RocketPayload", 42), P("AutoInjector", 43),
        };

        // ColorType (ProgrammableChip.InternalEnums: BasicEnum<ColorType>("Color")).
        public static readonly KeyValuePair<string, int>[] ColorType =
        {
            P("Blue", 0), P("Gray", 1), P("Green", 2), P("Orange", 3), P("Red", 4), P("Yellow", 5), P("White", 6), P("Black", 7), P("Brown", 8), P("Khaki", 9), P("Pink", 10), P("Purple", 11),
        };

        // VentDirection (BasicEnum<VentDirection>("Vent")).
        public static readonly KeyValuePair<string, int>[] VentDirection =
        {
            P("Outward", 0), P("Inward", 1),
        };

        public static readonly KeyValuePair<string, int>[] SorterInstruction =
        {
            P("None", 0), P("FilterPrefabHashEquals", 1), P("FilterPrefabHashNotEquals", 2), P("FilterSortingClassCompare", 3), P("FilterSlotTypeCompare", 4), P("FilterQuantityCompare", 5), P("LimitNextExecutionByCount", 6),
        };

        public static readonly KeyValuePair<string, int>[] PrinterInstruction =
        {
            P("None", 0), P("StackPointer", 1), P("ExecuteRecipe", 2), P("WaitUntilNextValid", 3), P("JumpIfNextInvalid", 4), P("JumpToAddress", 5), P("DeviceSetLock", 6), P("EjectReagent", 7), P("EjectAllReagents", 8), P("MissingRecipeReagent", 9),
        };

        private static KeyValuePair<string, int> P(string name, int value)
        {
            return new KeyValuePair<string, int>(name, value);
        }
    }
}
