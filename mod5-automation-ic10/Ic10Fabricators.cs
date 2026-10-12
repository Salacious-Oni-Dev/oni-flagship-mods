using System;
using System.Runtime.Serialization;
using HarmonyLib;
using Ic10;
using KSerialization;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The recipe a chip has selected on a machine that makes things (ComplexFabricator), and
    // the machine as the adapter sees it.
    //
    // Stationeers' SimpleFabricatorBase has one selected recipe (CurrentIndex) and a run
    // flag; ONI's ComplexFabricator has an order count on every recipe at once and no
    // selection. So the selection is a number this component keeps and saves. What a logic
    // type does with it is decided in the adapter (Ic10/Ic10OniBuilding.cs, MapFabricator).
    //
    // Run in the game: RecipeHash, Activate, the printer stack's put/get/clr. A counted order
    // finishing is read from ComplexFabricator.CompleteWorkingOrder, not run.
    //
    // The component is put on every building prefab that has a ComplexFabricator, so a
    // machine another mod adds gets it too.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10FabricatorSelect : KMonoBehaviour, ISim200ms
    {
        // GameHashes.FabricatorOrderCompleted. ComplexFabricator.CompleteWorkingOrder:
        // Trigger(1355439576, recipe), once for each order finished, which is one run of
        // the recipe and one off its order count, however much that run makes.
        private const int OrderCompleteEvent = 1355439576;

        // Stationeers' logic tick, in seconds of game time.
        private const float LogicTickSeconds = 0.5f;

        // ComplexRecipe.id of the selected recipe. Null: never selected, which is the
        // machine's first recipe, as SimpleFabricatorBase.CurrentIndex starts at 0.
        [Serialize]
        private string selected;

        // The instruction stack, as Ic10PrinterStack.Save gives it.
        [Serialize]
        private double[] stackCells;

        [Serialize]
        private int stackPointer;

        [Serialize]
        private int stackOrderCell = -1;

        [Serialize]
        private double stackOrderValue;

        [Serialize]
        private int stackOrderHandler;

        private ComplexFabricator fabricator;
        private Operational operational;
        private int[] productHashes;
        private int[] idHashes;
        private int[] made;
        private Ic10OniFabricator described;
        private Ic10PrinterStack stack;
        private float sinceTick;
        // ComplexFabricator fills its order counts in its own OnSpawn; asked before that,
        // GetRecipeQueueCount throws and SetRecipeQueueCount throws further in.
        private bool spawned;

        public Ic10PrinterStack Stack
        {
            get
            {
                Describe();
                return stack;
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            operational = GetComponent<Operational>();
            spawned = true;
            Subscribe(OrderCompleteEvent, OnOrderComplete);
            Describe();
            stack.Load(new Ic10PrinterStackState
            {
                Cells = stackCells,
                Pointer = stackPointer,
                OrderCell = stackOrderCell,
                OrderValue = stackOrderValue,
                OrderHandler = stackOrderHandler,
            });
        }

        [OnSerializing]
        private void OnSerializing()
        {
            if (stack == null)
            {
                return;
            }
            Ic10PrinterStackState state = stack.Save();
            stackCells = state.Cells;
            stackPointer = state.Pointer;
            stackOrderCell = state.OrderCell;
            stackOrderValue = state.OrderValue;
            stackOrderHandler = state.OrderHandler;
        }

        private void OnOrderComplete(object data)
        {
            ComplexRecipe recipe = data as ComplexRecipe;
            if (recipe == null)
            {
                return;
            }
            int index = Array.IndexOf(Recipes(), recipe);
            if (index >= 0)
            {
                made[index]++;
            }
        }

        // The stack runs at Stationeers' logic tick, while the machine could work
        // (SimpleFabricatorBase.OnLogicTick: OnOff and Powered).
        public void Sim200ms(float dt)
        {
            sinceTick += dt;
            if (sinceTick < LogicTickSeconds)
            {
                return;
            }
            sinceTick -= LogicTickSeconds;
            if (stack != null)
            {
                stack.Tick();
            }
        }

        public string SelectedId
        {
            get { return selected; }
        }

        private ComplexFabricator Fabricator
        {
            get
            {
                if (fabricator == null)
                {
                    fabricator = GetComponent<ComplexFabricator>();
                }
                return fabricator;
            }
        }

        // ComplexFabricator.GetRecipes: the machine's recipes for the save's content, sorted
        // once and kept. The side screen lists the same array.
        private ComplexRecipe[] Recipes()
        {
            ComplexRecipe[] recipes = Fabricator.GetRecipes();
            if (productHashes == null || productHashes.Length != recipes.Length)
            {
                productHashes = new int[recipes.Length];
                idHashes = new int[recipes.Length];
                made = new int[recipes.Length];
                for (int i = 0; i < recipes.Length; i++)
                {
                    ComplexRecipe recipe = recipes[i];
                    productHashes[i] = recipe.results != null && recipe.results.Length > 0 ? Ic10Math.Hash(recipe.FirstResult.Name) : 0;
                    idHashes[i] = Ic10Math.Hash(recipe.id);
                }
            }
            return recipes;
        }

        // The side screen offers a recipe once its research is done
        // (ComplexFabricatorSideScreen, IsRequiredTechOrPOIUnlocked); a chip gets no more.
        private static bool Offered(ComplexRecipe recipe)
        {
            return recipe.IsRequiredTechOrPOIUnlocked();
        }

        // The machine fills its order counts in its own OnSpawn; this component's OnSpawn
        // does not show that the machine's has run, so the machine is asked.
        private bool CountsReady()
        {
            return spawned && Fabricator != null && Fabricator.isSpawned;
        }

        private int SelectedIndex()
        {
            ComplexRecipe[] recipes = Recipes();
            if (selected != null)
            {
                for (int i = 0; i < recipes.Length; i++)
                {
                    if (recipes[i].id == selected)
                    {
                        return i;
                    }
                }
            }
            return recipes.Length > 0 ? 0 : -1;
        }

        public Ic10OniFabricator Describe()
        {
            if (described != null)
            {
                return described;
            }
            described = new Ic10OniFabricator
            {
                Count = () => Recipes().Length,
                ProductHash = i =>
                {
                    ComplexRecipe[] recipes = Recipes();
                    return Offered(recipes[i]) ? productHashes[i] : 0;
                },
                RecipeIdHash = i =>
                {
                    ComplexRecipe[] recipes = Recipes();
                    return Offered(recipes[i]) ? idHashes[i] : 0;
                },
                GetQueue = i => CountsReady() ? Fabricator.GetRecipeQueueCount(Recipes()[i]) : 0,
                // SetRecipeQueueCount has no range check of its own; the adapter passes 0,
                // 1 and QUEUE_INFINITE, the instruction stack 0 to 99.
                SetQueue = (i, count) =>
                {
                    if (CountsReady())
                    {
                        Fabricator.SetRecipeQueueCount(Recipes()[i], count);
                    }
                },
                Working = () =>
                {
                    ComplexRecipe working = Fabricator.CurrentWorkingOrder;
                    return working == null ? -1 : Array.IndexOf(Recipes(), working);
                },
                Progress = () => Fabricator.OrderProgress,
                GetSelected = SelectedIndex,
                SetSelected = i => selected = Recipes()[i].id,
                Made = i =>
                {
                    Recipes();
                    return made[i];
                },
            };
            stack = new Ic10PrinterStack(described, () => operational == null || operational.IsOperational);
            described.Stack = stack;
            return described;
        }
    }

    // BuildingLoader.CreateBuildingComplete makes the finished-building prefab, after the
    // config's ConfigureBuildingTemplate has put the ComplexFabricator on it.
    [HarmonyPatch(typeof(BuildingLoader), "CreateBuildingComplete")]
    internal static class Ic10FabricatorSelectPrefabPatch
    {
        private static void Postfix(GameObject __result)
        {
            if (__result != null && __result.GetComponent<ComplexFabricator>() != null)
            {
                __result.AddOrGet<Ic10FabricatorSelect>();
            }
        }
    }

    // A planter's plot as the adapter sees it.
    internal static class Ic10Plots
    {
        public static Ic10OniPlot Describe(PlantablePlot plot)
        {
            Func<GameObject> plant = () => plot.Occupant;
            return new Ic10OniPlot
            {
                HasPlant = () => plant() != null,
                PlantHash = () =>
                {
                    KPrefabID id = plant().GetComponent<KPrefabID>();
                    return id == null ? 0 : Ic10Math.Hash(id.PrefabTag.Name);
                },
                // A plant with no Growing (a decor plant) is as grown as it gets.
                Grown = () =>
                {
                    Growing growing = plant().GetComponent<Growing>();
                    return growing == null ? 1.0 : growing.PercentGrown();
                },
                Harvestable = () =>
                {
                    Harvestable harvestable = plant().GetComponent<Harvestable>();
                    return harvestable != null && harvestable.CanBeHarvested;
                },
                Wilting = () =>
                {
                    WiltCondition wilt = plant().GetComponent<WiltCondition>();
                    return wilt != null && wilt.IsWilting();
                },
                Harvest = () =>
                {
                    HarvestDesignatable designatable = plant().GetComponent<HarvestDesignatable>();
                    if (designatable != null)
                    {
                        designatable.MarkForHarvest();
                    }
                },
                GetAutoHarvest = () =>
                {
                    HarvestDesignatable designatable = plant().GetComponent<HarvestDesignatable>();
                    return designatable != null && designatable.HarvestWhenReady;
                },
                SetAutoHarvest = value =>
                {
                    HarvestDesignatable designatable = plant().GetComponent<HarvestDesignatable>();
                    if (designatable != null && designatable.HarvestWhenReady != value)
                    {
                        designatable.SetHarvestWhenReady(value);
                    }
                },
            };
        }
    }
}
