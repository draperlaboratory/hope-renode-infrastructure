﻿/********************************************************
*
* Warning!
* This file was generated automatically.
* Please do not edit. Changes should be made in the
* appropriate *.tt file.
*
*/
using System;
using System.Linq;
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.CPU.Registers;
using Antmicro.Renode.Utilities.Binding;
using Antmicro.Renode.Exceptions;

namespace Antmicro.Renode.Peripherals.CPU
{
    public partial class RiscV32
    {
        public override void SetRegisterUnsafe(int register, ulong value)
        {
            if(!mapping.TryGetValue((RiscV32Registers)register, out var r))
            {
                throw new RecoverableException($"Wrong register index: {register}");
            }

            SetRegisterValue32(r.Index, checked((UInt32)value));
        }

        public override RegisterValue GetRegisterUnsafe(int register)
        {
            if(!mapping.TryGetValue((RiscV32Registers)register, out var r))
            {
                throw new RecoverableException($"Wrong register index: {register}");
            }

            return GetRegisterValue32(r.Index);
        }

        public override IEnumerable<CPURegister> GetRegisters()
        {
            return mapping.Values.OrderBy(x => x.Index);
        }

        [Register]
        public RegisterValue ZERO
        {
            get
            {
                return GetRegisterValue32((int)RiscV32Registers.ZERO);
            }
            set
            {
                SetRegisterValue32((int)RiscV32Registers.ZERO, value);
            }
        }
        [Register]
        public override RegisterValue PC
        {
            get
            {
                return GetRegisterValue32((int)RiscV32Registers.PC);
            }
            set
            {
                SetRegisterValue32((int)RiscV32Registers.PC, value);
            }
        }
        [Register]
        public RegisterValue PRIV
        {
            get
            {
                return GetRegisterValue32((int)RiscV32Registers.PRIV);
            }
            set
            {
                SetRegisterValue32((int)RiscV32Registers.PRIV, value);
            }
        }
        [Register]
        public RegisterValue MCAUSE
        {
            get
            {
                return GetRegisterValue32((int)RiscV32Registers.MCAUSE);
            }
            set
            {
                SetRegisterValue32((int)RiscV32Registers.MCAUSE, value);
            }
        }
        public RegistersGroup X { get; private set; }
        public RegistersGroup F { get; private set; }

        protected override void InitializeRegisters()
        {
            var indexValueMapX = new Dictionary<int, RiscV32Registers>
            {
                { 0, RiscV32Registers.X0 },
                { 1, RiscV32Registers.X1 },
                { 2, RiscV32Registers.X2 },
                { 3, RiscV32Registers.X3 },
                { 4, RiscV32Registers.X4 },
                { 5, RiscV32Registers.X5 },
                { 6, RiscV32Registers.X6 },
                { 7, RiscV32Registers.X7 },
                { 8, RiscV32Registers.X8 },
                { 9, RiscV32Registers.X9 },
                { 10, RiscV32Registers.X10 },
                { 11, RiscV32Registers.X11 },
                { 12, RiscV32Registers.X12 },
                { 13, RiscV32Registers.X13 },
                { 14, RiscV32Registers.X14 },
                { 15, RiscV32Registers.X15 },
                { 16, RiscV32Registers.X16 },
                { 17, RiscV32Registers.X17 },
                { 18, RiscV32Registers.X18 },
                { 19, RiscV32Registers.X19 },
                { 20, RiscV32Registers.X20 },
                { 21, RiscV32Registers.X21 },
                { 22, RiscV32Registers.X22 },
                { 23, RiscV32Registers.X23 },
                { 24, RiscV32Registers.X24 },
                { 25, RiscV32Registers.X25 },
                { 26, RiscV32Registers.X26 },
                { 27, RiscV32Registers.X27 },
                { 28, RiscV32Registers.X28 },
                { 29, RiscV32Registers.X29 },
                { 30, RiscV32Registers.X30 },
                { 31, RiscV32Registers.X31 },
            };
            X = new RegistersGroup(
                indexValueMapX.Keys,
                i => GetRegisterUnsafe((int)indexValueMapX[i]),
                (i, v) => SetRegisterUnsafe((int)indexValueMapX[i], v));

            var indexValueMapF = new Dictionary<int, RiscV32Registers>
            {
                { 0, RiscV32Registers.F0 },
                { 1, RiscV32Registers.F1 },
                { 2, RiscV32Registers.F2 },
                { 3, RiscV32Registers.F3 },
                { 4, RiscV32Registers.F4 },
                { 5, RiscV32Registers.F5 },
                { 6, RiscV32Registers.F6 },
                { 7, RiscV32Registers.F7 },
                { 8, RiscV32Registers.F8 },
                { 9, RiscV32Registers.F9 },
                { 10, RiscV32Registers.F10 },
                { 11, RiscV32Registers.F11 },
                { 12, RiscV32Registers.F12 },
                { 13, RiscV32Registers.F13 },
                { 14, RiscV32Registers.F14 },
                { 15, RiscV32Registers.F15 },
                { 16, RiscV32Registers.F16 },
                { 17, RiscV32Registers.F17 },
                { 18, RiscV32Registers.F18 },
                { 19, RiscV32Registers.F19 },
                { 20, RiscV32Registers.F20 },
                { 21, RiscV32Registers.F21 },
                { 22, RiscV32Registers.F22 },
                { 23, RiscV32Registers.F23 },
                { 24, RiscV32Registers.F24 },
                { 25, RiscV32Registers.F25 },
                { 26, RiscV32Registers.F26 },
                { 27, RiscV32Registers.F27 },
                { 28, RiscV32Registers.F28 },
                { 29, RiscV32Registers.F29 },
                { 30, RiscV32Registers.F30 },
                { 31, RiscV32Registers.F31 },
            };
            F = new RegistersGroup(
                indexValueMapF.Keys,
                i => GetRegisterUnsafe((int)indexValueMapF[i]),
                (i, v) => SetRegisterUnsafe((int)indexValueMapF[i], v));

        }

        // 649:  Field '...' is never assigned to, and will always have its default value null
        #pragma warning disable 649

        [Import(Name = "tlib_set_register_value_32")]
        protected ActionInt32UInt32 SetRegisterValue32;
        [Import(Name = "tlib_get_register_value_32")]
        protected FuncUInt32Int32 GetRegisterValue32;

        #pragma warning restore 649

        private static readonly Dictionary<RiscV32Registers, CPURegister> mapping = new Dictionary<RiscV32Registers, CPURegister>
        {
            { RiscV32Registers.ZERO,  new CPURegister(0, 32, true) },
            { RiscV32Registers.X1,  new CPURegister(1, 32, true) },
            { RiscV32Registers.X2,  new CPURegister(2, 32, true) },
            { RiscV32Registers.X3,  new CPURegister(3, 32, true) },
            { RiscV32Registers.X4,  new CPURegister(4, 32, true) },
            { RiscV32Registers.X5,  new CPURegister(5, 32, true) },
            { RiscV32Registers.X6,  new CPURegister(6, 32, true) },
            { RiscV32Registers.X7,  new CPURegister(7, 32, true) },
            { RiscV32Registers.X8,  new CPURegister(8, 32, true) },
            { RiscV32Registers.X9,  new CPURegister(9, 32, true) },
            { RiscV32Registers.X10,  new CPURegister(10, 32, true) },
            { RiscV32Registers.X11,  new CPURegister(11, 32, true) },
            { RiscV32Registers.X12,  new CPURegister(12, 32, true) },
            { RiscV32Registers.X13,  new CPURegister(13, 32, true) },
            { RiscV32Registers.X14,  new CPURegister(14, 32, true) },
            { RiscV32Registers.X15,  new CPURegister(15, 32, true) },
            { RiscV32Registers.X16,  new CPURegister(16, 32, true) },
            { RiscV32Registers.X17,  new CPURegister(17, 32, true) },
            { RiscV32Registers.X18,  new CPURegister(18, 32, true) },
            { RiscV32Registers.X19,  new CPURegister(19, 32, true) },
            { RiscV32Registers.X20,  new CPURegister(20, 32, true) },
            { RiscV32Registers.X21,  new CPURegister(21, 32, true) },
            { RiscV32Registers.X22,  new CPURegister(22, 32, true) },
            { RiscV32Registers.X23,  new CPURegister(23, 32, true) },
            { RiscV32Registers.X24,  new CPURegister(24, 32, true) },
            { RiscV32Registers.X25,  new CPURegister(25, 32, true) },
            { RiscV32Registers.X26,  new CPURegister(26, 32, true) },
            { RiscV32Registers.X27,  new CPURegister(27, 32, true) },
            { RiscV32Registers.X28,  new CPURegister(28, 32, true) },
            { RiscV32Registers.X29,  new CPURegister(29, 32, true) },
            { RiscV32Registers.X30,  new CPURegister(30, 32, true) },
            { RiscV32Registers.X31,  new CPURegister(31, 32, true) },
            { RiscV32Registers.PC,  new CPURegister(32, 32, true) },
            { RiscV32Registers.F0,  new CPURegister(33, 32, false) },
            { RiscV32Registers.F1,  new CPURegister(34, 32, false) },
            { RiscV32Registers.F2,  new CPURegister(35, 32, false) },
            { RiscV32Registers.F3,  new CPURegister(36, 32, false) },
            { RiscV32Registers.F4,  new CPURegister(37, 32, false) },
            { RiscV32Registers.F5,  new CPURegister(38, 32, false) },
            { RiscV32Registers.F6,  new CPURegister(39, 32, false) },
            { RiscV32Registers.F7,  new CPURegister(40, 32, false) },
            { RiscV32Registers.F8,  new CPURegister(41, 32, false) },
            { RiscV32Registers.F9,  new CPURegister(42, 32, false) },
            { RiscV32Registers.F10,  new CPURegister(43, 32, false) },
            { RiscV32Registers.F11,  new CPURegister(44, 32, false) },
            { RiscV32Registers.F12,  new CPURegister(45, 32, false) },
            { RiscV32Registers.F13,  new CPURegister(46, 32, false) },
            { RiscV32Registers.F14,  new CPURegister(47, 32, false) },
            { RiscV32Registers.F15,  new CPURegister(48, 32, false) },
            { RiscV32Registers.F16,  new CPURegister(49, 32, false) },
            { RiscV32Registers.F17,  new CPURegister(50, 32, false) },
            { RiscV32Registers.F18,  new CPURegister(51, 32, false) },
            { RiscV32Registers.F19,  new CPURegister(52, 32, false) },
            { RiscV32Registers.F20,  new CPURegister(53, 32, false) },
            { RiscV32Registers.F21,  new CPURegister(54, 32, false) },
            { RiscV32Registers.F22,  new CPURegister(55, 32, false) },
            { RiscV32Registers.F23,  new CPURegister(56, 32, false) },
            { RiscV32Registers.F24,  new CPURegister(57, 32, false) },
            { RiscV32Registers.F25,  new CPURegister(58, 32, false) },
            { RiscV32Registers.F26,  new CPURegister(59, 32, false) },
            { RiscV32Registers.F27,  new CPURegister(60, 32, false) },
            { RiscV32Registers.F28,  new CPURegister(61, 32, false) },
            { RiscV32Registers.F29,  new CPURegister(62, 32, false) },
            { RiscV32Registers.F30,  new CPURegister(63, 32, false) },
            { RiscV32Registers.F31,  new CPURegister(64, 32, false) },
            { RiscV32Registers.MCAUSE,  new CPURegister(834, 32, false) },
            { RiscV32Registers.PRIV,  new CPURegister(4161, 32, false) },
        };
    }

    public enum RiscV32Registers
    {
        ZERO = 0,
        PC = 32,
        PRIV = 4161,
        MCAUSE = 834,
        X0 = 0,
        X1 = 1,
        X2 = 2,
        X3 = 3,
        X4 = 4,
        X5 = 5,
        X6 = 6,
        X7 = 7,
        X8 = 8,
        X9 = 9,
        X10 = 10,
        X11 = 11,
        X12 = 12,
        X13 = 13,
        X14 = 14,
        X15 = 15,
        X16 = 16,
        X17 = 17,
        X18 = 18,
        X19 = 19,
        X20 = 20,
        X21 = 21,
        X22 = 22,
        X23 = 23,
        X24 = 24,
        X25 = 25,
        X26 = 26,
        X27 = 27,
        X28 = 28,
        X29 = 29,
        X30 = 30,
        X31 = 31,
        F0 = 33,
        F1 = 34,
        F2 = 35,
        F3 = 36,
        F4 = 37,
        F5 = 38,
        F6 = 39,
        F7 = 40,
        F8 = 41,
        F9 = 42,
        F10 = 43,
        F11 = 44,
        F12 = 45,
        F13 = 46,
        F14 = 47,
        F15 = 48,
        F16 = 49,
        F17 = 50,
        F18 = 51,
        F19 = 52,
        F20 = 53,
        F21 = 54,
        F22 = 55,
        F23 = 56,
        F24 = 57,
        F25 = 58,
        F26 = 59,
        F27 = 60,
        F28 = 61,
        F29 = 62,
        F30 = 63,
        F31 = 64,
    }
}
