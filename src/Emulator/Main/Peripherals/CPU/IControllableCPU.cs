//
// Copyright (c) 2010-2018 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System.Collections.Generic;
using ELFSharp.ELF;
using ELFSharp.UImage;

namespace Antmicro.Renode.Peripherals.CPU
{
    public interface IControllableCPU : ICPU
    {
        void SetRegisterUnsafe(int register, ulong value);

        RegisterValue GetRegisterUnsafe(int register);

        IEnumerable<CPURegister> GetRegisters();

        string[,] GetRegistersValues();

        void InitFromElf(IELF elf);

        void InitFromUImage(UImage uImage);

        Endianess Endianness { get; }
    }

    public struct CPURegister
    {
        public CPURegister(int index, int width, bool isGeneral)
        {
            Index = index;
            IsGeneral = isGeneral;
            Width = width;
        }

        public int Index { get; private set; }
        public bool IsGeneral { get; private set; }
        public int Width { get; private set; }

        // this is to support monitor output
        public override string ToString()
        {
            return Index.ToString();
        }
    }
}

