//
// Copyright (c) 2010-2018 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Timers;

namespace Antmicro.Renode.Peripherals.IRQControllers
{
    public class CoreLevelInterruptor : IDoubleWordPeripheral, IKnownSize, INumberedGPIOOutput
    {
        public CoreLevelInterruptor(Machine machine, long frequency)
        {
            this.machine = machine;
            this.timerFrequency = frequency;

            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {
                    (long)Registers.MTimeLo, new DoubleWordRegister(this).WithValueField(0, 32, FieldMode.Read,
                                 valueProviderCallback: _ => (uint)mTimers[0].Value,
                                 writeCallback: (_, value) =>
                    {
                        var timerValue = mTimers[0].Value;
                        timerValue &= ~0xffffffffUL;
                        timerValue |= value;
                        foreach(var timer in mTimers.Values)
                        {
                            timer.Value = timerValue;
                        }

                    })
                },
                {
                    (long)Registers.MTimeHi, new DoubleWordRegister(this).WithValueField(0, 32, FieldMode.Read,
                             valueProviderCallback: _ => (uint)(mTimers[0].Value >> 32),
                             writeCallback: (_, value) =>
                    {
                        var timerValue = mTimers[0].Value;
                        timerValue &= 0xffffffffUL;
                        timerValue |= (ulong)value << 32;
                        foreach(var timer in mTimers.Values) 
                        {
                            timer.Value = timerValue;
                        }
                    })
                }
            };

            for(var hart = 0; hart < MaxNumberOfTargets; ++hart)
            {
                var hartId = hart;
                registersMap.Add((long)Registers.MSipHart0 + 4 * hartId, new DoubleWordRegister(this).WithFlag(0, writeCallback: (_, value) => { irqs[2 * hartId].Set(value); }));
                registersMap.Add((long)Registers.MTimeCmpHart0Lo + 8 * hartId, new DoubleWordRegister(this).WithValueField(0, 32, writeCallback: (_, value) =>
                {
                    var limit = mTimers[hartId].Compare;
                    limit &= ~0xffffffffUL;
                    limit |= value;

                    irqs[2 * hartId + 1].Set(false);
                    mTimers[hartId].Compare = limit;
                }));

                registersMap.Add((long)Registers.MTimeCmpHart0Hi + 8 * hartId, new DoubleWordRegister(this).WithValueField(0, 32, writeCallback: (_, value) =>
                {
                    var limit = mTimers[hartId].Compare;
                    limit &= 0xffffffffUL;
                    limit |= (ulong)value << 32;

                    irqs[2 * hartId + 1].Set(false);
                    mTimers[hartId].Compare = limit;
                }));
            }

            registers = new DoubleWordRegisterCollection(this, registersMap);

            Connections = new ReadOnlyDictionary<int, IGPIO>(irqs);
        }

        public void Reset()
        {
            registers.Reset();
            foreach(var irq in irqs.Values)
            {
                irq.Set(false);
            }
            foreach(var timer in mTimers.Values)
            {
                timer.Reset();
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public void RegisterCPU(BaseRiscV cpu)
        {
            var hartId = (int)cpu.HartId;
            if(cpus.ContainsKey(hartId))
            {
                throw new ConstructionException($"CPU with hart id {hartId} already registered in CLINT.");
            }
            if(cpus.ContainsValue(cpu))
            {
                throw new ConstructionException("CPU already registered in CLINT");
            }
            cpus.Add(hartId, cpu);
            irqs[2 * hartId] = new GPIO();
            irqs[2 * hartId + 1] = new GPIO();

            var timer = new ComparingTimer(machine.ClockSource, timerFrequency, enabled: true, eventEnabled: true);
            timer.CompareReached += () => irqs[2 * hartId + 1].Set(true);

            mTimers.Add(hartId, timer);
        }

        public ulong TimerValue => mTimers[0]?.Value ?? 0; // "?." returns "(ulong?)null" instead of "default(ulong)", thus "?? 0"

        public long Size => 0x10000;

        public IReadOnlyDictionary<int, IGPIO> Connections { get; private set; }

        private readonly Machine machine;
        private readonly DoubleWordRegisterCollection registers;
        private readonly Dictionary<int, IGPIO> irqs = new Dictionary<int, IGPIO>();
        private readonly Dictionary<int, BaseRiscV> cpus = new Dictionary<int, BaseRiscV>();
        private readonly Dictionary<int, ComparingTimer> mTimers = new Dictionary<int, ComparingTimer>();
        private readonly long timerFrequency;

        private const int MaxNumberOfTargets = 5;

        private enum Registers : long
        {
            MSipHart0 = 0x0,
            MTimeCmpHart0Lo = 0x4000,
            MTimeCmpHart0Hi = 0x4004,
            MTimeLo = 0xBFF8,
            MTimeHi = 0xBFFC
        }
    }
}
