using System;
using System.Collections.Generic;

namespace Ic10
{
    // A device described as a table: logic type to a function that reads it and, for some, a
    // function that writes it. A logic type with no entry is one the device does not have,
    // and the chip reports IncorrectLogicType for it, as Device.CanLogicRead and
    // CanLogicWrite make it do in Stationeers.
    //
    // No slots, no memory, no reagents; a subclass adds those.
    public class Ic10MappedDevice : IIc10Device
    {
        private readonly Dictionary<int, Func<double>> _reads = new Dictionary<int, Func<double>>();
        private readonly Dictionary<int, Action<double>> _writes = new Dictionary<int, Action<double>>();
        private readonly Dictionary<int, Func<bool>> _readable = new Dictionary<int, Func<bool>>();

        public virtual int PrefabHash { get; set; }
        public virtual int NameHash { get; set; }

        // A negative logic type is a name the enum table in use does not have
        // (Ic10EnumTable.ValueOf); mapping it does nothing, so one mapping serves a table
        // with ONI's types and a table without.
        public void Read(int logicType, Func<double> read)
        {
            if (logicType >= 0 && read != null)
            {
                _reads[logicType] = read;
                // A new read replaces the old one whole, condition included.
                _readable.Remove(logicType);
            }
        }

        // A value that is there only while something holds, asked on every read: Stationeers'
        // RequiredPower is readable only on a device whose UsedPower is above zero.
        public void Read(int logicType, Func<double> read, Func<bool> readable)
        {
            if (logicType >= 0 && read != null)
            {
                _reads[logicType] = read;
                if (readable != null)
                {
                    _readable[logicType] = readable;
                }
                else
                {
                    _readable.Remove(logicType);
                }
            }
        }

        public void Write(int logicType, Action<double> write)
        {
            if (logicType >= 0 && write != null)
            {
                _writes[logicType] = write;
            }
        }

        // Takes a logic type away, read and write: the device no longer has it.
        public void Forget(int logicType)
        {
            _reads.Remove(logicType);
            _readable.Remove(logicType);
            _writes.Remove(logicType);
        }

        // What Device.SetLogicValue makes of a number written to an on/off state:
        // (int)Mathf.Clamp((float)value, 0f, 1f). So 1 and above is on, and 0.5 is off.
        // NaN is undefined there (a cast of NaN to int); here it is off.
        public static bool State(double value)
        {
            return (float)value >= 1f;
        }

        public virtual bool CanLogicRead(int logicType)
        {
            if (!_reads.ContainsKey(logicType))
            {
                return false;
            }
            Func<bool> readable;
            return !_readable.TryGetValue(logicType, out readable) || readable();
        }

        public virtual bool CanLogicWrite(int logicType)
        {
            return _writes.ContainsKey(logicType);
        }

        public virtual double GetLogicValue(int logicType)
        {
            Func<double> read;
            return _reads.TryGetValue(logicType, out read) ? read() : 0.0;
        }

        public virtual void SetLogicValue(int logicType, double value)
        {
            Action<double> write;
            if (_writes.TryGetValue(logicType, out write))
            {
                write(value);
            }
        }

        public virtual bool CanSlotRead(int logicSlotType, int slot)
        {
            return false;
        }

        public virtual double GetSlotValue(int logicSlotType, int slot)
        {
            return 0.0;
        }
    }
}
