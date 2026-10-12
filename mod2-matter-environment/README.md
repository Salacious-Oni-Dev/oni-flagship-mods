# Mod 2 — Matter / Environmental Physics

Mod 2 supplies the matter that Mod 1's physics works on: new elements for a physical
atmosphere, and the physical properties of each substance that the base game's element table
has no place for. It also makes fire real: fuel burns with oxygen into its products, smoke and
heat, and a fire can be lit, starved and put out. Several base-game buildings that turned one
thing into another by magic now do it physically. It is built only on the public `OniFramework` API, and everything it registers
can be read, and extended, by any other mod.

Requires the SDK's replacement `SimDLL.dll` and the `OniFramework` mod, on the game's Windows
version. Mod 2 is designed to run with Mod 1: Mod 1's pipes, tanks, phase change and breathing
are what put these properties to work, and Mod 1 is what hands them to the simulation.

## Elements

| element | notes |
|---|---|
| Nitrogen, Liquid Nitrogen | not breathable, and no solid phase. Boils at 77.35 K (about −196 °C). Mod 1's Gas Mixer can blend it with oxygen into a breathable air, about 75% nitrogen and 25% oxygen, which is what makes an atmosphere that is mostly not oxygen possible |
| Soot | the carbon a smoky fire did not burn, as a solid a duplicant can pick up. Nothing makes it yet: the airborne soot and the deposit it settles into are numbers on cells for now. It can be burned as fuel |
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

## Fire

Fuel burns by chemistry, not by a fixed recipe. Each fuel (wood, peat, coal, natural gas,
hydrogen, ethanol and soot) has a composition and a heating value, and the oxygen a burn needs
and everything it makes are worked out from them, so mass always adds up. A burn takes its
oxygen from the air around it, puts carbon dioxide, steam and smoke into its cell, and releases
the fuel's heat. A fire that runs short of oxygen burns sootier and goes out.

- **Gas catches fire.** Natural gas, hydrogen or ethanol vapour next to oxygen ignites when it is
  sparked or hotter than its auto-ignition temperature, as in Stationeers. This changes an
  ordinary game: natural gas beside oxygen near magma will burn.
- **You can see it.** A burning cell has a flame, embers, a light, and smoke puffs when it is
  starved of air. Airborne soot is drawn as a haze that rises and drifts with the gas carrying it.
- **Water puts it out** only by physics: water that takes a fire's cells and its heat ends it.

(The smoke system came from an idea by Kaz.)

## Falling water trades heat

A falling drop of liquid exchanges heat with the gas it falls through, and can boil or freeze on
the way down. This is on for all falling liquid while Mod 2 is installed.

## Sugar and ethanol in water

Water can carry dissolved sugar and ethanol, through pumps, pipes and storage, the way Mod 1's
liquids carry dissolved gas. Sugar comes from the **Mash Tun**, which splits fruit by its real
composition into its own water with the sugar in it and pulp, and from the base game's Sugar
Water, which now dissolves into water it touches instead of sitting on top of it. When the
liquid carrying it is gone, sugar is left as Sucrose and ethanol as Ethanol.

## Hygiene

Standard duplicants get grimy: about 40 grime a cycle at rest, more when they work, and 30 at
once for a bladder accident. Grime has three bands, Grubby, Grimy and Filthy, each a morale
penalty, and a grimy duplicant gives off a smell. A shower washes it off at a rate, so a filthy
duplicant showers longer, and a wash basin or sink takes a little off. Bionic duplicants are
not affected. Every rate is a balance placeholder for now.

## Buildings

New buildings:

| building | menu | does |
|---|---|---|
| Campfire | Utilities | a fire from solid fuel. Duplicants bring it wood, peat or coal and have to light it, and it burns until the fuel or the air runs out, drawing oxygen from the room and giving off heat, smoke and carbon dioxide. Every fuel gives the same heat. With "Keep it lit" on, duplicants relight it and keep it fed |
| Sparker | Automation | a 10 W ignition source. While its automation input is green it sparks its own cell and the one in front of it; it lights only what has fuel and oxygen. Unwired, it is off |
| Fire Sprinkler | Plumbing | a ceiling tile with a liquid input that sprays the cell below it. It opens on a modelled glass bulb heated by the gas under it (default 340 K, set in its side screen) and closes on the room's mean temperature. A green automation input forces it open; unwired, it still protects its room |
| Mash Tun | Food | turns fruit into a sugary mash: the fruit's own water carrying its sugar, and the pulp as Polluted Dirt. No power |

Base-game buildings it changes:

- **Hydrogen Generator** burns hydrogen with oxygen, from one mixed gas pipe (0.1 kg/s hydrogen
  and 0.8 kg/s oxygen for 800 W), into 0.9 kg/s of hot water out of a liquid pipe. An off mix
  burns in proportion to the reactant that runs short, and the unburned gas leaves by a second,
  gas pipe; nothing is vented into the room. On an existing save the generator gains both ports
  and waits until it has oxygen and a water pipe.
- **Electrolyzer** draws 960 W instead of 120 W. With the generator making water again, the two
  would otherwise be a loop that makes power from nothing.
- **Solar Panel** makes up to 760 W instead of 380 W, to pay for the Electrolyzer.

Mod 2 claims the Hydrogen Generator and the Electrolyzer through the framework's
`BuildingClaims`: another mod's patches to those two buildings are removed and logged, so the
two mods never fight over the same building.

## Building and installing

See the repository's README.

## Known limitations

- Each substance has one latent heat, at its stated reference point; it does not vary with
  temperature.
- Every seeded gas has the same heat-capacity ratio today. The field is per substance so that a
  gas registered later can differ.
- Exchange with a planet's environment and atmosphere is not part of this release. It is
  planned with the planetary mods.
