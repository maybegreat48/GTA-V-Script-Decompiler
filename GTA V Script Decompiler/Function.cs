using Decompiler.Hooks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace Decompiler
{
    [Serializable]
    public class DecompilingException : Exception
    {
        public DecompilingException(string Message) : base(Message)
        {
        }
    }

    public class Function
    {
        public string Name { get; set; }
        public int NumParams { get; private set; }
        public int NumLocals { get; private set; }
        public int NumReturns { get; private set; }
        public int Xrefs { get; set; }
        public int Location { get; private set; }
        public int MaxLocation { get; private set; }

        internal List<Instruction> Instructions;

        internal Dictionary<int, int> InstructionMap;
        private readonly Stack Stack;
        private int Offset = 0;

        public ScriptFile ScriptFile;

        public TypeContainer ReturnType;

        public bool ReturnTypeSealed { get; private set; }

        internal bool Decoded { get; private set; }

        internal bool DecodeStarted = false;

        public VariableStorage Vars { get; private set; }
        public VariableStorage Params { get; private set; }

        public int LineCount = 0;

        public Comments comments = new();

        internal Ast.StatementTree.Main MainTree { get; private set; }

        public uint Hash { get; private set; }
        public uint Mk2Hash { get; private set; }

        internal FunctionHook? Hook = null;

        public Function(ScriptFile Owner, string name, int pcount, int vcount, int rcount, int location, int locmax = -1)
        {
            ScriptFile = Owner;
            Name = name;
            NumParams = pcount;
            NumLocals = vcount;
            NumReturns = rcount;
            Location = location;
            MaxLocation = locmax != -1 ? locmax : Location;
            Decoded = false;
            Vars = new VariableStorage(VariableStorage.ListType.Vars, vcount - 2);
            Params = new VariableStorage(VariableStorage.ListType.Params, pcount);
            Stack = new(this);
            MainTree = new(this);
            ReturnType = new(Types.UNKNOWN);
        }

        /// <summary>
        /// Gets a persistent hash of the function that should not change across updates
        /// </summary>
        /// <param name="mk2">Gets the newer MK2 hash</param>
        /// <remarks>
        /// DO NOT call this after function decode as things get nopped
        /// </remarks>
        private uint GetFunctionHash(bool mk2 = false)
        {
            StringBuilder sb = new();
            sb.Append(NumParams);
            sb.Append(NumReturns);

            //if (Instructions.Count <= 5)
            //	return 0; too many collisions but lets still give it a try

            var i = 0;
            Instruction? lastIns = null;

            foreach (var ins in Instructions)
            {
                if (ins.OriginalOpcode == Opcode.ENTER)
                    continue;

                sb.Append(ins.Opcode.ToString());
                i++;

                if (ins.Opcode == Opcode.LOCAL_U8 || ins.Opcode == Opcode.LOCAL_U16 || ins.Opcode == Opcode.LOCAL_U8_LOAD || ins.Opcode == Opcode.LOCAL_U16_LOAD || ins.Opcode == Opcode.LOCAL_U8_STORE || ins.Opcode == Opcode.LOCAL_U16_STORE ||
                    (mk2 && (ins.Opcode == Opcode.IOFFSET_U8 || ins.Opcode == Opcode.IOFFSET_U8_LOAD || ins.Opcode == Opcode.IOFFSET_U8_STORE)))
                    sb.Append(ins.GetOperandsAsUInt);

                else if (ins.Opcode == Opcode.PUSH_CONST_U8)
                    sb.Append(ins.GetOperand(0));
                else if (mk2 && (ins.Opcode == Opcode.PUSH_CONST_S16 || ins.Opcode == Opcode.PUSH_CONST_U24 || ins.Opcode == Opcode.PUSH_CONST_U32))
                {
                    sb.Append(ins.GetOperandsAsUInt);
                }
                else if (ins.Opcode == Opcode.PUSH_CONST_U8_U8)
                {
                    sb.Append(ins.GetOperand(0));
                    sb.Append(ins.GetOperand(1));
                }
                else if (ins.Opcode == Opcode.PUSH_CONST_U8_U8_U8)
                {
                    sb.Append(ins.GetOperand(0));
                    sb.Append(ins.GetOperand(1));
                    sb.Append(ins.GetOperand(2));
                }
                else if (ins.Opcode == Opcode.NATIVE)
                {
                    sb.Append(ins.GetNativeParams);
                    sb.Append(ins.GetNativeReturns);

                    if (mk2)
                    {
                        sb.Append(ScriptFile.X64NativeTable.GetNativeHashFromIndex(ins.GetNativeIndex));
                    }
                }
                else if (ins.Opcode == Opcode.STRING)
                {
                    if (lastIns != null && (lastIns.Opcode == Opcode.PUSH_CONST_U8 || lastIns.Opcode == Opcode.PUSH_CONST_U24 || lastIns.Opcode == Opcode.PUSH_CONST_U32))
                        sb.Append(ScriptFile.StringTable[lastIns.GetOperandsAsInt]);
                }

                if (i > 22)
                    break;

                lastIns = ins;
            }

            return Utils.Joaat(sb.ToString());
        }

        internal void HintReturnType(ref TypeContainer container) => ReturnType.HintType(ref container);

        /// <summary>
        /// Disposes of the function and returns the function text
        /// </summary>
        /// <returns>The whole function high level code</returns>
        public override string ToString()
        {
            var str = FunctionHeader() + Environment.NewLine + MainTree.ToString();
            LineCount = Regex.Matches(str, Environment.NewLine).Count + 1;
            return str;
        }

        /// <summary>
        /// Gets the first line of the function Declaration
        /// return type + name + params
        /// </summary>
        public string FunctionHeader()
        {
            StringBuilder working = new();

            //extract return type of function
            foreach (var comment in comments)
            {
                working.AppendLine("// " + comment);
            }

            if (NumReturns == 0)
                working.Append("void ");
            else if (NumReturns == 1)
            {
                working.Append(ReturnType.Type.ReturnType);
            }

            else if (NumReturns == 3)
                working.Append("Vector3 ");

            else if (NumReturns > 1)
            {
                if (ReturnType.Type == Types.STRING)
                {
                    working.Append("char[" + (NumReturns * 4).ToString() + "] ");
                }
                else
                {
                    working.Append("struct<" + NumReturns.ToString() + "> ");
                }
            }
            else
                throw new DecompilingException("Unexpected return count");

            Params.checkvars();

            working.Append(Name);
            working.Append("(" + Params.GetPDec() + ")");
            if (Properties.Settings.Default.IncludeFunctionPosition || Properties.Settings.Default.IncludeFunctionHash)
                working.Append(" //");

            if (Properties.Settings.Default.IncludeFunctionPosition)
                working.Append($" Position - 0x{Location.ToString("X")} ({Location.ToString()})");

            if (Properties.Settings.Default.IncludeFunctionHash)
                working.Append($" Hash - 0x{Hash.ToString("X")} ^0x{Mk2Hash.ToString("X")}");

            return working.ToString();
        }

        /// <summary>
        /// Determines if a frame variable is a parameter or a variable and returns its name
        /// </summary>
        /// <param name="index">the frame variable index</param>
        /// <returns>the name of the variable</returns>
        public string GetFrameVarName(uint index)
        {
            if (index < NumParams)
                return Params.GetVarName(index);
            else if (index < NumParams + 2)
                throw new Exception("Unexpected fvar");
            return Vars.GetVarName((uint)(index - 2 - NumParams));
        }

        /// <summary>
        /// Determines if a frame variable is a parameter or a variable and returns its index
        /// </summary>
        /// <param name="index">the frame variable index</param>
        /// <returns>The variable</returns>
        public Variable GetFrameVar(uint index)
        {
            if (index < NumParams)
                return Params.GetVarAtIndex(index);
            else if (index < NumParams + 2)
                throw new Exception("Unexpected fvar");
            return Vars.GetVarAtIndex((uint)(index - 2 - NumParams));
        }

        internal void SetFrameVarAutoName(uint index, AutoName name)
        {
            if (index < NumParams)
                return; // don't apply autonames to parameters
            else if (index < NumParams + 2)
                throw new Exception("Unexpected fvar");
            else
                Vars.GetVarAtIndex((uint)(index - 2 - NumParams)).SetAutoName(name);
        }

        public void ApplyVarAutoNames(VariableStorage storage, string prefix = "")
        {
            Dictionary<string, int> setNames = new();

            foreach (var local in storage.Vars)
            {
                if (local.Name.Length != 0)
                    continue;

                var name = prefix + local.AutoName.GetName();

                if (setNames.ContainsKey(name))
                {
                    if (local.AutoName.GetNameCollisionBehavior() == AutoName.NameCollisionBehavior.IncrementCharacter && (name[^1] + (setNames[name]-1)) < 'z')
                    {
                        setNames[name]++;
                        var chr = name[^1];
                        chr += (char)(setNames[name] - 1);
                        name = chr.ToString(); // TODO
                    }
                    else
                    {
                        setNames[name]++;
                        name += setNames[name];
                    }
                }
                else
                {
                    setNames[name] = 1;
                }

                local.Name = name;
            }
        }

        public void SetName(string name)
        {
            foreach (var func in ScriptFile.Functions)
                if (func.Name == name)
                    name += "_0";

            Name = name;
        }

        /// <summary>
        /// The block of code that the function takes up
        /// </summary>
        public List<byte> CodeBlock { get; set; }

        /// <summary>
        /// Gets the function info given the offset where its called from
        /// </summary>
        /// <param name="offset">the offset that is being called</param>
        /// <returns>basic information about the function at that offset</returns>
        public Function GetFunctionFromOffset(int offset)
        {
            foreach (var f in ScriptFile.Functions)
            {
                if (f.Location <= offset && offset <= f.MaxLocation)
                    return f;
            }

            throw new Exception("Function Not Found");
        }

        /// <summary>
        /// The method that actually decodes the function into high level
        /// </summary>
        public void Decompile()
        {
            if (DecodeStarted)
                return;

            DecodeStarted = true;
            if (Decoded)
                return;

            DecodeStatementTree(MainTree);
            ApplyVarAutoNames(Vars);
            // ApplyVarAutoNames(Params, "a_"); currently makes code less readable

            Decoded = true;
            ScriptFile.NotifyFunctionDecompiled();
        }

        /// <summary>
        /// Check if a jump is jumping out of the function
        /// if not, then add it to the list of instructions
        /// </summary>	
        private void IsJumpWithinFunctionBounds()
        {
            var cur = Offset;
            Instruction temp = new(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(2), cur);
            if (temp.GetJumpOffset > 0)
            {
                if (temp.GetJumpOffset < CodeBlock.Count)
                {
                    AddInstruction(cur, temp);
                    return;
                }
            }

            temp.NopInstruction();
            AddInstruction(cur, temp);
        }

        /// <summary>
        /// See if a dup is being used for an AND or OR
        /// if it is, dont add it (Rockstars way of doing and/or conditionals)
        /// </summary>
        /// <remarks>Do we really need this?</remarks>
        /// TODO: if I ever come back to this, this is going to be the first thing I rewrite
        /// Of course this broke again. Idk what to do with this anymore
        private void CheckDupForInstruction()
        {
            var off = 0;
            var dup = new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), Offset);
            AddInstruction(Offset, dup);

        Start:
            off += 1;
            if (Instruction.MapOpcode(CodeBlock[Offset + off]) == Opcode.NOP)
            {
                AddInstruction(Offset + off, new Instruction(Instruction.MapOpcode(CodeBlock[Offset + off]), Offset + off)); // add nop so it shows up in diassembler
                goto Start;
            }
            if (Instruction.MapOpcode(CodeBlock[Offset + off]) == Opcode.JZ)
            {
                var jz = new Instruction(Instruction.MapOpcode(CodeBlock[Offset + off]), CodeBlock.GetRange(Offset + off + 1, 2), Offset + off);
                jz.NopInstruction();
                AddInstruction(Offset + off, jz);
                dup.NopInstruction();
                Offset = Offset + off + 2;
                return;
            }
            if (Instruction.MapOpcode(CodeBlock[Offset + off]) == Opcode.INOT)
            {
                var inot = new Instruction(Instruction.MapOpcode(CodeBlock[Offset + off]), Offset + off);
                inot.NopInstruction();
                AddInstruction(Offset + off, inot);
                goto Start;
            }

            if (off != 1)
                Offset += (off - 1);

            return;
        }

        /// <summary>
        /// Gets the given amount of bytes from the codeblock at its offset
        /// while advancing its position by how ever many items it uses
        /// </summary>
        /// <param name="items">how many bytes to grab</param>
        /// <returns>the operands for the instruction</returns>
        private IEnumerable<byte> GetArray(int items)
        {
            var temp = Offset + 1;
            Offset += items;

            return CodeBlock.GetRange(temp, items);
        }

        /// <summary>
        /// Create a switch statement, then set up the rest of the decompiler to handle the rest of the switch statement
        /// </summary>
        private void HandleSwitch(Ast.StatementTree.Tree tree)
        {
            Dictionary<int, List<Ast.AstToken>> cases = new();
            Ast.AstToken case_val;
            int offset;
            int defaultloc;
            int breakloc;
            bool usedefault;
            Instruction temp;

            for (var i = 0; i < Instructions[tree.Offset].GetOperand(0); i++)
            {
                if (CodeOffsetToFunctionOffset(Instructions[tree.Offset].GetSwitchOffset(i)) <= tree.Offset)
                    throw new InvalidOperationException("Switch case offset goes backwards???");

                //Check if the case is a known hash
                case_val = new Ast.ConstantInt(this, (ulong)Instructions[tree.Offset].GetSwitchCase(i));

                //Get the offset to jump to
                offset = Instructions[tree.Offset].GetSwitchOffset(i);

                if (!cases.ContainsKey(offset))
                {
                    //unknown offset
                    cases.Add(offset, [case_val]);
                }
                else
                {
                    //This offset is known, multiple cases are jumping to this path
                    cases[offset].Add(case_val);
                }
            }

            //Not sure how necessary this step is, but just incase R* compiler doesnt order jump offsets, do it anyway
            var sorted = cases.Keys.ToList();
            sorted.Sort();

            //Hanldle(skip past) any Nops immediately after switch statement
            var tempoff = 0;
            while (Instructions[tree.Offset + 1 + tempoff].OriginalOpcode == Opcode.NOP)
                tempoff++;

            //Extract the location to jump to if no cases match
            defaultloc = Instructions[tree.Offset + 1 + tempoff].GetJumpOffset;

            //We have found the jump location, so that instruction is no longer needed and can be nopped
            Instructions[tree.Offset + 1 + tempoff].NopInstruction(); // TODO nopping stuff seems strange

            //Temporary stage
            breakloc = defaultloc;
            usedefault = true;

            //check if case last instruction is a jump to default location, if so default location is a break;
            //if not break location is where last instrcution jumps to
            for (var i = 0; i <= sorted.Count; i++)
            {
                var index = i == sorted.Count ? InstructionMap[defaultloc] - 1 : InstructionMap[sorted[i]] - 1;
                if (index - 1 == tree.Offset)
                {
                    continue;
                }

                temp = Instructions[index];
                if (temp.Opcode != Opcode.J)
                {
                    continue;
                }

                if (temp.GetJumpOffset == defaultloc)
                {
                    usedefault = false;
                    breakloc = defaultloc;
                    break;
                }

                breakloc = temp.GetJumpOffset;
            }

            if (usedefault)
            {
                //this seems to be causing some errors
                // if (allreturns)
                // usedefault = false;
            }

            if (usedefault)
            {
                //Default location found, best add it in
                if (cases.ContainsKey(defaultloc))
                {
                    //Default location shares code path with other known case
                    cases[defaultloc].Add(new Ast.Default(this));
                }
                else
                {
                    //Default location is a new code path
                    cases.Add(defaultloc, new List<Ast.AstToken>([new Ast.Default(this)]));
                }
            }

            if (CodeOffsetToFunctionOffset(breakloc) <= tree.Offset)
                throw new InvalidOperationException("Switch break offset goes backwards???");

            var @switch = new Ast.StatementTree.Switch(this, tree, tree.Offset, cases, breakloc, Stack.Pop());

            foreach (var statement in @switch.Statements)
            {
                DecodeStatementTree(statement as Ast.StatementTree.Tree);
            }

            tree.Statements.Add(@switch);
            tree.Offset = CodeOffsetToFunctionOffset(breakloc) - 1;
        }

        /// <summary>
        /// Turns the raw code into a list of instructions
        /// </summary>
        public void BuildInstructions()
        {
            Offset = 0;
            Instructions = new List<Instruction>();
            InstructionMap = new Dictionary<int, int>();
            int curoff;
            while (Offset < CodeBlock.Count)
            {
                curoff = Offset;
                switch (Instruction.MapOpcode(CodeBlock[Offset]))
                {
                    case Opcode.PUSH_CONST_U8:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(1), curoff));
                        break;
                    case Opcode.PUSH_CONST_U8_U8:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(2), curoff));
                        break;
                    case Opcode.PUSH_CONST_U8_U8_U8:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(3), curoff));
                        break;
                    case Opcode.PUSH_CONST_U32:
                    case Opcode.PUSH_CONST_F:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(4), curoff));
                        break;
                    case Opcode.DUP:
                        // Because of how rockstar codes and/or conditionals, its neater to detect dups
                        // and only add them if they are not used for conditionals
                        CheckDupForInstruction();
                        break;
                    case Opcode.NATIVE:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(3), curoff));
                        break;
                    case Opcode.ENTER:
                        var nameLen = CodeBlock[Offset + 4];
                        var ins = new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(4 + nameLen), curoff);
                        ins.NopInstruction();
                        AddInstruction(curoff, ins);
                        break;
                    case Opcode.LEAVE:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(2), curoff));
                        break;
                    case Opcode.ARRAY_U8:
                    case Opcode.ARRAY_U8_LOAD:
                    case Opcode.ARRAY_U8_STORE:
                    case Opcode.LOCAL_U8:
                    case Opcode.LOCAL_U8_LOAD:
                    case Opcode.LOCAL_U8_STORE:
                    case Opcode.STATIC_U8:
                    case Opcode.STATIC_U8_LOAD:
                    case Opcode.STATIC_U8_STORE:
                    case Opcode.IADD_U8:
                    case Opcode.IMUL_U8:
                    case Opcode.IOFFSET_U8:
                    case Opcode.IOFFSET_U8_LOAD:
                    case Opcode.IOFFSET_U8_STORE:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(1), curoff));
                        break;
                    case Opcode.PUSH_CONST_S16:
                    case Opcode.IADD_S16:
                    case Opcode.IMUL_S16:
                    case Opcode.IOFFSET_S16:
                    case Opcode.IOFFSET_S16_LOAD:
                    case Opcode.IOFFSET_S16_STORE:
                    case Opcode.ARRAY_U16:
                    case Opcode.ARRAY_U16_LOAD:
                    case Opcode.ARRAY_U16_STORE:
                    case Opcode.LOCAL_U16:
                    case Opcode.LOCAL_U16_LOAD:
                    case Opcode.LOCAL_U16_STORE:
                    case Opcode.STATIC_U16:
                    case Opcode.STATIC_U16_LOAD:
                    case Opcode.STATIC_U16_STORE:
                    case Opcode.GLOBAL_U16:
                    case Opcode.GLOBAL_U16_LOAD:
                    case Opcode.GLOBAL_U16_STORE:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(2), curoff));
                        break;
                    case Opcode.J:
                        IsJumpWithinFunctionBounds();
                        break;
                    case Opcode.JZ:
                    case Opcode.IEQ_JZ:
                    case Opcode.INE_JZ:
                    case Opcode.IGT_JZ:
                    case Opcode.IGE_JZ:
                    case Opcode.ILT_JZ:
                    case Opcode.ILE_JZ:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(2), curoff));
                        break;
                    case Opcode.CALL:
                    case Opcode.STATIC_U24:
                    case Opcode.STATIC_U24_LOAD:
                    case Opcode.STATIC_U24_STORE:
                    case Opcode.GLOBAL_U24:
                    case Opcode.GLOBAL_U24_LOAD:
                    case Opcode.GLOBAL_U24_STORE:
                    case Opcode.PUSH_CONST_U24:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(3), curoff));
                        break;
                    case Opcode.SWITCH:
                        {
                            if (Properties.Settings.Default.IsRDR2)
                            {
                                int length = (CodeBlock[Offset + 2] << 8) | CodeBlock[Offset + 1];
                                AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(length * 6 + 2), curoff));
                            }
                            else
                            {
                                int temp = CodeBlock[Offset + 1];
                                AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(temp * 6 + 1), curoff));
                            }
                            break;
                        }
                    case Opcode.TEXT_LABEL_ASSIGN_STRING:
                    case Opcode.TEXT_LABEL_ASSIGN_INT:
                    case Opcode.TEXT_LABEL_APPEND_STRING:
                    case Opcode.TEXT_LABEL_APPEND_INT:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), GetArray(1), curoff));
                        break;
                    default:
                        AddInstruction(curoff, new Instruction(Instruction.MapOpcode(CodeBlock[Offset]), curoff));
                        break;
                }

                Offset++;
            }

            Hash = GetFunctionHash();
            Mk2Hash = GetFunctionHash(true);
        }

        /// <summary>
        /// Adds an instruction to the list of instructions
        /// then adds the offset to a dictionary
        /// </summary>
        /// <param name="offset">the offset in the code</param>
        /// <param name="instruction">the instruction</param>
        private void AddInstruction(int offset, Instruction instruction)
        {
            Instructions.Add(instruction);
            InstructionMap.Add(offset, Instructions.Count - 1);
        }

        internal int CodeOffsetToFunctionOffset(int codeOffset) => InstructionMap[codeOffset];

        internal int FunctionOffsetToCodeOffset(int funcOffset) => (from p in InstructionMap where p.Value == funcOffset select p.Key).First();

        /// <summary>
        /// Decodes the instruction at the current offset
        /// </summary>
        internal const Opcode CONDITIONAL_JUMP = (Opcode)999;
        internal void DecodeStatementTree(Ast.StatementTree.Tree tree)
        {
            Stack<Ast.StatementTree.Tree> treeStack = new();
            treeStack.Push(tree);

            while (true)
            {
                switch (Instructions[tree.Offset].Opcode)
                {
                    case Opcode.NOP:
                        break;
                    case Opcode.IADD:
                        Stack.Push(new Ast.IntegerAdd(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.ISUB:
                        Stack.Push(new Ast.IntegerSub(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IMUL:
                        Stack.Push(new Ast.IntegerMul(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IDIV:
                        Stack.Push(new Ast.IntegerDiv(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IMOD:
                        Stack.Push(new Ast.IntegerMod(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.INOT:
                        Stack.Push(new Ast.IntegerNot(this, Stack.Pop()));
                        break;
                    case Opcode.INEG:
                        Stack.Push(new Ast.IntegerNeg(this, Stack.Pop()));
                        break;
                    case Opcode.IEQ:
                        Stack.Push(new Ast.IntegerEq(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.INE:
                        Stack.Push(new Ast.IntegerNe(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IGT:
                        Stack.Push(new Ast.IntegerGt(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IGE:
                        Stack.Push(new Ast.IntegerGe(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.ILT:
                        Stack.Push(new Ast.IntegerLt(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.ILE:
                        Stack.Push(new Ast.IntegerLe(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FADD:
                        Stack.Push(new Ast.FloatAdd(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FSUB:
                        Stack.Push(new Ast.FloatSub(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FMUL:
                        Stack.Push(new Ast.FloatMul(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FDIV:
                        Stack.Push(new Ast.FloatDiv(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FMOD:
                        Stack.Push(new Ast.FloatMod(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FNEG:
                        Stack.Push(new Ast.FloatNeg(this, Stack.Pop()));
                        break;
                    case Opcode.FEQ:
                        Stack.Push(new Ast.FloatEq(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FNE:
                        Stack.Push(new Ast.FloatNe(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FGT:
                        Stack.Push(new Ast.FloatGt(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FGE:
                        Stack.Push(new Ast.FloatGe(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FLT:
                        Stack.Push(new Ast.FloatLt(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.FLE:
                        Stack.Push(new Ast.FloatLe(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.VADD:
                        Stack.Push(new Ast.VectorAdd(this, Stack.PopVector(), Stack.PopVector()));
                        break;
                    case Opcode.VSUB:
                        Stack.Push(new Ast.VectorSub(this, Stack.PopVector(), Stack.PopVector()));
                        break;
                    case Opcode.VMUL:
                        Stack.Push(new Ast.VectorMul(this, Stack.PopVector(), Stack.PopVector()));
                        break;
                    case Opcode.VDIV:
                        Stack.Push(new Ast.VectorDiv(this, Stack.PopVector(), Stack.PopVector()));
                        break;
                    case Opcode.VNEG:
                        Stack.Push(new Ast.VectorNeg(this, Stack.PopVector()));
                        break;
                    case Opcode.IAND:
                        Stack.Push(new Ast.IntegerAnd(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IOR:
                        Stack.Push(new Ast.IntegerOr(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.IXOR:
                        Stack.Push(new Ast.IntegerXor(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.I2F:
                        Stack.Push(new Ast.IntToFloat(this, Stack.Pop()));
                        break;
                    case Opcode.F2I:
                        Stack.Push(new Ast.FloatToInt(this, Stack.Pop()));
                        break;
                    case Opcode.F2V:
                        var val = Stack.Pop();
                        if (val.HasSideEffects())
                            Stack.Push(new Ast.FloatToVec(this, val));
                        else
                            Stack.Push(new Ast.Vector(this, val, val, val));
                        break;
                    case Opcode.PUSH_CONST_U8:
                        Stack.Push(new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperand(0)));
                        break;
                    case Opcode.PUSH_CONST_U8_U8:
                        Stack.Push(new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperand(0)));
                        Stack.Push(new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperand(1)));
                        break;
                    case Opcode.PUSH_CONST_U8_U8_U8:
                        Stack.Push(new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperand(0)));
                        Stack.Push(new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperand(1)));
                        Stack.Push(new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperand(2)));
                        break;
                    case Opcode.PUSH_CONST_U32:
                    case Opcode.PUSH_CONST_S16:
                    case Opcode.PUSH_CONST_U24:
                        Stack.Push(new Ast.ConstantInt(this, (ulong)Instructions[tree.Offset].GetOperandsAsInt));
                        break;
                    case Opcode.PUSH_CONST_F:
                        Stack.Push(new Ast.ConstantFloat(this, Instructions[tree.Offset].GetFloat));
                        break;
                    case Opcode.DUP:
                        var _val = Stack.Pop();
                        if (_val.HasSideEffects())
                            throw new InvalidOperationException("Cannot dup token with side effects");
                        Stack.Push(_val);
                        Stack.Push(_val);
                        break;
                    case Opcode.DROP:
                        if (Stack.Peek().GetStackCount() > 1)
                        {
                            if (Stack.Peek() is Ast.FunctionCallBase)
                            {
                                (Stack.Peek() as Ast.FunctionCallBase).DropReturn();
                                break;
                            }
                            else
                            {
                                comments.Add(new("DROP: Dropped token with GetStackCount() > 1 && Peek() is not Ast.FunctionCallBase, not sure what to do, defaulting to incorrect behavior"));
                            }
                        }

                        var dropped = Stack.Pop(true);
                        if (dropped.HasSideEffects())
                            tree.Statements.Add(new Ast.Drop(this, dropped));
                        break;
                    case Opcode.NATIVE:
                        var native = new Ast.NativeCall(this, Stack.PopCount(Instructions[tree.Offset].GetNativeParams), ScriptFile.X64NativeTable.GetNativeFromIndex(Instructions[tree.Offset].GetNativeIndex),
                            ScriptFile.X64NativeTable.GetNativeHashFromIndex(Instructions[tree.Offset].GetNativeIndex), Instructions[tree.Offset].GetNativeReturns);

                        if (native.Entry != null && native.Entry.NativeHook != null && native.Entry.NativeHook.Hook(this, native.Arguments, Stack))
                            break;

                        if (native.IsStatement())
                            tree.Statements.Add(native);
                        else
                            Stack.Push(native);
                        break;
                    case Opcode.ENTER:
                        throw new InvalidOperationException("Should not find an ENTER here");
                    case Opcode.LEAVE:
                        tree.Statements.Add(new Ast.Return(this, Stack.PopCount(Instructions[tree.Offset].GetOperand(1))));
                        break;
                    case Opcode.LOAD:
                        Stack.Push(new Ast.Load(this, Stack.Pop()));
                        break;
                    case Opcode.STORE:
                        tree.Statements.Add(new Ast.Store(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.STORE_REV:
                        var value = Stack.Pop();
                        tree.Statements.Add(new Ast.Store(this, Stack.Peek(), value));
                        break;
                    case Opcode.LOAD_N:
                        Stack.Push(new Ast.LoadN(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.STORE_N:
                        var pointer = Stack.Pop();
                        var count = Stack.Pop();
                        if (count is not Ast.ConstantInt)
                            throw new InvalidOperationException("Cannot handle non-constant STORE_N count");
                        tree.Statements.Add(new Ast.StoreN(this, pointer, count, Stack.PopCount((int)(count as Ast.ConstantInt).GetValue())));
                        break;
                    case Opcode.ARRAY_U8:
                    case Opcode.ARRAY_U16:
                        Stack.Push(new Ast.Array(this, Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.ARRAY_U8_LOAD:
                    case Opcode.ARRAY_U16_LOAD:
                        Stack.Push(new Ast.ArrayLoad(this, Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.ARRAY_U8_STORE:
                    case Opcode.ARRAY_U16_STORE:
                        tree.Statements.Add(new Ast.ArrayStore(this, Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop(), Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.LOCAL_U8:
                    case Opcode.LOCAL_U16:
                        Stack.Push(new Ast.Local(this, Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.LOCAL_U8_LOAD:
                    case Opcode.LOCAL_U16_LOAD:
                        Stack.Push(new Ast.LocalLoad(this, Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.LOCAL_U8_STORE:
                    case Opcode.LOCAL_U16_STORE:
                        tree.Statements.Add(new Ast.LocalStore(this, Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop()));
                        break;
                    case Opcode.STATIC_U8:
                    case Opcode.STATIC_U16:
                    case Opcode.STATIC_U24:
                        Stack.Push(new Ast.Static(this, Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.STATIC_U8_LOAD:
                    case Opcode.STATIC_U16_LOAD:
                    case Opcode.STATIC_U24_LOAD:
                        Stack.Push(new Ast.StaticLoad(this, Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.STATIC_U8_STORE:
                    case Opcode.STATIC_U16_STORE:
                    case Opcode.STATIC_U24_STORE:
                        tree.Statements.Add(new Ast.StaticStore(this, Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop()));
                        break;
                    case Opcode.IADD_U8:
                    case Opcode.IADD_S16:
                        Stack.Push(new Ast.IntegerAdd(this, new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperandsAsUInt), Stack.Pop()));
                        break;
                    case Opcode.IMUL_U8:
                    case Opcode.IMUL_S16:
                        Stack.Push(new Ast.IntegerMul(this, new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperandsAsUInt), Stack.Pop())); ;
                        break;
                    case Opcode.IOFFSET:
                        var offset = Stack.Pop();
                        Stack.Push(new Ast.Offset(this, Stack.Pop(), offset));
                        break;
                    case Opcode.IOFFSET_U8:
                    case Opcode.IOFFSET_S16:
                        Stack.Push(new Ast.Offset(this, Stack.Pop(), new Ast.ConstantInt(this, Instructions[tree.Offset].GetOperandsAsUInt)));
                        break;
                    case Opcode.IOFFSET_U8_LOAD:
                    case Opcode.IOFFSET_S16_LOAD:
                        Stack.Push(new Ast.OffsetLoad(this, Stack.Pop(), (int)Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.IOFFSET_U8_STORE:
                    case Opcode.IOFFSET_S16_STORE:
                        tree.Statements.Add(new Ast.OffsetStore(this, Stack.Pop(), (int)Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop()));
                        break;
                    case Opcode.GLOBAL_U16:
                    case Opcode.GLOBAL_U24:
                        Stack.Push(new Ast.Global(this, Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.GLOBAL_U16_LOAD:
                    case Opcode.GLOBAL_U24_LOAD:
                        Stack.Push(new Ast.GlobalLoad(this, Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.GLOBAL_U16_STORE:
                    case Opcode.GLOBAL_U24_STORE:
                        tree.Statements.Add(new Ast.GlobalStore(this, Instructions[tree.Offset].GetOperandsAsUInt, Stack.Pop()));
                        break;
                    case Opcode.CALL:
                        {
                            var loc = Instructions[tree.Offset].GetOperandsAsInt;

                            if (!ScriptFile.FunctionAtLocation.TryGetValue(loc, out var function))
                                throw new InvalidOperationException("Cannot find function");

                            if (function.Hook != null)
                                if (function.Hook.Hook(function, Stack.PopCount(function.NumParams), Stack))
                                    break;

                            var call = new Ast.FunctionCall(this, Stack.PopCount(function.NumParams), function);
                            function.Decompile(); // this is a very bad idea that will break everything but can give better type inference??? TODO: find better way to propagate type info

                            if (call.IsStatement())
                                tree.Statements.Add(call);
                            else
                                Stack.Push(call);

                            function.Xrefs++;
                            break;
                        }
                    case Opcode.STRING:
                        Stack.Push(new Ast.String(this, Stack.Pop()));
                        break;
                    case Opcode.STRINGHASH:
                        Stack.Push(new Ast.StringHash(this, Stack.Pop()));
                        break;
                    case Opcode.TEXT_LABEL_ASSIGN_STRING:
                        tree.Statements.Add(new Ast.TextLabelAssignString(this, Stack.Pop(), Stack.Pop(), (int)Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.TEXT_LABEL_ASSIGN_INT:
                        tree.Statements.Add(new Ast.TextLabelAssignInt(this, Stack.Pop(), Stack.Pop(), (int)Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.TEXT_LABEL_APPEND_STRING:
                        tree.Statements.Add(new Ast.TextLabelAppendString(this, Stack.Pop(), Stack.Pop(), (int)Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.TEXT_LABEL_APPEND_INT:
                        tree.Statements.Add(new Ast.TextLabelAppendInt(this, Stack.Pop(), Stack.Pop(), (int)Instructions[tree.Offset].GetOperandsAsUInt));
                        break;
                    case Opcode.TEXT_LABEL_COPY:
                        var ptr = Stack.Pop();
                        var _value = Stack.Pop();
                        var _count = Stack.Pop();

                        if (_count is not Ast.ConstantInt)
                            throw new InvalidOperationException("Cannot handle non-constant TEXT_LABEL_COPY count");

                        tree.Statements.Add(new Ast.TextLabelCopy(this, ptr, Stack.PopCount((int)(_count as Ast.ConstantInt).GetValue()), _value));
                        break;
                    case Opcode.CATCH:
                    case Opcode.THROW:
                        throw new NotImplementedException();
                    case Opcode.CALLINDIRECT:
                        var location = Stack.Pop();
                        tree.Statements.Add(new Ast.IndirectCall(this, Stack.PopCount(Stack.GetCount()), location));
                        break;
                    case Opcode.PUSH_CONST_M1:
                    case Opcode.PUSH_CONST_0:
                    case Opcode.PUSH_CONST_1:
                    case Opcode.PUSH_CONST_2:
                    case Opcode.PUSH_CONST_3:
                    case Opcode.PUSH_CONST_4:
                    case Opcode.PUSH_CONST_5:
                    case Opcode.PUSH_CONST_6:
                    case Opcode.PUSH_CONST_7:
                        Stack.Push(new Ast.ConstantInt(this, (ulong)Instructions[tree.Offset].GetImmBytePush));
                        break;
                    case Opcode.PUSH_CONST_FM1:
                    case Opcode.PUSH_CONST_F0:
                    case Opcode.PUSH_CONST_F1:
                    case Opcode.PUSH_CONST_F2:
                    case Opcode.PUSH_CONST_F3:
                    case Opcode.PUSH_CONST_F4:
                    case Opcode.PUSH_CONST_F5:
                    case Opcode.PUSH_CONST_F6:
                    case Opcode.PUSH_CONST_F7:
                        Stack.Push(new Ast.ConstantFloat(this, Instructions[tree.Offset].GetImmFloatPush));
                        break;
                    case Opcode.IS_BIT_SET:
                        Stack.Push(new Ast.BitTest(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.LOCAL_LOAD_S:
                    case Opcode.STATIC_LOAD_S:
                    case Opcode.GLOBAL_LOAD_S:
                        Stack.Push(new Ast.SecureLoad(this, Stack.Pop()));
                        break;
                    case Opcode.LOCAL_STORE_S:
                    case Opcode.LOCAL_STORE_SR:
                    case Opcode.STATIC_STORE_S:
                    case Opcode.STATIC_STORE_SR:
                    case Opcode.GLOBAL_STORE_S:
                    case Opcode.GLOBAL_STORE_SR:
                        tree.Statements.Add(new Ast.SecureStore(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.STORE_N_S:
                    case Opcode.STORE_N_SR:
                        tree.Statements.Add(new Ast.SecureMemcpy(this, Stack.Pop(), Stack.Pop()));
                        break;
                    case Opcode.LOAD_N_S: // unused
                        tree.Statements.Add(new Ast.Attribute(this, Instructions[tree.Offset].Opcode.ToString()));
                        break;
                    case Opcode.SWITCH:
                        HandleSwitch(tree);
                        break;
                    case Opcode.J:
                        var t = tree;

                        while (t != null)
                        {
                            // break from switch case
                            if (t is Ast.StatementTree.Case)
                            {
                                if (Instructions[tree.Offset].GetJumpOffset == (t as Ast.StatementTree.Case).BreakOffset)
                                {
                                    tree.Statements.Add(new Ast.Break(this));
                                    goto DONE;
                                }
                            }

                            // break from while loop
                            if (t is Ast.StatementTree.While)
                            {
                                if (Instructions[tree.Offset].GetJumpOffset == (t as Ast.StatementTree.While).BreakOffset)
                                {
                                    tree.Statements.Add(new Ast.Break(this));
                                    goto DONE;
                                }
                            }

                            t = t.Parent;
                        }

                        // else
                        if (tree is Ast.StatementTree.If && Instructions[tree.Offset + 1].Offset == (tree as Ast.StatementTree.If).EndOffset && Instructions[tree.Offset].GetJumpOffset != Instructions[tree.Offset + 1].Offset)
                        {
                            var @else = new Ast.StatementTree.Else(this, tree, tree.Offset + 1, Instructions[tree.Offset].GetJumpOffset);
                            (tree as Ast.StatementTree.If).ElseTree = @else;
                            treeStack.Push(@else);

                            //if (tree.Function.Name == "func_159")
                            //	Debugger.Break();

                            tree.Parent.Offset = CodeOffsetToFunctionOffset(@else.EndOffset);
                            tree.Offset = CodeOffsetToFunctionOffset(@else.EndOffset);
                            break;
                        }

                        var tempoff = 0;
start:
//Check to see if the jump is just jumping past nops(end of code table)
//should be the only case for finding another jump now
                        if (Instructions[tree.Offset].GetJumpOffset != Instructions[tree.Offset + 1 + tempoff].Offset)
                        {
                            if (Instructions[tree.Offset + 1 + tempoff].Opcode == Opcode.NOP)
                            {
                                tempoff++;
                                goto start;
                            }
                            else if (Instructions[tree.Offset + 1 + tempoff].Opcode == Opcode.J)
                            {
                                if (Instructions[tree.Offset + 1 + tempoff].GetOperandsAsInt == 0)
                                {
                                    tempoff++;
                                    goto start;
                                }
                            }

                            tree.Statements.Add(new Ast.Jump(this, Instructions[tree.Offset].GetJumpOffset));
                            comments.Add(new("Unhandled jump detected. Output should be considered invalid"));
                            break;
                        }

DONE:
                        break;
                    case Opcode.JZ:
                        goto case CONDITIONAL_JUMP;
                    case Opcode.IEQ_JZ:
                        Stack.Push(new Ast.IntegerEq(this, Stack.Pop(), Stack.Pop()));
                        goto case CONDITIONAL_JUMP;
                    case Opcode.INE_JZ:
                        Stack.Push(new Ast.IntegerNe(this, Stack.Pop(), Stack.Pop()));
                        goto case CONDITIONAL_JUMP;
                    case Opcode.IGT_JZ:
                        Stack.Push(new Ast.IntegerGt(this, Stack.Pop(), Stack.Pop()));
                        goto case CONDITIONAL_JUMP;
                    case Opcode.IGE_JZ:
                        Stack.Push(new Ast.IntegerGe(this, Stack.Pop(), Stack.Pop()));
                        goto case CONDITIONAL_JUMP;
                    case Opcode.ILT_JZ:
                        Stack.Push(new Ast.IntegerLt(this, Stack.Pop(), Stack.Pop()));
                        goto case CONDITIONAL_JUMP;
                    case Opcode.ILE_JZ:
                        Stack.Push(new Ast.IntegerLe(this, Stack.Pop(), Stack.Pop()));
                        goto case CONDITIONAL_JUMP;
                    case CONDITIONAL_JUMP:
                        var condition = Stack.Pop();
                        condition.HintType(ref Types.BOOL.GetContainer());

                        var tr = tree;

                        if (Instructions[tree.Offset].GetOperandsAsUInt == 0 || CodeOffsetToFunctionOffset(Instructions[tree.Offset].GetJumpOffset) == tree.Offset + 1)
                        {
                            tree.Statements.Add(new Ast.Drop(this, condition));
                            break;
                        }

                        while (tr != null)
                        {
                            if (tr is Ast.StatementTree.While)
                            {
                                if (Instructions[tree.Offset].GetJumpOffset == (tr as Ast.StatementTree.While).BreakOffset)
                                {
                                    var condBreak = new Ast.StatementTree.If(this, tree, -1, condition, -1);
                                    condBreak.Statements.Add(new Ast.Break(this));
                                    tree.Statements.Add(condBreak);
                                    goto DONE_COND;
                                }
                            }

                            tr = tr.Parent;
                        }

                        var jumpLoc = Instructions[CodeOffsetToFunctionOffset(Instructions[tree.Offset].GetJumpOffset) - 1];
                        if (jumpLoc.IsWhileJump && jumpLoc.GetJumpOffset < Instructions[tree.Offset].Offset)
                        {
                            if (CodeOffsetToFunctionOffset(Instructions[tree.Offset].GetJumpOffset) <= tree.Offset)
                                throw new InvalidOperationException("Break offset <= Current offset");

                            Instructions[CodeOffsetToFunctionOffset(Instructions[tree.Offset].GetJumpOffset) - 1].NopInstruction(); // is this really required?
                            var @while = new Ast.StatementTree.While(this, tree, tree.Offset + 1, condition, Instructions[tree.Offset].GetJumpOffset);
                            treeStack.Push(@while);
                            tree.Statements.Add(@while);
                            tree.Offset = CodeOffsetToFunctionOffset(@while.BreakOffset);
                            break;
                        }
                        else
                        {
                            if (Instructions[tree.Offset].GetJumpOffset <= Instructions[tree.Offset].Offset)
                            {
                                //break;
                                throw new InvalidOperationException("Do while loops are not supported");
                            }

                            var @if = new Ast.StatementTree.If(this, tree, tree.Offset + 1, condition, Instructions[tree.Offset].GetJumpOffset);
                            treeStack.Push(@if);
                            tree.Statements.Add(@if);
                            tree.Offset = CodeOffsetToFunctionOffset(@if.EndOffset);
                            break;
                        }

DONE_COND:
                        break;
                    default:
                        throw new InvalidOperationException("Opcode not handled: " + Instructions[tree.Offset].Opcode);
                }

                if (tree == treeStack.Peek())
                    tree.Offset++;
                else
                    tree = treeStack.Peek();

                while (tree.IsTreeEnd())
                {
                    var oldTree = treeStack.Pop();

                    if (treeStack.Count == 0)
                        return;
                    tree = treeStack.Peek();
                    TryOptimizeTree(tree);
                }
            }
        }

        internal static bool IsForLoopPair(Ast.AstToken tk1, Ast.AstToken tk2)
        {
            if (tk1 is Ast.GlobalStore && tk2 is Ast.GlobalStore)
            {
                return (tk1 as Ast.GlobalStore).Index == (tk2 as Ast.GlobalStore).Index;
            }
            else if (tk1 is Ast.LocalStore && tk2 is Ast.LocalStore)
            {
                return (tk1 as Ast.LocalStore).Index == (tk2 as Ast.LocalStore).Index;
            }
            else if (tk1 is Ast.StaticStore && tk2 is Ast.StaticStore)
            {
                return (tk1 as Ast.StaticStore).Index == (tk2 as Ast.StaticStore).Index;
            }

            return false;
        }

        /// <summary>
        /// 1: Collapse if into else if statements
        /// 2: Try to create for loops
        /// </summary>
        /// <param name="tree"></param>
        internal void TryOptimizeTree(Ast.StatementTree.Tree tree)
        {
            if (tree.Statements.Count == 0)
                return;

            var lastTree = tree.Statements[^1];
            if (lastTree is Ast.StatementTree.If)
            {
                var lastIf = lastTree as Ast.StatementTree.If;
                if (lastIf.ElseTree != null)
                {
                    var @else = lastIf.ElseTree!;
                    if (@else.Statements.Count == 1 && @else.Statements[^1] is Ast.StatementTree.If)
                    {
                        var insideIf = @else.Statements[^1] as Ast.StatementTree.If;
                        var newElseif = new Ast.StatementTree.ElseIf(this, lastIf, -1, insideIf.Condition, -1)
                        {
                            Statements = insideIf.Statements
                        };
                        lastIf.ElseIfTrees.Add(newElseif);
                        foreach (var elseIf in insideIf.ElseIfTrees)
                        {
                            elseIf.Parent = lastIf;
                            lastIf.ElseIfTrees.Add(elseIf);
                        }

                        lastIf.ElseTree = insideIf.ElseTree;
                        if (lastIf.ElseTree != null)
                            lastIf.ElseTree!.Parent = lastIf;
                    }
                }
            }
            else if (lastTree is Ast.StatementTree.While && tree.Statements.Count > 1)
            {
                var lastWhile = lastTree as Ast.StatementTree.While;
                var lastSet = tree.Statements[^2];
                if (lastWhile.Statements.Count >= 1 && lastWhile.Statements[^1] is Ast.AstToken && lastSet is Ast.AstToken)
                {
                    var lastStmtInWhile = lastWhile.Statements[^1] as Ast.AstToken;
                    if (IsForLoopPair(lastSet as Ast.AstToken, lastStmtInWhile))
                    {
                        var @for = new Ast.StatementTree.For(this, tree, -1, lastSet as Ast.AstToken, lastWhile.Condition, lastStmtInWhile);
                        lastWhile.Statements.Remove(lastStmtInWhile);
                        tree.Statements.Remove(lastWhile);
                        tree.Statements.Remove(lastSet);
                        @for.Statements = lastWhile.Statements;
                        tree.Statements.Add(@for);
                        // TODO add support for continue statements (fix jumps)

                        if (lastSet is Ast.LocalStore)
                        {
                            SetFrameVarAutoName((lastSet as Ast.LocalStore).Index, new LoopIndexAutoName());
                        }
                    }
                }
            }
        }
    }
}
