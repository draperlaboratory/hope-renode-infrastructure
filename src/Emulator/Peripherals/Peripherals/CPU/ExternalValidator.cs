using System;
using System.Runtime.InteropServices;

using Antmicro.Renode.Logging;
using Antmicro.Renode.Utilities.Binding;

namespace Antmicro.Renode.Peripherals.CPU
{
    public class ExternalValidator : IExecutionValidator, IEmulationElement
    {
	public ExternalValidator(string shared_lib_name)
	{
	    binder = new NativeBinder(this, shared_lib_name);
	}

	public void SetCallbacks(RegisterReader RegReader, MemoryReader MemReader)
	{
	    EVSetCallbacks(RegReader, MemReader);
	}

	public bool Validate(uint PC, uint InstructionBits)
	{
	    return EVValidate(PC, InstructionBits) != 0;
	}

	public void Commit()
	{
	    EVCommit();
	}

	private NativeBinder binder;
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void ActionSetCallbacks(RegisterReader param0, MemoryReader param1);

        // 649:  Field '...' is never assigned to, and will always have its default value null
        #pragma warning disable 649
	[Import]
	private ActionSetCallbacks EVSetCallbacks;
	[Import]
	private FuncUInt32UInt32UInt32 EVValidate;
	[Import]
	private Action EVCommit;
    }
}
