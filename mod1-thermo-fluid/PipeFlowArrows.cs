using System.Collections.Generic;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// ARROWS ON THE PIPES: an arrow for the flow direction on each pipe and vent tile, because
    /// some buildings now connect to pipes that can flow both ways. In the Gas Ventilation and the
    /// Plumbing overlay every pipe tile that moved anything on the last conduit tick carries an
    /// arrow the way it went, in the colour of what is moving (the element's own pipe colour,
    /// the one vanilla's pipe blobs use), thicker the more it carries; an arrow fades over three
    /// ticks once its tile stops. A vent or a pump that is exchanging with the world gets a burst
    /// of four diagonal arrows: pointing OUT while it blows the pipe's contents into the room,
    /// pointing IN while it draws the room into the pipe -- so the Active Vent's two modes, and
    /// an airlock's two halves, read at a glance.
    ///
    /// The directions are the framework's (<see cref="PipeFlowField"/>): on a vanilla
    /// run what the mover actually did, on a run Mod 1 levels as one pressure the flow solved
    /// from its ports. This class only draws.
    ///
    /// Drawn as one world-space mesh with <c>Graphics.DrawMesh</c> on the <c>MaskedOverlay</c>
    /// layer, in front of the conduit layer -- the layer and depth vanilla's own
    /// <c>ConduitFlowVisualizer</c> draws its blobs at in an overlay -- so the arrows sit on the
    /// pipes, under the HUD, and are clipped to the screen by the camera rather than by code.
    /// Rebuilt four times a second (the arrows change once a second) and when the view moves.
    /// </summary>
    public sealed class PipeFlowArrows : MonoBehaviour
    {
        /// <summary>Off switch for a player or a rig that wants the bare overlay.</summary>
        public static bool Enabled = true;

        // Above this many visible cells (zoomed far out) the arrows are smaller than a pixel.
        private const int MaxVisibleCells = 90000;
        private const float RebuildSeconds = 0.25f;

        private static readonly Color Outline = new Color(0f, 0f, 0f, 0.85f);

        private Mesh mesh;
        private Material material;
        private int overlayLayer;
        private float nextBuild;
        private RectInt lastView;
        private HashedString lastMode;

        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color> colours = new List<Color>();
        private readonly List<int> tris = new List<int>();

        private void Awake()
        {
            mesh = new Mesh { name = "Mod1PipeFlowArrows" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.MarkDynamic();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null)
            {
                material = new Material(shader) { renderQueue = 4000 };
            }
            overlayLayer = LayerMask.NameToLayer("MaskedOverlay");
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
            if (material != null)
            {
                Destroy(material);
            }
        }

        private void LateUpdate()
        {
            if (!Enabled || material == null || SimDebugView.Instance == null || Camera.main == null
                || Game.Instance == null)
            {
                return;
            }
            HashedString mode = SimDebugView.Instance.GetMode();
            PipeContentType type;
            Grid.SceneLayer sceneLayer;
            ConduitFlow flow;
            if (mode == OverlayModes.GasConduits.ID)
            {
                type = PipeContentType.Gas;
                sceneLayer = Grid.SceneLayer.GasConduits;
                flow = Game.Instance.gasConduitFlow;
            }
            else if (mode == OverlayModes.LiquidConduits.ID)
            {
                type = PipeContentType.Liquid;
                sceneLayer = Grid.SceneLayer.LiquidConduits;
                flow = Game.Instance.liquidConduitFlow;
            }
            else
            {
                return;
            }
            if (flow == null)
            {
                return;
            }

            RectInt view = VisibleCells();
            if (view.width * view.height > MaxVisibleCells)
            {
                return;
            }
            if (Time.unscaledTime >= nextBuild || !view.Equals(lastView) || mode != lastMode)
            {
                nextBuild = Time.unscaledTime + RebuildSeconds;
                lastView = view;
                lastMode = mode;
                Build(view, type, flow);
            }
            if (verts.Count == 0)
            {
                return;
            }
            float z = Grid.GetLayerZ(sceneLayer) - 0.6f;
            Graphics.DrawMesh(mesh, new Vector3(0f, 0f, z), Quaternion.identity, material, overlayLayer);
        }

        private static RectInt VisibleCells()
        {
            Camera main = Camera.main;
            float h = main.orthographicSize;
            float w = h * main.aspect;
            Vector3 c = main.transform.position;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - w) / Grid.CellSizeInMeters) - 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt((c.y - h) / Grid.CellSizeInMeters) - 1);
            int x1 = Mathf.Min(Grid.WidthInCells - 1, Mathf.CeilToInt((c.x + w) / Grid.CellSizeInMeters) + 1);
            int y1 = Mathf.Min(Grid.HeightInCells - 1, Mathf.CeilToInt((c.y + h) / Grid.CellSizeInMeters) + 1);
            return new RectInt(x0, y0, Mathf.Max(0, x1 - x0 + 1), Mathf.Max(0, y1 - y0 + 1));
        }

        private void Build(RectInt view, PipeContentType type, ConduitFlow flow)
        {
            verts.Clear();
            colours.Clear();
            tris.Clear();
            for (int y = view.yMin; y < view.yMax; y++)
            {
                for (int x = view.xMin; x < view.xMax; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!flow.HasConduit(cell))
                    {
                        continue;
                    }
                    Vector2 centre = new Vector2((x + 0.5f) * Grid.CellSizeInMeters, (y + 0.5f) * Grid.CellSizeInMeters);
                    PipeCellFlow cellFlow;
                    if (PipeFlowField.TryGetCellFlow(cell, type, out cellFlow))
                    {
                        Color colour = ColourOf(cellFlow.Element, cellFlow.StaleTicks);
                        PipeArrow(centre, Vector2.down, cellFlow.Down, colour);
                        PipeArrow(centre, Vector2.left, cellFlow.Left, colour);
                        PipeArrow(centre, Vector2.right, cellFlow.Right, colour);
                        PipeArrow(centre, Vector2.up, cellFlow.Up, colour);
                    }
                    float port;
                    SimHashes element;
                    int stale;
                    if (PipeFlowField.TryGetPortTraffic(cell, type, out port, out element, out stale)
                        && Mathf.Abs(port) >= PipeFlowField.MinKgPerSecond && ExchangesWithWorld(cell))
                    {
                        // Put into the pipe = the building drew it from the room: arrows in.
                        Burst(centre, port > 0f, Mathf.Abs(port), ColourOf(element, stale));
                    }
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        // A building that moves its port's contents to or from the world around it.
        private static bool ExchangesWithWorld(int cell)
        {
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.Building];
            if (go == null)
            {
                return false;
            }
            return go.GetComponent<Vent>() != null || go.GetComponent<Pump>() != null
                || go.GetComponent<AirVentComponent>() != null;
        }

        private static Color ColourOf(SimHashes id, int staleTicks)
        {
            Element element = ElementLoader.FindElementByHash(id);
            Color colour = element != null && element.substance != null
                ? (Color)element.substance.conduitColour
                : Color.white;
            colour.a = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(staleTicks / (float)Mathf.Max(1, PipeFlowField.FadeTicks)));
            return colour;
        }

        // 1 g/s thin, 1 kg/s and up thick.
        private static float Thickness(float kgPerSecond)
        {
            float t = Mathf.Clamp01(Mathf.Log10(Mathf.Max(kgPerSecond, 1e-3f) / 1e-3f) / 3f);
            return Mathf.Lerp(0.035f, 0.085f, t);
        }

        private void PipeArrow(Vector2 centre, Vector2 dir, float kgPerSecond, Color colour)
        {
            if (kgPerSecond < PipeFlowField.MinKgPerSecond)
            {
                return;
            }
            Vector2 tail = centre - dir * 0.26f;
            Vector2 tip = centre + dir * 0.40f;
            Arrow(tail, tip, Thickness(kgPerSecond), 0.17f, 0.20f, colour);
        }

        private static readonly Vector2[] Diagonals =
        {
            new Vector2(0.7071f, 0.7071f), new Vector2(-0.7071f, 0.7071f),
            new Vector2(-0.7071f, -0.7071f), new Vector2(0.7071f, -0.7071f),
        };

        private void Burst(Vector2 centre, bool inward, float kgPerSecond, Color colour)
        {
            float width = Thickness(kgPerSecond) * 0.8f;
            foreach (Vector2 d in Diagonals)
            {
                Vector2 near = centre + d * 0.30f;
                Vector2 far = centre + d * 0.66f;
                if (inward)
                {
                    Arrow(far, near, width, 0.12f, 0.15f, colour);
                }
                else
                {
                    Arrow(near, far, width, 0.12f, 0.15f, colour);
                }
            }
        }

        // An outlined arrow: shaft half-width `half`, head `headHalf` wide and `headLength` long.
        private void Arrow(Vector2 tail, Vector2 tip, float half, float headHalf, float headLength, Color colour)
        {
            Vector2 dir = (tip - tail).normalized;
            const float pad = 0.03f;
            Color outline = Outline;
            outline.a *= colour.a;
            Shape(tail - dir * pad, tip + dir * pad * 1.6f, dir, half + pad, headHalf + pad * 1.6f, headLength + pad, outline);
            Shape(tail, tip, dir, half, headHalf, headLength, colour);
        }

        private void Shape(Vector2 tail, Vector2 tip, Vector2 dir, float half, float headHalf, float headLength, Color colour)
        {
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 neck = tip - dir * headLength;
            int b = verts.Count;
            verts.Add(tail + side * half);
            verts.Add(tail - side * half);
            verts.Add(neck - side * half);
            verts.Add(neck + side * half);
            verts.Add(neck + side * headHalf);
            verts.Add(neck - side * headHalf);
            verts.Add(tip);
            for (int i = 0; i < 7; i++)
            {
                colours.Add(colour);
            }
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            tris.Add(b + 4); tris.Add(b + 5); tris.Add(b + 6);
        }
    }
}
