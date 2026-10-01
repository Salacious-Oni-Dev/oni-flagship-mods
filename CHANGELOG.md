# Changelog

Notable changes to this repository, newest first. All SDK repositories share one version
number per release; see [Compatibility](https://github.com/Salacious-Oni-Dev/oni-sdk-docs/blob/main/guides/compatibility.md).

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
