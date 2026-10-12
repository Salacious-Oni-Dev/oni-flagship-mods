using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using STRINGS;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // A power transformer's output cap: the most watts it passes, set by the player on a
    // slider or by a chip through Setting. Owner.
    //
    // This is Stationeers' Transformer.Setting: its GetGeneratedPower is
    // Min(Setting, InputNetwork.PotentialLoad), and Setting is clamped to 0..OutputMaximum.
    // ONI's PowerTransformer has no such number, so this adds one. PowerTransformer offers
    // its output circuit Min(battery.JoulesAvailable, WattageRating * dt) joules each 200 ms
    // (EnergySim200ms); the patch below lowers that offer to the cap. A transformer whose
    // cap was never touched behaves exactly as vanilla's.
    //
    // The component is put on every building prefab that has a PowerTransformer, so a
    // transformer another mod adds gets the cap too.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10TransformerCap : KMonoBehaviour, ISingleSliderControl
    {
        public const string TitleKey = "STRINGS.UI.UISIDESCREENS.IC10TRANSFORMERCAP.TITLE";
        public const string TooltipKey = "STRINGS.UI.UISIDESCREENS.IC10TRANSFORMERCAP.TOOLTIP";

        // The transformers in the world, for the patch: a lookup, not a GetComponent, five
        // times a second for each.
        internal static readonly Dictionary<PowerTransformer, Ic10TransformerCap> Live =
            new Dictionary<PowerTransformer, Ic10TransformerCap>();

        // Watts. Negative: never set, which is the rating whatever the rating becomes.
        [Serialize]
        private float cap = -1f;

        private PowerTransformer transformer;

        private PowerTransformer Transformer
        {
            get
            {
                if (transformer == null)
                {
                    transformer = GetComponent<PowerTransformer>();
                }
                return transformer;
            }
        }

        // Generator.WattageRating: Stationeers' OutputMaximum.
        public float Rating
        {
            get { return Transformer != null ? Transformer.WattageRating : 0f; }
        }

        public bool IsCapped
        {
            get { return cap >= 0f && cap < Rating; }
        }

        public float Cap
        {
            get { return cap < 0f ? Rating : Mathf.Min(cap, Rating); }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (Transformer != null)
            {
                Live[Transformer] = this;
            }
        }

        protected override void OnCleanUp()
        {
            // Not Unity's null test: the transformer may already be destroyed, and its
            // entry has to go all the same.
            if ((object)transformer != null)
            {
                Live.Remove(transformer);
            }
            base.OnCleanUp();
        }

        public string SliderTitleKey
        {
            get { return TitleKey; }
        }

        public string SliderUnits
        {
            get { return UI.UNITSUFFIXES.ELECTRICAL.WATT; }
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
            return Rating;
        }

        public float GetSliderValue(int index)
        {
            return Cap;
        }

        // Transformer.Setting's setter: Mathf.Clamp(value, 0f, OutputMaximum).
        public void SetSliderValue(float value, int index)
        {
            if (float.IsNaN(value))
            {
                return;
            }
            cap = Mathf.Clamp(value, 0f, Rating);
        }

        public string GetSliderTooltipKey(int index)
        {
            return TooltipKey;
        }

        public string GetSliderTooltip(int index)
        {
            return string.Format(Strings.Get(TooltipKey), GameUtil.GetFormattedWattage(Cap));
        }
    }

    internal static class Ic10TransformerCapStrings
    {
        internal static void Install()
        {
            Strings.Add(Ic10TransformerCap.TitleKey, "Output Limit");
            Strings.Add(Ic10TransformerCap.TooltipKey, "This transformer passes at most {0} to its output wire");
        }
    }

    // BuildingLoader.CreateBuildingComplete makes the finished-building prefab, after the
    // config's ConfigureBuildingTemplate has put the PowerTransformer on it.
    [HarmonyPatch(typeof(BuildingLoader), "CreateBuildingComplete")]
    internal static class Ic10TransformerCapPrefabPatch
    {
        private static void Postfix(GameObject __result)
        {
            if (__result != null && __result.GetComponent<PowerTransformer>() != null)
            {
                __result.AddOrGet<Ic10TransformerCap>();
            }
        }
    }

    // After PowerTransformer.EnergySim200ms has set what the transformer offers its output
    // circuit this tick, and before CircuitManager.Sim200msLast draws on it (Game.SimEveryTick
    // runs them in that order).
    [HarmonyPatch(typeof(PowerTransformer), "EnergySim200ms")]
    internal static class Ic10TransformerCapPatch
    {
        private static void Postfix(PowerTransformer __instance, float dt)
        {
            Ic10TransformerCap cap;
            if (Ic10TransformerCap.Live.TryGetValue(__instance, out cap) && cap.IsCapped)
            {
                float most = cap.Cap * dt;
                if (__instance.JoulesAvailable > most)
                {
                    __instance.AssignJoulesAvailable(most);
                }
            }
        }
    }

    // The player's energy panel is vanilla's: ONI files a transformer under
    // outputTransformers, never under generators, so CircuitManager's generated-watts sums
    // never count it, and a postfix there could only take watts off the real generators of a
    // circuit a capped transformer also fed. A chip's PowerPotential counts transformers
    // itself (Ic10OniViews.CircuitWatts).
}
