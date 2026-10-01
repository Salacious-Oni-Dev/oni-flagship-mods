using System;
using HarmonyLib;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    // The overlay texture hook for promoted rooms, consuming GasMixtureFacade.TryGetBatch (native
    // SIM_GasMassBatchQuery). Design constraints:
    //
    //   * TextureRegion is a STRUCT, but the pixel data it writes through -- NativeArray<byte>
    //     bytes -- is a handle into unmanaged memory shared by every copy of the struct, so
    //     region.SetBytes(...) on a by-value copy mutates the same buffer. No ref parameter and
    //     no transpiler is needed for the byte-write itself.
    //
    //   * The real constraint is the SimDLL's WaitIdle(), which is game-thread only.
    //     PropertyTextures's per-cell callbacks run INSIDE GlobalJobManager.Run, across Klei's
    //     worker pool (the calling thread also processes work items). Calling into the native
    //     synchronization from several of those at once is a hang risk: it has exactly one
    //     writer and one waiter in mind, and the done event is auto-reset, so a second concurrent
    //     waiter can block forever past a SetEvent another thread already consumed. Nothing
    //     below may relax this.
    //
    //   * So the native query runs on the game thread, in a PREFIX on the dispatcher
    //     (UpdateTextureThreaded), once per frame for all overridden properties, and the per-cell
    //     reads and writes run in Klei's strips, where they touch nothing but vanilla's own grid
    //     arrays and the locked texture buffer. OverlayBatch carries the argument. A strip is
    //     handed a SUB-RANGE of rows and indexes a batch covering the whole region; that mapping
    //     has been checked cell by cell against Grid.XYToCell.
    //
    //   * On a world with no promoted rooms this override paints NOTHING, and still costs a full
    //     native batch query plus a scan of every visible cell, every frame:
    //     SIM_GasMassBatchQuery returns its input count, not the number of owned cells.
    //
    //   * UpdateTextureThreaded is called for every texture property PropertyTextures manages,
    //     and its update_texture_cb parameter is PropertyTextures.WorkItem.Callback -- a PRIVATE
    //     nested delegate type this assembly cannot name. It is declared as object: Harmony binds
    //     patch parameters by name, and the prefix checks the delegate's Method.Name, so every
    //     other overlay texture is untouched.
    //
    //   * The visible colour comes from PropertyTextures.UpdateGasColour, which writes a gas
    //     cell's species tint (element.substance.colour) at a flat alpha of 255 from vanilla's
    //     Grid.Element, with no mass check. A promoted cell whose vanilla mass has drained would
    //     paint nothing, whatever the mixture holds, so ApplyGasColourOverrides substitutes the
    //     dominant mixture species' own colour with vanilla's binary alpha (255 present, 0
    //     absent). BoostColour pushes saturation and value up in HSV, keeping each species' hue,
    //     so a promoted room looks obviously different rather than measurably different.
    /// <summary>
    /// The game-thread half of the overlay override: one native batch query per frame, shared by
    /// every property that needs it.
    ///
    /// WHY THE WORK IS SPLIT IN TWO. `UpdateTextureThreaded` dispatches sixteen-row strips to
    /// `GlobalJobManager.Run` and does not return until every worker has finished, so doing all
    /// the per-cell work in a postfix would run it on the game thread, serially, after all of
    /// Klei's threaded writes -- about 0.13 ms a frame of pure critical path on a late-game save,
    /// 9 % of `PropertyTextures.LateUpdate`.
    ///
    /// It could not simply be moved. The per-cell callbacks run on Klei's worker pool, and
    /// `GasMixtureFacade.TryGetBatch` is a `WaitIdle()` rendezvous with the sim thread that
    /// `sim/simdll.cpp` documents as game-thread-only: `g_worker.busy`/`g_worker.done` have
    /// exactly one writer and one waiter in mind, and the done event is auto-reset, so a second
    /// concurrent waiter can block forever past a `SetEvent` another thread already consumed.
    /// That hazard is why the query stays on the dispatcher.
    ///
    /// So the query stays here, on the game thread, in a PREFIX that runs before the strips are
    /// dispatched -- and only the per-cell reads and writes, which touch nothing but vanilla's own
    /// grid arrays and the locked texture buffer, move into the strips. Klei's own callbacks
    /// already read exactly those fields from exactly those threads.
    ///
    /// The split pays a second time. Up to three properties match per frame and each would run its
    /// own full query over the same rectangle: two mass passes every frame plus the gas colour when
    /// the round robin reached it. They share one, so the game thread does one query instead of
    /// up to three, and runs none of the per-cell loops.
    /// </summary>
    internal sealed class OverlayBatch
    {
        /// <summary>
        /// The batch the strips read, or null when this dispatch has nothing to override.
        /// Published by the prefix on the game thread and read by the workers, with no lock and no
        /// barrier of its own: `JobManager.Run` releases a semaphore per worker thread after this
        /// field is written and does not return until every one of them has finished, which fences
        /// both directions. The field is `volatile` anyway, because the cost is nothing and the
        /// alternative is a comment asking a future reader to re-derive that argument.
        ///
        /// A worker never checks which frame this is for, and MUST NOT: `Time.frameCount` is a
        /// main-thread-only Unity API, and a development build throws on it rather than quietly
        /// answering. It does not
        /// need to check. Our prefix runs immediately before every dispatch whose callback we
        /// postfix, in the same frame, on the same thread -- so what a strip reads here was
        /// written for the dispatch it is part of, or is null.
        /// </summary>
        private static volatile OverlayBatch current;

        /// <summary>
        /// The arrays, kept across frames even when a frame publishes nothing.
        ///
        /// Separate from <see cref="current"/> precisely so that a frame with no owned cells can
        /// publish null without throwing the buffers away -- allocating them again next frame
        /// would put back exactly the garbage this class exists to avoid. Only the game thread
        /// ever touches this field.
        ///
        /// Reuse is safe for a structural reason: no worker can be running when the next prefix
        /// refills these, because the dispatch that started them blocks until they are done. It
        /// would stop being safe the moment any of this is handed to a job that outlives its
        /// dispatcher.
        /// </summary>
        private static OverlayBatch pool;

        internal int[] Cells;
        internal bool[] Owned;
        internal ushort[] Dominant;
        internal float[] Mass;

        /// <summary>The rectangle the arrays cover, which is the WHOLE visible region -- not the
        /// strip a worker is given. A strip indexes into it with <see cref="IndexOf"/>.</summary>
        internal int X0;
        internal int Y0;
        internal int Width;

        /// <summary>The frame this was filled for. Used only on the game thread, to share one
        /// query between the two or three properties that run in the same frame.</summary>
        internal int Frame;

        /// <summary>
        /// What <see cref="Cells"/> currently holds, so it is refilled only when it is actually
        /// stale.
        ///
        /// The cell index of a coordinate is `y * Grid.WidthInCells + x` and nothing else, so the
        /// contents are a function of the rectangle and the grid width alone -- not of the frame,
        /// and not of anything the simulation does. The rectangle is the visible region, which
        /// changes only when the camera moves or the window resizes, so on a still camera this
        /// loop ran ten thousand multiply-adds every frame to write the same ten thousand numbers
        /// back over themselves.
        /// </summary>
        private int cellsX0 = int.MinValue;
        private int cellsY0;
        private int cellsWidth;
        private int cellsHeight;
        private int cellsGridWidth;

        /// <summary>
        /// Ensure a batch exists for this frame and this rectangle, querying the sim if not.
        ///
        /// MUST be called from the game thread only. Returns the batch, or null when there is
        /// nothing to override -- an unavailable mixture layer, an empty rectangle, or a query
        /// that reported no owned cells -- in which case every strip leaves whatever Klei's own
        /// pass already wrote.
        /// </summary>
        internal static OverlayBatch EnsureForFrame(int x0, int y0, int x1, int y1)
        {
            int width = x1 - x0 + 1;
            int height = y1 - y0 + 1;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // Legal here and only here: this method is game-thread only.
            int frame = Time.frameCount;
            OverlayBatch b = pool;
            if (b != null && b.Frame == frame && b.X0 == x0 && b.Y0 == y0 && b.Width == width
                && b.Cells != null && b.Cells.Length >= width * height)
            {
                // Already queried this frame for this rectangle by an earlier property. This is
                // the shared-query case and it is the common one: two mass passes run every frame
                // over the same region. `current` already says whether that query found anything.
                return current;
            }

            int count = width * height;
            b = b ?? new OverlayBatch();
            pool = b;

            // REUSED BUFFERS, NOT FRESH ARRAYS. This runs once per frame over the whole visible
            // region -- roughly ten thousand cells on a 1902x991 window -- and four `new` arrays
            // here were once the single largest source of garbage in the game, about 110 KB per
            // call and up to three calls a frame. The cost was never paid here: it was paid in a
            // stop-the-world Mono collection on a 2.6 GB heap, which is a 400-750 ms freeze.
            //
            // It was invisible to every profile sorted by TIME, because the work is a memset and
            // a loop, and it was attributed to `PropertyTextures.LateUpdate` -- Klei's own method
            // -- because that is where a Harmony postfix on a vanilla method appears. It was
            // written off as vanilla twice before an allocation-sorted profile put it at the top.
            //
            // Never shrunk: the region tracks the window, so it is stable in practice, and a
            // shrink only trades a bounded steady-state footprint back for the churn this exists
            // to remove.
            if (b.Cells == null || b.Cells.Length < count)
            {
                b.Cells = new int[count];
                b.Owned = new bool[count];
                b.Dominant = new ushort[count];
                b.Mass = new float[count];
                // The new array holds nothing, whatever the rectangle fields still say.
                b.cellsX0 = int.MinValue;
            }
            else
            {
                // Only the flags have to be cleared: TryGetBatch writes `dominant` and `mass`
                // exactly where it sets `owned`, and every reader is gated on `owned`.
                Array.Clear(b.Owned, 0, count);
            }

            int[] cells = b.Cells;
            int gridWidth = Grid.WidthInCells;
            if (b.cellsX0 != x0 || b.cellsY0 != y0 || b.cellsWidth != width
                || b.cellsHeight != height || b.cellsGridWidth != gridWidth)
            {
                int idx = 0;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        cells[idx++] = Grid.XYToCell(x, y);
                    }
                }

                b.cellsX0 = x0;
                b.cellsY0 = y0;
                b.cellsWidth = width;
                b.cellsHeight = height;
                b.cellsGridWidth = gridWidth;
            }

            int written = GasMixtureFacade.TryGetBatch(cells, b.Owned, b.Dominant, b.Mass, count);

            b.X0 = x0;
            b.Y0 = y0;
            b.Width = width;
            b.Frame = frame;

            // Published only when the query actually found owned cells. A frame with none leaves
            // every strip reading null, which is the same "fall back to vanilla for this cell"
            // collapse every other facade consumer already does.
            current = written > 0 ? b : null;

            return current;
        }

        /// <summary>
        /// The batch for the dispatch now running, or null. Safe from a worker thread, and it
        /// deliberately touches no Unity API -- see <see cref="current"/>.
        /// </summary>
        internal static OverlayBatch ForThisDispatch()
        {
            return current;
        }

        /// <summary>Index of a cell's entry, given absolute grid coordinates inside the region.</summary>
        internal int IndexOf(int x, int y)
        {
            return (y - Y0) * Width + (x - X0);
        }
    }

    /// <summary>
    /// The overlay colouring for promoted rooms -- the game-thread prefix that
    /// arms the batch the per-property strips below consume.
    ///
    /// `UpdateTextureThreaded` is called for every texture property this component manages
    /// (StateChange, GasPressure, Temperature, ...), not just the ones we override, and its
    /// `update_texture_cb` parameter is `PropertyTextures.WorkItem.Callback` -- a PRIVATE nested
    /// delegate type this mod's assembly cannot name at compile time. Declared as `object` here
    /// instead: Harmony binds patch parameters by name, not by exact type, and a delegate is a
    /// reference type so no boxing or cast is needed. The callback's own `Method.Name` decides
    /// whether the batch is needed at all, so every other overlay texture pays one string
    /// comparison and nothing else.
    /// </summary>
    [HarmonyPatch(typeof(PropertyTextures), "UpdateTextureThreaded")]
    internal static class PropertyTextures_UpdateTextureThreaded
    {
        private static void Prefix(int x0, int y0, int x1, int y1, object update_texture_cb)
        {
            try
            {
                Delegate cb = update_texture_cb as Delegate;
                if (cb?.Method == null)
                {
                    return;
                }

                string name = cb.Method.Name;
                if (name != "UpdateSolidLiquidGasMass"
                    && name != "UpdateSolidLiquidGasMassForLight"
                    && name != "UpdateGasColour")
                {
                    return;
                }

                OverlayBatch.EnsureForFrame(x0, y0, x1, y1);

            }
            catch
            {
                // Never break the render pipeline over this -- with no batch published, every
                // strip below leaves exactly what Klei's own pass wrote.
            }
        }
    }

    /// <summary>
    /// `Property.SolidLiquidGasMass`, per strip, on a worker thread.
    ///
    /// The callback the game dispatches is the five-argument wrapper, which forwards to the
    /// six-argument implementation with `diferenciateImpermeable: true`. Patching the wrapper
    /// rather than the implementation keeps the two mass properties distinguishable, which is
    /// exactly what the old dispatcher postfix had to recover from the delegate's name.
    /// </summary>
    [HarmonyPatch(typeof(PropertyTextures), "UpdateSolidLiquidGasMass",
        new[] { typeof(TextureRegion), typeof(int), typeof(int), typeof(int), typeof(int) })]
    internal static class PropertyTextures_UpdateSolidLiquidGasMass
    {
        private static void Postfix(TextureRegion region, int x0, int y0, int x1, int y1)
        {
            OverlayStrips.ApplyGasMixtureOverrides(region, x0, y0, x1, y1, diferenciateImpermeable: true);
        }
    }

    /// <summary>
    /// `Property.SolidLiquidGasMassForLight`, per strip, on a worker thread. Same implementation
    /// as above with `diferenciateImpermeable: false`, which is the only difference between the
    /// two properties on Klei's side as well.
    /// </summary>
    [HarmonyPatch(typeof(PropertyTextures), "UpdateSolidLiquidGasMassForLight",
        new[] { typeof(TextureRegion), typeof(int), typeof(int), typeof(int), typeof(int) })]
    internal static class PropertyTextures_UpdateSolidLiquidGasMassForLight
    {
        private static void Postfix(TextureRegion region, int x0, int y0, int x1, int y1)
        {
            OverlayStrips.ApplyGasMixtureOverrides(region, x0, y0, x1, y1, diferenciateImpermeable: false);
        }
    }

    /// <summary>
    /// `Property.GasColour`, per strip, on a worker thread. This is the property that actually
    /// paints a gas cell's visible tint; the mass property's alpha channel has no confirmed
    /// visible consumer at all.
    /// </summary>
    [HarmonyPatch(typeof(PropertyTextures), "UpdateGasColour",
        new[] { typeof(TextureRegion), typeof(int), typeof(int), typeof(int), typeof(int) })]
    internal static class PropertyTextures_UpdateGasColour
    {
        private static void Postfix(TextureRegion region, int x0, int y0, int x1, int y1)
        {
            OverlayStrips.ApplyGasColourOverrides(region, x0, y0, x1, y1);
        }
    }

    /// <summary>
    /// The per-cell overlay overrides, as they run inside Klei's own worker strips.
    ///
    /// WHAT MAKES THESE SAFE OFF THE GAME THREAD. They read three things: the batch, which was
    /// published before the strips were dispatched and is not written again until after they all
    /// finish; vanilla's `Grid.Element`, `Grid.LiquidImpermeable` and `Grid.IsActiveWorld`, which
    /// Klei's own per-cell callbacks read from these same threads on the same cells; and
    /// `ElementLoader.elements`, which is immutable after load. They write only through
    /// `TextureRegion.SetBytes`, and strips own disjoint row ranges, so two workers never touch
    /// the same pixel.
    ///
    /// `TextureRegion` is a STRUCT, but the pixel data it writes through -- `NativeArray&lt;byte&gt;
    /// bytes` -- is a handle into unmanaged memory shared by every copy of the struct. Harmony
    /// passes struct parameters by value to a postfix, but `SetBytes` on that copy still mutates
    /// the same underlying buffer the original method just wrote into. And the buffer is still
    /// live: `UpdateProperty` calls `texture_region.Unlock()` only after the whole
    /// `UpdateTextureThreaded` call returns, which is after every strip has run.
    ///
    /// Nothing here calls into the native library. That is the constraint the split exists to
    /// honour, and it is the one line of this file that must not be relaxed without re-reading
    /// `WaitIdle()`.
    /// </summary>
    internal static class OverlayStrips
    {
        /// <summary>
        /// Re-derives the SAME per-cell channel selection `PropertyTextures.UpdateSolidLiquidGasMass`
        /// uses,
        /// but only far enough to know a promoted cell structurally MUST be gas/vacuum right
        /// now (a room only ever spans open, non-solid cells -- `gas_rooms.h`'s `IsOpenCell`) -- the solid/impermeable/liquid checks below
        /// read vanilla's own STRUCTURAL fields (`Element.IsSolid`, `Grid.LiquidImpermeable`,
        /// `Element.IsLiquid`), which promotion never touches (only gas-phase mass/temperature
        /// transport is redirected), so trusting them here is safe even for a promoted cell --
        /// and doubles as the item-7 safety net: a promoted cell that solidified/liquefied
        /// without a room-graph rebuild would still report owned=true from the native side,
        /// but vanilla's own live element field catches it here and this method leaves it
        /// alone rather than force it back to a gas-colored pixel.
        ///
        /// Only the GAS branch's mass-derived alpha is replaced with the real mixture total,
        /// and only for cells `SIM_GasMassBatchQuery` reported as actually promoted -- every
        /// other cell in the strip is left exactly as the original pass already wrote it.
        /// </summary>
        internal static void ApplyGasMixtureOverrides(TextureRegion region, int x0, int y0, int x1, int y1,
            bool diferenciateImpermeable)
        {
            try
            {
                OverlayBatch batch = OverlayBatch.ForThisDispatch();
                if (batch == null)
                {
                    return;
                }

                bool[] owned = batch.Owned;
                float[] mass = batch.Mass;
                int[] cells = batch.Cells;

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = batch.IndexOf(x, y);
                        if (i < 0 || i >= owned.Length || !owned[i])
                        {
                            continue;
                        }

                        int cell = cells[i];

                        if (!Grid.IsActiveWorld(cell))
                        {
                            continue;
                        }

                        Element vanillaElement = Grid.Element[cell];
                        bool solidOrImpermeable = vanillaElement.IsSolid ||
                            (diferenciateImpermeable && Grid.LiquidImpermeable[cell]);
                        if (solidOrImpermeable || vanillaElement.IsLiquid)
                        {
                            // Structurally not a gas cell right now -- leave whatever the original
                            // pass wrote for the R/G channels alone.
                            continue;
                        }

                        float massKg = mass[i];
                        float alpha = Mathf.Min(1f, massKg / 2000f);
                        if (massKg > 0f)
                        {
                            alpha = Mathf.Max(0.003921569f, alpha);
                        }
                        region.SetBytes(x, y, 0, 0, byte.MaxValue, (byte)(alpha * 255f));
                    }
                }
            }
            catch
            {
                // Never break the render pipeline over this, and never throw out of a job worker:
                // degrade to whatever the original pass already wrote for every cell in the strip.
            }
        }

        /// <summary>
        /// `PropertyTextures.UpdateGasColour`: for a gas cell, unconditionally
        /// writes `element.substance.colour` at full alpha (255) -- no mass check at all, so a
        /// promoted cell that has correctly had its vanilla mass drained (items 3/4/5's whole
        /// point) shows nothing here no matter how much real gas-mixture mass it actually
        /// holds. Same structural safety guard as `ApplyGasMixtureOverrides` (vanilla's own
        /// live `Element.IsSolid`/`IsLiquid` decide eligibility, promotion never touches those)
        /// -- and the same item-7 safety net for the same reason.
        ///
        /// Deliberately mirrors vanilla's own binary alpha convention (255 or 0, no gradient)
        /// rather than reusing `ApplyGasMixtureOverrides`'s `mass/2000` fade -- that formula
        /// was written for `SolidLiquidGasMass`'s own alpha channel, which this investigation
        /// found has no confirmed visible consumer at all; there is no evidence vanilla's own
        /// `UpdateGasColour` scales its visible opacity by mass, so this does not invent a
        /// scaling vanilla itself does not have.
        /// </summary>
        internal static void ApplyGasColourOverrides(TextureRegion region, int x0, int y0, int x1, int y1)
        {
            try
            {
                OverlayBatch batch = OverlayBatch.ForThisDispatch();
                if (batch == null)
                {
                    return;
                }

                bool[] owned = batch.Owned;
                float[] mass = batch.Mass;
                ushort[] dominant = batch.Dominant;
                int[] cells = batch.Cells;

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = batch.IndexOf(x, y);
                        if (i < 0 || i >= owned.Length || !owned[i])
                        {
                            continue;
                        }

                        int cell = cells[i];

                        if (!Grid.IsActiveWorld(cell))
                        {
                            continue;
                        }

                        Element vanillaElement = Grid.Element[cell];
                        if (vanillaElement.IsSolid || vanillaElement.IsLiquid)
                        {
                            continue;
                        }

                        if (!(mass[i] > 0f) || dominant[i] >= ElementLoader.elements.Count)
                        {
                            // No real mixture mass (or an out-of-range index, defensively) -- same
                            // "nothing here" vanilla itself writes for an empty/vacuum cell.
                            region.SetBytes(x, y, 0, 0, 0, 0);
                            continue;
                        }

                        Color32 colour = BoostColour(ElementLoader.elements[dominant[i]].substance.colour, dominant[i]);
                        region.SetBytes(x, y, colour.r, colour.g, colour.b, byte.MaxValue);
                    }
                }
            }
            catch
            {
                // As above: a job worker must not throw.
            }
        }

        /// <summary>
        /// A species' own `substance.colour` is often close to neutral gray (e.g. Oxygen's is
        /// a pale near-white blue) -- correct for vanilla's own subtle per-gas tint, but at the
        /// user's explicit request the promoted-cell indicator needs to read at a glance
        /// without a diff tool, not just be provably correct under one. Keeps the species'
        /// real hue (so different gases still look different) but forces saturation and value
        /// up to a strong minimum via HSV, deliberately more vivid than vanilla's own
        /// equivalent cell would ever render -- a promoted room is meant to look distinct.
        ///
        /// REAL BUG found and fixed via the screenshot demo's own diagnostic:
        /// `Color.RGBToHSV` returns hue 0 for any fully desaturated (gray) input -- not "no
        /// hue," just the same value `h` would have for actual red. CarbonDioxide's raw
        /// colour is (30,30,30), exactly gray, so the old version (force s=1,v=1 regardless)
        /// turned EVERY near-gray species into the same solid red, confirmed live: a real
        /// CO2-dominant cell rendered as red, not CO2's own (colourless) tint boosted. Any
        /// species whose raw colour is gray enough to have no meaningful hue needs a
        /// substitute hue that still distinguishes it from other species -- picked
        /// deterministically from `elementIdx` (stable across a session, cheap, no lookup
        /// table to maintain as new elements are added) rather than collapsing them all to
        /// one arbitrary colour.
        /// </summary>
        private static Color32 BoostColour(Color32 c, ushort elementIdx)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.15f)
            {
                // Below this, hue is noise -- RGBToHSV's own h for a gray input is an
                // artifact of the algorithm (always 0), not a real property of the colour.
                // Golden-angle spacing (same technique HSV-based categorical palettes use)
                // keeps adjacent element indices visually distinct rather than clustering.
                // Color.RGBToHSV/HSVToRGB both use hue normalized to [0,1], not degrees, so
                // this stays in the same units the non-fallback branch's `h` is already in.
                h = (elementIdx * 137.508f % 360f) / 360f;
            }
            s = 1f;
            v = 1f;
            return Color.HSVToRGB(h, s, v);
        }
    }
}
