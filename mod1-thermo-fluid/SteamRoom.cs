using HarmonyLib;
using Klei.AI;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// The Steam Room. A Rec Room whose Sauna sits in warm, humid, sealed air becomes a Steam Room,
    /// and a Sauna used there gives a stronger effect, the way Klei's Massage Clinic does for a
    /// massage table (<c>RelaxationPoint.OnStartWork</c>).
    ///
    /// Everything room-shaped here goes through the framework (<see cref="CustomRoomTypes"/>,
    /// <see cref="AtmosphereConstraints"/>), so a third-party mod can build its
    /// own air-conditioned room the same way. What is Mod 1's own is the Sauna made physical.
    /// Klei's Sauna turns 25 kg of piped steam per use into 25 kg of 80 °C water inside the
    /// building (<c>SaunaWorkable.OnStopWork</c>), deleting the steam's latent heat on the way.
    /// Here it vents its steam into its own room instead, through a humidistat, and promotes
    /// that room with its air (<see cref="GasMixtureFacade.PromoteRoomAndAbsorb"/>).
    /// The steam brings its heat in with it, the sim's mixture condensation rains
    /// out what the air cannot hold, and the player drains the water. Steam the humidistat does
    /// not want stays in the Sauna's storage: nothing is deleted.
    /// </summary>
    public static class SteamRoom
    {
        public const string RoomTypeId = "SteamRoom";
        public const string EffectId = "SteamRoomSauna";

        /// <summary>The air a room needs to BECOME a Steam Room...</summary>
        public const float EnterHumidity = 0.60f;
        public const float EnterLoK = 313.15f;
        public const float EnterHiK = 333.15f;

        /// <summary>...and the looser band it keeps it in.</summary>
        public const float StayHumidity = 0.50f;
        public const float StayMarginK = 3f;

        /// <summary>
        /// The Steam Room effect: morale for one cycle. Ours, untuned, and meant to beat Klei's own "Sauna" effect,
        /// whose values live in game data rather than in the C#.
        /// </summary>
        public const float EffectMorale = 4f;
        public const float EffectSeconds = 600f;

        public static void Register()
        {
            CustomRoomTypes.Register(new CustomRoomTypeSpec
            {
                Id = RoomTypeId,
                Name = "Steam Room",
                Description = "A sealed room of warm, humid air around a Sauna. Duplicants who "
                    + "use the Sauna here come out far more relaxed than in a plain Rec Room.",
                Tooltip = "A Rec Room whose Sauna sits in warm steam. The Sauna fills the room "
                    + "itself; condensed water collects on the floor and needs draining.",
                Effect = "Sauna use gives \"Steam Room\": +" + EffectMorale + " Morale for a cycle.",
                CategoryId = "Recreation",
                Primary = () => new RoomConstraints.Constraint(
                    building => building.IsPrefabID(new Tag("Sauna")), null, 1, "Sauna",
                    "Requires a Sauna."),
                Additional = () => new[]
                {
                    RoomConstraints.NO_INDUSTRIAL_MACHINERY,
                    RoomConstraints.DECORATIVE_ITEM,
                    RoomConstraints.MINIMUM_SIZE_12,
                    RoomConstraints.MAXIMUM_SIZE_64,
                    AtmosphereConstraints.SealedMixtureRoom("Sealed air",
                        "The room's air must be sealed in (the Sauna does this when it vents)."),
                    AtmosphereConstraints.RelativeHumidityAtLeast(SimHashes.Steam, EnterHumidity,
                        StayHumidity, "Humid",
                        "Steam humidity at least " + (int)(EnterHumidity * 100f) + " %."),
                    AtmosphereConstraints.GasTemperatureBetween(EnterLoK, EnterHiK, StayMarginK,
                        "Warm", "Air between " + (int)(EnterLoK - 273.15f) + " and "
                        + (int)(EnterHiK - 273.15f) + " °C."),
                },
                // Above Klei's Rec Room (priority 0) so that, with every condition met, Klei's own
                // HasAmbiguousRoomType picks this; an upgrade path, so that with only some met the
                // room stays a Rec Room rather than dropping to none.
                Priority = 1,
                UpgradeOf = "RecRoom",
                SingleAssignee = true,
                PriorityBuildingUse = true,
                SortKey = 17,
            });
        }

        [HarmonyPatch(typeof(Db), "Initialize")]
        internal static class Db_Initialize_AddSteamRoomEffect
        {
            private static void Postfix()
            {
                var effect = new Effect(EffectId, "Steam Room",
                    "This Duplicant came out of a proper Steam Room glowing.", EffectSeconds,
                    show_in_ui: true, trigger_floating_text: true, is_bad: false);
                effect.Add(new AttributeModifier(Db.Get().Attributes.QualityOfLife.Id, EffectMorale,
                    "Steam Room"));
                Db.Get().effects.Add(effect);
            }
        }

        // This Sauna makes no water, so it has no water outlet. Klei's def declares a liquid output port, and BuildingLoader gives every def
        // with one a RequireOutputs, whose output_connected flag kept an unpiped Sauna
        // non-operational. With no output conduit type there is no port to
        // draw or pipe, and no RequireOutputs.
        [HarmonyPatch(typeof(SaunaConfig), nameof(SaunaConfig.CreateBuildingDef))]
        internal static class SaunaConfig_CreateBuildingDef_NoOutlet
        {
            private static void Postfix(BuildingDef __result)
            {
                __result.OutputConduitType = ConduitType.None;
            }
        }

        [HarmonyPatch(typeof(SaunaConfig), nameof(SaunaConfig.ConfigureBuildingTemplate))]
        internal static class SaunaConfig_ConfigureBuildingTemplate_Vent
        {
            private static void Postfix(GameObject go)
            {
                go.AddOrGet<SaunaVent>();
                // Klei's water dispenser goes with the port: its output_conduit flag would keep
                // the Sauna non-operational the same way. Nothing in Sauna or SaunaWorkable
                // references it.
                ConduitDispenser dispenser = go.GetComponent<ConduitDispenser>();
                if (dispenser != null)
                {
                    Object.DestroyImmediate(dispenser);
                }
            }
        }

        // A Sauna used in a Steam Room gives the Steam Room effect in place of Klei's "Sauna".
        [HarmonyPatch(typeof(SaunaWorkable), "OnCompleteWork")]
        internal static class SaunaWorkable_OnCompleteWork_SteamRoom
        {
            private static void Postfix(SaunaWorkable __instance, WorkerBase worker)
            {
                if (worker == null || !CustomRoomTypes.IsIn(__instance.gameObject, RoomTypeId))
                {
                    return;
                }
                Effects effects = worker.GetComponent<Effects>();
                if (effects == null)
                {
                    return;
                }
                Sauna sauna = __instance.GetComponent<Sauna>();
                if (sauna != null && !string.IsNullOrEmpty(sauna.specificEffect))
                {
                    effects.Remove(sauna.specificEffect);
                }
                effects.Add(EffectId, should_save: true);
            }
        }

        // No steam-to-water conversion inside the building. Klei's OnStopWork does three
        // things; the first two are kept, the third (ConsumeAndGetDisease 25 kg of steam, then
        // AddLiquid 25 kg of water at 353.15 K) is what this mod replaces with SaunaVent.
        [HarmonyPatch(typeof(SaunaWorkable), "OnStopWork")]
        internal static class SaunaWorkable_OnStopWork_NoConversion
        {
            private static bool Prefix(SaunaWorkable __instance, WorkerBase worker)
            {
                Operational operational = __instance.GetComponent<Operational>();
                if (operational != null)
                {
                    operational.SetActive(false);
                }
                if (worker != null)
                {
                    worker.GetComponent<Effects>()?.Remove("SaunaRelaxing");
                }
                return false;
            }
        }
    }

    /// <summary>
    /// The Sauna's humidistat. Once a second, while the Sauna is operational and stands in
    /// an enclosed room: promote the room with its air if it is not sealed yet, then, while the
    /// room is below <see cref="TargetHumidity"/> and <see cref="TargetK"/>, move up to
    /// <see cref="VentKgPerSecond"/> of steam out of storage into the room at the steam's own
    /// temperature. UNTUNED numbers.
    /// </summary>
    public class SaunaVent : KMonoBehaviour, ISim1000ms
    {
        public const float DefaultTargetHumidity = 0.70f;
        public const float DefaultTargetK = 333.15f;
        public const float VentKgPerSecond = 0.1f;

        /// <summary>The humidistat's setpoints. Static, so a test can push the room past
        /// saturation (to make the sim's condensation run) and back; gameplay never changes them.</summary>
        public static float TargetHumidity = DefaultTargetHumidity;
        public static float TargetK = DefaultTargetK;

        /// <summary>Steam vented into the room so far, kg (the rig's books read it).</summary>
        public float VentedKg;

        /// <summary>The heat that steam carried in, kJ (m c T at the steam's temperature).</summary>
        public float VentedKJ;

        [MyCmpGet]
        private Operational operational;

        [MyCmpGet]
        private Storage storage;

        public void Sim1000ms(float dt)
        {
            if (operational == null || storage == null || !operational.IsOperational)
            {
                return;
            }
            Room room = Game.Instance != null && Game.Instance.roomProber != null
                ? Game.Instance.roomProber.GetRoomOfGameObject(gameObject)
                : null;
            if (room == null)
            {
                return; // not enclosed: there is no room to fill, so the steam stays stored
            }
            int ventCell = Grid.OffsetCell(Grid.PosToCell(this), 0, 1);
            if (!Grid.IsValidCell(ventCell) || Grid.Solid[ventCell])
            {
                return;
            }
            AtmosphereConstraints.Snapshot air = AtmosphereConstraints.Measure(room);
            if (air == null)
            {
                return;
            }
            if (!air.Sealed)
            {
                GasMixtureFacade.PromoteRoomAndAbsorb(ventCell);
                return; // lands next tick; vent once the room is sealed
            }
            if (AtmosphereConstraints.RelativeHumidity(air, SimHashes.Steam) >= TargetHumidity
                || air.TemperatureK >= TargetK)
            {
                return;
            }
            PrimaryElement stored = storage.FindPrimaryElement(SimHashes.Steam);
            if (stored == null || stored.Mass <= 0f)
            {
                return;
            }
            float want = Mathf.Min(VentKgPerSecond * dt, stored.Mass);
            storage.ConsumeAndGetDisease(SimHashes.Steam.CreateTag(), want, out float taken,
                out Klei.SimUtil.DiseaseInfo _, out float temperature);
            if (taken <= 0f)
            {
                return;
            }
            Element steam = ElementLoader.FindElementByHash(SimHashes.Steam);
            GasMixtureFacade.Inject(ventCell, ElementLoader.elements.IndexOf(steam), taken, temperature);
            VentedKg += taken;
            VentedKJ += taken * steam.specificHeatCapacity * temperature;
        }
    }
}
