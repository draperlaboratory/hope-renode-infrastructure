//
// Copyright (c) 2010-2018 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;

namespace Antmicro.Renode.Utilities
{
    // it's not possible to limit generic type parameter to enum in C# directly, so we verify it in the constructor
    public class InterruptManager<TInterrupt> where TInterrupt : struct, IConvertible
    {
        public InterruptManager(IPeripheral master)
        {
            if(!typeof(TInterrupt).IsEnum)
            {
                throw new ArgumentException("TInterrupt must be an enum");
            }

            this.master = master;
            activeInterrupts = new HashSet<TInterrupt>();
            enabledInterrupts = new HashSet<TInterrupt>();
            subvectors = new Dictionary<IGPIO, HashSet<TInterrupt>>();
            gpioNames = new Dictionary<IGPIO, string>();
            nonsettableInterrupts = new HashSet<TInterrupt>();

            var subvectorIdToGpio = new Dictionary<int, IGPIO>();

            // scan for irq providers
            foreach(var member in master.GetType().GetProperties())
            {
                var irqProviderAttribute = (IrqProviderAttribute)member.GetCustomAttributes(typeof(IrqProviderAttribute), false).SingleOrDefault();
                if(irqProviderAttribute == null)
                {
                    continue;
                }

                var gpioField = member.GetMethod.Invoke(master, new object[0]) as IGPIO;
                if(gpioField == null)
                {
                    throw new ArgumentException("IrqProviderAttribute can only be used on properties of type IGPIO.");
                }
                subvectorIdToGpio.Add(irqProviderAttribute.SubvectorId, gpioField);
                gpioNames[gpioField] = irqProviderAttribute.Name ?? member.Name;
            }

            // this iterates over all values of an enum (SpecialName here is to filter out non-value members of enum type)
            foreach(var member in typeof(TInterrupt).GetFields().Where(x => !x.Attributes.HasFlag(FieldAttributes.SpecialName)))
            {
                var subvectorId = 0;
                var subvectorAttribute = member.GetCustomAttributes(false).OfType<SubvectorAttribute>().SingleOrDefault();
                var nonsettableAttribute = member.GetCustomAttributes(false).OfType<NotSettableAttribute>().SingleOrDefault();

                if(subvectorAttribute != null)
                {
                    if(!subvectorIdToGpio.ContainsKey(subvectorAttribute.SubvectorId))
                    {
                        throw new ArgumentException(string.Format("There is no gpio defined for subvector {0}", subvectorAttribute.SubvectorId));
                    }
                    subvectorId = subvectorAttribute.SubvectorId;
                }
                else
                {
                    if(!subvectorIdToGpio.ContainsKey(-1))
                    {
                        throw new ArgumentException("There is no default gpio defined");
                    }
                    subvectorId = -1;
                }

                var gpio = subvectorIdToGpio[subvectorId];
                if(!subvectors.TryGetValue(gpio, out HashSet<TInterrupt> interrupts))
                {
                    interrupts = new HashSet<TInterrupt>();
                    subvectors.Add(gpio, interrupts);
                }

                var interrupt = (TInterrupt)Enum.Parse(typeof(TInterrupt), member.Name);
                interrupts.Add(interrupt);
                if(nonsettableAttribute != null)
                {
                    nonsettableInterrupts.Add(interrupt);
                }
            }
        }

        public void Reset()
        {
            activeInterrupts.Clear();
            enabledInterrupts.Clear();
            RefreshInterrupts();
        }

        public TRegister GetRegister<TRegister>(Func<TInterrupt, bool, bool> valueProviderCallback = null, Action<TInterrupt, bool, bool> writeCallback = null) where TRegister : PeripheralRegister
        {
            var mode = default(FieldMode);
            if(valueProviderCallback != null)
            {
                mode |= FieldMode.Read;
            }
            if(writeCallback != null)
            {
                mode |= FieldMode.Write;
            }

            var result = CreateRegister<TRegister>();
            foreach(TInterrupt interruptType in Enum.GetValues(typeof(TInterrupt)))
            {
                var local = interruptType;
                result.DefineFlagField((int)(object)interruptType, mode, name: interruptType.ToString(),
                    valueProviderCallback: valueProviderCallback == null ? null : (Func<bool, bool>)(oldValue => valueProviderCallback(local, oldValue)),
                    writeCallback: writeCallback == null ? null : (Action<bool, bool>)((oldValue, newValue) => writeCallback(local, oldValue, newValue)));
            }
            return result;
        }

        public TRegister GetInterruptFlagRegister<TRegister>() where TRegister : PeripheralRegister
        {
            var result = CreateRegister<TRegister>();
            foreach(TInterrupt interruptType in Enum.GetValues(typeof(TInterrupt)))
            {
                var local = interruptType;
                result.DefineFlagField((int)(object)interruptType, FieldMode.Read, name: interruptType.ToString(),
                                       valueProviderCallback: _ => IsSet(local));
            }
            return result;
        }

        public TRegister GetInterruptEnableRegister<TRegister>() where TRegister : PeripheralRegister
        {
            var result = CreateRegister<TRegister>();
            foreach(TInterrupt interruptType in Enum.GetValues(typeof(TInterrupt)))
            {
                var local = interruptType;
                result.DefineFlagField((int)(object)interruptType, name: interruptType.ToString(),
                                       valueProviderCallback: _ => IsEnabled(local),
                                       writeCallback: (_, v) => EnableInterrupt(local, v));
            }
            return result;
        }

        public TRegister GetInterruptSetRegister<TRegister>() where TRegister : PeripheralRegister
        {
            var result = CreateRegister<TRegister>();
            foreach(TInterrupt interruptType in Enum.GetValues(typeof(TInterrupt)))
            {
                var local = interruptType;
                if(!nonsettableInterrupts.Contains(interruptType))
                {
                    result.DefineFlagField((int)(object)interruptType, FieldMode.Set, name: interruptType.ToString(),
                                           writeCallback: (_, __) => SetInterrupt(local));
                }
            }

            return result;
        }

        public TRegister GetInterruptClearRegister<TRegister>() where TRegister : PeripheralRegister
        {
            var result = CreateRegister<TRegister>();
            foreach(TInterrupt interruptType in Enum.GetValues(typeof(TInterrupt)))
            {
                var local = interruptType;
                if(!nonsettableInterrupts.Contains(interruptType))
                {
                    result.DefineFlagField((int)(object)interruptType, FieldMode.Set, name: interruptType.ToString(),
                                           writeCallback: (_, __) => ClearInterrupt(local));
                }
            }

            return result;
        }

        public void EnableInterrupt(TInterrupt interrupt, bool status = true)
        {
            if(status)
            {
                enabledInterrupts.Add(interrupt);
            }
            else
            {
                enabledInterrupts.Remove(interrupt);
            }
            RefreshInterrupts();
        }

        public void DisableInterrupt(TInterrupt interrupt)
        {
            EnableInterrupt(interrupt, false);
        }

        public void SetInterrupt(TInterrupt interrupt, bool status = true)
        {
            if(status)
            {
                activeInterrupts.Add(interrupt);
            }
            else
            {
                activeInterrupts.Remove(interrupt);
            }
            RefreshInterrupts();
        }

        public bool IsSet(TInterrupt interrupt)
        {
            return activeInterrupts.Contains(interrupt);
        }

        public bool IsEnabled(TInterrupt interrupt)
        {
            return enabledInterrupts.Contains(interrupt);
        }

        public void ClearInterrupt(TInterrupt interrupt)
        {
            SetInterrupt(interrupt, false);
        }

        private TRegister CreateRegister<TRegister>() where TRegister : PeripheralRegister
        {
            TRegister result = null;
            if(typeof(TRegister) == typeof(DoubleWordRegister))
            {
                result = (TRegister)(PeripheralRegister)new DoubleWordRegister(master);
            }
            else if(typeof(TRegister) == typeof(WordRegister))
            {
                result = (TRegister)(PeripheralRegister)new WordRegister(master);
            }
            else if(typeof(TRegister) == typeof(ByteRegister))
            {
                result = (TRegister)(PeripheralRegister)new ByteRegister(master);
            }
            return result;
        }

        private void RefreshInterrupts()
        {
            foreach(var irq in subvectors)
            {
                var value = enabledInterrupts.Intersect(irq.Value).Intersect(activeInterrupts).Any();
                irq.Key.Set(value);
                master.Log(LogLevel.Noisy, "{0} set to: {1}", gpioNames[irq.Key], value);
            }
        }

        private readonly HashSet<TInterrupt> nonsettableInterrupts;
        private readonly Dictionary<IGPIO, string> gpioNames;
        private readonly Dictionary<IGPIO, HashSet<TInterrupt>> subvectors;
        private readonly HashSet<TInterrupt> activeInterrupts;
        private readonly HashSet<TInterrupt> enabledInterrupts;
        private readonly IPeripheral master;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SubvectorAttribute : Attribute
    {
        public SubvectorAttribute(int subvectorId)
        {
            SubvectorId = subvectorId;
        }

        public int SubvectorId { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class NotSettableAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class IrqProviderAttribute : Attribute
    {
        public IrqProviderAttribute()
        {
            SubvectorId = -1;
        }

        public IrqProviderAttribute(string name, int subvectorId)
        {
            if(subvectorId < 0)
            {
                throw new ArgumentException("Subvector id must be non-negative");
            }

            Name = name;
            SubvectorId = subvectorId;
        }

        public string Name { get; private set; }
        public int SubvectorId { get; private set; }
    }
}
