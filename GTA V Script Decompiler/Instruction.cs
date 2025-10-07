using Decompiler.Ast;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Decompiler
{
    internal enum Opcode //opcodes reversed from gta v eboot.bin
    {
        NOP,
        IADD,
        ISUB,
        IMUL,
        IDIV,
        IMOD,
        INOT,
        INEG,
        IEQ,
        INE,
        IGT,
        IGE,
        ILT,
        ILE,
        FADD,
        FSUB,
        FMUL,
        FDIV,
        FMOD,
        FNEG,
        FEQ,
        FNE,
        FGT,
        FGE,
        FLT,
        FLE,
        VADD,
        VSUB,
        VMUL,
        VDIV,
        VNEG,
        IAND,
        IOR,
        IXOR,
        I2F,
        F2I,
        F2V,
        PUSH_CONST_U8,
        PUSH_CONST_U8_U8,
        PUSH_CONST_U8_U8_U8,
        PUSH_CONST_U32,
        PUSH_CONST_F,
        DUP,
        DROP,
        NATIVE,
        ENTER,
        LEAVE,
        LOAD,
        STORE,
        STORE_REV,
        LOAD_N,
        STORE_N,
        ARRAY_U8,
        ARRAY_U8_LOAD,
        ARRAY_U8_STORE,
        LOCAL_U8,
        LOCAL_U8_LOAD,
        LOCAL_U8_STORE,
        STATIC_U8,
        STATIC_U8_LOAD,
        STATIC_U8_STORE,
        IADD_U8,
        IMUL_U8,
        IOFFSET,
        IOFFSET_U8,
        IOFFSET_U8_LOAD,
        IOFFSET_U8_STORE,
        PUSH_CONST_S16,
        IADD_S16,
        IMUL_S16,
        IOFFSET_S16,
        IOFFSET_S16_LOAD,
        IOFFSET_S16_STORE,
        ARRAY_U16,
        ARRAY_U16_LOAD,
        ARRAY_U16_STORE,
        LOCAL_U16,
        LOCAL_U16_LOAD,
        LOCAL_U16_STORE,
        STATIC_U16,
        STATIC_U16_LOAD,
        STATIC_U16_STORE,
        GLOBAL_U16,
        GLOBAL_U16_LOAD,
        GLOBAL_U16_STORE,
        J,
        JZ,
        IEQ_JZ,
        INE_JZ,
        IGT_JZ,
        IGE_JZ,
        ILT_JZ,
        ILE_JZ,
        CALL,
        STATIC_U24,
        STATIC_U24_LOAD,
        STATIC_U24_STORE,
        GLOBAL_U24,
        GLOBAL_U24_LOAD,
        GLOBAL_U24_STORE,
        PUSH_CONST_U24,
        SWITCH,
        STRING,
        STRINGHASH,
        TEXT_LABEL_ASSIGN_STRING,
        TEXT_LABEL_ASSIGN_INT,
        TEXT_LABEL_APPEND_STRING,
        TEXT_LABEL_APPEND_INT,
        TEXT_LABEL_COPY,
        CATCH,
        THROW,
        CALLINDIRECT,
        PUSH_CONST_M1,
        PUSH_CONST_0,
        PUSH_CONST_1,
        PUSH_CONST_2,
        PUSH_CONST_3,
        PUSH_CONST_4,
        PUSH_CONST_5,
        PUSH_CONST_6,
        PUSH_CONST_7,
        PUSH_CONST_FM1,
        PUSH_CONST_F0,
        PUSH_CONST_F1,
        PUSH_CONST_F2,
        PUSH_CONST_F3,
        PUSH_CONST_F4,
        PUSH_CONST_F5,
        PUSH_CONST_F6,
        PUSH_CONST_F7,
        IS_BIT_SET,

        // RDR2 stuff

        LOCAL_LOAD_S,
        LOCAL_STORE_S,
        LOCAL_STORE_SR,
        STATIC_LOAD_S,
        STATIC_STORE_S,
        STATIC_STORE_SR,
        LOAD_N_S,
        STORE_N_S,
        STORE_N_SR,
        GLOBAL_LOAD_S,
        GLOBAL_STORE_S,
        GLOBAL_STORE_SR,
    };

    internal class Instruction
    {
        // from https://github.com/EROOTIIK/GTA-V-Script-Decompiler/blob/master/GTA%20V%20Script%20Decompiler/Instruction.cs#L829
        public static readonly Dictionary<int, Opcode> ShuffledOpcodes = new Dictionary<int, Opcode> {
            { 19, Opcode.NOP },
            { 67, Opcode.IADD },
            { 16, Opcode.ISUB },
            { 30, Opcode.IMUL },
            { 89, Opcode.IDIV },
            { 71, Opcode.IMOD },
            { 5, Opcode.INOT },
            { 43, Opcode.INEG },
            { 11, Opcode.IEQ },
            { 28, Opcode.INE },
            { 32, Opcode.IGT },
            { 126, Opcode.IGE },
            { 53, Opcode.ILT },
            { 119, Opcode.ILE },
            { 113, Opcode.FADD },
            { 81, Opcode.FSUB },
            { 36, Opcode.FMUL },
            { 12, Opcode.FDIV },
            { 27, Opcode.FMOD },
            { 101, Opcode.FNEG },
            { 125, Opcode.FEQ },
            { 87, Opcode.FNE },
            { 49, Opcode.FGT },
            { 22, Opcode.FGE },
            { 52, Opcode.FLT },
            { 132, Opcode.FLE },
            { 96, Opcode.VADD },
            { 25, Opcode.VSUB },
            { 7, Opcode.VMUL },
            { 18, Opcode.VDIV },
            { 97, Opcode.VNEG },
            { 105, Opcode.IAND },
            { 48, Opcode.IOR },
            { 73, Opcode.IXOR },
            { 54, Opcode.I2F },
            { 69, Opcode.F2I },
            { 56, Opcode.F2V },
            { 109, Opcode.PUSH_CONST_U8 },
            { 111, Opcode.PUSH_CONST_U8_U8 },
            { 123, Opcode.PUSH_CONST_U8_U8_U8 },
            { 55, Opcode.PUSH_CONST_U32 },
            { 134, Opcode.PUSH_CONST_F },
            { 106, Opcode.DUP },
            { 65, Opcode.DROP },
            { 3, Opcode.NATIVE },
            { 34, Opcode.ENTER },
            { 80, Opcode.LEAVE },
            { 118, Opcode.LOAD },
            { 50, Opcode.STORE },
            { 61, Opcode.STORE_REV },
            { 45, Opcode.LOAD_N },
            { 6, Opcode.STORE_N },
            { 99, Opcode.ARRAY_U8 },
            { 23, Opcode.ARRAY_U8_LOAD },
            { 100, Opcode.ARRAY_U8_STORE },
            { 75, Opcode.LOCAL_U8 },
            { 102, Opcode.LOCAL_U8_LOAD },
            { 103, Opcode.LOCAL_U8_STORE },
            { 137, Opcode.STATIC_U8 },
            { 84, Opcode.STATIC_U8_LOAD },
            { 78, Opcode.STATIC_U8_STORE },
            { 92, Opcode.IADD_U8 },
            { 20, Opcode.IMUL_U8 },
            { 86, Opcode.IOFFSET },
            { 128, Opcode.IOFFSET_U8 },
            { 39, Opcode.IOFFSET_U8_LOAD },
            { 108, Opcode.IOFFSET_U8_STORE },
            { 37, Opcode.PUSH_CONST_S16 },
            { 59, Opcode.IADD_S16 },
            { 127, Opcode.IMUL_S16 },
            { 24, Opcode.IOFFSET_S16 },
            { 120, Opcode.IOFFSET_S16_LOAD },
            { 140, Opcode.IOFFSET_S16_STORE },
            { 64, Opcode.ARRAY_U16 },
            { 2, Opcode.ARRAY_U16_LOAD },
            { 10, Opcode.ARRAY_U16_STORE },
            { 88, Opcode.LOCAL_U16 },
            { 1, Opcode.LOCAL_U16_LOAD },
            { 68, Opcode.LOCAL_U16_STORE },
            { 70, Opcode.STATIC_U16 },
            { 58, Opcode.STATIC_U16_LOAD },
            { 95, Opcode.STATIC_U16_STORE },
            { 135, Opcode.GLOBAL_U16 },
            { 112, Opcode.GLOBAL_U16_LOAD },
            { 74, Opcode.GLOBAL_U16_STORE },
            { 104, Opcode.J },
            { 139, Opcode.JZ },
            { 21, Opcode.IEQ_JZ },
            { 114, Opcode.INE_JZ },
            { 46, Opcode.IGT_JZ },
            { 117, Opcode.IGE_JZ },
            { 138, Opcode.ILT_JZ },
            { 35, Opcode.ILE_JZ },
            { 57, Opcode.CALL },
            { 93, Opcode.GLOBAL_U24 },
            { 133, Opcode.GLOBAL_U24_LOAD },
            { 38, Opcode.GLOBAL_U24_STORE },
            { 33, Opcode.PUSH_CONST_U24 },
            { 60, Opcode.SWITCH },
            { 4, Opcode.STRING },
            { 129, Opcode.STRINGHASH },
            { 31, Opcode.TEXT_LABEL_ASSIGN_STRING },
            { 121, Opcode.TEXT_LABEL_ASSIGN_INT },
            { 94, Opcode.TEXT_LABEL_APPEND_STRING },
            { 41, Opcode.TEXT_LABEL_APPEND_INT },
            { 26, Opcode.TEXT_LABEL_COPY },
            { 110, Opcode.CATCH },
            { 85, Opcode.THROW },
            { 141, Opcode.CALLINDIRECT },
            { 8, Opcode.PUSH_CONST_M1 },
            { 47, Opcode.PUSH_CONST_0 },
            { 9, Opcode.PUSH_CONST_1 },
            { 17, Opcode.PUSH_CONST_2 },
            { 29, Opcode.PUSH_CONST_3 },
            { 66, Opcode.PUSH_CONST_4 },
            { 98, Opcode.PUSH_CONST_5 },
            { 77, Opcode.PUSH_CONST_6 },
            { 13, Opcode.PUSH_CONST_7 },
            { 76, Opcode.PUSH_CONST_FM1 },
            { 115, Opcode.PUSH_CONST_F0 },
            { 72, Opcode.PUSH_CONST_F1 },
            { 91, Opcode.PUSH_CONST_F2 },
            { 44, Opcode.PUSH_CONST_F3 },
            { 90, Opcode.PUSH_CONST_F4 },
            { 124, Opcode.PUSH_CONST_F5 },
            { 122, Opcode.PUSH_CONST_F6 },
            { 51, Opcode.PUSH_CONST_F7 },
            // RDR3 extended
            { 15, Opcode.LOCAL_LOAD_S },
            { 0, Opcode.LOCAL_STORE_S },
            { 82, Opcode.LOCAL_STORE_SR },
            { 14, Opcode.STATIC_LOAD_S },
            { 79, Opcode.STATIC_STORE_S },
            { 116, Opcode.STATIC_STORE_SR },
            { 136, Opcode.LOAD_N_S },
            { 83, Opcode.STORE_N_S },
            { 130, Opcode.STORE_N_SR },
            { 131, Opcode.GLOBAL_LOAD_S },
            { 63, Opcode.GLOBAL_STORE_S },
            { 42, Opcode.GLOBAL_STORE_SR },
            // >= 1311
            { 62, Opcode.STATIC_U24 },
            { 107, Opcode.STATIC_U24_LOAD },
            { 40, Opcode.STATIC_U24_STORE },
        };

        public static Opcode MapOpcode(byte opcode)
        {
            if (!Properties.Settings.Default.IsRDR2)
                return (Opcode)opcode;

            if (ShuffledOpcodes.ContainsKey(opcode))
                return (Opcode)ShuffledOpcodes[opcode];


            throw new InvalidOperationException("Invalid RDR2 opcode");
        }

        public static byte UnmapOpcode(Opcode opcode)
        {
            if (!Properties.Settings.Default.IsRDR2)
                return (byte)opcode;

            return (byte)ShuffledOpcodes.First(x => x.Value == opcode).Key;
        }

        public Opcode Opcode { get; private set; }
        public Opcode OriginalOpcode { get; private set; }
        public byte[] Operands { get; private set; }

        public Instruction(Opcode Instruction, IEnumerable<byte> Operands, int Offset)
        {
            Opcode = Instruction;
            OriginalOpcode = Opcode;
            this.Operands = Operands.ToArray();
            this.Offset = Offset;
        }

        public Instruction(Opcode Instruction, int Offset)
        {
            Opcode = Instruction;
            OriginalOpcode = Opcode;
            Operands = new byte[0];
            this.Offset = Offset;
        }

        public void NopInstruction()
        {
            Opcode = Opcode.NOP;
        }

        public int UnmappedOpcode { get { return UnmapOpcode(OriginalOpcode); } }

		public int Offset { get; }

		public int InstructionLength
		{
			get { return 1 + Operands.Length; }
		}

		public int GetOperandsAsInt
		{
			get
			{
				return Operands.Length switch
				{
					1 => Operands[0],
					2 => BitConverter.ToInt16(Operands, 0),
					3 => (Operands[2] << 16) | (Operands[1] << 8) | Operands[0],
					4 => BitConverter.ToInt32(Operands, 0),
					_ => throw new Exception("Invalid amount of operands (" + Operands.Length.ToString() + ")"),
				};
			}
		}

		public float GetFloat
		{
			get
			{
				return Operands.Length != 4 ? throw new Exception("Not a Float") : BitConverter.ToSingle(Operands, 0);
			}
		}

		public byte GetOperand(int index)
		{
			return Operands[index];
		}

		public uint GetOperandsAsUInt
		{
			get
			{
				return Operands.Length switch
				{
					1 => Operands[0],
					2 => BitConverter.ToUInt16(Operands, 0),
					3 => (uint)((Operands[2] << 16) | (Operands[1] << 8) | Operands[0]),
					4 => BitConverter.ToUInt32(Operands, 0),
					_ => throw new Exception("Invalid amount of operands (" + Operands.Length.ToString() + ")"),
				};
			}
		}

		public int GetJumpOffset
		{
			get
			{
				return IsJumpInstruction ? BitConverter.ToInt16(Operands, 0) + Offset + 3 : throw new Exception("Not A jump");
			}
		}

		public byte GetNativeParams
		{
			get
			{
				return Opcode == Opcode.NATIVE ? (byte)(Operands[0] >> 2) : throw new Exception("Not A Native");
			}
		}

		public byte GetNativeReturns
		{
			get
			{
				return Opcode == Opcode.NATIVE ? (byte)(Operands[0] & 0x3) : throw new Exception("Not A Native");
			}
		}

		public ushort GetNativeIndex
		{
			get
			{
				if (Opcode == Opcode.NATIVE)
				{
					return Utils.SwapEndian(BitConverter.ToUInt16(Operands, 1));
				}

				throw new Exception("Not a native");
			}
		}

		public int GetSwitchCase(int index)
		{
			if (Opcode == Opcode.SWITCH)
			{
                if (Properties.Settings.Default.IsRDR2)
                    return index >= SwitchCases ? throw new Exception("Out of range script case") : BitConverter.ToInt32(Operands, 2 + (index * 6));
                else
                    return index >= SwitchCases ? throw new Exception("Out of range script case") : BitConverter.ToInt32(Operands, 1 + (index * 6));

            }

			throw new Exception("Not a switch statement");
		}

		public int GetSwitchOffset(int index)
		{
			if (Opcode == Opcode.SWITCH)
			{
                if (Properties.Settings.Default.IsRDR2)
				    return index >= SwitchCases
                        ? throw new Exception("Out of range script case")
					    : (Offset + 8 + 1) + index * 6 + BitConverter.ToInt16(Operands, 6 + index * 6);
                else
                    return index >= SwitchCases
                        ? throw new Exception("Out of range script case")
                        : Offset + 8 + (index * 6) + BitConverter.ToInt16(Operands, 5 + (index * 6));
            }

			throw new Exception("Not a switch statement");
		}

        public ushort SwitchCases
        {
            get
            {
                if (Properties.Settings.Default.IsRDR2)
                    return BitConverter.ToUInt16(Operands, 0);
                else
                    return GetOperand(0);
            }
        }


        public int GetImmBytePush
		{
			get
			{
				var _instruction = (int)Opcode;
				return _instruction is>=(int)Opcode.PUSH_CONST_M1 and <=(int)Opcode.PUSH_CONST_7 ? _instruction - ((int)Opcode.PUSH_CONST_M1 + 1) : throw new Exception("Not An Immediate Int Push");
			}
		}

		public float GetImmFloatPush
		{
			get
			{
				var _instruction = (int)Opcode;
				return _instruction is>=(int)Opcode.PUSH_CONST_FM1 and <=(int)Opcode.PUSH_CONST_F7 ? _instruction - ((int)Opcode.PUSH_CONST_FM1 + 1) : throw new Exception("Not An Immediate Float Push");
			}
		}

		public bool IsJumpInstruction
		{
			get { return (int)OriginalOpcode is >84 and <93; }
		}

		public bool IsConditionJump
		{
			get { return (int)Opcode is >85 and <93; }
		}

		public bool IsWhileJump
		{
			get
			{
				return Opcode == Opcode.J ? GetJumpOffset <= 0 ? false : GetOperandsAsInt < 0 : false;
			}
		}
	}
}
