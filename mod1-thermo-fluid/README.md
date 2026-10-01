# Mod 1 — Physical Thermodynamics + Fluid Dynamics

Mod 1 changes what heat, gas and liquid physically are in Oxygen Not Included. Heat that the
game would delete is moved somewhere instead. A room's air can hold several gases at once, and
duplicants breathe that mixture. Pipe networks have a volume, a pressure and a phase, and push
back when they are pushed too far. Liquids carry dissolved gas, and phase change costs and
releases real latent heat. Together these make a working refrigeration cycle possible: cooling
that moves heat from one room to another, with nothing deleted.

It is built only on the public `OniFramework` API, so everything it does is open to any other
mod through the same calls.

Requires the SDK's replacement `SimDLL.dll` and the `OniFramework` mod, on the game's Windows
version. The repository's README has short videos of each system below.

## What changes in play

### Heat goes somewhere

In the base game some heat simply disappears: a building's power draw, exhaust heat, the Steam
Turbine, generators and the Wheezewort all remove or skip energy without putting it anywhere.
With Mod 1:

- A building's power draw becomes heat in the building's own structure, which then conducts into
  the room around it.
- Exhaust heat, the Steam Turbine's heat, generator heat and the Wheezewort's cooling are
  delivered to a real destination instead of being destroyed.
- Cooling therefore becomes a question of where the heat goes. A cold room needs a warm room
  somewhere else, and the colony eventually needs a heat sink.

The rules live in the framework (`PowerHeat`, `ExhaustHeat`, `TurbineHeat`, `GeneratorEnthalpy`,
`ColdBreatherHeat`); this mod switches them on.

### Air is a mixture

In the base game a cell holds exactly one element, so "air" can only be pure oxygen, and a gas
added to a room displaces the oxygen instead of diluting it. Mod 1 keeps a room's air in a
mixture layer where several gases share each cell, each with its own mass, all adding to the
cell's pressure. Breathable air of about 75% nitrogen and 25% oxygen, blended by the Gas Mixer
from Mod 2's nitrogen, is a normal thing to build.

Air moves the way real air does: the simulation carries each gas down its own partial-pressure
gradient between neighbouring cells. An Air Intake at one end of a room and an Air Diffuser at
the other create a real draft through it.

### Duplicants breathe air, not oxygen

Breathing is graded by oxygen partial pressure, which is what decides whether a lung works: a
100 kPa room at 25% oxygen is the same breath as a 25 kPa room of pure oxygen.

| oxygen partial pressure | effect |
|---|---|
| 16 kPa or more | normal breathing |
| 12 to 16 kPa | low oxygen: the game's own warning |
| 5 to 12 kPa | critical: a new warning and effect the base game does not have |
| below 5 kPa | suffocation |

Two further rules have no counterpart in the base game:

- **Air temperature.** Air between 0 °C and 50 °C is comfortable. Outside that range the
  duplicant is warned, and air below −10 °C or above 50 °C damages the lungs.
- **Carbon dioxide is a poison**, not only an absence of oxygen. Above 0.5% it causes a headache,
  above 1% it impairs, and above 7% it suffocates a duplicant even in a room with plenty of
  oxygen. A sealed room with an oxygen supply but no scrubbing is no longer enough.

Critters are not affected: in the base game no critter needs oxygen.

### Pipes have pressure

A base-game pipe tile holds a mass and a temperature. Mod 1 gives each pipe network a volume, a
pressure (from the ideal gas law for gases, from the fill for liquids) and a phase:

- **A Stationeers-style tooltip** on a pipe shows the network's pressure, temperature, volume
  and liquid volume, one line per gas with its share and amount, and any stress warning.
- **Stress.** Past 80% of a pipe's pressure rating the network raises a minor warning; past the
  rating it raises an alarm and starts taking damage at its weakest tile, which the notification
  jumps the camera to. Liquid in a gas pipe and frozen contents stress a pipe too. An
  overpressure burst vents the line into the room.
- **Condensation and freezing.** Gas that condenses inside a pipe stays there as liquid instead
  of being deleted, and a line can freeze solid. Both release or absorb their latent heat.

### Gas dissolves in liquid

Every liquid cell can carry dissolved gas, and the simulation moves it with the liquid:

- Dissolved gas travels with flowing water, through pumps and pipes, into reservoirs and bottles.
  When water is spilled, taken out or boiled off, its share of the gas comes back out.
- Gas that comes out of solution, or is released under water, forms **bubbles** that rise at the
  speed their size gives them, grow as the water pressure above them drops, and give their heat
  to the water they pass through.
- The **Gas Vent** works under water: it holds its gas until the pipe pressure exceeds the
  water's pressure at its depth, then releases it as bubbles.
- **Carbonated water** is a product: bubble carbon dioxide through cold water under pressure,
  and a **Soda Fountain** serves it. Each drink holds a real 40 g of carbon dioxide, which the
  drinker breathes back into the room, instead of the 1 kg per drink the base game deletes.
- Water **looks** like what it carries: a liquid's colour shifts towards the colour of its
  dissolved gas.

### Refrigeration

Phase change costs and releases latent heat (the values come from Mod 2's material
properties). That makes a vapour-compression cycle work the way it does in reality:

1. In the cold room, a working fluid boils in an **Evaporation Chamber** or a low-pressure line,
   taking heat from the room.
2. A **Volume Pump** or **Purge Valve** moves the vapour to the high-pressure side.
3. In the hot room, the vapour condenses in a **Condensation Chamber** or a high-pressure line,
   releasing that heat plus the work that compressed it.
4. A **Condensation Valve** returns the liquid to the cold side.

Only the working fluid crosses between the rooms, and every joule is accounted for.

### Overlays

Two overlays are added to the overlay menu:

- **Dissolved Gas** tints every liquid cell by what is dissolved in it: the hue is the mixture of
  dissolved gases, the strength is the concentration in grams per kilogram.
- **Gas Mixture** tints every gas cell by its composition. The colours blend the way Stationeers
  blends an atmosphere's colour, each gas's colour weighted by its mole fraction.

Each overlay's legend lists every gas in view with its share, and the hover card of the cell
under the cursor gives the exact breakdown.

## Buildings

New buildings:

| building | does |
|---|---|
| Gas Mixer | blends two gas pipe networks into one at a ratio you set, modelled on Stationeers' Gas Mixer; this is how breathable nitrogen-oxygen air is made |
| Volume Pump | a positive-displacement pipe-to-pipe gas pump (up to 100 L/s, 240 W at full rate): the pressure boundary between the two sides of a refrigeration loop |
| Condensation Valve | drains condensed liquid out of a gas line into a liquid line |
| Purge Valve | pulls boil-off gas out of a liquid line above a pressure you set; a powered 100 W device that bills its compression work into the gas it moves |
| Evaporation Chamber, Condensation Chamber | the two vessels of a phase-change cooling loop, modelled on Stationeers' phase chambers |
| Gas and Liquid Conduit Radiators, and Radiator Panels | reject a pipe's heat into the room around them |
| Air Intake, Air Diffuser | draw a room's air into a pipe and put air back into a room, so air can be moved, mixed and treated, and a draft created |
| Carbon Scrubber | takes the carbon dioxide out of piped air |
| Inline Carbon Skimmer | the base game's Carbon Skimmer with its intake on a pipe instead of the open room |
| Dissolved Gas Sensor | reads the dissolved gas in the liquid at its cell, in grams per kilogram, for a gas you choose, and switches an automation wire on a threshold |

Base-game buildings it augments rather than replaces:

- **Gas Pump and Gas Reservoir** become a compressor and tank pair: real pressure (PV = nRT),
  compression heat, conduction to the surrounding cell, and gradual phase change paid for out
  of the tank's own heat capacity. **Liquid Pump and Liquid Reservoir** are the liquid
  counterpart, tracked by volume.
- **Gas Vent** works under liquid, releasing its gas as bubbles.
- **Gas Element Sensor and Gas Vent** move mass between the ordinary atmosphere and the gas
  mixture.
- **Soda Fountain** serves carbonated water and keeps the carbon dioxide it used to delete.

## Launch options

`--oni-debug-inspector` starts the framework's HTTP debug server on port 9788. It has no
authentication and listens on every network interface: use it only on a machine you trust,
for development.

## Building and installing

See the repository's README.

## Known limitations

- Disease is not carried through pipe equalisation.
- Pipe stress is checked only on pipe networks that Mod 1's pumps, tanks and valves have
  exchanged with. A network that no Mod 1 building has touched is not monitored.
