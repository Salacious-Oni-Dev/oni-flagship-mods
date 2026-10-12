# Changelog

Notable changes to this repository, newest first. All SDK repositories share one version
number per release; see [Compatibility](https://github.com/Salacious-Oni-Dev/oni-sdk-docs/blob/main/guides/compatibility.md).

## 0.1.0-alpha.2 (2026-10-12)

*Oxygen Not Included: now with actual reasons for the oxygen to be not included.*

Mods 1 and 2 now declare API level 0.2, and a new mod joins them: Mod 5, Automation / IC10,
also at API level 0.2.

- Mod 1, Physical Thermodynamics + Fluid Dynamics:
  - Active Vent: Stationeers' Active Vent as one building with an Outward and an Inward mode.
    Its port turns round with the mode, and its mode and setpoints are open to automation mods
    through the framework's `DataDevices`.
  - The Steam Room: the Sauna vents its steam into its own sealed room instead of turning it
    into water, and a warm, humid Rec Room with a Sauna becomes a Steam Room with a stronger
    morale effect.
  - Germs ride with the mass through pipe networks and the Gas and Liquid Reservoirs, by the
    base game's own merge, growth and death rules, instead of being deleted.
  - Arrows on the pipes in the Gas Ventilation and Plumbing overlays show which way each tile
    flowed, and whether a vent is blowing out or drawing in.
  - A tank and the pipe run it is plumbed to now share one temperature, and a gas run carrying
    several gases is levelled as one mixture.
  - Pumps, tanks and valves work on simulation time, so their throughput no longer depends on
    the frame rate.
  - Vents fill the eight cells of the room nearest them rather than their own tile, and start
    the gas mixture in a room that has none yet.
  - `--mod1-hydrostatics` turns on the framework's liquid hydrostatics. Off by default.
- Mod 2, Matter / Environmental Physics:
  - Fire: fuel burns with oxygen into its real products and heat, gas catches fire when
    sparked or hot enough, fires are drawn with flames, embers, light and a smoke haze, and a
    fire starved of air goes out. A new Soot element. (The smoke system came from an idea by
    Kaz.)
  - New buildings: Campfire (solid fuel, lit by a duplicant), Sparker, Fire Sprinkler and Mash
    Tun.
  - The Hydrogen Generator burns hydrogen with oxygen from one mixed pipe into hot water; the
    Electrolyzer draws 960 W; the Solar Panel makes up to 760 W.
  - Falling liquid trades heat with the gas it falls through.
  - Sugar and ethanol dissolved in water, from the Mash Tun and from Sugar Water.
  - Hygiene: duplicants get grimy, with morale bands and a smell, and showers wash it off.
  - Claims the Hydrogen Generator and the Electrolyzer through the framework's
    `BuildingClaims`, so another mod's patches to them are removed and logged instead of
    fighting Mod 2's.
- Mod 5, Automation / IC10 (new):
  - Stationeers' integrated-circuit automation in ONI. The IC Housing holds a program written in
    IC10, the language Stationeers uses, and runs it twice a second against the buildings on its
    data cable. All 154 instructions are there, and most programs written for Stationeers
    compile here unchanged.
  - It reads and drives the game's own buildings through one adapter: temperature, pressure, gas
    composition, power and battery charge, a valve's flow, a sensor's threshold, a door, a
    plant's growth and harvest, a fabricator's recipe and counted orders, a bin's or fridge's
    item checklist, a rocket's fuel and flight state, and the automation input of most buildings
    a wire could switch. With Mod 1, it also drives Mod 1's buildings.
  - Data Cable: a third kind of wire, with a bridge and its own overlay. A building is on a data
    network when cable runs under it.
  - A program editor inside the game, with Stationeers' limits, a compile check, the chip's
    registers and sockets, and a reference line for the instruction being typed; a link tool for
    choosing which building each socket points at.
  - Stationeers' logic units, for automation without a program: reader, writer, writer switch,
    math, unary math, compare, select, min/max, slot and batch readers, batch writer, hash
    generator, logic mirror and a PID controller. Memory, dial, switch, button and display
    bricks. The IC Sorter, a conveyor filter a program sets.
  - ONI's power transformers gain an output limit, set on a slider or by a chip. A transformer
    left alone behaves as before.
  - Other mods can make their buildings readable and writable from a chip through the
    framework's `DataDevices`, without referencing Mod 5.
  - The building art is AI placeholder art, to be replaced. The IC10 interpreter follows
    Stationeers' own closely and is not under the MIT licence; see `NOTICE`.

## 0.1.0-alpha.1 (2026-10-01)

First public release. Both mods declare API level 0.1 and use only the public `OniFramework`
API.

- Mod 1, Physical Thermodynamics + Fluid Dynamics:
  - Heat that is moved instead of deleted: a building's power draw becomes heat in its own
    structure, and exhaust, Steam Turbine, generator and Wheezewort heat is delivered instead of
    destroyed.
  - Gas mixtures: a room's air holds several gases at once, and duplicants breathe the
    mixture.
  - Pipes with real pressure, shown in a Stationeers-style tooltip. Overpressure, liquid in a
    gas pipe and frozen contents stress and damage a pipe, and gas that condenses in a pipe
    stays there as liquid.
  - Dissolved gas carried by liquids through pipes, storage and bottles, with overlays for
    dissolved gas and gas composition.
  - New buildings: Gas Mixer, Volume Pump, Condensation Valve and Purge Valve, Evaporation and
    Condensation Chambers, Gas and Liquid Conduit Radiators and Radiator Panels, Air Intake, Air
    Diffuser and Carbon Scrubber, Inline Carbon Skimmer, and Dissolved Gas Sensor.
  - Vanilla buildings augmented rather than replaced: Gas Pump and Gas Reservoir as a
    compressor and tank, Liquid Pump and Liquid Reservoir, Gas Vent under liquid, and a Soda
    Fountain that keeps the carbon dioxide it serves.
  - A paint colour for every eligible building, vanilla and other mods' included, chosen in the
    building's details panel.
  - Switches on the simulation's `CellEnergyCarry` each time a game opens, as one of its heat
    rules, so small heat payments into cells land in full instead of being rounded the same way
    every time.
  - `--oni-debug-inspector` starts the framework's HTTP debug server on port 9788, for
    development. It has no authentication and listens on every network interface.
- Mod 2, Matter / Environmental Physics: nitrogen, liquid nitrogen and a toxic pollutant gas,
  with the physical properties the simulation needs to treat them physically, registered where
  any mod can read them.
- `build.ps1` builds the mods on Windows in PowerShell, without WSL or bash.
