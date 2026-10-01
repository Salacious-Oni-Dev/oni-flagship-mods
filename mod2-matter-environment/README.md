# Mod 2 — Matter / Environmental Physics

Mod 2 supplies the matter that Mod 1's physics works on: new elements for a physical
atmosphere, and the physical properties of each substance that the base game's element table
has no place for. It is built only on the public `OniFramework` API, and everything it registers
can be read, and extended, by any other mod.

Requires the SDK's replacement `SimDLL.dll` and the `OniFramework` mod, on the game's Windows
version. Mod 2 is designed to run with Mod 1: Mod 1's pipes, tanks, phase change and breathing
are what put these properties to work, and Mod 1 is what hands them to the simulation.

## Elements

| element | notes |
|---|---|
| Nitrogen, Liquid Nitrogen | not breathable, and no solid phase. Boils at 77.35 K (about −196 °C). Mod 1's Gas Mixer can blend it with oxygen into a breathable air, about 75% nitrogen and 25% oxygen, which is what makes an atmosphere that is mostly not oxygen possible |
| Pollutant | a toxic gas that shares cells with other gases as a real mixture. The base game stores one element per cell, so its Polluted Oxygen is a substance of its own; with Mod 1, oxygen and Pollutant can sit in the same cells, both measurable and both adding to the pressure |

Each element is registered through `OniFramework.ElementRegistry`, the same call a third-party
mod would use to add its own, and its physical properties through
`OniFramework.MaterialPropertyRegistry`.

**Molecular mass.** The base game's element table stores the atomic mass for diatomic gases, so
nitrogen is entered there as 14.0067, which layers it just above oxygen as the game expects. The
registry holds the real molecular mass, 28.0134 g/mol, which is what the simulation's gas law
uses for pressure.

## Material properties

The base game's `Element` carries specific heat, thermal conductivity, molar mass, transition
temperatures, viscosity and a few flags. It has no field for:

- the latent heat of vaporisation and of fusion,
- a vapour-pressure curve, which sets the pressure at which a gas condenses at a given
  temperature,
- the lowest pressure at which a liquid can exist,
- a critical point,
- a heat-capacity ratio, which sets how much a gas heats when it is compressed,
- a liquid density.

These live in the framework's `MaterialPropertyRegistry`, where any mod can read or extend them.
Mod 2 registers its elements there; Mod 1's pipes, tanks, phase change and compression read them.

### Latent heats are physical

Every seeded substance carries its real latent heats. Vaporisation values are taken from NIST's
reference data at the normal boiling point (carbon dioxide, which has no liquid at normal
pressure, at its triple point); fusion values from cited reference tables.

| substance | vaporisation, kJ/kg | fusion, kJ/kg |
|---|---|---|
| Water | 2256.5 | 333.55 |
| Polluted Water (equal to water per kg) | 2256.5 | 333.55 |
| Carbon dioxide | 350.4 | 204.9 |
| Oxygen | 213.1 | 13.9 |
| Hydrogen | 448.7 | 58.0 |
| Methane | 510.8 | 58.5 |
| Nitrogen (this mod) | 199.2 | 25.7 |
| Pollutant (this mod) | 71.4 | 14.3 |

Pollutant has no real-world counterpart, so it keeps the values of the Stationeers gas it is
modelled on.

These are what make phase change matter in play: boiling a kilogram of water takes as much
energy as heating it from freezing to boiling five times over, so a refrigeration loop built on
evaporation moves a great deal of heat, and condensation in a cold pipe releases it.

## Building and installing

See the repository's README.

## Known limitations

- Each substance has one latent heat, at its stated reference point; it does not vary with
  temperature.
- Every seeded gas has the same heat-capacity ratio today. The field is per substance so that a
  gas registered later can differ.
- Exchange with a planet's environment and atmosphere is not part of this release. It is
  planned with the planetary mods.
