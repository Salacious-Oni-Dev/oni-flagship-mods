using System.Collections.Generic;

namespace Ic10
{
    // What touches what, answered by whoever owns the map (the game, or a test). A cable is
    // an int the owner chooses; in ONI it is a cell.
    public interface IIc10CableMap
    {
        // SmallGrid.FillConnected<Cable>: the cables joined to this one, in a fixed order.
        // It may still name a cable that has been removed; such a cable is skipped.
        void ConnectedCables(int cable, List<int> into);

        // SmallGrid.FillConnected<Device>: the devices attached to this cable, in a fixed
        // order.
        void ConnectedDevices(int cable, List<IIc10Device> into);
    }

    // Which data network each cable belongs to, and which devices are on each network, kept
    // the way Stationeers keeps it: a port of the network bookkeeping in CableNetwork, Cable
    // and Device, with no game in it.
    //
    // This exists because a network's eight channels live exactly as long as the network
    // object does, so when a network object survives an edit and
    // when it is replaced is behaviour a script can see. ONI's own UtilityNetworkManager
    // throws every network object away on any edit, so it cannot be the keeper.
    //
    //   a cable placed touching nothing        a new network, channels NaN
    //   a cable placed touching one network    joins it, channels kept
    //   a cable placed touching several        the first in the cable's connection order
    //                                          survives; the others' channels are lost
    //   a cable removed                        every piece left gets a new network, NaN
    //   a device added or removed              channels kept
    //
    // The order of a network's device list is also Stationeers': newest first (the data list
    // is DeviceList walked backwards, CableNetwork.RefreshPowerAndDataDeviceLists).
    //
    // One thing is not reproduced. Stationeers rebuilds a network's data list lazily, the
    // next time something asks for DataDeviceList, while StackSize and `get` read the list
    // without asking; so there a count can lag an edit. Here the list is rebuilt at once
    // (a deliberate divergence).
    //
    // A bridge is a cable: the map gives it an id of its own and names it among the
    // connections of the cable at each end.
    public sealed class Ic10Cables
    {
        private sealed class Net
        {
            public Ic10Network Network;
            // CableNetwork.CableList.
            public readonly List<int> Cables = new List<int>();
            // CableNetwork.DeviceList, in the order devices joined.
            public readonly List<IIc10Device> Devices = new List<IIc10Device>();
            // CableNetwork.DeviceRegister: the cables of this network a device is attached by.
            public readonly Dictionary<IIc10Device, HashSet<int>> Register = new Dictionary<IIc10Device, HashSet<int>>();
            public bool Dirty;
        }

        private readonly Ic10EnumSet _enums;
        private readonly IIc10CableMap _map;
        // Cable.CableNetwork.
        private readonly Dictionary<int, Net> _owner = new Dictionary<int, Net>();
        // Device.ConnectedCableNetworks, in the order the device joined them.
        private readonly Dictionary<IIc10Device, List<Net>> _connected = new Dictionary<IIc10Device, List<Net>>();
        private readonly List<Net> _dirty = new List<Net>();

        public Ic10Cables(Ic10EnumSet enums, IIc10CableMap map)
        {
            _enums = enums;
            _map = map;
        }

        public int CableCount
        {
            get { return _owner.Count; }
        }

        public bool HasCable(int cable)
        {
            return _owner.ContainsKey(cable);
        }

        // The network a cable is part of, or null where there is no cable.
        public Ic10Network NetworkOf(int cable)
        {
            Net net;
            return _owner.TryGetValue(cable, out net) ? net.Network : null;
        }

        // Device.ConnectedCableNetworks: the networks a device is on, in the order it joined
        // them. Not the numbering `:n` uses, which is by connection point (Device.GetNetwork).
        public void NetworksOf(IIc10Device device, List<Ic10Network> into)
        {
            List<Net> nets;
            if (device == null || !_connected.TryGetValue(device, out nets))
            {
                return;
            }
            for (int i = 0; i < nets.Count; i++)
            {
                into.Add(nets[i].Network);
            }
        }

        // Cable.OnRegistered. The map must already answer for the new cable.
        public void AddCable(int cable)
        {
            if (_owner.ContainsKey(cable))
            {
                return;
            }
            Net net = Merge(ConnectedNetworks(cable));
            if (net == null)
            {
                net = NewNet();
            }
            Add(net, cable);
            Flush();
        }

        // Cable.OnDestroy. `wereConnected` is what the map answered for the cable before it
        // went (Stationeers takes it before the cable is unregistered, in the same order).
        public void RemoveCable(int cable, IList<int> wereConnected)
        {
            Net net;
            if (!_owner.TryGetValue(cable, out net))
            {
                return;
            }
            Remove(net, cable);
            if (wereConnected != null)
            {
                for (int i = 0; i < wereConnected.Count; i++)
                {
                    Rebuild(wereConnected[i]);
                }
            }
            Flush();
        }

        // ONI only: two cables that both stay stop touching (a player cuts the connection
        // between adjacent wires). Stationeers has no such edit. A cut is
        // a removal, so each end is rebuilt as the neighbours of a removed cable are, in the
        // order given. The map must already answer without the link.
        public void CutLink(int first, int second)
        {
            Rebuild(first);
            Rebuild(second);
            Flush();
        }

        // ONI only: two cables that both exist start touching. The same as a cable placed
        // between them whose connections are `first`, then `second`: the first one's network
        // survives.
        public void JoinLink(int first, int second)
        {
            Net keep;
            Net old;
            if (_owner.TryGetValue(first, out keep) && _owner.TryGetValue(second, out old))
            {
                Merge(keep, old);
                Flush();
            }
        }

        // Which network `:n` means for an ONI building: the
        // networks under its cells, each once, in the building's cell order. Stationeers
        // numbers a device's connection points (Device.GetNetwork); an ONI building has none.
        public void NetworksUnder(IList<int> cells, IList<Ic10Network> into)
        {
            if (cells == null)
            {
                return;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                Ic10Network network = NetworkOf(cells[i]);
                if (network != null && !into.Contains(network))
                {
                    into.Add(network);
                }
            }
        }

        // Device.InitializeDevice: a new device joins the network of each cable it touches.
        public void AddDevice(IIc10Device device, IList<int> cables)
        {
            if (device == null || cables == null)
            {
                return;
            }
            for (int i = 0; i < cables.Count; i++)
            {
                Net net;
                if (_owner.TryGetValue(cables[i], out net))
                {
                    AddDevice(net, cables[i], device);
                }
            }
            Flush();
        }

        // Device.OnDeregistered: off every network it was on, last joined first.
        public void RemoveDevice(IIc10Device device)
        {
            List<Net> nets;
            if (device == null || !_connected.TryGetValue(device, out nets))
            {
                return;
            }
            int count = nets.Count;
            while (count-- > 0)
            {
                RemoveDevice(nets[count], device);
            }
            Flush();
        }

        private Net NewNet()
        {
            Net net = new Net();
            net.Network = new Ic10Network(_enums);
            return net;
        }

        private void MarkDirty(Net net)
        {
            if (!net.Dirty)
            {
                net.Dirty = true;
                _dirty.Add(net);
            }
        }

        // CableNetwork.RefreshPowerAndDataDeviceLists, for every network an edit touched.
        private void Flush()
        {
            for (int i = 0; i < _dirty.Count; i++)
            {
                Net net = _dirty[i];
                net.Dirty = false;
                Ic10DeviceList list = net.Network.Devices;
                list.Clear();
                for (int j = net.Devices.Count - 1; j >= 0; j--)
                {
                    list.Add(net.Devices[j]);
                }
            }
            _dirty.Clear();
        }

        // CableNetwork.ConnectedNetworks: each neighbour's network once, in connection order.
        private List<Net> ConnectedNetworks(int cable)
        {
            List<int> near = new List<int>();
            _map.ConnectedCables(cable, near);
            List<Net> nets = new List<Net>(near.Count);
            for (int i = 0; i < near.Count; i++)
            {
                Net net;
                if (near[i] != cable && _owner.TryGetValue(near[i], out net) && !nets.Contains(net))
                {
                    nets.Add(net);
                }
            }
            return nets;
        }

        // CableNetwork.Merge(List<CableNetwork>): the first absorbs the rest.
        private Net Merge(List<Net> nets)
        {
            if (nets.Count <= 0)
            {
                return null;
            }
            Net first = nets[0];
            for (int i = 1; i < nets.Count; i++)
            {
                Merge(first, nets[i]);
            }
            return first;
        }

        // CableNetwork.Merge(CableNetwork): the old network's cables move over, last first.
        private void Merge(Net net, Net old)
        {
            if (old == null || old == net)
            {
                return;
            }
            for (int i = old.Cables.Count - 1; i >= 0; i--)
            {
                Add(net, old.Cables[i]);
            }
            old.Cables.Clear();
            Refresh(old);
            MarkDirty(old);
        }

        // CableNetwork.Add(Cable).
        private void Add(Net net, int cable)
        {
            Net was;
            _owner.TryGetValue(cable, out was);
            if (was == net)
            {
                return;
            }
            if (was != null)
            {
                Remove(was, cable);
            }
            _owner[cable] = net;
            net.Cables.Add(cable);
            List<IIc10Device> devices = new List<IIc10Device>();
            _map.ConnectedDevices(cable, devices);
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] != null)
                {
                    AddDevice(net, cable, devices[i]);
                }
            }
        }

        // CableNetwork.Remove(Cable).
        private void Remove(Net net, int cable)
        {
            Net was;
            if (!_owner.TryGetValue(cable, out was) || was != net)
            {
                return;
            }
            _owner.Remove(cable);
            net.Cables.Remove(cable);
            Refresh(net);
            MarkDirty(net);
        }

        // CableNetwork.RefreshNetwork: a network with no cable left lets go of its devices.
        private void Refresh(Net net)
        {
            if (net.Cables.Count > 0)
            {
                return;
            }
            List<IIc10Device> devices = new List<IIc10Device>(net.Devices);
            for (int i = 0; i < devices.Count; i++)
            {
                RemoveDevice(net, devices[i]);
            }
        }

        // CableNetwork.AddDevice(Cable, Device).
        private void AddDevice(Net net, int cable, IIc10Device device)
        {
            HashSet<int> registration;
            if (!net.Register.TryGetValue(device, out registration))
            {
                registration = new HashSet<int>();
                registration.Add(cable);
                net.Devices.Add(device);
                net.Register.Add(device, registration);
                List<Net> nets;
                if (!_connected.TryGetValue(device, out nets))
                {
                    nets = new List<Net>();
                    _connected.Add(device, nets);
                }
                nets.Add(net);
            }
            else
            {
                registration.Add(cable);
            }
            MarkDirty(net);
        }

        // CableNetwork.RemoveDevice(Device).
        private void RemoveDevice(Net net, IIc10Device device)
        {
            net.Register.Remove(device);
            net.Devices.Remove(device);
            List<Net> nets;
            if (_connected.TryGetValue(device, out nets))
            {
                nets.Remove(net);
                if (nets.Count == 0)
                {
                    _connected.Remove(device);
                }
            }
            MarkDirty(net);
        }

        // CableNetwork.RemoveDevice(Cable, Device): off the network once no cable holds it.
        private void RemoveDevice(Net net, int cable, IIc10Device device)
        {
            HashSet<int> registration;
            if (device == null || !net.Register.TryGetValue(device, out registration))
            {
                return;
            }
            registration.Remove(cable);
            if (registration.Count == 0)
            {
                RemoveDevice(net, device);
            }
        }

        // CableNetwork.RebuildCableNetworkServer and RebuildNetwork: a brand new network for
        // this cable and everything still joined to it, found by flood fill. Called once for
        // each neighbour of a removed cable, so two neighbours still joined by another path
        // are rebuilt twice and end in the network made for the later one.
        private void Rebuild(int cable)
        {
            Net old;
            if (!_owner.TryGetValue(cable, out old))
            {
                return;
            }
            Net fresh = NewNet();
            Add(fresh, cable);

            List<int> near = new List<int>();
            List<IIc10Device> devices = new List<IIc10Device>();
            Queue<int> queue = new Queue<int>();
            HashSet<int> seen = new HashSet<int>();
            seen.Add(cable);
            _map.ConnectedCables(cable, near);
            for (int i = 0; i < near.Count; i++)
            {
                queue.Enqueue(near[i]);
            }
            while (queue.Count > 0)
            {
                int next = queue.Dequeue();
                if (seen.Contains(next) || !_owner.ContainsKey(next))
                {
                    continue;
                }
                seen.Add(next);
                near.Clear();
                _map.ConnectedCables(next, near);
                for (int i = 0; i < near.Count; i++)
                {
                    if (!seen.Contains(near[i]))
                    {
                        queue.Enqueue(near[i]);
                    }
                }
                devices.Clear();
                _map.ConnectedDevices(next, devices);
                for (int i = 0; i < devices.Count; i++)
                {
                    RemoveDevice(old, next, devices[i]);
                }
                Add(fresh, next);
            }
        }
    }
}
