using System;

using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Peripherals.CPU
{
    public class PassthroughValidator : IExecutionValidator, IEmulationElement
    {
	public void SetCallbacks(RegisterReader RegReader, MemoryReader MemReader) { }
	public bool Validate(uint PC, uint InstructionBits)
	{
//	    this.Log(LogLevel.Info, "Validating 0x{0:x}:  0x{1:x}", PC, InstructionBits);
	    return true;
	}
	public void Commit() { }
    }
}
