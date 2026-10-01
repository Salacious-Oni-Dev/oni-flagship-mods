using System.Collections.Generic;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Turns the F9 console-log readout (see Patches.cs) into an actual on-screen label -- the
    /// first thing in Mod 1 a player would see without opening the log. Still a tech-demo
    /// overlay, not a real gameplay mechanic: nothing in the game generates gas-mixture state
    /// yet (no extractor, no pipe, no vent), so the only way a cell ends up gas-mixture-active
    /// today is the F9 debug injection this same mod provides.
    ///
    /// Spawned once from a Harmony postfix on Game.OnSpawn (Patches.cs), not tied to any
    /// vanilla UI object -- kept standalone even after HoverPatch.cs started patching Klei's
    /// own tooltip (HoverTextDrawer.EndDrawing), since this overlay works in any screen state
    /// the vanilla tooltip doesn't (e.g. it never gets suppressed by ShowHoverUI()).
    ///
    /// Shows the FULL composition (GasMixtureFacade.TryGetComposition), not just the dominant
    /// species -- the only way to actually see two species sharing one cell, which is the
    /// entire point of the volume-fractions layer this reads from. A cell with only one
    /// species still just shows one line.
    /// </summary>
    public class GasMixtureOverlay : MonoBehaviour
    {
        private int cell;
        private GasMixtureFacade.GasComponent[] composition = System.Array.Empty<GasMixtureFacade.GasComponent>();
        private float pressurePa;
        private bool hasPressure;

        // Compressor/tank mechanic (Compressor.cs): the Reservoir's storage is its own isolated
        // managed state, not per-cell gas-mixture data --
        // shown separately here rather than forced into the GasComponent[] shape above.
        private KeyValuePair<int, float>[] tankComposition = System.Array.Empty<KeyValuePair<int, float>>();
        private float tankPressurePa;
        private bool tankHasPressure;
        private bool isTank;

        // Real gas-pipe pressure (Compressor.cs's pipe-network mechanic) -- vanilla
        // conduits have no pressure concept at all, so GasMixtureFacade.TryGetConduitPressure is
        // the only place this number exists. Independent of isTank/composition above: a conduit
        // cell is a different object layer than a room cell, so this can be true alongside
        // either of them.
        private float conduitPressurePa;
        private bool hasConduitPressure;

        private void Update()
        {
            Camera main = Camera.main;
            if (main == null)
            {
                composition = System.Array.Empty<GasMixtureFacade.GasComponent>();
                tankComposition = System.Array.Empty<KeyValuePair<int, float>>();
                isTank = false;
                hasConduitPressure = false;
                return;
            }

            Vector3 mouseWorld = main.ScreenToWorldPoint(KInputManager.GetMousePos());
            int hoveredCell = Grid.PosToCell(mouseWorld);
            if (!Grid.IsValidCell(hoveredCell))
            {
                composition = System.Array.Empty<GasMixtureFacade.GasComponent>();
                tankComposition = System.Array.Empty<KeyValuePair<int, float>>();
                isTank = false;
                hasConduitPressure = false;
                return;
            }

            cell = hoveredCell;
            hasConduitPressure = GasMixtureFacade.TryGetConduitPressure(hoveredCell, out conduitPressurePa);

            GasMixtureTankComponent tank = GasMixtureTankComponent.AtCell(hoveredCell);
            isTank = tank != null;
            if (isTank)
            {
                tankComposition = tank.Composition();
                tankHasPressure = tank.TryGetPressurePa(out tankPressurePa);
                composition = System.Array.Empty<GasMixtureFacade.GasComponent>();
                return;
            }

            composition = GasMixtureFacade.TryGetComposition(hoveredCell);
            hasPressure = GasMixtureFacade.TryGetPressure(hoveredCell, out pressurePa);
        }

        // Fixed corner, not the mouse: HoverPatch.cs now puts this same data into Klei's own
        // tooltip, which DOES follow the cursor, so anchoring here too meant two boxes fighting
        // over the same few pixels. Pinning this one to a corner keeps it visible without ever
        // overlapping the vanilla tooltip, wherever that ends up drawing.
        private const float PanelX = 16f;

        // Not a constant any more: DemoNarrator.cs draws a full-width title banner across the top
        // of the screen during a harness run, and this panel's old fixed 16 px would sit under it.
        // Pushed clear of the banner only while narration is actually up, so an ordinary session
        // still gets the panel in the corner where it has always been.
        private static float PanelY
        {
            get { return DemoNarrator.IsActive ? 210f : 16f; }
        }
        private const float PanelWidth = 260f;
        private const float LineHeight = 18f;

        private static GUIStyle boxStyle;
        private static GUIStyle titleStyle;
        private static GUIStyle bodyStyle;

        private void EnsureStyles()
        {
            if (boxStyle != null)
            {
                return;
            }

            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            bg.Apply();

            boxStyle = new GUIStyle(GUI.skin.box) { normal = { background = bg } };
            titleStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
        }

        private void OnGUI()
        {
            float nextY = PanelY;

            if (isTank && tankComposition.Length > 0)
            {
                EnsureStyles();

                float tankHeight = LineHeight * (tankComposition.Length + 1) + 8f;
                GUI.Box(new Rect(PanelX, nextY, PanelWidth, tankHeight), GUIContent.none, boxStyle);

                string tankPressureSuffix = tankHasPressure ? $", {tankPressurePa:F0} Pa" : string.Empty;
                GUI.Label(new Rect(PanelX + 8f, nextY + 4f, PanelWidth - 16f, LineHeight),
                    $"[tank] Gas Reservoir ({tankComposition.Length} species{tankPressureSuffix})", titleStyle);
                for (int i = 0; i < tankComposition.Length; i++)
                {
                    KeyValuePair<int, float> c = tankComposition[i];
                    Element element = ElementLoader.elements[c.Key];
                    GUI.Label(new Rect(PanelX + 8f, nextY + 4f + LineHeight * (i + 1), PanelWidth - 16f, LineHeight),
                        $"{element.tag} {c.Value:F3} kg", bodyStyle);
                }
                nextY += tankHeight + 4f;
            }
            else if (!isTank && composition.Length > 0)
            {
                EnsureStyles();

                float height = LineHeight * (composition.Length + 1) + 8f;
                GUI.Box(new Rect(PanelX, nextY, PanelWidth, height), GUIContent.none, boxStyle);

                string pressureSuffix = hasPressure ? $", {pressurePa:F0} Pa" : string.Empty;
                GUI.Label(new Rect(PanelX + 8f, nextY + 4f, PanelWidth - 16f, LineHeight),
                    $"[cell {cell}] volume-fractions ({composition.Length} species{pressureSuffix})", titleStyle);
                for (int i = 0; i < composition.Length; i++)
                {
                    GasMixtureFacade.GasComponent c = composition[i];
                    Element element = ElementLoader.elements[c.ElementIdx];
                    GUI.Label(new Rect(PanelX + 8f, nextY + 4f + LineHeight * (i + 1), PanelWidth - 16f, LineHeight),
                        $"{element.tag} {c.MassKg:F3} kg", bodyStyle);
                }
                nextY += height + 4f;
            }

            // Real gas-pipe pressure (Compressor.cs's pipe-network mechanic) --
            // stacked below whichever panel (if any) drew above, since a conduit cell is a
            // different object layer than a room cell and can coexist with either.
            if (hasConduitPressure)
            {
                EnsureStyles();

                float conduitHeight = LineHeight + 8f;
                GUI.Box(new Rect(PanelX, nextY, PanelWidth, conduitHeight), GUIContent.none, boxStyle);
                GUI.Label(new Rect(PanelX + 8f, nextY + 4f, PanelWidth - 16f, LineHeight),
                    $"[pipe] Pressure: {conduitPressurePa:F0} Pa", titleStyle);
            }
        }
    }
}
