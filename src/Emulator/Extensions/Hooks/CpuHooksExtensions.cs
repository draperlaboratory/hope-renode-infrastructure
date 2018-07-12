//
// Copyright (c) 2010-2018 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Core;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Hooks
{
    public static class CpuHooksExtensions
    {
        public static void SetHookAtBlockBegin(this ICPUWithHooks cpu, [AutoParameter]Machine m, string pythonScript)
        {
            var engine = new BlockPythonEngine(m, cpu, pythonScript);
            cpu.SetHookAtBlockBegin(engine.HookWithSize);
        }

        public static void AddHook(this ICPUWithHooks cpu, [AutoParameter]Machine m, ulong addr, string pythonScript)
        {
            var engine = new BlockPythonEngine(m, cpu, pythonScript);
            cpu.AddHook(addr, engine.Hook);
        }
    }
}

