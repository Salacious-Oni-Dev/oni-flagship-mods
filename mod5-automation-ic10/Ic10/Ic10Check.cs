using System.Collections.Generic;

namespace Ic10
{
    // Whether a program would compile, without touching the housing it is for: the editor
    // shows this as the player types. Stationeers compiles on submit and stops at the first
    // error; this reports that same first error.
    public sealed class Ic10Check : IIc10Host
    {
        private readonly Ic10Chip _chip;
        private readonly int _sockets;

        public Ic10Check(Ic10EnumSet enums, int sockets)
        {
            _sockets = sockets;
            _chip = new Ic10Chip(enums) { Host = this };
        }

        // Null when the text compiles, else "line N, Type".
        public string FirstError(string source)
        {
            _chip.SetSourceCode(source ?? string.Empty);
            return _chip.CompilationError ? "line " + _chip.CompileErrorLine + ", " + _chip.CompileErrorType : null;
        }

        public IIc10Device GetDeviceFromIndex(int deviceIndex, int networkIndex)
        {
            return null;
        }

        public IIc10Device GetDeviceFromId(int deviceId, int networkIndex)
        {
            return null;
        }

        public IList<IIc10Device> GetBatchDevices()
        {
            return null;
        }

        // Ic10Housing.IsValidDeviceIndex.
        public bool IsValidDeviceIndex(int index)
        {
            return index == Ic10Chip.BaseUnitIndex || (index >= 0 && index < _sockets);
        }

        public void SetDeviceLabel(int index, string label)
        {
        }

        public void RaiseError(int state)
        {
        }

        public void HaltAndCatchFire()
        {
        }

        public void HasPut()
        {
        }
    }
}
