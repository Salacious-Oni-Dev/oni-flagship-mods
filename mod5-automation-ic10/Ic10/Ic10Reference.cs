using System;
using System.Collections.Generic;
using System.Text;

namespace Ic10
{
    // What each instruction takes and does, for the player: the text of the instruction reference and,
    // later, of the editor's help. The operand shapes are ProgrammableChip.GetCommandExample's;
    // the wording is this mod's own.
    //
    // Operand words:
    //   r      a register to write: r0 to r15, sp, ra, or an alias of one
    //   a b c  a value to read: a register, a number, a define, a constant, HASH("...")
    //   d      a device: d0 to d5, db, an alias, or a register holding a reference id
    //   id     a reference id: a register or a number
    //   type   a logic type by name or number        slotType  a slot logic type
    //   hash   a prefab hash                          name      a name hash
    //   mode   a batch mode: Average, Sum, Minimum, Maximum (0 to 3)
    public static class Ic10Reference
    {
        public sealed class Entry
        {
            public readonly string Name;
            public readonly string Operands;
            public readonly string Group;
            public readonly string Text;

            public Entry(string group, string name, string operands, string text)
            {
                Group = group;
                Name = name;
                Operands = operands;
                Text = text;
            }
        }

        public static readonly string[] Groups =
        {
            "Devices", "Devices by batch", "Device memory", "Slots and reagents", "Values", "Arithmetic", "Trigonometry",
            "Bits", "Comparison", "Selection", "Jumps", "Branches", "Branches, relative", "Branches, and link",
            "Stack", "Names", "Flow",
        };

        public static readonly Entry[] Instructions = Build();

        private static Entry[] Build()
        {
            List<Entry> e = new List<Entry>();
            Action<string, string, string, string> add = (group, name, operands, text) => e.Add(new Entry(group, name, operands, text));

            add("Devices", "l", "r d type", "Read a logic type from a device.");
            add("Devices", "s", "d type a", "Write a value to a logic type of a device.");
            add("Devices", "ld", "r id type", "Read a logic type from the device with that reference id.");
            add("Devices", "sd", "id type a", "Write a logic type of the device with that reference id.");
            add("Devices", "sdse", "r d", "r = 1 if the device is set, else 0.");
            add("Devices", "sdns", "r d", "r = 1 if the device is not set, else 0.");

            add("Devices by batch", "lb", "r hash type mode", "Read a logic type from every device of that prefab on the data network, combined by the batch mode.");
            add("Devices by batch", "lbn", "r hash name type mode", "As lb, for the devices of that prefab that also have that name.");
            add("Devices by batch", "sb", "hash type a", "Write a logic type of every device of that prefab on the data network.");
            add("Devices by batch", "sbn", "hash name type a", "As sb, for the devices that also have that name.");
            add("Devices by batch", "lbs", "r hash slot slotType mode", "Read a slot logic type from every device of that prefab, combined by the batch mode.");
            add("Devices by batch", "lbns", "r hash name slot slotType mode", "As lbs, for the devices that also have that name.");
            add("Devices by batch", "sbs", "hash slot slotType a", "Write a slot logic type of every device of that prefab.");

            add("Device memory", "get", "r d address", "Read a cell of a device's memory: another housing's stack, a machine's instruction stack, a data network's device list.");
            add("Device memory", "getd", "r id address", "As get, by reference id.");
            add("Device memory", "put", "d address a", "Write a cell of a device's memory.");
            add("Device memory", "putd", "id address a", "As put, by reference id.");
            add("Device memory", "clr", "d", "Empty a device's memory.");
            add("Device memory", "clrd", "id", "As clr, by reference id.");

            add("Slots and reagents", "ls", "r d slot slotType", "Read a slot logic type from one slot of a device.");
            add("Slots and reagents", "ss", "d slot slotType a", "Write a slot logic type of one slot of a device.");
            add("Slots and reagents", "lr", "r d reagentMode a", "Read how much of something a device holds. Mode Contents (0) with an element's hash gives kilograms; TotalContents (3) gives all of it.");
            add("Slots and reagents", "rmap", "r d a", "The prefab a reagent hash stands for on that device.");

            add("Values", "move", "r a", "r = a.");
            add("Values", "rand", "r", "r = a random number from 0 up to 1.");
            add("Values", "lerp", "r a b c", "r = a + (b - a) * c, with c held between 0 and 1.");
            add("Values", "clamp", "r a min max", "r = a, held between min and max.");

            add("Arithmetic", "add", "r a b", "r = a + b.");
            add("Arithmetic", "sub", "r a b", "r = a - b.");
            add("Arithmetic", "mul", "r a b", "r = a * b.");
            add("Arithmetic", "div", "r a b", "r = a / b.");
            add("Arithmetic", "mod", "r a b", "r = the remainder of a / b, never negative for a positive b.");
            add("Arithmetic", "pow", "r a b", "r = a to the power b.");
            add("Arithmetic", "sqrt", "r a", "r = the square root of a.");
            add("Arithmetic", "exp", "r a", "r = e to the power a.");
            add("Arithmetic", "log", "r a", "r = the natural logarithm of a.");
            add("Arithmetic", "abs", "r a", "r = a without its sign.");
            add("Arithmetic", "sgn", "r a", "r = -1, 0 or 1 by the sign of a.");
            add("Arithmetic", "round", "r a", "r = a rounded to the nearest whole number, a half going to the even one.");
            add("Arithmetic", "trunc", "r a", "r = a with its fraction cut off.");
            add("Arithmetic", "ceil", "r a", "r = the smallest whole number not below a.");
            add("Arithmetic", "floor", "r a", "r = the largest whole number not above a.");
            add("Arithmetic", "max", "r a b", "r = the larger of a and b.");
            add("Arithmetic", "min", "r a b", "r = the smaller of a and b.");

            add("Trigonometry", "sin", "r a", "r = the sine of a, a in radians.");
            add("Trigonometry", "cos", "r a", "r = the cosine of a.");
            add("Trigonometry", "tan", "r a", "r = the tangent of a.");
            add("Trigonometry", "asin", "r a", "r = the angle whose sine is a, in radians.");
            add("Trigonometry", "acos", "r a", "r = the angle whose cosine is a.");
            add("Trigonometry", "atan", "r a", "r = the angle whose tangent is a.");
            add("Trigonometry", "atan2", "r a b", "r = the angle of the point (b, a).");

            add("Bits", "and", "r a b", "r = a AND b, bit by bit.");
            add("Bits", "or", "r a b", "r = a OR b.");
            add("Bits", "xor", "r a b", "r = a XOR b.");
            add("Bits", "nor", "r a b", "r = NOT (a OR b).");
            add("Bits", "not", "r a", "r = NOT a.");
            add("Bits", "sll", "r a b", "r = a shifted left b bits.");
            add("Bits", "sla", "r a b", "The same as sll.");
            add("Bits", "srl", "r a b", "r = a shifted right b bits, zeros coming in.");
            add("Bits", "sra", "r a b", "r = a shifted right b bits, the sign kept.");
            add("Bits", "rol", "r a b", "r = a rotated left b bits.");
            add("Bits", "ror", "r a b", "r = a rotated right b bits.");
            add("Bits", "ext", "r a start length", "r = length bits of a, from bit start.");
            add("Bits", "ins", "r a start length", "Put the low length bits of a into r at bit start.");

            add("Comparison", "seq", "r a b", "r = 1 if a equals b, else 0.");
            add("Comparison", "sne", "r a b", "r = 1 if a does not equal b.");
            add("Comparison", "slt", "r a b", "r = 1 if a is less than b.");
            add("Comparison", "sgt", "r a b", "r = 1 if a is greater than b.");
            add("Comparison", "sle", "r a b", "r = 1 if a is less than or equal to b.");
            add("Comparison", "sge", "r a b", "r = 1 if a is greater than or equal to b.");
            add("Comparison", "seqz", "r a", "r = 1 if a is 0.");
            add("Comparison", "snez", "r a", "r = 1 if a is not 0.");
            add("Comparison", "sltz", "r a", "r = 1 if a is below 0.");
            add("Comparison", "sgtz", "r a", "r = 1 if a is above 0.");
            add("Comparison", "slez", "r a", "r = 1 if a is 0 or below.");
            add("Comparison", "sgez", "r a", "r = 1 if a is 0 or above.");
            add("Comparison", "sap", "r a b c", "r = 1 if a and b are about equal: within c of the larger of them, as a fraction.");
            add("Comparison", "sna", "r a b c", "r = 1 if a and b are not about equal.");
            add("Comparison", "sapz", "r a b", "r = 1 if a is about 0, by the fraction b.");
            add("Comparison", "snaz", "r a b", "r = 1 if a is not about 0.");
            add("Comparison", "snan", "r a", "r = 1 if a is not a number.");
            add("Comparison", "snanz", "r a", "r = 1 if a is a number.");

            add("Selection", "select", "r a b c", "r = b if a is not 0, else c.");

            add("Jumps", "j", "line", "Go to a line or a label.");
            add("Jumps", "jr", "offset", "Go that many lines on from this one; back if negative.");
            add("Jumps", "jal", "line", "Go to a line or a label and keep the next line's number in ra, to come back with `j ra`.");

            string[][] tests =
            {
                new[] { "eq", "a b", "a equals b" }, new[] { "ne", "a b", "a does not equal b" },
                new[] { "lt", "a b", "a is less than b" }, new[] { "gt", "a b", "a is greater than b" },
                new[] { "le", "a b", "a is less than or equal to b" }, new[] { "ge", "a b", "a is greater than or equal to b" },
                new[] { "eqz", "a", "a is 0" }, new[] { "nez", "a", "a is not 0" },
                new[] { "ltz", "a", "a is below 0" }, new[] { "gtz", "a", "a is above 0" },
                new[] { "lez", "a", "a is 0 or below" }, new[] { "gez", "a", "a is 0 or above" },
                new[] { "ap", "a b c", "a and b are about equal, by the fraction c" }, new[] { "na", "a b c", "a and b are not about equal" },
                new[] { "apz", "a b", "a is about 0, by the fraction b" }, new[] { "naz", "a b", "a is not about 0" },
            };
            foreach (string[] t in tests)
            {
                add("Branches", "b" + t[0], t[1] + " line", "Go to the line if " + t[2] + ".");
                add("Branches, relative", "br" + t[0], t[1] + " offset", "Go that many lines on if " + t[2] + ".");
                add("Branches, and link", "b" + t[0] + "al", t[1] + " line", "Go to the line and keep the way back in ra, if " + t[2] + ".");
            }
            add("Branches", "bnan", "a line", "Go to the line if a is not a number.");
            add("Branches, relative", "brnan", "a offset", "Go that many lines on if a is not a number.");
            add("Branches", "bdse", "d line", "Go to the line if the device is set.");
            add("Branches", "bdns", "d line", "Go to the line if the device is not set.");
            add("Branches, relative", "brdse", "d offset", "Go that many lines on if the device is set.");
            add("Branches, relative", "brdns", "d offset", "Go that many lines on if the device is not set.");
            add("Branches, and link", "bdseal", "d line", "Go to the line and keep the way back in ra, if the device is set.");
            add("Branches, and link", "bdnsal", "d line", "Go to the line and keep the way back in ra, if the device is not set.");
            add("Branches", "bdnvl", "d type line", "Go to the line if the device cannot be read for that logic type.");
            add("Branches", "bdnvs", "d type line", "Go to the line if the device cannot be written for that logic type.");

            add("Stack", "push", "a", "Put a on the stack and move sp up one.");
            add("Stack", "pop", "r", "Move sp down one and take the value there.");
            add("Stack", "peek", "r", "Read the value on top of the stack and leave it there.");
            add("Stack", "poke", "address a", "Write a cell of this chip's own stack.");

            add("Names", "alias", "name r|d", "Give a register or a device a name for the rest of the program.");
            add("Names", "define", "name number", "Give a number a name.");
            add("Names", "label", "d name", "Give a device socket a name that the housing shows.");

            add("Flow", "yield", "", "Stop until the next tick.");
            add("Flow", "sleep", "a", "Stop for that many seconds.");
            add("Flow", "hcf", "", "Halt and catch fire: the program stops.");
            return e.ToArray();
        }

        // A logic type as ONI's buildings answer it: who answers, whether it can be written,
        // and what the number is.
        public sealed class TypeEntry
        {
            public readonly string Name;
            public readonly string Access;
            public readonly string Who;
            public readonly string Text;

            public TypeEntry(string name, string access, string who, string text)
            {
                Name = name;
                Access = access;
                Who = who;
                Text = text;
            }
        }

        public static readonly TypeEntry[] LogicTypes =
        {
            new TypeEntry("PrefabHash", "read", "every building", "The hash of the building's id: HASH(\"LiquidPump\")."),
            new TypeEntry("NameHash", "read", "every building", "The hash of the name the player gave it."),
            new TypeEntry("ReferenceId", "read", "every building", "Its own number, for ld, sd, getd and putd."),
            new TypeEntry("On", "read, write", "a building with an enable button; a logic valve, the Automated Notifier", "1 while it is switched on. On a logic valve or the Automated Notifier it drives the automation input instead: a valve flows while On, the notifier fires as On goes to 1."),
            new TypeEntry("Power", "read", "a building that takes power", "1 while it has power."),
            new TypeEntry("Error", "read", "most buildings", "1 while something keeps it from working."),
            new TypeEntry("Activate", "read", "most buildings", "1 while it is working. On a machine with recipes: 1 while the selected recipe is on order, and written, 1 orders it without end and 0 cancels. On an IC Button, written 1 presses it."),
            new TypeEntry("RequiredPower", "read", "a building that takes power", "The watts it is rated to draw; 0 switched off, -1 with no wire."),
            new TypeEntry("PowerDraw", "read", "a building that takes power", "ONI only. The watts it is drawing at this moment."),
            new TypeEntry("PowerGeneration", "read", "a generator", "The watts it is making at this moment."),
            new TypeEntry("PowerActual", "read", "a battery, a wattage sensor", "The watts the wire's consumers are drawing."),
            new TypeEntry("PowerPotential", "read", "a battery, a wattage sensor", "The watts the wire's generators are making."),
            new TypeEntry("PowerRequired", "read", "a wattage sensor", "The watts the wire's consumers are rated to draw."),
            new TypeEntry("Charge", "read", "a battery, a solar panel", "A battery: what it holds, in watts over a half-second tick (joules times two). A solar panel: the watts it makes now."),
            new TypeEntry("Maximum", "read", "anything with a Setting, a battery, a solar panel, a transformer", "The top of the range Setting or Charge moves in."),
            new TypeEntry("Ratio", "read", "the same", "Setting or Charge over Maximum."),
            new TypeEntry("Setting", "read, write", "anything with one number to set", "The building's slider, threshold or capacity, in the unit its side screen shows: a valve's grams per second, a transformer's watt limit, a sensor's threshold, a bin's capacity, a signal counter's top count, an IC Memory's number. A door: what the last press left, 1 asking it to open, 0 to close (a duplicant arriving is the press; in Logic mode writing Open sets it too)."),
            new TypeEntry("Color", "read, write", "the IC Display", "The colour its text is drawn in: Color.Blue 0 to Color.Purple 11. A new display is green (2)."),
            new TypeEntry("Mode", "read, write", "a sensor; a building with a few options", "A sensor: 1 sends its signal above the threshold, 0 below. Otherwise the number of the chosen option, from 0 in the side screen's order. An IC Display: the form the number is shown in. An IC Dial: the top of its range. A door: 0 Operate (opens for duplicants), 1 Logic (a duplicant arriving sets Setting and waits until a program opens it)."),
            new TypeEntry("Reading", "read", "a sensor", "ONI only. What the sensor measures, in its own unit."),
            new TypeEntry("Temperature", "read", "a temperature sensor, an atmo or pipe sensor, a building that holds gas or liquid", "Kelvin: of what the sensor watches, or of the gas and liquid the building holds."),
            new TypeEntry("BodyTemperature", "read", "every building", "ONI only. The building's own temperature, kelvin."),
            new TypeEntry("Pressure", "read", "an atmo, thermo or element sensor; a pipe sensor", "Kilopascals. Needs the custom SimDLL."),
            new TypeEntry("TotalMoles", "read", "the same, and a building that holds gas or liquid", "Moles of everything there."),
            new TypeEntry("RatioOxygen", "read", "the same", "The share of oxygen by moles, 0 to 1. Likewise RatioCarbonDioxide, RatioHydrogen, RatioWater, RatioSteam, RatioMethane, RatioPollutedWater and the rest of Stationeers' list."),
            new TypeEntry("Volume", "read", "a pipe sensor", "Litres of the pipe network."),
            new TypeEntry("VolumeOfLiquid", "read", "a cell or pipe sensor", "Litres of liquid there."),
            new TypeEntry("NetworkFault", "read", "a pipe sensor", "1 while the pipe network is under stress."),
            new TypeEntry("Quantity", "read, write", "a building with storage; a meter valve; a signal counter", "Kilograms stored. A meter valve: what has passed, and writing 0 resets it. A signal counter: its count."),
            new TypeEntry("Open", "read, write", "a door, a switch, the Object Dispenser", "A door: 1 asks it to stand open. A switch: its lever. The Object Dispenser: drives its automation input, 1 opens it."),
            new TypeEntry("Lock", "read, write", "a door", "1 locks it. A locked door stays locked when Open is written."),
            new TypeEntry("OpenNow", "read", "a door", "ONI only. 1 while the door stands open at this moment, whoever opened it."),
            new TypeEntry("Filter", "read, write", "a filter, an element sensor", "ONI only. The hash of the element it lets through or looks for; 0 for none."),
            new TypeEntry("ActivateAt", "read, write", "a smart battery, a reservoir", "ONI only. The percentage at which its signal comes on."),
            new TypeEntry("DeactivateAt", "read, write", "the same", "ONI only. The percentage at which its signal goes off."),
            new TypeEntry("RecipeHash", "read, write", "a machine with recipes", "The hash of the product being made, or of the selected recipe's product. Written, it selects the recipe for that product and starts nothing."),
            new TypeEntry("CompletionRatio", "read", "a machine with recipes; a research station", "How far along the item or the research in hand is, 0 to 1 in hundredths."),
            new TypeEntry("StackSize", "read", "a machine with recipes; a data network; the IC Sorter; a building with an item checklist", "The cells of its memory: 64 on a machine, 32 on a sorter or a checklist."),
            new TypeEntry("PressureInput", "read", "a building with a gas or liquid pipe port", "Kilopascals of the pipe at its input. Likewise TemperatureInput, TotalMolesInput and the Ratio...Input types; Output for its output pipe; Input2 and Output2 for a second port. 0 while no pipe is there."),
            new TypeEntry("Harvest", "write", "a planter", "Above 0, asks for the ripe plant to be harvested."),
            new TypeEntry("AutoHarvest", "read, write", "a planter", "ONI only. 1 while the plant is harvested as soon as it is ripe."),
            new TypeEntry("VentState", "read", "a gas or liquid vent", "ONI only. 0 invalid, 1 venting normally, 2 blocked, 3 over pressure, 4 closed."),
            new TypeEntry("ConsumedRate", "read", "a converter (electrolyzer, polymer press, oil refinery, algae terrarium and the rest)", "ONI only. Grams per second it is taking in, averaged as its status line is, all inputs added."),
            new TypeEntry("ProducedRate", "read", "the same", "ONI only. Grams per second it is giving out, all outputs added."),
            new TypeEntry("FlushesLeft", "read", "an outhouse", "ONI only. Uses left before it must be emptied."),
            new TypeEntry("Cycle", "read", "the IC Housing, a cycle sensor", "ONI only. The cycle number the HUD shows, from 1."),
            new TypeEntry("CycleTime", "read", "the same", "ONI only. Seconds into the cycle, 0 to 600."),
            new TypeEntry("Night", "read", "the same", "ONI only. 1 at night (the last eighth of the cycle)."),
            new TypeEntry("OnDuration", "read, write", "a timer sensor", "ONI only. Seconds its output stays green, 0 to 6000 (ten cycles), as its side screen sets."),
            new TypeEntry("OffDuration", "read, write", "the same", "ONI only. Seconds its output stays red, 0 to 6000."),
            new TypeEntry("WindowStart", "read, write", "a cycle sensor", "ONI only. Where in the cycle its green window starts, 0 to 1 (0.5 is halfway)."),
            new TypeEntry("WindowLength", "read, write", "the same", "ONI only. How long the window lasts, 0 to 1 of a cycle."),
            new TypeEntry("FlightState", "read", "any module of a Spaced Out rocket; a launch pad", "ONI only. 0 grounded, 1 launching, 2 in flight, 3 landing. An empty pad reads 0 here and below."),
            new TypeEntry("TimeToDestination", "read", "the same", "Seconds of game time until it arrives; 0 while not travelling."),
            new TypeEntry("FuelRemaining", "read", "the same", "ONI only. Kilograms of fuel on board."),
            new TypeEntry("OxidizerRemaining", "read", "the same", "ONI only. Kilograms of oxidizer on board, counted as the game counts its strength."),
            new TypeEntry("RocketRange", "read", "the same", "ONI only. How many hexes it can fly on what it carries."),
            new TypeEntry("ReadyToLaunch", "read", "the same", "ONI only. 1 while every launch check passes."),
            new TypeEntry("LogicInput", "read, write", "a building with an automation input", "ONI only, to be retired. The value on its first automation input. Written, a chip drives that input as an automation wire would (0 or 1; 0 to 15 on a ribbon input), but only while no wire is attached: a wire wins. The value is kept, and saved, until written again."),
            new TypeEntry("LogicOutput", "read", "a building with an automation output", "ONI only, to be retired. The value on its first automation output."),
        };

        public static readonly TypeEntry[] SlotTypes =
        {
            new TypeEntry("Occupied", "read", "a building with storage; a planter", "1 if the slot holds something."),
            new TypeEntry("OccupantHash", "read", "the same", "The hash of what is in the slot."),
            new TypeEntry("PrefabHash", "read", "the same", "The same as OccupantHash."),
            new TypeEntry("Quantity", "read", "the same", "Kilograms in the slot; 1 for a plant."),
            new TypeEntry("Temperature", "read", "a building with storage", "Kelvin of what is in the slot."),
            new TypeEntry("TotalSlots", "read", "the same", "How many slots are in use."),
            new TypeEntry("Mature", "read", "a planter, slot 0", "1 if the plant can be harvested, 0 if not, -1 with no plant."),
            new TypeEntry("MaturityRatio", "read", "a planter, slot 0", "How far the plant has grown, 0 to 1; -1 with no plant."),
            new TypeEntry("Health", "read", "a planter, slot 0", "0 while the plant is wilting, else 1; -1 with no plant."),
        };

        public static string TypesMarkdown(TypeEntry[] entries)
        {
            StringBuilder text = new StringBuilder("| Name | Access | Answered by | Meaning |\n|---|---|---|---|\n");
            foreach (TypeEntry entry in entries)
            {
                text.Append("| `").Append(entry.Name).Append("` | ").Append(entry.Access).Append(" | ").Append(entry.Who)
                    .Append(" | ").Append(entry.Text).Append(" |\n");
            }
            return text.ToString();
        }

        public static Entry Find(string mnemonic)
        {
            for (int i = 0; i < Instructions.Length; i++)
            {
                if (Instructions[i].Name == mnemonic)
                {
                    return Instructions[i];
                }
            }
            return null;
        }

        // The reference line for the instruction a line of source starts with, or null: for
        // the editor, under the line being typed. A comment, a label and a blank line have
        // none.
        public static string HelpFor(string sourceLine)
        {
            if (sourceLine == null)
            {
                return null;
            }
            int comment = sourceLine.IndexOf('#');
            if (comment >= 0)
            {
                sourceLine = sourceLine.Substring(0, comment);
            }
            sourceLine = sourceLine.Trim();
            if (sourceLine.Length == 0)
            {
                return null;
            }
            int space = sourceLine.IndexOfAny(new[] { ' ', '\t' });
            Entry entry = Find(space < 0 ? sourceLine : sourceLine.Substring(0, space));
            if (entry == null)
            {
                return null;
            }
            return entry.Name + (entry.Operands.Length > 0 ? " " + entry.Operands : string.Empty) + "    " + entry.Text;
        }

        // The line of a text that holds the character at that position, 0 for the first.
        public static string LineAt(string text, int position)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            if (position < 0)
            {
                position = 0;
            }
            if (position > text.Length)
            {
                position = text.Length;
            }
            int start = position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
            int end = text.IndexOf('\n', position);
            return text.Substring(start, (end < 0 ? text.Length : end) - start);
        }

        // The instruction part of the reference.
        public static string InstructionsMarkdown()
        {
            StringBuilder text = new StringBuilder();
            foreach (string group in Groups)
            {
                text.Append("### ").Append(group).Append("\n\n| Instruction | Does |\n|---|---|\n");
                foreach (Entry entry in Instructions)
                {
                    if (entry.Group == group)
                    {
                        text.Append("| `").Append(entry.Name);
                        if (entry.Operands.Length > 0)
                        {
                            text.Append(' ').Append(entry.Operands.Replace("|", "\\|"));
                        }
                        text.Append("` | ").Append(entry.Text).Append(" |\n");
                    }
                }
                text.Append('\n');
            }
            return text.ToString();
        }
    }
}
