using OniFramework;
using UnityEngine;

namespace Mod1ThermoFluid
{
    /// <summary>
    /// Turns a pair of independent <see cref="GasMixtureTankComponent"/>s into a real closed
    /// vapor-compression refrigeration loop: cooling that consumes energy and moves heat
    /// somewhere else instead of deleting it.
    ///
    /// MASS ISOLATION. Both link components move mass EXCLUSIVELY through
    /// <see cref="GasMixtureTankComponent.TryRemoveMass"/> / <see cref="GasMixtureTankComponent.AddMass"/>
    /// -- a tank's own managed-state API -- never a cell, never <c>SimMessages</c>. Nothing in
    /// this file calls `AddRemoveSubstance`, `ReplaceElement`, `ModifyCell` or `SimMessages`;
    /// venting condensed liquid into a tank's own building cell would be a mass leak.
    ///
    /// SCOPE. These links are plain <c>MonoBehaviour</c>s wired directly to two tank references
    /// (assigned by whoever builds the loop), NOT a third augmented vanilla building. A
    /// compressor that pumps gas between two otherwise-disconnected pipe networks has no vanilla
    /// building shape to reuse (vanilla's Gas Pump only moves cell&lt;-&gt;pipe). The two tanks
    /// either side are augmented vanilla Gas Reservoirs doing all the actual physics (phase
    /// change, ElementChunk heat exchange with the environment).
    ///
    /// THE CYCLE, using Water/Steam as the working fluid:
    ///
    ///   Evaporator tank (low pressure, sits in the room to be COOLED)
    ///     holds mostly liquid Water. At low pressure the curve's crossing point drops below the
    ///     room's ambient temperature, so ApplyPhaseChange boils it to Steam, paying the latent
    ///     heat cost out of the tank's own temperature -- which then conducts colder than the
    ///     room via ElementChunkFacade, pulling heat OUT of the room.
    ///   CompressorLinkComponent
    ///     pulls Steam out of the evaporator (TryRemoveMass) and pushes it into the condenser
    ///     (AddMass) -- AddMass's AdiabaticFillTemperature blend is the compression heat.
    ///   Condenser tank (high pressure, sits in the room that REJECTS heat)
    ///     holds hot, high-pressure Steam. At high pressure the curve's crossing point rises above
    ///     the tank's own temperature, so ApplyPhaseChange condenses it back to Water, RELEASING
    ///     the latent heat into the tank -- which then conducts hotter than its room, pushing
    ///     heat INTO that room.
    ///   ExpansionValveComponent
    ///     pulls Water back out of the condenser and returns it to the evaporator, closing the
    ///     loop. Arriving in the evaporator's low-pressure tank is what lets the curve threshold
    ///     drop back below ambient so ApplyPhaseChange boils it again.
    ///
    /// Net result: the evaporator room cools, the condenser room warms, and the energy moved is
    /// conserved end to end.
    ///
    /// A TANK WITH NO VAPOUR. <see cref="TryGetVaporPressurePa"/> reads exactly 0 whenever a tank
    /// holds no vapour (including an evaporator whose vapour is being drained continuously). The
    /// threshold is then the CLAMPED curve at that pressure
    /// (<see cref="GasMixtureFacade.TryGetEvaporationTemperatureClampedK"/>), which resolves to
    /// the freezing point: the raw power-law curve would give an unphysical 89.28 K for water at
    /// 1 Pa, and <see cref="GasMixtureTankComponent.ApplyPhaseChange"/> would install that as a
    /// floor. Freezing is a separate boundary with a flat threshold and its own latent heat
    /// (water's 333.55 kJ/kg).
    ///
    /// BOTH LEGS ARE REGULATORS, NOT FLAT RATES. A leg that moves a flat kg/s has to be re-rated
    /// by hand whenever a latent heat changes, and a wrong rate empties the evaporator's vapour
    /// space and parks it on its freezing clamp. Stationeers is immune structurally, and the legs
    /// follow its shape:
    ///
    ///   | Stationeers site                                      | here      |
    ///   |-------------------------------------------------------|-----------|
    ///   | AtmosphereHelper.MoveRegulatedGas, Downstream          | ported    |
    ///   | AtmosphereHelper.DrainLiquids, TotalVolumeLiquids / 2  | ported    |
    ///   | StateChangeDevice.PressurePerTick as a CEILING         | ported    |
    ///   | the clamped evaporation temperature                    | matched   |
    ///
    /// Neither leg carries a number derived from a latent heat, so a change to the material
    /// tables does not re-tune the loop.
    /// </summary>
    public class CompressorLinkComponent : MonoBehaviour
    {
        public GasMixtureTankComponent Evaporator;
        public GasMixtureTankComponent Condenser;
        public int WorkingFluidGasElementIdx;

        /// <summary>
        /// The suction pressure this leg regulates the evaporator down to, named as the
        /// temperature we want the evaporator to boil AT rather than as a pressure. The pressure
        /// is read live off the working fluid's own saturation curve
        /// (<see cref="GasMixtureFacade.TryGetSaturationPressurePa"/>), so nothing here has to be
        /// re-derived when a latent heat, a vapour curve or the fluid itself changes -- which is
        /// the whole reason this replaced a hand-rated kg/s.
        ///
        /// 280 K sits about 13 K under the rig's ~293 K room, so heat still flows in and drives
        /// the boiling, and 6.85 K over water's 273.15 K freezing clamp, so the cold side has
        /// somewhere to settle that is not the floor.
        ///
        /// THE MARGIN IS IN PRESSURE, WHICH IS WHAT MAKES IT STRUCTURAL RATHER THAN TUNED. Against
        /// the water family's own registered curve (MaterialProperties.SeedStationeersDefaults,
        /// A = 3.8782059839e-19, B = 7.90030107708) the setpoint resolves to 8354 Pa, while the
        /// freezing clamp's own saturation pressure is 6870 Pa and the family's
        /// minLiquidPressurePa is 6300 Pa. Reaching the clamp therefore requires the tank BELOW
        /// 6870 Pa and this leg stops pumping at 8354 Pa, so the clamp is not merely unlikely --
        /// the regulator cannot walk the tank to it. Raising this constant raises the setpoint and
        /// widens that margin; lowering it past roughly 273 K would put the setpoint under the
        /// liquid-pressure floor, which is the one direction that is not safe.
        /// </summary>
        private const float EvaporatingTemperatureK = 280f;

        /// <summary>
        /// The per-second ceiling on how far this leg may pull the evaporator's pressure down,
        /// the analogue of Stationeers' <c>StateChangeDevice.PressurePerTick</c> fed to
        /// <c>AtmosphereHelper.MoveRegulatedGas</c> as its <c>pressurePerTick</c> argument. It is
        /// a CEILING, not a target: the setpoint above decides where the leg stops, and this only
        /// decides how fast it may get there, exactly as <c>val</c> does in that helper.
        /// </summary>
        private const float MaxDrawdownPaPerSecond = 2000f;

        private const float SampleIntervalSeconds = 1f;
        private float timer;

        /// <summary>
        /// What this leg has actually moved, and at what temperature, since the run started.
        ///
        /// INSTRUMENTATION BEFORE DIAGNOSIS. The rig could see the two tanks' contents and the two
        /// rooms' temperatures and could not see the one thing that decides whether this is a heat
        /// PUMP or a heat SHUTTLE: how much mass each leg carries and how hot it is when it
        /// arrives. Reading a stalled cycle off tank totals alone is guessing at which of the two
        /// legs stopped, which is the failure mode this project has already paid for twice.
        /// </summary>
        public static float MovedKg;
        public static float LastTempK;

        /// <summary>
        /// The suction setpoint this leg last regulated against, and the evaporator pressure it
        /// saw. Added with the regulator itself: a leg that stops because it has reached its
        /// setpoint and a leg that stops because the tank is empty look identical in
        /// <see cref="MovedKg"/> alone, and telling those two apart is the entire point of the
        /// change. LastSuctionSetpointPa is 0 until the leg has run once.
        /// </summary>
        public static float LastSuctionSetpointPa;
        public static float LastEvaporatorPressurePa;

        // THE SUCTION REGULATOR, the shape of AtmosphereHelper.MoveRegulatedGas (Stationeers),
        // RegulatorType.Downstream, which its Evaporation Chamber drives its gas-out leg through:
        //
        //     if (input.PressureGassesAndLiquids <= setting) { return; }
        //     moleQuantity = Min(IdealGas.Quantity(inputPressure - setting, ...), val);
        //
        // a HARD FLOOR at the setpoint, and a moved quantity that is the pressure EXCESS rather
        // than a quota -- so the transfer is asymptotic to the setpoint and the chamber can never
        // be evacuated below it. `val`, the per-tick rate, is a ceiling on that excess. The
        // setpoint governs; the rate does not.
        //
        // A flat quota instead over-rates the leg against what the room can supply (by a factor
        // of about 20 at 0.006 kg/s): the evaporator cools at an accelerating rate, its vapour
        // pressure collapses, and it parks on the freezing clamp at two orders below saturation --
        // an evacuated tank, not one at equilibrium.
        //
        // The freezing clamp itself is NOT the defect and is not touched here: bounding the vapour
        // curve to freezing..critical is Stationeers' own clamped evaporation temperature, and water evaporatively cooling below its own freezing point would be the
        // bug. Reaching that clamp is the defect. With a setpoint the leg cannot reach it.
        private void Update()
        {
            timer += Time.deltaTime;
            if (timer < SampleIntervalSeconds)
            {
                return;
            }
            timer = 0f;

            if (Evaporator == null || Condenser == null)
            {
                return;
            }

            if (!Evaporator.TryGetVaporPressurePa(out float evaporatorPressurePa))
            {
                return;
            }

            Element gas = ElementLoader.elements[WorkingFluidGasElementIdx];
            if (!GasMixtureFacade.TryGetSaturationPressurePa(gas.id, EvaporatingTemperatureK,
                    out float suctionSetpointPa) || suctionSetpointPa <= 0f)
            {
                return;
            }

            LastSuctionSetpointPa = suctionSetpointPa;
            LastEvaporatorPressurePa = evaporatorPressurePa;

            // MoveRegulatedGas's own first line for a downstream regulator.
            if (evaporatorPressurePa <= suctionSetpointPa)
            {
                return;
            }

            float excessPa = Mathf.Min(evaporatorPressurePa - suctionSetpointPa,
                MaxDrawdownPaPerSecond * SampleIntervalSeconds);

            // Pressure -> mass WITHOUT a second copy of P = nRT/V. Pressure is linear in mass at a
            // fixed temperature and volume, so the tank's own pressure for one kilogram of this
            // species is the conversion factor, and it comes from the same framework entry point
            // the tank itself uses (GasMixtureTankComponent.TryGetVaporPressurePa ->
            // GasMixtureFacade.TryComputePressure). Compressor.cs's own note records why the
            // hand-copied GasConstantR that used to sit beside that formula was removed.
            if (!GasMixtureFacade.TryComputePressure(new int[] { WorkingFluidGasElementIdx },
                    new float[] { 1f }, Evaporator.TemperatureK,
                    GasMixtureTankComponent.TankVolumeM3, out float pascalsPerKilogram)
                || pascalsPerKilogram <= 0f)
            {
                return;
            }

            float requestKg = excessPa / pascalsPerKilogram;
            if (requestKg <= 0f)
            {
                return;
            }

            if (Evaporator.TryRemoveMass(WorkingFluidGasElementIdx, requestKg, out float removedKg, out float removedTempK) &&
                removedKg > 0f)
            {
                Condenser.AddMass(WorkingFluidGasElementIdx, removedKg, removedTempK);
                MovedKg += removedKg;
                LastTempK = removedTempK;
            }
        }
    }

    /// <summary>
    /// The return line -- see <see cref="CompressorLinkComponent"/>'s class doc comment (on
    /// GasMixtureTankComponent) for the full cycle. Deliberately the mirror image of
    /// CompressorLinkComponent (condenser -&gt; evaporator, liquid phase, no separate physics of
    /// its own) rather than a shared base class -- two four-field components duplicating a dozen
    /// lines each isn't worth a shared abstraction yet, and keeping them syntactically identical
    /// makes the direction of each link (which is the only thing that actually differs) easy to
    /// eyeball-verify at the call site instead of hidden behind a constructor argument.
    /// </summary>
    public class ExpansionValveComponent : MonoBehaviour
    {
        public GasMixtureTankComponent Evaporator;
        public GasMixtureTankComponent Condenser;
        public int WorkingFluidLiquidElementIdx;

        /// <summary>
        /// The vapour phase of the same working fluid, needed because a real expansion FLASHES
        /// part of the liquid. Optional: left at -1 the valve degrades to delivering everything as
        /// liquid at the flash temperature, which is still enormously better than delivering it at
        /// the condenser's.
        /// </summary>
        public int WorkingFluidGasElementIdx = -1;

        private const float GramsPerKilogram = 1000f;

        // THE RETURN REGULATOR, from a different Stationeers site because Stationeers uses a
        // different shape on this side. Its Condensation Chamber drains its liquid with
        //
        //     AtmosphereHelper.DrainLiquids(source, destination,
        //         RocketMath.Min(source.TotalVolumeLiquids / 2.0, Chemistry.PipeVolume));
        //
        // -- HALF of whatever the source is actually holding, capped by one pipe volume. That is
        // proportional and therefore asymptotic: the drain cannot empty the condenser, and it
        // needs no knowledge of what the other leg is delivering, so the two legs stop having to
        // be rated against each other by hand. Two quotas that must be kept equal by hand are one
        // quota too many; the proportional form removes the coupling instead of maintaining it.
        private const float DrainFractionPerSecond = 0.5f;

        /// <summary>
        /// The ceiling on the proportional drain, standing in for Stationeers'
        /// <c>Chemistry.PipeVolume</c> (10 L) in a model whose tanks are sized in kilograms rather
        /// than litres. Like that one it is expected not to bind in normal running -- the
        /// condenser holds a fraction of a kilogram of condensate -- and exists so a
        /// single tick can never move an unbounded amount.
        /// </summary>
        private const float MaxReturnKgPerSecond = 0.05f;

        private const float SampleIntervalSeconds = 1f;
        private float timer;

        /// <summary>Mirror of <see cref="CompressorLinkComponent.MovedKg"/> for the return leg.</summary>
        public static float MovedKg;
        public static float LastTempK;

        /// <summary>
        /// How much of the returned liquid FLASHED to vapour crossing the restriction, and what
        /// temperature it arrived at before expanding. The pair is the evidence that the valve is
        /// doing an expansion at all: an inlet far above the delivery temperature with a non-zero
        /// flashed mass is a throttling process, and equal inlet and delivery temperatures with
        /// nothing flashed is the plain pipe this used to be.
        /// </summary>
        public static float FlashedKg;
        public static float LastInletTempK;

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer < SampleIntervalSeconds)
            {
                return;
            }
            timer = 0f;

            if (Evaporator == null || Condenser == null)
            {
                return;
            }

            // DrainLiquids' own bound: half of what the source actually holds, capped.
            float condenserLiquidKg = 0f;
            foreach (var species in Condenser.Composition())
            {
                if (species.Key == WorkingFluidLiquidElementIdx)
                {
                    condenserLiquidKg = species.Value;
                    break;
                }
            }

            float requestKg = Mathf.Min(condenserLiquidKg * DrainFractionPerSecond,
                MaxReturnKgPerSecond) * SampleIntervalSeconds;
            if (requestKg <= 0f)
            {
                return;
            }

            if (!Condenser.TryRemoveMass(WorkingFluidLiquidElementIdx, requestKg,
                    out float removedKg, out float removedTempK) || removedKg <= 0f)
            {
                return;
            }

            // THE EXPANSION. Handing `removedTempK` -- the CONDENSER's temperature -- straight to
            // the evaporator would deliver liquid at around 417 K into the tank whose entire job
            // is to be cold. That is not an expansion valve, it is a pipe, and it carries the
            // condenser's sensible heat back to the cold side every cycle.
            //
            // WHAT A REAL VALVE DOES: throttling is ISENTHALPIC. The liquid crosses a restriction
            // into the low-pressure side, and because enthalpy is conserved rather than
            // temperature, part of it flashes to vapour and the rest arrives at the saturation
            // temperature of the pressure it arrived at. Equating enthalpy across the restriction,
            //
            //     m*cp*T_in  =  m*cp*T_sat  +  x*m*L
            //     x          =  cp * (T_in - T_sat) / L
            //
            // gives the flash fraction x directly. The sensible heat the old code dumped into the
            // cold room is exactly the term that now becomes latent heat of the flashed vapour --
            // energy conserved, but carried as phase rather than as temperature, which is the
            // whole point of the component.
            float deliveredTempK = removedTempK;
            float flashedKg = 0f;

            Evaporator.TryGetVaporPressurePa(out float evapPressurePa);
            Element liquid = ElementLoader.elements[WorkingFluidLiquidElementIdx];
            if (GasMixtureFacade.TryGetEvaporationTemperatureClampedK(liquid.id, evapPressurePa,
                    out float saturationK)
                && GasMixtureFacade.TryGetLatentHeatJPerKg(liquid.id, out float latentJPerKg)
                && latentJPerKg > 0f
                && removedTempK > saturationK)
            {
                deliveredTempK = saturationK;
                float cpJPerKgK = liquid.specificHeatCapacity * GramsPerKilogram;
                float fraction = cpJPerKgK * (removedTempK - saturationK) / latentJPerKg;

                // A fraction at or above one means the incoming stream carries more sensible heat
                // than the latent heat of vaporising ALL of it -- it arrives as pure vapour, and
                // still at the saturation temperature, because there is no liquid left to be
                // superheated in this model. Clamped rather than allowed to create mass.
                fraction = Mathf.Clamp01(fraction);
                if (WorkingFluidGasElementIdx >= 0)
                {
                    flashedKg = removedKg * fraction;
                }
            }

            float liquidKg = removedKg - flashedKg;
            if (liquidKg > 0f)
            {
                Evaporator.AddMass(WorkingFluidLiquidElementIdx, liquidKg, deliveredTempK);
            }
            if (flashedKg > 0f)
            {
                Evaporator.AddMass(WorkingFluidGasElementIdx, flashedKg, deliveredTempK);
            }

            MovedKg += removedKg;
            FlashedKg += flashedKg;
            LastTempK = deliveredTempK;
            LastInletTempK = removedTempK;
        }
    }
}
