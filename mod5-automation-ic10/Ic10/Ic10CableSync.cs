using System.Collections.Generic;

namespace Ic10
{
    // What the cables look like right now, asked of whoever owns the grid (the game, or a
    // test).
    public interface IIc10CableSource
    {
        // Every cable, lowest id first.
        void Cables(List<int> into);

        // The cables joined to this one, in the owner's fixed order (in ONI: left, right,
        // below, above, then a bridge). A link is named from both of its ends.
        void Links(int cable, List<int> into);

        // The devices attached to this cable, in a fixed order.
        void Devices(int cable, List<IIc10Device> into);
    }

    // Turns "the cables as they are now" into the edits Ic10Cables is told about.
    //
    // Stationeers tells its networks about one cable at a time, as each is placed or
    // destroyed. ONI does not: a wire edit only marks its UtilityNetworkManager dirty, and
    // the manager rebuilds every network of that kind on the next frame, whatever changed.
    // Several edits can land in one frame (a row of cable finished together, a load). So the
    // game side hands over the whole picture after each rebuild, and this class finds what
    // changed since the last picture and applies it in one fixed order:
    //
    //   1. cables that have gone, lowest id first          Ic10Cables.RemoveCable
    //   2. links that have gone between cables that stay   Ic10Cables.CutLink
    //   3. cables that are new, lowest id first            Ic10Cables.AddCable
    //   4. links that are new between cables that stayed   Ic10Cables.JoinLink
    //
    // A network no edit touched is not mentioned, so it keeps its object and its channels.
    // After a load every cable is new, and they are added in id
    // order.
    public sealed class Ic10CableSync : IIc10CableMap
    {
        private static readonly int[] NoLinks = new int[0];

        public readonly Ic10Cables Model;

        private readonly IIc10CableSource _source;
        private Dictionary<int, int[]> _links = new Dictionary<int, int[]>();
        private readonly List<int> _scratch = new List<int>();

        public Ic10CableSync(Ic10EnumSet enums, IIc10CableSource source)
        {
            _source = source;
            Model = new Ic10Cables(enums, this);
        }

        // How many edits the last Sync applied, for the log and the tests.
        public int Removed { get; private set; }
        public int Cut { get; private set; }
        public int Added { get; private set; }
        public int Joined { get; private set; }

        // Read the source and apply what changed. Returns the number of edits.
        public int Sync()
        {
            Dictionary<int, int[]> old = _links;
            Dictionary<int, int[]> now = new Dictionary<int, int[]>();
            List<int> cables = new List<int>();
            _source.Cables(cables);
            for (int i = 0; i < cables.Count; i++)
            {
                now[cables[i]] = NoLinks;
            }
            for (int i = 0; i < cables.Count; i++)
            {
                _scratch.Clear();
                _source.Links(cables[i], _scratch);
                // A link to something that is not a cable is no link.
                _scratch.RemoveAll(other => other == cables[i] || !now.ContainsKey(other));
                if (_scratch.Count > 0)
                {
                    now[cables[i]] = _scratch.ToArray();
                }
            }

            // The model asks the map while it works, and must be answered with the new picture.
            _links = now;
            Removed = Cut = Added = Joined = 0;

            List<int> before = new List<int>(old.Keys);
            before.Sort();
            for (int i = 0; i < before.Count; i++)
            {
                if (!now.ContainsKey(before[i]))
                {
                    Model.RemoveCable(before[i], old[before[i]]);
                    Removed++;
                }
            }
            for (int i = 0; i < before.Count; i++)
            {
                int cable = before[i];
                int[] nowLinks;
                if (!now.TryGetValue(cable, out nowLinks))
                {
                    continue;
                }
                int[] oldLinks = old[cable];
                for (int j = 0; j < oldLinks.Length; j++)
                {
                    int other = oldLinks[j];
                    if (other > cable && now.ContainsKey(other) && System.Array.IndexOf(nowLinks, other) < 0)
                    {
                        Model.CutLink(cable, other);
                        Cut++;
                    }
                }
            }
            for (int i = 0; i < cables.Count; i++)
            {
                if (!old.ContainsKey(cables[i]))
                {
                    Model.AddCable(cables[i]);
                    Added++;
                }
            }
            for (int i = 0; i < cables.Count; i++)
            {
                int cable = cables[i];
                int[] oldLinks;
                if (!old.TryGetValue(cable, out oldLinks))
                {
                    continue;
                }
                int[] nowLinks = now[cable];
                for (int j = 0; j < nowLinks.Length; j++)
                {
                    int other = nowLinks[j];
                    if (other > cable && old.ContainsKey(other) && System.Array.IndexOf(oldLinks, other) < 0)
                    {
                        Model.JoinLink(cable, other);
                        Joined++;
                    }
                }
            }
            return Removed + Cut + Added + Joined;
        }

        public void ConnectedCables(int cable, List<int> into)
        {
            int[] links;
            if (_links.TryGetValue(cable, out links))
            {
                into.AddRange(links);
            }
        }

        public void ConnectedDevices(int cable, List<IIc10Device> into)
        {
            _source.Devices(cable, into);
        }
    }
}
