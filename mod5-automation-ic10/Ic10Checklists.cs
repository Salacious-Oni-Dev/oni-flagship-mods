using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ic10;
using KSerialization;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // A bin's or a fridge's item checklist (TreeFilterable) set by a chip through the IC
    // Sorter's memory. The rule is Ic10/Ic10Checklist.cs.
    //
    // TreeFilterable.UpdateFilters drops what is no longer accepted.
    //
    // The component is put on every building prefab with a Storage, because most bins get
    // their TreeFilterable only when spawned (FilteredStorage adds it in the bin's
    // OnPrefabInit), not on the prefab. It does nothing on a building that has none. The
    // hermit's house is left out: its checklist is the quest's.
    //
    // No tick of its own: a write schedules one apply, a fifth of a second of game time later.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class Ic10ChecklistFilter : KMonoBehaviour
    {
        private const float ApplyDelaySeconds = 0.2f;

        // Null while every cell is 0, so a building no program has written saves nothing.
        [Serialize]
        private double[] cells;

        [Serialize]
        private bool dirty;

        private readonly Ic10Checklist checklist = new Ic10Checklist();
        private TreeFilterable filterable;
        private bool scheduled;

        // Ic10Math.Hash of a prefab's id, to the prefab's tag. Built once, from every prefab.
        private static Dictionary<int, Tag> prefabsByHash;

        // Null on a building with no checklist. Looked up again when asked if OnSpawn found
        // none: a bin or a fridge can have no checklist at OnSpawn, depending on spawn order.
        // Not spawn order: the fridge never got this component (see Ic10ChecklistPrefabPatch).
        // The rerun logged TreeFilterable found at spawn on both, so the second lookup is a
        // guard only.
        public Ic10Checklist Checklist
        {
            get { return Filterable() != null ? checklist : null; }
        }

        internal bool HasFilterableAtSpawn { get; private set; }

        private TreeFilterable Filterable()
        {
            if (filterable == null)
            {
                filterable = GetComponent<TreeFilterable>();
            }
            return filterable;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            filterable = GetComponent<TreeFilterable>();
            HasFilterableAtSpawn = filterable != null;
            checklist.Load(new Ic10ChecklistState { Cells = cells, Dirty = dirty });
            checklist.Changed = Schedule;
            if (Filterable() != null && DiscoveredResources.Instance != null)
            {
                DiscoveredResources.Instance.OnDiscover += OnDiscover;
            }
            if (checklist.Dirty)
            {
                Schedule();
            }
        }

        protected override void OnCleanUp()
        {
            if (DiscoveredResources.Instance != null)
            {
                DiscoveredResources.Instance.OnDiscover -= OnDiscover;
            }
            base.OnCleanUp();
        }

        // An item kind the list names, found by the colony only now: the list is applied
        // again so it takes the item in, as the side screen would have offered it from now.
        private void OnDiscover(Tag tag, Tag category)
        {
            if (checklist.IsEmpty || !checklist.Hashes().Contains(Ic10Math.Hash(tag.Name)))
            {
                return;
            }
            checklist.Dirty = true;
            Schedule();
        }

        [OnSerializing]
        private void OnSerializing()
        {
            Ic10ChecklistState state = checklist.Save();
            cells = checklist.IsEmpty ? null : state.Cells;
            dirty = state.Dirty;
        }

        private void Schedule()
        {
            if (scheduled || Filterable() == null)
            {
                return;
            }
            scheduled = true;
            GameScheduler.Instance.Schedule("Ic10Checklist", ApplyDelaySeconds, data => Apply());
        }

        private void Apply()
        {
            scheduled = false;
            if (this == null || filterable == null || !checklist.Dirty)
            {
                return;
            }
            checklist.Dirty = false;
            HashSet<Tag> tags = Resolve(checklist.Hashes());
            if (!tags.SetEquals(filterable.AcceptedTags))
            {
                filterable.UpdateFilters(tags);
            }
        }

        // The item kinds named, keeping only those this building could store and the colony
        // has discovered: a prefab carrying one of its Storage's category tags, as the side
        // screen's checklist offers.
        // A hash naming no such prefab is ignored.
        private HashSet<Tag> Resolve(List<int> hashes)
        {
            HashSet<Tag> tags = new HashSet<Tag>();
            Storage storage = filterable.GetFilterStorage();
            List<Tag> categories = storage != null ? storage.storageFilters : null;
            Dictionary<int, Tag> byHash = PrefabsByHash();
            foreach (int hash in hashes)
            {
                Tag tag;
                if (!byHash.TryGetValue(hash, out tag))
                {
                    continue;
                }
                GameObject prefab = Assets.GetPrefab(tag);
                KPrefabID id = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
                // Only what the colony has discovered, as the side screen offers; the game
                // itself drops an undiscovered accepted item on load
                // (TreeFilterable.RemoveIncorrectAcceptedTags). OnDiscover adds it later.
                if (id == null || !Storable(id, categories) || !DiscoveredResources.Instance.IsDiscovered(tag))
                {
                    continue;
                }
                tags.Add(tag);
            }
            return tags;
        }

        private static bool Storable(KPrefabID id, List<Tag> categories)
        {
            if (categories == null)
            {
                return false;
            }
            foreach (Tag category in categories)
            {
                if (id.HasTag(category))
                {
                    return true;
                }
            }
            return false;
        }

        private static Dictionary<int, Tag> PrefabsByHash()
        {
            if (prefabsByHash == null)
            {
                prefabsByHash = new Dictionary<int, Tag>();
                foreach (KPrefabID prefab in Assets.Prefabs)
                {
                    if (prefab != null)
                    {
                        prefabsByHash[Ic10Math.Hash(prefab.PrefabTag.Name)] = prefab.PrefabTag;
                    }
                }
            }
            return prefabsByHash;
        }
    }

    // Assets.AddBuildingDef is the last step of BuildingConfigManager.RegisterBuilding, after
    // the config's DoPostConfigureComplete. Not BuildingLoader.CreateBuildingComplete, which
    // runs BEFORE DoPostConfigureComplete: a fridge, whose config adds its Storage there,
    // would get no checklist.
    [HarmonyPatch(typeof(Assets), nameof(Assets.AddBuildingDef))]
    internal static class Ic10ChecklistPrefabPatch
    {
        private static void Postfix(BuildingDef def)
        {
            GameObject complete = def != null ? def.BuildingComplete : null;
            if (complete != null && complete.GetComponent<Storage>() != null
                && def.PrefabID != LonelyMinionHouseConfig.ID)
            {
                complete.AddOrGet<Ic10ChecklistFilter>();
            }
        }
    }
}
