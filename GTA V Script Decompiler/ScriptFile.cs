using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Decompiler
{
    public class ScriptFile
    {
        public List<byte> CodeTable;
        public StringTable StringTable;
        public NativeTable X64NativeTable;
        private int offset = 0;
        public List<Function> Functions;
        private readonly Stream file;
        public ScriptHeader Header;
        internal VariableStorage Statics;
#if OS_WINDOWS
        internal ProgressBar? ProgressBar = null;
#endif // OS_WINDOWS

        public Dictionary<int, Function> FunctionAtLocation = new();
        public Dictionary<Function, int> FunctionLines = new();

        public ScriptFile(Stream scriptStream)
        {
            file = scriptStream;
            Header = ScriptHeader.Generate(scriptStream);
            StringTable = new StringTable(scriptStream, Header.StringTableOffsets, Header.StringBlocks, Header.StringsSize);
            X64NativeTable = new NativeTable(scriptStream, Header.NativesOffset + Header.RSC7Offset, Header.NativesCount, Header.CodeLength);

            CodeTable = new List<byte>();
            for (var i = 0; i < Header.CodeBlocks; i++)
            {
                var tablesize = ((i + 1) * 0x4000 >= Header.CodeLength) ? Header.CodeLength % 0x4000 : 0x4000;
                var working = new byte[tablesize];
                scriptStream.Position = Header.CodeTableOffsets[i];
                scriptStream.Read(working, 0, tablesize);
                CodeTable.AddRange(working);
            }
        }

#if OS_WINDOWS
        public async Task Decompile(ProgressBar bar = null)
        {
            ProgressBar = bar;
#else
        public async Task Decompile()
        {
#endif // OS_WINDOWS

            GetStaticInfo();

            Functions = new List<Function>();
            GetFunctions();

            Statics.checkvars();

            foreach (var func in Functions)
            {
                func.BuildInstructions();
                Program.FunctionDB.Visit(func);

                foreach (var hook in Program.FunctionHooks)
                    if (hook.Hashes.Contains(func.Hash))
                        func.Hook = hook;
            }

#if OS_WINDOWS
            bar?.SetMax(Functions.Count + 1);
#endif // OS_WINDOWS

            foreach (var func in Functions)
            {
                await Task.Run(
                    () => func.Decompile());
            }
        }

        public void Save(string filename)
        {
            Stream savefile = File.Create(filename);
            Save(savefile, true);
        }

        public void Save(Stream stream, bool close = false)
        {
            var i = 1;
            StreamWriter savestream = new(stream);

            if (Header.GlobalsCount > 0)
            {
                savestream.WriteLine($"// Program registers {Header.GlobalsCount & 0x3FFFF} globals at index {Header.GlobalsCount >> 18} starting from Global_{0x40000 * (Header.GlobalsCount >> 18)}");
                i++;
            }

            if (Properties.Settings.Default.DeclareVariables)
            {
                if (Header.StaticsCount > 0)
                {
                    savestream.WriteLine("#region Local Var");
                    i++;
                    foreach (var s in Statics.GetDeclaration())
                    {
                        savestream.WriteLine("\t" + s);
                        i++;
                    }

                    savestream.WriteLine("#endregion");
                    savestream.WriteLine("");
                    i += 2;
                }
            }

            foreach (var f in Functions)
            {
                var s = f.ToString();
                savestream.WriteLine(s);
                FunctionLines.Add(f, i);
                i += f.LineCount;
            }

            savestream.Flush();
            if (close)
                savestream.Close();
        }

        public void Close() => file.Close();

        public string[] GetStringTable()
        {
            List<string> table = new();
            foreach (var item in StringTable)
            {
                table.Add(item.Key.ToString() + ": " + item.Value);
            }

            return table.ToArray();
        }

        public string[] GetNativeTable() => X64NativeTable.GetNativeTable();

        public void GetFunctionCode()
        {
            for (var i = 0; i < Functions.Count - 1; i++)
            {
                var start = Functions[i].MaxLocation;
                var end = Functions[i + 1].Location;
                Functions[i].CodeBlock = CodeTable.GetRange(start, end - start);
            }

            Functions[^1].CodeBlock = CodeTable.GetRange(Functions[^1].MaxLocation, CodeTable.Count - Functions[^1].MaxLocation);
            foreach (var func in Functions)
            {
                if (Instruction.MapOpcode(func.CodeBlock[0]) != Opcode.ENTER && Instruction.MapOpcode(func.CodeBlock[^3]) != Opcode.LEAVE)
                    throw new Exception("Function has incorrect start/ends");
            }
        }

        private void advpos(int pos) => offset += pos;

        private void AddFunction(int start1, int start2)
        {
            var namelen = CodeTable[start1 + 4];
            var name = "";
            if (namelen > 0)
            {
                for (var i = 0; i < namelen; i++)
                {
                    var chr = (char)CodeTable[start1 + 5 + i];
                    if (chr != '\0')
                        name += chr;
                }

                if (name.StartsWith("__", StringComparison.Ordinal))
                    name = name.TrimStart('_');

                foreach (var fun in Functions)
                    if (fun.Name == name)
                        name += "_0";
            }
            else
            {
                name = start1 == 0 ? "main" : "func_" + Functions.Count.ToString();
            }

            int pcount = CodeTable[offset + 1];
            int tmp1 = CodeTable[offset + 2], tmp2 = CodeTable[offset + 3];
            var vcount = (tmp2 << 0x8) | tmp1;
            if (vcount < 0)
            {
                throw new Exception("Invalid local count");
            }

            var temp = start1 + 5 + namelen;
            while (Instruction.MapOpcode(CodeTable[temp]) != Opcode.LEAVE)
            {
                switch (Instruction.MapOpcode(CodeTable[temp]))
                {
                    case Opcode.PUSH_CONST_U8: temp += 1; break;
                    case Opcode.PUSH_CONST_U8_U8: temp += 2; break;
                    case Opcode.PUSH_CONST_U8_U8_U8: temp += 3; break;
                    case Opcode.PUSH_CONST_U32:
                    case Opcode.PUSH_CONST_F: temp += 4; break;
                    case Opcode.NATIVE: temp += 3; break;
                    case Opcode.ENTER: throw new Exception("Return expected");
                    case Opcode.LEAVE: throw new Exception("Return expected");
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
                    case Opcode.IOFFSET_U8_STORE: temp += 1; break;
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
                    case Opcode.J:
                    case Opcode.JZ:
                    case Opcode.IEQ_JZ:
                    case Opcode.INE_JZ:
                    case Opcode.IGT_JZ:
                    case Opcode.IGE_JZ:
                    case Opcode.ILT_JZ:
                    case Opcode.ILE_JZ: temp += 2; break;
                    case Opcode.CALL:
                    case Opcode.STATIC_U24:
                    case Opcode.STATIC_U24_LOAD:
                    case Opcode.STATIC_U24_STORE:
                    case Opcode.GLOBAL_U24:
                    case Opcode.GLOBAL_U24_LOAD:
                    case Opcode.GLOBAL_U24_STORE:
                    case Opcode.PUSH_CONST_U24: temp += 3; break;
                    case Opcode.SWITCH:
                        {
                            if (Properties.Settings.Default.IsRDR2)
                            {
                                int length = (CodeTable[temp + 2] << 8) | CodeTable[temp + 1];
                                temp += 2 + 6 * length;
                            }
                            else
                                temp += 1 + 6 * CodeTable[temp + 1];
                            break;
                        }
                    case Opcode.TEXT_LABEL_ASSIGN_STRING:
                    case Opcode.TEXT_LABEL_ASSIGN_INT:
                    case Opcode.TEXT_LABEL_APPEND_STRING:
                    case Opcode.TEXT_LABEL_APPEND_INT: temp += 1; break;
                }

                temp += 1;
            }

            int rcount = CodeTable[temp + 2];
            var Location = start2;
            if (start1 == start2)
            {
                var func = new Function(this, name, pcount, vcount, rcount, Location);
                Functions.Add(func);
                FunctionAtLocation[Location] = func;
            }
            else
            {
                var func = new Function(this, name, pcount, vcount, rcount, Location, start1);
                Functions.Add(func);
                FunctionAtLocation[Location] = func;
            }
        }

        private void GetFunctions()
        {
            var returnpos = -3;
            while (offset < CodeTable.Count)
            {
                switch (Instruction.MapOpcode(CodeTable[offset]))
                {
                    case Opcode.PUSH_CONST_U8: advpos(1); break;
                    case Opcode.PUSH_CONST_U8_U8: advpos(2); break;
                    case Opcode.PUSH_CONST_U8_U8_U8: advpos(3); break;
                    case Opcode.PUSH_CONST_U32:
                    case Opcode.PUSH_CONST_F: advpos(4); break;
                    case Opcode.NATIVE: advpos(3); break;
                    case Opcode.ENTER: AddFunction(offset, returnpos + 3); ; advpos(CodeTable[offset + 4] + 4); break;
                    case Opcode.LEAVE: returnpos = offset; advpos(2); break;
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
                    case Opcode.IOFFSET_U8_STORE: advpos(1); break;
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
                    case Opcode.J:
                    case Opcode.JZ:
                    case Opcode.IEQ_JZ:
                    case Opcode.INE_JZ:
                    case Opcode.IGT_JZ:
                    case Opcode.IGE_JZ:
                    case Opcode.ILT_JZ:
                    case Opcode.ILE_JZ: advpos(2); break;
                    case Opcode.CALL:
                    case Opcode.STATIC_U24:
                    case Opcode.STATIC_U24_LOAD:
                    case Opcode.STATIC_U24_STORE:
                    case Opcode.GLOBAL_U24:
                    case Opcode.GLOBAL_U24_LOAD:
                    case Opcode.GLOBAL_U24_STORE:
                    case Opcode.PUSH_CONST_U24: advpos(3); break;
                    case Opcode.SWITCH:
                        {
                            if (Properties.Settings.Default.IsRDR2)
                            {
                                int length = (CodeTable[offset + 2] << 8) | CodeTable[offset + 1];
                                advpos(2 + 6 * length);
                            }
                            else
                                advpos(1 + 6 * CodeTable[offset + 1]);
                            break;
                        }
                    case Opcode.TEXT_LABEL_ASSIGN_STRING:
                    case Opcode.TEXT_LABEL_ASSIGN_INT:
                    case Opcode.TEXT_LABEL_APPEND_STRING:
                    case Opcode.TEXT_LABEL_APPEND_INT: advpos(1); break;
                }

                advpos(1);
            }

            offset = 0;
            GetFunctionCode();
        }

        private void GetStaticInfo()
        {
            Statics = new VariableStorage(VariableStorage.ListType.Statics);
            Statics.SetScriptParamCount(Header.ParameterCount);
            IO.Reader reader = new(file);
            reader.BaseStream.Position = Header.StaticsOffset + Header.RSC7Offset;
            for (var count = 0; count < Header.StaticsCount; count++)
            {
                Statics.AddVar(reader.ReadInt64());
            }
        }

        public void NotifyFunctionDecompiled()
        {
#if OS_WINDOWS
            if (!Debugger.IsAttached) // Cross-thread operation not valid: Control 'progressBar1' accessed from a thread other than the thread it was created on. ???
                ProgressBar?.IncrementValue();
#endif // OS_WINDOWS
        }
    }
}
