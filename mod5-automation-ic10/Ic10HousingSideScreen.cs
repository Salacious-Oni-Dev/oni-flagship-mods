using System;
using System.Collections.Generic;
using HarmonyLib;
using Ic10;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mod5AutomationIc10
{
    // The IC Housing in ONI's own details panel: what a player sets on a housing without
    // writing code.
    //
    //   status       running / switched off / no power / the error and its line
    //   d0..d5       each socket's label and building, with
    //                  <  >   Stationeers' screwdriver on the housing's six buttons
    //                         (CircuitHousing.InteractWith -> Logicable._TryGetNextLogicDevice
    //                         -> _GetNext with allowNull): a plain press steps to the next
    //                         building on the cable in name order and wraps, never to none;
    //                         the other way steps back, and back from the first is none, and
    //                         back from none is the last
    //                  Link   the link tool
    //                  Clear  none
    //   Edit program the program window (Ic10Editor, stage 2)
    //
    // Built in code as OniFramework's BuildingPaintSideScreen is, and registered the same way
    // (DetailsScreen.sideScreens, a private list every mod with a side screen reaches by
    // reflection): the widgets on a prefab once, the listeners on each clone, because
    // Instantiate does not copy a listener added in code. The user-menu button stays, so a
    // game update that moves the list leaves the housing usable.
    public sealed class Ic10HousingSideScreen : SideScreenContent
    {
        private const float RowHeight = 26f;
        private const float SmallButton = 26f;
        private const float WideButton = 48f;
        private const float RefreshSeconds = 0.25f;

        private Ic10HousingBuilding target;
        private TextMeshProUGUI status;
        private readonly TextMeshProUGUI[] socketText = new TextMeshProUGUI[Ic10Housing.SocketCount];
        private float nextRefresh;

        // The housing shown, and a socket row's text, for tests.
        public Ic10HousingBuilding Target
        {
            get { return target; }
        }

        public string SocketLine(int socket)
        {
            return socket >= 0 && socket < socketText.Length && socketText[socket] != null ? socketText[socket].text : null;
        }

        public override bool IsValidForTarget(GameObject go)
        {
            return go != null && go.GetComponent<Ic10HousingBuilding>() != null;
        }

        public override string GetTitle()
        {
            return "IC10";
        }

        public override void SetTarget(GameObject go)
        {
            target = go != null ? go.GetComponent<Ic10HousingBuilding>() : null;
            Refresh();
        }

        public override void ClearTarget()
        {
            target = null;
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            BindUi();
        }

        private void Update()
        {
            if (target == null || Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        // The housing's state and its sockets, read fresh each time: they change under the
        // screen (a program writes, a building is deconstructed, the link tool sets one).
        private void Refresh()
        {
            if (target == null || status == null)
            {
                return;
            }
            string error = target.ErrorText();
            Ic10Housing housing = target.Housing;
            if (error != null)
            {
                status.text = "Error: " + error;
                status.color = Ic10Ui.ErrorColour;
            }
            else
            {
                Ic10Chip chip = target.Chip;
                string state = housing == null ? "No chip" : !housing.On ? "Switched off" : !housing.Powered ? "No power" : "Running";
                status.text = chip == null || housing == null || !housing.On || !housing.Powered
                    ? state
                    : state + ", line " + chip.LineNumber + " of " + chip.LineCount;
                status.color = Ic10Ui.TextColour;
            }
            for (int i = 0; i < socketText.Length; i++)
            {
                if (socketText[i] == null)
                {
                    continue;
                }
                int id = target.GetSocket(i);
                string name = id > 0 ? (Ic10Devices.Name(id) ?? "(gone)") : "none";
                string label = housing != null ? housing.GetDeviceLabel(i) : null;
                socketText[i].text = "<b>d" + i + "</b>" + (string.IsNullOrEmpty(label) ? string.Empty : " " + label)
                    + ": " + STRINGS.UI.StripLinkFormatting(name);
            }
        }

        // Stationeers' screwdriver step (Logicable._GetNext, allowNull): `back` is the press
        // with the modifier held.
        public static int Step(List<KPrefabID> devices, int current, bool back)
        {
            int count = devices == null ? 0 : devices.Count;
            if (count == 0)
            {
                return 0;
            }
            int index = current > 0 ? devices.FindIndex(d => d != null && d.InstanceID == current) : -1;
            if (current > 0 && index < 0)
            {
                // A building no longer on the cable steps as from none.
                current = 0;
            }
            int at = current > 0 ? index : (back ? count : -1);
            for (int tries = count; tries-- > 0;)
            {
                at += back ? -1 : 1;
                if (back && at < 0)
                {
                    return 0;
                }
                at = ((at % count) + count) % count;
                if (devices[at] != null)
                {
                    return devices[at].InstanceID;
                }
            }
            return 0;
        }

        private void OnStep(int socket, bool back)
        {
            if (target == null)
            {
                return;
            }
            target.SetSocket(socket, Step(target.SocketCandidates(), target.GetSocket(socket), back));
            Refresh();
        }

        private void OnLink(int socket)
        {
            if (target == null)
            {
                return;
            }
            List<Ic10LinkSlot> pins = Ic10Links.For(target.gameObject);
            if (socket < pins.Count)
            {
                Ic10LinkTool.Begin(target.gameObject, pins[socket], Refresh);
            }
        }

        private void OnClear(int socket)
        {
            if (target != null)
            {
                target.SetSocket(socket, 0);
                Refresh();
            }
        }

        private void OnEdit()
        {
            if (target != null)
            {
                Ic10Editor.Open(target);
            }
        }

        // ---------------------------------------------------------------------------------
        // The widgets, on the prefab.
        // ---------------------------------------------------------------------------------

        private void BuildUi()
        {
            RectTransform root = gameObject.GetComponent<RectTransform>();
            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }
            root.sizeDelta = new Vector2(320f, 0f);
            VerticalLayoutGroup column = gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(10, 10, 8, 8);
            column.spacing = 4f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            ContentSizeFitter fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI statusText = Ic10Ui.Text(gameObject.transform, "Status", 16f, true);
            statusText.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;

            for (int i = 0; i < Ic10Housing.SocketCount; i++)
            {
                GameObject row = new GameObject("Socket" + i, typeof(RectTransform));
                row.transform.SetParent(gameObject.transform, false);
                HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
                line.spacing = 3f;
                line.childControlWidth = true;
                line.childControlHeight = true;
                line.childForceExpandWidth = false;
                line.childForceExpandHeight = true;
                row.AddComponent<LayoutElement>().minHeight = RowHeight;

                TextMeshProUGUI label = Ic10Ui.Text(row.transform, "Label", 15f, false);
                LayoutElement grow = label.gameObject.AddComponent<LayoutElement>();
                grow.flexibleWidth = 1f;
                grow.minWidth = 80f;
                label.overflowMode = TextOverflowModes.Ellipsis;
                Ic10Ui.Button(row.transform, "Back", "<", SmallButton);
                Ic10Ui.Button(row.transform, "Next", ">", SmallButton);
                Ic10Ui.Button(row.transform, "Link", "Link", WideButton);
                Ic10Ui.Button(row.transform, "Clear", "Clear", WideButton);
            }

            GameObject edit = Ic10Ui.Button(gameObject.transform, "Edit", "Edit program", 0f);
            edit.GetComponent<LayoutElement>().minHeight = RowHeight + 4f;
        }

        // ---------------------------------------------------------------------------------
        // The listeners and references, on each clone.
        // ---------------------------------------------------------------------------------

        private void BindUi()
        {
            status = transform.Find("Status")?.GetComponent<TextMeshProUGUI>();
            for (int i = 0; i < Ic10Housing.SocketCount; i++)
            {
                Transform row = transform.Find("Socket" + i);
                if (row == null)
                {
                    continue;
                }
                int socket = i;
                socketText[i] = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
                Ic10Ui.Wire(row.Find("Back"), () => OnStep(socket, true),
                    "Previous building on the cable; back from the first is none (Stationeers: screwdriver with the modifier)");
                Ic10Ui.Wire(row.Find("Next"), () => OnStep(socket, false),
                    "Next building on the cable, in name order (Stationeers: screwdriver on the socket's button)");
                Ic10Ui.Wire(row.Find("Link"), () => OnLink(socket), "Click the building in the world");
                Ic10Ui.Wire(row.Find("Clear"), () => OnClear(socket), "Point d" + socket + " at nothing");
            }
            Ic10Ui.Wire(transform.Find("Edit"), OnEdit, "Write the program this housing runs");
            Refresh();
        }

        // ---------------------------------------------------------------------------------
        // Registration.
        // ---------------------------------------------------------------------------------

        private static Ic10HousingSideScreen prefab;

        public static bool Registered { get; private set; }

        [HarmonyPatch(typeof(DetailsScreen), "OnPrefabInit")]
        public static class Install
        {
            public static void Postfix(DetailsScreen __instance)
            {
                try
                {
                    if (prefab == null)
                    {
                        GameObject go = new GameObject("Ic10HousingSideScreen", typeof(RectTransform));
                        go.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(go);
                        prefab = go.AddComponent<Ic10HousingSideScreen>();
                        prefab.ContentContainer = go;
                        prefab.BuildUi();
                    }
                    List<DetailsScreen.SideScreenRef> screens =
                        AccessTools.Field(typeof(DetailsScreen), "sideScreens")?.GetValue(__instance) as List<DetailsScreen.SideScreenRef>;
                    if (screens == null)
                    {
                        Mod5Log.Warn("side screen: DetailsScreen.sideScreens is not a List<SideScreenRef>; the housing keeps its menu button only");
                        return;
                    }
                    if (screens.Exists(s => s != null && s.screenPrefab is Ic10HousingSideScreen))
                    {
                        return;
                    }
                    screens.Add(new DetailsScreen.SideScreenRef
                    {
                        name = "Ic10HousingSideScreen",
                        screenPrefab = prefab,
                        offset = Vector2.zero,
                        tab = DetailsScreen.SidescreenTabTypes.Config,
                    });
                    Registered = true;
                }
                catch (Exception e)
                {
                    Mod5Log.Warn("side screen could not register: " + e.Message + "; the housing keeps its menu button only");
                }
            }
        }
    }
}
