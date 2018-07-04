﻿/********************************************************
*
* Warning!
* This file was generated automatically.
* Please do not edit. Changes should be made in the
* appropriate *.tt file.
*
*/

using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Bus.Wrappers;

namespace Antmicro.Renode.Peripherals.Bus
{
    public partial class SystemBus
    {
        public byte ReadByte(ulong address)
        {
            ulong startAddress, endAddress;

            InvokeWatchpointHooks(hooksOnRead, address, SysbusAccessWidth.Byte);

            var accessMethods = peripherals.FindAccessMethods(address, out startAddress, out endAddress);
            if (accessMethods == null)
            {
                return (byte)ReportNonExistingRead(address, SysbusAccessWidth.Byte);
            }
            var lockTaken = false;
            try
            {
                accessMethods.Lock.Enter(ref lockTaken);
                if(accessMethods.SetAbsoluteAddress != null)
                {
                    accessMethods.SetAbsoluteAddress(address);
                }
                return accessMethods.ReadByte(checked((long)(address - startAddress)));
            }
            finally
            {
                if(lockTaken)
                {
                    accessMethods.Lock.Exit();
                }
            }
        }

        public void WriteByte(ulong address, byte value)
        {
            ulong startAddress, endAddress;

            var accessMethods = peripherals.FindAccessMethods(address, out startAddress, out endAddress);
            if (accessMethods == null)
            {
                ReportNonExistingWrite(address, value, SysbusAccessWidth.Byte);
                InvokeWatchpointHooks(hooksOnWrite, address, SysbusAccessWidth.Byte);
                return;
            }
            var lockTaken = false;
            try
            {
                accessMethods.Lock.Enter(ref lockTaken);
                if(accessMethods.SetAbsoluteAddress != null)
                {
                    accessMethods.SetAbsoluteAddress(address);
                }
                accessMethods.WriteByte(checked((long)(address - startAddress)), value);
                InvokeWatchpointHooks(hooksOnWrite, address, SysbusAccessWidth.Byte);
            }
            finally
            {
                if(lockTaken)
                {
                    accessMethods.Lock.Exit();
                }
            }
        }

        public ushort ReadWord(ulong address)
        {
            ulong startAddress, endAddress;

            InvokeWatchpointHooks(hooksOnRead, address, SysbusAccessWidth.Word);

            var accessMethods = peripherals.FindAccessMethods(address, out startAddress, out endAddress);
            if (accessMethods == null)
            {
                return (ushort)ReportNonExistingRead(address, SysbusAccessWidth.Word);
            }
            var lockTaken = false;
            try
            {
                accessMethods.Lock.Enter(ref lockTaken);
                if(accessMethods.SetAbsoluteAddress != null)
                {
                    accessMethods.SetAbsoluteAddress(address);
                }
                return accessMethods.ReadWord(checked((long)(address - startAddress)));
            }
            finally
            {
                if(lockTaken)
                {
                    accessMethods.Lock.Exit();
                }
            }
        }

        public void WriteWord(ulong address, ushort value)
        {
            ulong startAddress, endAddress;

            var accessMethods = peripherals.FindAccessMethods(address, out startAddress, out endAddress);
            if (accessMethods == null)
            {
                ReportNonExistingWrite(address, value, SysbusAccessWidth.Word);
                InvokeWatchpointHooks(hooksOnWrite, address, SysbusAccessWidth.Word);
                return;
            }
            var lockTaken = false;
            try
            {
                accessMethods.Lock.Enter(ref lockTaken);
                if(accessMethods.SetAbsoluteAddress != null)
                {
                    accessMethods.SetAbsoluteAddress(address);
                }
                accessMethods.WriteWord(checked((long)(address - startAddress)), value);
                InvokeWatchpointHooks(hooksOnWrite, address, SysbusAccessWidth.Word);
            }
            finally
            {
                if(lockTaken)
                {
                    accessMethods.Lock.Exit();
                }
            }
        }

        public uint ReadDoubleWord(ulong address)
        {
            ulong startAddress, endAddress;

            InvokeWatchpointHooks(hooksOnRead, address, SysbusAccessWidth.DoubleWord);

            var accessMethods = peripherals.FindAccessMethods(address, out startAddress, out endAddress);
            if (accessMethods == null)
            {
                return (uint)ReportNonExistingRead(address, SysbusAccessWidth.DoubleWord);
            }
            var lockTaken = false;
            try
            {
                accessMethods.Lock.Enter(ref lockTaken);
                if(accessMethods.SetAbsoluteAddress != null)
                {
                    accessMethods.SetAbsoluteAddress(address);
                }
                return accessMethods.ReadDoubleWord(checked((long)(address - startAddress)));
            }
            finally
            {
                if(lockTaken)
                {
                    accessMethods.Lock.Exit();
                }
            }
        }

        public void WriteDoubleWord(ulong address, uint value)
        {
            ulong startAddress, endAddress;

            var accessMethods = peripherals.FindAccessMethods(address, out startAddress, out endAddress);
            if (accessMethods == null)
            {
                ReportNonExistingWrite(address, value, SysbusAccessWidth.DoubleWord);
                InvokeWatchpointHooks(hooksOnWrite, address, SysbusAccessWidth.DoubleWord);
                return;
            }
            var lockTaken = false;
            try
            {
                accessMethods.Lock.Enter(ref lockTaken);
                if(accessMethods.SetAbsoluteAddress != null)
                {
                    accessMethods.SetAbsoluteAddress(address);
                }
                accessMethods.WriteDoubleWord(checked((long)(address - startAddress)), value);
                InvokeWatchpointHooks(hooksOnWrite, address, SysbusAccessWidth.DoubleWord);
            }
            finally
            {
                if(lockTaken)
                {
                    accessMethods.Lock.Exit();
                }
            }
        }

        public void ClearHookAfterPeripheralRead<T>(IBusPeripheral peripheral)
        {
            SetHookAfterPeripheralRead<T>(peripheral, null);
        }

        public void SetHookAfterPeripheralRead<T>(IBusPeripheral peripheral, Func<T, long, T> hook, Range? subrange = null)
        {
            if(!machine.IsRegistered(peripheral))
            {
                throw new RecoverableException(string.Format("Cannot set hook on peripheral {0}, it is not registered.", peripheral));
            }
            var type = typeof(T);
            if(type == typeof(byte))
            {
                peripherals.VisitAccessMethods(peripheral, pam =>
                {
                    if(pam.ReadByte.Target is ReadHookWrapper<byte>)
                    {
                        pam.ReadByte = new BusAccess.ByteReadMethod(((ReadHookWrapper<byte>)pam.ReadByte.Target).OriginalMethod);
                    }
                    if(hook != null)
                    {
                        pam.ReadByte = new BusAccess.ByteReadMethod(new ReadHookWrapper<byte>(peripheral, new Func<long, byte>(pam.ReadByte), (Func<byte, long, byte>)(object)hook, subrange).Read);
                    }
                    return pam;
                });
                return;
            }
            if(type == typeof(ushort))
            {
                peripherals.VisitAccessMethods(peripheral, pam =>
                {
                    if(pam.ReadWord.Target is ReadHookWrapper<ushort>)
                    {
                        pam.ReadWord = new BusAccess.WordReadMethod(((ReadHookWrapper<ushort>)pam.ReadWord.Target).OriginalMethod);
                    }
                    if(hook != null)
                    {
                        pam.ReadWord = new BusAccess.WordReadMethod(new ReadHookWrapper<ushort>(peripheral, new Func<long, ushort>(pam.ReadWord), (Func<ushort, long, ushort>)(object)hook, subrange).Read);
                    }
                    return pam;
                });
                return;
            }
            if(type == typeof(uint))
            {
                peripherals.VisitAccessMethods(peripheral, pam =>
                {
                    if(pam.ReadDoubleWord.Target is ReadHookWrapper<uint>)
                    {
                        pam.ReadDoubleWord = new BusAccess.DoubleWordReadMethod(((ReadHookWrapper<uint>)pam.ReadDoubleWord.Target).OriginalMethod);
                    }
                    if(hook != null)
                    {
                        pam.ReadDoubleWord = new BusAccess.DoubleWordReadMethod(new ReadHookWrapper<uint>(peripheral, new Func<long, uint>(pam.ReadDoubleWord), (Func<uint, long, uint>)(object)hook, subrange).Read);
                    }
                    return pam;
                });
                return;
            }
        }
        public void ClearHookBeforePeripheralWrite<T>(IBusPeripheral peripheral)
        {
            SetHookBeforePeripheralWrite<T>(peripheral, null);
        }

        public void SetHookBeforePeripheralWrite<T>(IBusPeripheral peripheral, Func<T, long, T> hook, Range? subrange = null)
        {
            if(!machine.IsRegistered(peripheral))
            {
                throw new RecoverableException(string.Format("Cannot set hook on peripheral {0}, it is not registered.", peripheral));
            }
            var type = typeof(T);
            if(type == typeof(byte))
            {
                peripherals.VisitAccessMethods(peripheral, pam =>
                {
                    if(pam.WriteByte.Target is WriteHookWrapper<byte>)
                    {
                        pam.WriteByte = new BusAccess.ByteWriteMethod(((WriteHookWrapper<byte>)pam.WriteByte.Target).OriginalMethod);
                    }
                    if(hook != null)
                    {
                        pam.WriteByte = new BusAccess.ByteWriteMethod(new WriteHookWrapper<byte>(peripheral, new Action<long, byte>(pam.WriteByte), (Func<byte, long, byte>)(object)hook, subrange).Write);
                    }
                    return pam;
                });
                return;
            }
            if(type == typeof(ushort))
            {
                peripherals.VisitAccessMethods(peripheral, pam =>
                {
                    if(pam.WriteWord.Target is WriteHookWrapper<ushort>)
                    {
                        pam.WriteWord = new BusAccess.WordWriteMethod(((WriteHookWrapper<ushort>)pam.WriteWord.Target).OriginalMethod);
                    }
                    if(hook != null)
                    {
                        pam.WriteWord = new BusAccess.WordWriteMethod(new WriteHookWrapper<ushort>(peripheral, new Action<long, ushort>(pam.WriteWord), (Func<ushort, long, ushort>)(object)hook, subrange).Write);
                    }
                    return pam;
                });
                return;
            }
            if(type == typeof(uint))
            {
                peripherals.VisitAccessMethods(peripheral, pam =>
                {
                    if(pam.WriteDoubleWord.Target is WriteHookWrapper<uint>)
                    {
                        pam.WriteDoubleWord = new BusAccess.DoubleWordWriteMethod(((WriteHookWrapper<uint>)pam.WriteDoubleWord.Target).OriginalMethod);
                    }
                    if(hook != null)
                    {
                        pam.WriteDoubleWord = new BusAccess.DoubleWordWriteMethod(new WriteHookWrapper<uint>(peripheral, new Action<long, uint>(pam.WriteDoubleWord), (Func<uint, long, uint>)(object)hook, subrange).Write);
                    }
                    return pam;
                });
                return;
            }
        }
    }
}
