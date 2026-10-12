using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ic10;
using UnityEngine;

namespace Mod5AutomationIc10
{
    // The data overlay: the cables, and the buildings on them, each layer chosen by a toggle.
    //
    // It is a vanilla overlay mode like the automation overlay (OverlayModes.Logic), and
    // works the way that one does: what the overlay is about is moved to the two
    // "MaskedOverlay" render layers, which the camera draws over the darkened world and
    // which are all the select tool will pick while the overlay is up.
    //
    // THE LAYER CHOOSER is the row of filter toggles vanilla puts in the legend
    // (ToolParameterMenu, as the germ overlay's "all, liquid pipes, gas pipes"). The rows
    // are the object layers a device can stand on, named as the deconstruct tool names them
    // (FilteredDragTool.GetFilterLayerFromObjectLayer), so the strings are vanilla's. One
    // row is on at a time. Only the devices of the chosen row are lifted into the overlay,
    // so only they are lit and only they can be clicked: where a building and a pipe bridge
    // share a cable cell, the row decides which of the two the click selects.
    //
    // Stationeers has nothing to compare: its cables are in the open and it has no overlays.
    internal sealed class Ic10DataOverlay : OverlayModes.Mode
    {
        public static readonly HashedString ID = "Ic10Data";

        public const string NameKey = "STRINGS.UI.OVERLAYS.IC10DATA.NAME";
        public const string ButtonKey = "STRINGS.UI.OVERLAYS.IC10DATA.BUTTON";
        public const string TooltipKey = "STRINGS.UI.TOOLTIPS.IC10DATAOVERLAYSTRING";

        // The mode registered with the loaded colony's OverlayScreen.
        public static Ic10DataOverlay Current { get; private set; }

        private static readonly HashSet<Tag> CableTags = new HashSet<Tag>
        {
            new Tag(Ic10DataCableConfig.Id),
            new Tag(Ic10DataBridgeConfig.Id)
        };

        private readonly int cableLayer;
        private readonly int deviceLayer;
        private readonly int cameraLayerMask;

        private UniformGrid<SaveLoadRoot> cablePartition;
        private readonly HashSet<SaveLoadRoot> cableTargets = new HashSet<SaveLoadRoot>();
        private readonly HashSet<SaveLoadRoot> deviceTargets = new HashSet<SaveLoadRoot>();

        // The devices of the chosen row, on any network. Rebuilt when the networks or the
        // row change, not every frame.
        private readonly List<SaveLoadRoot> wanted = new List<SaveLoadRoot>();
        private readonly HashSet<SaveLoadRoot> wantedSet = new HashSet<SaveLoadRoot>();
        private int wantedVersion = -1;
        private bool rowChanged = true;

        private readonly HashSet<Ic10Network> hovered = new HashSet<Ic10Network>();
        private readonly List<Ic10Network> networks = new List<Ic10Network>();
        private readonly List<KPrefabID> buildings = new List<KPrefabID>();
        private readonly List<SaveLoadRoot> dropped = new List<SaveLoadRoot>();

        public Ic10DataOverlay()
        {
            cableLayer = LayerMask.NameToLayer("MaskedOverlay");
            deviceLayer = LayerMask.NameToLayer("MaskedOverlayBG");
            cameraLayerMask = LayerMask.GetMask("MaskedOverlay", "MaskedOverlayBG");
            legendFilters = CreateDefaultFilters();
            Current = this;
        }

        public override HashedString ViewMode()
        {
            return ID;
        }

        public override string GetSoundName()
        {
            return "Logic";
        }

        // One row for each kind of object layer a device can be on. Backwall is left out:
        // nothing on it is a device (Ic10Devices.IsDevice).
        public override ToolParameterMenu.ToggleData[] CreateDefaultFilters()
        {
            return new ToolParameterMenu.ToggleData[]
            {
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.ALL, ToolParameterMenu.ToggleState.On),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.BUILDINGS, ToolParameterMenu.ToggleState.Off),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.TILES, ToolParameterMenu.ToggleState.Off),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.WIRES, ToolParameterMenu.ToggleState.Off),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.LIQUIDCONDUIT, ToolParameterMenu.ToggleState.Off),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.GASCONDUIT, ToolParameterMenu.ToggleState.Off),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.SOLIDCONDUIT, ToolParameterMenu.ToggleState.Off),
                new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.LOGIC, ToolParameterMenu.ToggleState.Off)
            };
        }

        // The row a device's object layer belongs to; null for a layer with no row, which is
        // shown only under "All". The grouping is FilteredDragTool.GetFilterLayerFromObjectLayer's.
        public static string RowOf(ObjectLayer layer)
        {
            switch (layer)
            {
                case ObjectLayer.Building:
                case ObjectLayer.Gantry:
                    return ToolParameterMenu.FILTERLAYERS.BUILDINGS;
                case ObjectLayer.Wire:
                case ObjectLayer.WireConnectors:
                    return ToolParameterMenu.FILTERLAYERS.WIRES;
                case ObjectLayer.LiquidConduit:
                case ObjectLayer.LiquidConduitConnection:
                    return ToolParameterMenu.FILTERLAYERS.LIQUIDCONDUIT;
                case ObjectLayer.GasConduit:
                case ObjectLayer.GasConduitConnection:
                    return ToolParameterMenu.FILTERLAYERS.GASCONDUIT;
                case ObjectLayer.SolidConduit:
                case ObjectLayer.SolidConduitConnection:
                    return ToolParameterMenu.FILTERLAYERS.SOLIDCONDUIT;
                case ObjectLayer.FoundationTile:
                    return ToolParameterMenu.FILTERLAYERS.TILES;
                case ObjectLayer.LogicGate:
                case ObjectLayer.LogicWire:
                    return ToolParameterMenu.FILTERLAYERS.LOGIC;
                default:
                    return null;
            }
        }

        public override void OnFiltersChanged()
        {
            rowChanged = true;
        }

        // The row that is on.
        public string Row
        {
            get
            {
                for (int i = 0; i < legendFilters.Length; i++)
                {
                    if (legendFilters[i].IsOn)
                    {
                        return legendFilters[i].name;
                    }
                }
                return ToolParameterMenu.FILTERLAYERS.ALL;
            }
        }

        // Choose a row as a click on its toggle does. For tests; the player uses the
        // legend. False if there is no such row.
        public bool Choose(string row)
        {
            bool found = false;
            for (int i = 0; i < legendFilters.Length; i++)
            {
                found |= legendFilters[i].name == row;
            }
            if (!found)
            {
                return false;
            }
            for (int i = 0; i < legendFilters.Length; i++)
            {
                legendFilters[i].state = legendFilters[i].name == row
                    ? ToolParameterMenu.ToggleState.On : ToolParameterMenu.ToggleState.Off;
            }
            OnFiltersChanged();
            return true;
        }

        // Whether the overlay has this object lifted into view. For tests.
        public bool Shows(GameObject go)
        {
            SaveLoadRoot root = go == null ? null : go.GetComponent<SaveLoadRoot>();
            return root != null && (cableTargets.Contains(root) || deviceTargets.Contains(root));
        }

        public override List<LegendEntry> GetCustomLegendData()
        {
            return new List<LegendEntry>
            {
                new LegendEntry("Data Cable", "Every building with data cable under it is on that cable's network.",
                    Ic10DataCable.LegendColour),
                new LegendEntry("Device", "A building on a data network, on the layer chosen above. "
                    + "Only these can be selected while the overlay is up.", Color.white),
                new LegendEntry("Hover over a cable or a device to light its whole network.", null, Color.white,
                    null, null, displaySprite: false)
            };
        }

        public override void Enable()
        {
            Camera.main.cullingMask |= cameraLayerMask;
            SelectTool.Instance.SetLayerMask(cameraLayerMask);
            RegisterSaveLoadListeners();
            cablePartition = PopulatePartition<SaveLoadRoot>(CableTags);
            GridCompositor.Instance.ToggleMinor(on: true);
            // The automation overlay also starts a sound filter of its own here
            // (TechFilterLogicOn). Left out: it needs a reference to the FMOD assembly, and
            // OverlayScreen already starts the filter every overlay shares.
            wantedVersion = -1;
            base.Enable();
        }

        public override void Disable()
        {
            foreach (SaveLoadRoot device in deviceTargets)
            {
                SendBack(device);
            }
            ResetDisplayValues(deviceTargets);
            ResetDisplayValues(cableTargets);
            foreach (SaveLoadRoot cable in cableTargets)
            {
                OwnTint(cable);
            }
            Camera.main.cullingMask &= ~cameraLayerMask;
            SelectTool.Instance.ClearLayerMask();
            UnregisterSaveLoadListeners();
            if (cablePartition != null)
            {
                cablePartition.Clear();
            }
            cableTargets.Clear();
            deviceTargets.Clear();
            wanted.Clear();
            wantedSet.Clear();
            hovered.Clear();
            GridCompositor.Instance.ToggleMinor(on: false);
            base.Disable();
        }

        protected override void OnSaveLoadRootRegistered(SaveLoadRoot item)
        {
            if (cablePartition != null && CableTags.Contains(item.GetComponent<KPrefabID>().GetSaveLoadTag()))
            {
                cablePartition.Add(item);
            }
        }

        protected override void OnSaveLoadRootUnregistered(SaveLoadRoot item)
        {
            if (item == null || item.gameObject == null)
            {
                return;
            }
            cableTargets.Remove(item);
            deviceTargets.Remove(item);
            if (wantedSet.Remove(item))
            {
                wanted.Remove(item);
            }
            if (cablePartition != null)
            {
                cablePartition.Remove(item);
            }
        }

        public override void Update()
        {
            Ic10DataGrid grid = Ic10DataGrid.Current;
            if (grid == null || cablePartition == null)
            {
                return;
            }
            Vector2I min;
            Vector2I max;
            Grid.GetVisibleExtents(out min, out max);

            RemoveOffscreenTargets(cableTargets, min, max, OwnTint);
            RemoveOffscreenTargets(deviceTargets, min, max, SendBack);

            foreach (SaveLoadRoot cable in cablePartition.GetAllIntersecting(new Vector2(min.x, min.y), new Vector2(max.x, max.y)))
            {
                if (cable != null)
                {
                    AddTargetIfVisible(cable, min, max, cableTargets, cableLayer);
                }
            }

            if (rowChanged || wantedVersion != grid.Version)
            {
                ChooseDevices(grid);
            }
            for (int i = 0; i < wanted.Count; i++)
            {
                if (wanted[i] != null)
                {
                    AddTargetIfVisible(wanted[i], min, max, deviceTargets, deviceLayer, BringForward);
                }
            }

            // What the pointer is over lights its whole network, as the pipe and automation
            // overlays do (ModeUtil.GetHighlightScale).
            hovered.Clear();
            float scale = 1f;
            KSelectable hover = SelectTool.Instance == null ? null : SelectTool.Instance.hover;
            if (hover != null)
            {
                networks.Clear();
                grid.NetworksTouching(hover.gameObject, networks);
                for (int i = 0; i < networks.Count; i++)
                {
                    hovered.Add(networks[i]);
                }
                if (hovered.Count > 0)
                {
                    scale = OverlayModes.ModeUtil.GetHighlightScale();
                }
            }
            foreach (SaveLoadRoot cable in cableTargets)
            {
                if (cable != null)
                {
                    Tint(cable, Ic10DataCable.Tint, grid, scale);
                }
            }
            foreach (SaveLoadRoot device in deviceTargets)
            {
                if (device != null)
                {
                    Tint(device, Color.white, grid, scale);
                }
            }
        }

        private void Tint(SaveLoadRoot root, Color32 colour, Ic10DataGrid grid, float scale)
        {
            KBatchedAnimController anim = root.GetComponent<KBatchedAnimController>();
            if (anim == null)
            {
                return;
            }
            if (hovered.Count > 0)
            {
                networks.Clear();
                grid.NetworksTouching(root.gameObject, networks);
                for (int i = 0; i < networks.Count; i++)
                {
                    if (hovered.Contains(networks[i]))
                    {
                        colour.r = (byte)(colour.r * scale);
                        colour.g = (byte)(colour.g * scale);
                        colour.b = (byte)(colour.b * scale);
                        break;
                    }
                }
            }
            anim.TintColour = colour;
        }

        // The buildings on any network whose layer is the chosen row.
        private void ChooseDevices(Ic10DataGrid grid)
        {
            rowChanged = false;
            wantedVersion = grid.Version;
            wanted.Clear();
            wantedSet.Clear();
            string row = Row;
            bool all = row == ToolParameterMenu.FILTERLAYERS.ALL;
            buildings.Clear();
            grid.BuildingsOnNetworks(buildings);
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i].GetComponent<Building>();
                SaveLoadRoot root = buildings[i].GetComponent<SaveLoadRoot>();
                if (building == null || root == null)
                {
                    continue;
                }
                if ((all || RowOf(building.Def.ObjectLayer) == row) && wantedSet.Add(root))
                {
                    wanted.Add(root);
                }
            }

            dropped.Clear();
            foreach (SaveLoadRoot device in deviceTargets)
            {
                if (device == null || !wantedSet.Contains(device))
                {
                    dropped.Add(device);
                }
            }
            for (int i = 0; i < dropped.Count; i++)
            {
                deviceTargets.Remove(dropped[i]);
                if (dropped[i] != null)
                {
                    KBatchedAnimController anim = dropped[i].GetComponent<KBatchedAnimController>();
                    if (anim != null)
                    {
                        ResetDisplayValues(anim);
                    }
                    SendBack(dropped[i]);
                }
            }
            dropped.Clear();
        }

        // As OverlayModes.Logic.Update: a building tagged to sit in front of or behind wires
        // in an overlay is moved to just in front of or behind the cable.
        private static void BringForward(SaveLoadRoot root)
        {
            Vector3 position = root.transform.GetPosition();
            KPrefabID id = root.GetComponent<KPrefabID>();
            if (id != null)
            {
                if (id.HasTag(GameTags.OverlayInFrontOfConduits))
                {
                    position.z = Grid.GetLayerZ(Grid.SceneLayer.LogicWires) - 0.2f;
                }
                else if (id.HasTag(GameTags.OverlayBehindConduits))
                {
                    position.z = Grid.GetLayerZ(Grid.SceneLayer.LogicWires) + 0.2f;
                }
            }
            root.transform.SetPosition(position);
            Resort(root);
        }

        private static void SendBack(SaveLoadRoot root)
        {
            if (root == null)
            {
                return;
            }
            Vector3 position = root.transform.GetPosition();
            position.z = GetDefaultDepth(root);
            root.transform.SetPosition(position);
            Resort(root);
        }

        private static void Resort(SaveLoadRoot root)
        {
            KBatchedAnimController anim = root.GetComponent<KBatchedAnimController>();
            if (anim != null && anim.enabled)
            {
                anim.enabled = false;
                anim.enabled = true;
            }
        }

        // Mode.ResetDisplayValues leaves a controller white; the cable's own tint (white since it
        // has art of its own; it was a placeholder blue).
        private static void OwnTint(SaveLoadRoot root)
        {
            KBatchedAnimController anim = root == null ? null : root.GetComponent<KBatchedAnimController>();
            if (anim != null)
            {
                anim.TintColour = Ic10DataCable.Tint;
            }
        }
    }

    internal static class Ic10DataOverlayStrings
    {
        internal static void Install()
        {
            Strings.Add(Ic10DataOverlay.NameKey, "DATA OVERLAY");
            Strings.Add(Ic10DataOverlay.ButtonKey, "Data Overlay");
            Strings.Add(Ic10DataOverlay.TooltipKey, "Displays data cables and the buildings on them");

            // SelectToolHoverTextCard asks which status items an overlay shows, for every
            // item on every frame of hovering, and warns in the log for a mode it does not
            // know. This overlay shows what the plain view shows.
            IDictionary known = AccessTools.Field(typeof(StatusItem), "overlayBitfieldMap").GetValue(null) as IDictionary;
            if (known != null)
            {
                known[Ic10DataOverlay.ID] = StatusItem.StatusItemOverlays.None;
            }
            else
            {
                Mod5Log.Info("data overlay: StatusItem.overlayBitfieldMap not found; the log will warn while hovering");
            }
        }
    }

    // ---- the places a new overlay has to be added -----------------------------------------

    // The modes are a private list built in OnSpawn.
    [HarmonyPatch(typeof(OverlayScreen), "RegisterModes")]
    internal static class OverlayScreen_RegisterModes_Ic10
    {
        private static void Postfix(OverlayScreen __instance)
        {
            AccessTools.Method(typeof(OverlayScreen), "RegisterMode").Invoke(__instance, new object[] { new Ic10DataOverlay() });
        }
    }

    // The legend shows nothing, the filter toggles included, for a mode with no entry in its
    // list. The list comes from the prefab; OnSpawn then looks every name up as a string key.
    [HarmonyPatch(typeof(OverlayLegend), "OnSpawn")]
    internal static class OverlayLegend_OnSpawn_Ic10
    {
        private static void Prefix(OverlayLegend __instance)
        {
            List<OverlayLegend.OverlayInfo> list =
                AccessTools.Field(typeof(OverlayLegend), "overlayInfoList").GetValue(__instance) as List<OverlayLegend.OverlayInfo>;
            if (list == null || list.Exists(info => info.mode == Ic10DataOverlay.ID))
            {
                return;
            }
            list.Add(new OverlayLegend.OverlayInfo
            {
                name = Ic10DataOverlay.NameKey,
                mode = Ic10DataOverlay.ID,
                infoUnits = new List<OverlayLegend.OverlayInfoUnit>(),
                diagrams = new List<GameObject>(),
                isProgrammaticallyPopulated = true
            });
        }
    }

    // The hover card lists, in an overlay, only what that overlay is about: it keeps a
    // filter per mode (the automation overlay's is ShouldShowLogicOverlay). Ours is the same
    // test the overlay draws by, so the card names the cable and the devices of the chosen
    // row and nothing else.
    [HarmonyPatch(typeof(SelectToolHoverTextCard), "ShouldShowSelectableInCurrentOverlay")]
    internal static class SelectToolHoverTextCard_ShouldShow_Ic10
    {
        private static void Postfix(KSelectable selectable, ref bool __result)
        {
            if (!__result || selectable == null || OverlayScreen.Instance == null
                || OverlayScreen.Instance.GetMode() != Ic10DataOverlay.ID)
            {
                return;
            }
            Ic10DataOverlay overlay = Ic10DataOverlay.Current;
            // Something with no prefab id is the cell itself, which every overlay keeps.
            if (overlay != null && selectable.GetComponent<KPrefabID>() != null)
            {
                __result = overlay.Shows(selectable.gameObject);
            }
        }
    }

    // The button in the overlay bar. No hotkey: all fifteen overlay actions are taken.
    // PLACEHOLDER ICON: the automation overlay's.
    [HarmonyPatch(typeof(OverlayMenu), "InitializeToggles")]
    internal static class OverlayMenu_InitializeToggles_Ic10
    {
        private static void Postfix(OverlayMenu __instance)
        {
            IList toggles = AccessTools.Field(typeof(OverlayMenu), "overlayToggleInfos").GetValue(__instance) as IList;
            System.Type info = AccessTools.Inner(typeof(OverlayMenu), "OverlayToggleInfo");
            if (toggles == null || info == null)
            {
                Mod5Log.Info("data overlay: OverlayMenu has changed shape; no button added");
                return;
            }
            string button = Strings.Get(Ic10DataOverlay.ButtonKey);
            string tooltip = Strings.Get(Ic10DataOverlay.TooltipKey);
            // (text, icon_name, sim_view, required_tech_item, hotKey, tooltip, tooltip_header)
            toggles.Add(System.Activator.CreateInstance(info, new object[]
            {
                button, "overlay_logic", Ic10DataOverlay.ID, "", global::Action.NumActions, tooltip, button
            }));
        }
    }
}
