//
// Copyright (c) 2010-2018 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Bus;
using SysbusAccessWidth = Antmicro.Renode.Peripherals.Bus.SysbusAccessWidth;
using System.Collections.Generic;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Utilities.GDB.Commands
{
    internal class BreakpointCommand : Command
    {
        public BreakpointCommand(CommandsManager manager) : base(manager)
        {
            watchpoints = new Dictionary<WatchpointDescriptor, int>();
        }

        [Execute("Z")]
        public PacketData InsertBreakpoint(
            [Argument(Separator = ',')]BreakpointType type,
            [Argument(Separator = ',', Encoding = ArgumentAttribute.ArgumentEncoding.HexNumber)]ulong address,
            [Argument(Separator = ';', Encoding = ArgumentAttribute.ArgumentEncoding.HexNumber)]uint kind)
        {
            switch(type)
            {
                case BreakpointType.MemoryBreakpoint:
                    manager.Cpu.AddHook(address, MemoryBreakpointHook);
                    break;
                case BreakpointType.HardwareBreakpoint:
                    manager.Cpu.AddHook(address, HardwareBreakpointHook);
                    break;
                case BreakpointType.AccessWatchpoint:
                    AddWatchpointsCoveringMemoryArea(address, kind, Access.ReadAndWrite, AccessWatchpointHook);
                    break;
                case BreakpointType.ReadWatchpoint:
                    AddWatchpointsCoveringMemoryArea(address, kind, Access.Read, ReadWatchpointHook);
                    break;
                case BreakpointType.WriteWatchpoint:
                    AddWatchpointsCoveringMemoryArea(address, kind, Access.Write, WriteWatchpointHook);
                    break;
                default:
                    Logger.LogAs(this, LogLevel.Warning, "Unsupported breakpoint type: {0}, not inserting.", type);
                    return PacketData.ErrorReply(0);
            }

            return PacketData.Success;
        }

        [Execute("z")]
        public PacketData RemoveBreakpoint(
            [Argument(Separator = ',')]BreakpointType type,
            [Argument(Separator = ',', Encoding = ArgumentAttribute.ArgumentEncoding.HexNumber)]ulong address,
            [Argument(Separator = ';', Encoding = ArgumentAttribute.ArgumentEncoding.HexNumber)]uint kind)
        {
            switch(type)
            {
                case BreakpointType.MemoryBreakpoint:
                    manager.Cpu.RemoveHook(address, MemoryBreakpointHook);
                    break;
                case BreakpointType.HardwareBreakpoint:
                    manager.Cpu.RemoveHook(address, HardwareBreakpointHook);
                    break;
                case BreakpointType.AccessWatchpoint:
                    RemoveWatchpointsCoveringMemoryArea(address, kind, Access.ReadAndWrite, AccessWatchpointHook);
                    break;
                case BreakpointType.ReadWatchpoint:
                    RemoveWatchpointsCoveringMemoryArea(address, kind, Access.Read, ReadWatchpointHook);
                    break;
                case BreakpointType.WriteWatchpoint:
                    RemoveWatchpointsCoveringMemoryArea(address, kind, Access.Write, WriteWatchpointHook);
                    break;
                default:
                    Logger.LogAs(this, LogLevel.Warning, "Unsupported breakpoint type: {0}, not removing.", type);
                    return PacketData.ErrorReply(0);
            }

            return PacketData.Success;
        }

        private void HardwareBreakpointHook(ulong address)
        {
            manager.Cpu.EnterSingleStepModeSafely(new HaltArguments(HaltReason.Breakpoint, breakpointType: BreakpointType.HardwareBreakpoint));
        }

        private void MemoryBreakpointHook(ulong address)
        {
            manager.Cpu.EnterSingleStepModeSafely(new HaltArguments(HaltReason.Breakpoint, breakpointType: BreakpointType.MemoryBreakpoint));
        }

        private void AccessWatchpointHook(ulong address, SysbusAccessWidth width)
        {
            manager.Cpu.EnterSingleStepModeSafely(new HaltArguments(HaltReason.Breakpoint, address, BreakpointType.AccessWatchpoint));
        }

        private void WriteWatchpointHook(ulong address, SysbusAccessWidth width)
        {
            manager.Cpu.EnterSingleStepModeSafely(new HaltArguments(HaltReason.Breakpoint, address, BreakpointType.WriteWatchpoint));
        }

        private void ReadWatchpointHook(ulong address, SysbusAccessWidth width)
        {
            manager.Cpu.EnterSingleStepModeSafely(new HaltArguments(HaltReason.Breakpoint, address, BreakpointType.ReadWatchpoint));
        }

        private void AddWatchpointsCoveringMemoryArea(ulong address, uint kind, Access access, Action<ulong, SysbusAccessWidth> hook)
        {
            // we need to register hooks for all possible access widths covering memory fragment
            // [address, address + kind) referred by GDB
            foreach(var descriptor in CalculateAllCoveringAddressess(address, kind, access, hook))
            {
                lock(watchpoints)
                {
                    if(watchpoints.ContainsKey(descriptor))
                    {
                        watchpoints[descriptor]++;
                    }
                    else
                    {
                        watchpoints.Add(descriptor, 1);
                        manager.Machine.SystemBus.AddWatchpointHook(descriptor.Address, descriptor.Width, access, false, hook);
                    }
                }
            }
        }

        private void RemoveWatchpointsCoveringMemoryArea(ulong address, uint kind, Access access, Action<ulong, SysbusAccessWidth> hook)
        {
            // we need to unregister hooks from all possible access widths convering memory fragment
            // [address, address + kind) referred by GDB
            foreach(var descriptor in CalculateAllCoveringAddressess(address, kind, access, hook))
            {
                lock(watchpoints)
                {
                    if(watchpoints[descriptor] > 1)
                    {
                        watchpoints[descriptor]--;
                    }
                    else
                    {
                        watchpoints.Remove(descriptor);
                        manager.Machine.SystemBus.RemoveWatchpointHook(descriptor.Address, hook);
                    }
                }
            }
        }

        private static IEnumerable<WatchpointDescriptor> CalculateAllCoveringAddressess(ulong address, uint kind, Access access, Action<ulong, SysbusAccessWidth> hook)
        {
            foreach(SysbusAccessWidth width in Enum.GetValues(typeof(SysbusAccessWidth)))
            {
                for(var offset = -(long)(address % (ulong)width); offset < kind; offset += (long)width)
                {
                    yield return new WatchpointDescriptor(address - (ulong)(-offset), width, access, hook);
                }
            }
        }

        private readonly Dictionary<WatchpointDescriptor, int> watchpoints;

        private class WatchpointDescriptor
        {
            public WatchpointDescriptor(ulong address, SysbusAccessWidth width, Access access, Action<ulong, SysbusAccessWidth> hook)
            {
                Address = address;
                Width = width;
                Access = access;
                Hook = hook;
            }

            public override bool Equals(object obj)
            {
                var objAsBreakpointDescriptor = obj as WatchpointDescriptor;
                if(objAsBreakpointDescriptor == null)
                {
                    return false;
                }

                return objAsBreakpointDescriptor.Address == Address
                        && objAsBreakpointDescriptor.Width == Width
                        && objAsBreakpointDescriptor.Access == Access
                        && objAsBreakpointDescriptor.Hook == Hook;
            }

            public override int GetHashCode()
            {
                return 17 * (int)Address
                    + 23 * (int)Width
                    + 17 * (int)Access
                    + 17 * Hook.GetHashCode();
            }

            public readonly ulong Address;
            public readonly SysbusAccessWidth Width;
            public readonly Access Access;
            public readonly Action<ulong, SysbusAccessWidth> Hook;
        }
    }
}

