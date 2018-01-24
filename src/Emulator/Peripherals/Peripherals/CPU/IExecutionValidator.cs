using System;

namespace Antmicro.Renode.Peripherals.CPU
{
    public delegate IntPtr RegisterReader(int regno);
    public delegate UInt32 MemoryReader(IntPtr address);
    public interface IExecutionValidator
    {
	void SetCallbacks(RegisterReader RegReader, MemoryReader MemReader);
	bool Validate(uint PC, uint InstructionBits);
	void Commit();
    }
}
