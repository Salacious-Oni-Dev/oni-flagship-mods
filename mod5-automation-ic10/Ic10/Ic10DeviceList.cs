using System.Collections;
using System.Collections.Generic;

namespace Ic10
{
    // The devices on a housing's data network: Stationeers' CableNetwork.DataDeviceList.
    //
    // A list, because the batch instructions walk it in order, and beside it a count per
    // device, because every device access asks "is this device on the network" and
    // List.Contains is a linear search.
    //
    // Contains answers exactly what List<IIc10Device>.Contains answers, including for a null
    // entry and for a device that was added twice: the same default equality, only looked up
    // instead of searched.
    public sealed class Ic10DeviceList : IList<IIc10Device>
    {
        private readonly List<IIc10Device> _items = new List<IIc10Device>();
        private readonly Dictionary<IIc10Device, int> _counts = new Dictionary<IIc10Device, int>();
        private int _nulls;

        public Ic10DeviceList()
        {
        }

        public Ic10DeviceList(IEnumerable<IIc10Device> devices)
        {
            foreach (IIc10Device device in devices)
            {
                Add(device);
            }
        }

        public int Count
        {
            get { return _items.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public IIc10Device this[int index]
        {
            get { return _items[index]; }
            set
            {
                Forget(_items[index]);
                _items[index] = value;
                Note(value);
            }
        }

        public void Add(IIc10Device device)
        {
            _items.Add(device);
            Note(device);
        }

        public void Insert(int index, IIc10Device device)
        {
            _items.Insert(index, device);
            Note(device);
        }

        public bool Remove(IIc10Device device)
        {
            if (!_items.Remove(device))
            {
                return false;
            }
            Forget(device);
            return true;
        }

        public void RemoveAt(int index)
        {
            IIc10Device device = _items[index];
            _items.RemoveAt(index);
            Forget(device);
        }

        public void Clear()
        {
            _items.Clear();
            _counts.Clear();
            _nulls = 0;
        }

        public bool Contains(IIc10Device device)
        {
            return device == null ? _nulls > 0 : _counts.ContainsKey(device);
        }

        public int IndexOf(IIc10Device device)
        {
            return _items.IndexOf(device);
        }

        public void CopyTo(IIc10Device[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        public IEnumerator<IIc10Device> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        private void Note(IIc10Device device)
        {
            if (device == null)
            {
                _nulls++;
                return;
            }
            int count;
            _counts.TryGetValue(device, out count);
            _counts[device] = count + 1;
        }

        private void Forget(IIc10Device device)
        {
            if (device == null)
            {
                _nulls--;
                return;
            }
            int count = _counts[device];
            if (count == 1)
            {
                _counts.Remove(device);
            }
            else
            {
                _counts[device] = count - 1;
            }
        }
    }
}
