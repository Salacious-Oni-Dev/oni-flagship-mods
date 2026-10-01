using System;
using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// ONE COPY A TICK OF EVERYTHING THE TWO MIXTURE OVERLAYS READ.
    ///
    /// Both layers the overlays draw -- the dissolved gas and the volume-fractions layer's
    /// per-cell species -- are SimDLL extension cell properties, and the managed way to read one
    /// cell of one is <c>SimExtCellProperties.TryReadFloat</c>, which takes the sim's worker
    /// barrier. That is the right call for a hover card and the wrong one for an overlay by two
    /// separate arguments: a colour function runs once per VISIBLE CELL per frame, so the barrier
    /// count would be in the tens of thousands a second; and it runs on Klei's job workers, while
    /// <c>WaitIdle()</c> in sim/simdll.cpp is documented "Game thread only" and its auto-reset
    /// done-event has exactly one waiter in mind.
    ///
    /// So the properties are subscribed to the per-frame publish and the whole published array is
    /// copied once per tick, on the game thread, inside <c>SimExtFrame.Bound</c> -- the same
    /// trade <c>DissolveRig.OnBound</c> already makes for its sweeps. After that a read is a
    /// managed array index with no synchronisation of any kind.
    ///
    /// WHY THE COPY IS SAFE TO READ OFF-THREAD. It is written only inside <c>Bound</c>, which
    /// fires from <c>Sim.HandleMessage(PrepareGameData)</c> on the game thread, and it is read
    /// only inside <c>SimDebugView.UpdateData</c>'s work items, which the game thread dispatches
    /// through <c>GlobalJobManager.Run</c> and then blocks on until every worker is finished.
    /// The two can therefore never overlap, and the dispatch itself is the barrier that publishes
    /// the writes. This is the same argument <c>OverlayTexturePatch.OverlayBatch</c> makes, and
    /// it is the only reason either of them may hand a plain array to a worker.
    ///
    /// STALENESS IS A REAL FAILURE MODE AND IS CHECKED. <c>Publish</c> is queued: it lands on the
    /// next tick, so the first frame after an overlay opens has no data. Worse, if publishing is
    /// ever turned off while a copy is still held, every read would keep answering from a frozen
    /// tick and the overlay would paint a world that no longer exists. Every read therefore
    /// checks the copy's own tick against <see cref="SimExtFrame.TickId"/> and answers "nothing
    /// here" once it falls behind, which renders as an untinted cell rather than a lie.
    /// </summary>
    internal sealed class MixtureSnapshot
    {
        /// <summary><c>oni_sim::gas::kMaxSpeciesPerCell</c> (abi/gas_mixture_abi.h).</summary>
        public const int GasSlots = 8;

        /// <summary><c>ext::kGasSpeciesProperty</c> -- u16, arity <see cref="GasSlots"/>, the
        /// element index in each of a cell's mixture slots, 0 for an unused slot
        /// (<c>gas::kEmptySpecies</c>).</summary>
        public const string GasSpeciesProperty = "sim.gas_species";

        /// <summary><c>ext::kGasMassProperty</c> -- f32, arity <see cref="GasSlots"/>, kilograms
        /// in each slot.</summary>
        public const string GasMassProperty = "sim.gas_mass";

        /// <summary>
        /// How many ticks a copy may lag before a read refuses it. Two rather than one: the bind
        /// and the overlay's own update are different points in the same frame, and a copy taken
        /// at the top of this frame is the freshest thing that can possibly exist.
        /// </summary>
        private const long StaleAfterTicks = 2;

        public static readonly MixtureSnapshot Instance = new MixtureSnapshot();

        private sealed class Copy
        {
            public byte[] Bytes;
            public int Arity;
            public int Stride;
            public int CellCount;
            public long Tick = -1;

            public bool Fresh
            {
                get { return Tick >= 0 && SimExtFrame.TickId - Tick <= StaleAfterTicks; }
            }

            public bool Take(string name)
            {
                SimExtFrame.PublishedProperty property;
                if (!SimExtFrame.TryGetProperty(name, out property) || !property.IsCurrent)
                {
                    return false;
                }
                if (Bytes == null || Bytes.Length < property.ByteCount)
                {
                    Bytes = new byte[property.ByteCount];
                }
                if (!property.CopyBytes(Bytes))
                {
                    return false;
                }
                Arity = property.Arity;
                Stride = property.Stride;
                CellCount = property.CellCount;
                Tick = property.Tick;
                return true;
            }
        }

        private readonly Copy dissolved = new Copy();
        private readonly Copy gasSpecies = new Copy();
        private readonly Copy gasMass = new Copy();

        private int dissolvedHolders;
        private int gasHolders;
        private bool hooked;

        // Lane -> element index, rebuilt whenever a lane answers with an element this cache does
        // not have yet. The assignment is stable for a session (DissolvedGas's own doc comment:
        // "a gas never changes lane"), so this is built once in practice.
        private int[] laneElementIdx;

        /// <summary>
        /// Start keeping a copy of the dissolved-gas lanes (<paramref name="wantDissolved"/>) or
        /// of the gas-mixture slots. Reference-counted, so two overlays or an overlay and a probe
        /// can ask at once.
        /// </summary>
        public void Retain(bool wantDissolved, bool wantGas)
        {
            if (wantDissolved)
            {
                dissolvedHolders++;
            }
            if (wantGas)
            {
                gasHolders++;
            }
            Hook();
            Subscribe();
        }

        /// <summary>The other half of <see cref="Retain"/>.</summary>
        public void Release(bool hadDissolved, bool hadGas)
        {
            if (hadDissolved && dissolvedHolders > 0)
            {
                dissolvedHolders--;
            }
            if (hadGas && gasHolders > 0)
            {
                gasHolders--;
            }
        }

        private void Hook()
        {
            if (hooked)
            {
                return;
            }
            SimExtFrame.Bound += OnBound;
            hooked = true;
        }

        /// <summary>
        /// Ask the sim to publish what is held. Deliberately NEVER UNSUBSCRIBES.
        ///
        /// Publishing a property costs one descriptor in a per-tick table and no copy on the sim
        /// side, so holding it open is close to free. Turning it OFF is not free of consequence:
        /// <c>sim.dissolved_mass</c> is published by the dissolved-gas rigs as well, and a
        /// subscription is one flag in the sim, not a count, so an overlay that unsubscribed on
        /// close would silently blind a rig that was running at the time. Between a descriptor
        /// nobody reads and a measurement that quietly stops, the descriptor wins.
        /// </summary>
        private void Subscribe()
        {
            if (dissolvedHolders > 0 && DissolvedGas.Available)
            {
                SimExtCellProperties.Publish(DissolvedGas.PropertyIndex, true);
            }
            if (gasHolders > 0)
            {
                int species = SimExtCellProperties.Find(GasSpeciesProperty);
                int mass = SimExtCellProperties.Find(GasMassProperty);
                if (species >= 0)
                {
                    SimExtCellProperties.Publish(species, true);
                }
                if (mass >= 0)
                {
                    SimExtCellProperties.Publish(mass, true);
                }
            }
        }

        private void OnBound()
        {
            if (dissolvedHolders > 0)
            {
                dissolved.Take(DissolvedGas.PropertyName);
            }
            if (gasHolders > 0)
            {
                gasSpecies.Take(GasSpeciesProperty);
                gasMass.Take(GasMassProperty);
            }
        }

        private static float ReadFloat(Copy copy, int paddedCell, int component)
        {
            int offset = (paddedCell * copy.Arity + component) * copy.Stride;
            if (offset < 0 || offset + 4 > copy.Bytes.Length)
            {
                return 0f;
            }
            return BitConverter.ToSingle(copy.Bytes, offset);
        }

        private static ushort ReadU16(Copy copy, int paddedCell, int component)
        {
            int offset = (paddedCell * copy.Arity + component) * copy.Stride;
            if (offset < 0 || offset + 2 > copy.Bytes.Length)
            {
                return 0;
            }
            return BitConverter.ToUInt16(copy.Bytes, offset);
        }

        private int LaneElement(int lane)
        {
            int[] cache = laneElementIdx;
            if (cache == null || cache.Length != DissolvedGas.Lanes)
            {
                cache = new int[DissolvedGas.Lanes];
                for (int i = 0; i < cache.Length; i++)
                {
                    SimHashes gas = DissolvedGas.LaneElement(i);
                    cache[i] = gas == (SimHashes)0 ? -1 : ElementLoader.GetElementIndex(gas);
                }
                laneElementIdx = cache;
            }
            return lane >= 0 && lane < cache.Length ? cache[lane] : -1;
        }

        /// <summary>
        /// A liquid cell's dissolved load, as (element index, kilograms) pairs written into the
        /// caller's buffers. Returns how many were written and, through
        /// <paramref name="totalKg"/>, what they add up to. 0 for a cell with nothing dissolved,
        /// for a copy that has gone stale, or on a SimDLL without the property.
        ///
        /// Both buffers must hold <see cref="DissolvedGas.Lanes"/> entries.
        /// </summary>
        public int ReadDissolved(int gameCell, ushort[] elementIdx, float[] kg, out float totalKg)
        {
            totalKg = 0f;
            Copy copy = dissolved;
            if (copy.Bytes == null || !copy.Fresh || copy.Arity <= 0)
            {
                return 0;
            }
            int padded = SimExtFrame.PaddedCell(gameCell);
            if (padded < 0 || padded >= copy.CellCount)
            {
                return 0;
            }
            int count = 0;
            int lanes = copy.Arity;
            for (int lane = 0; lane < lanes; lane++)
            {
                float amount = ReadFloat(copy, padded, lane);
                if (!(amount > 0f))
                {
                    continue;
                }
                int element = LaneElement(lane);
                if (element < 0)
                {
                    // A lane carrying mass with no element assigned to it is a real fault, but an
                    // overlay is the wrong place to raise it: skip the term so the cell paints
                    // from the lanes that DO resolve rather than dropping the whole cell.
                    continue;
                }
                if (count >= elementIdx.Length)
                {
                    break;
                }
                elementIdx[count] = (ushort)element;
                kg[count] = amount;
                totalKg += amount;
                count++;
            }
            return count;
        }

        /// <summary>
        /// A gas cell's mixture, as (element index, kilograms) pairs. Returns 0 for a cell the
        /// mixture layer does not own -- which is not an error and is the common case, since an
        /// unpromoted cell's gas lives in vanilla's own <c>Grid.Element</c>/<c>Grid.Mass</c> and
        /// the caller falls back to those.
        ///
        /// Both buffers must hold <see cref="GasSlots"/> entries.
        /// </summary>
        public int ReadGas(int gameCell, ushort[] elementIdx, float[] kg, out float totalKg)
        {
            totalKg = 0f;
            Copy species = gasSpecies;
            Copy mass = gasMass;
            if (species.Bytes == null || mass.Bytes == null
                || !species.Fresh || !mass.Fresh
                || species.Arity <= 0 || mass.Arity <= 0)
            {
                return 0;
            }
            int padded = SimExtFrame.PaddedCell(gameCell);
            if (padded < 0 || padded >= species.CellCount || padded >= mass.CellCount)
            {
                return 0;
            }
            int slots = species.Arity < mass.Arity ? species.Arity : mass.Arity;
            int count = 0;
            for (int slot = 0; slot < slots; slot++)
            {
                ushort element = ReadU16(species, padded, slot);
                if (element == 0)
                {
                    // gas::kEmptySpecies. Element index 0 is not a gas any cell can hold, which
                    // is why the sim can use it as the empty marker.
                    continue;
                }
                float amount = ReadFloat(mass, padded, slot);
                if (!(amount > 0f))
                {
                    continue;
                }
                if (count >= elementIdx.Length)
                {
                    break;
                }
                elementIdx[count] = element;
                kg[count] = amount;
                totalKg += amount;
                count++;
            }
            return count;
        }
    }
}
