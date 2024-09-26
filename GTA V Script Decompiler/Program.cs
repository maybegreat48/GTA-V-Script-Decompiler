using Decompiler.Hooks;
using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
#if OS_WINDOWS
using System.Windows.Forms;
#endif // OS_WINDOWS
using CommandLine;

namespace Decompiler
{
	internal static class Program
	{
		public static Crossmap Crossmap;
		public static NativeDB NativeDB;
		public static FunctionDB FunctionDB;
		public static TextDB TextDB;
		public static GlobalTypeMgr GlobalTypeMgr;
		public static Hashes Hashes;

		public static FunctionHook[] FunctionHooks = FunctionHook.GetHooks();
		public static NativeHook[] NativeHooks = NativeHook.GetHooks();

		public static bool ShouldShiftVariables = Properties.Settings.Default.ShiftVariables;
		public static bool ShouldReverseHashes = Properties.Settings.Default.ReverseHashes;
		public static int EnumDisplayTypeCache = Properties.Settings.Default.EnumDisplayType;

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main(string[] args)
		{
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

			Crossmap = new Crossmap();

			Hashes = new Hashes();

			NativeDB = new NativeDB();
			NativeDB.LoadData();

			FunctionDB = new FunctionDB();

			TextDB = new TextDB();

			GlobalTypeMgr = new GlobalTypeMgr();

			if (args.Length == 0)
			{
#if OS_WINDOWS
				Application.EnableVisualStyles();
				Application.SetHighDpiMode(HighDpiMode.SystemAware);
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new MainForm());
#endif // OS_WINDOWS
			}
			else
			{
				CommandLine.Parser.Default.ParseArguments<Options>(args)
				  .WithParsed(RunOptions)
				  .WithNotParsed(HandleParseError);
			}
		}

		static void RunOptions(Options opts)
		{
			if (opts.Recursive)
			{
				BatchDecompile(opts.FileName, !opts.DontExtractNativeTables, opts.Verbose);
			}
			else
			{
				Decompile(opts.FileName, !opts.DontExtractNativeTables, opts.Verbose);
			}

			Console.WriteLine("All done & saved!");
		}

		static void Decompile(string fileName, bool extractNativeTables, bool verbose)
		{
			ScriptFile fileopen;
			var Start = DateTime.Now;
			var ext = Path.GetExtension(fileName);
			if (ext == ".full")
			{
				ext = Path.GetExtension(Path.GetFileNameWithoutExtension(fileName));
			}

			if(verbose)
			{
				Console.WriteLine("Decompiling " + fileName + "...");
			}

			try
			{
				fileopen = new ScriptFile(File.OpenRead(fileName));
				fileopen.Decompile().Wait();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error decompiling script " + ex.Message);
				return;
			}

			ExtractNativeTables(fileName, fileopen);

			Console.WriteLine("Decompiled in " + (DateTime.Now - Start).ToString());
			fileopen.Save(File.OpenWrite(fileName + ".c"), true);
		}

		static void BatchDecompile(string dirPath, bool extractNativeTables, bool verbose)
		{
			Queue<string> CompileList = new Queue<string>();

			var Start = DateTime.Now;
			var saveDirectory = Path.Combine(dirPath, "exported");
			if (!Directory.Exists(saveDirectory))
				Directory.CreateDirectory(saveDirectory);

            foreach (var file in Directory.GetFiles(dirPath, "*.ysc"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.osc"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.dsc"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.psc"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.zsc"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.ysc.full"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.osc.full"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.dsc.full"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.psc.full"))
            {
                CompileList.Enqueue(file);
            }

            foreach (var file in Directory.GetFiles(dirPath, "*.zsc.full"))
            {
                CompileList.Enqueue(file);
            }

            while (CompileList.Count > 0)
			{
				ScriptFile fileopen;
				string scriptToDecode;

				lock (CompileList)
				{
					scriptToDecode = CompileList.Dequeue();
				}

				if(verbose)
				{
					Console.WriteLine("Decompiling " + scriptToDecode + "...");
				}

				try
				{
					fileopen = new ScriptFile(File.OpenRead(scriptToDecode));
					fileopen.Decompile().Wait();
					if(extractNativeTables)
					{
						ExtractNativeTables(Path.Combine(saveDirectory, Path.GetFileNameWithoutExtension(scriptToDecode)), fileopen);
					}
					fileopen.Save(Path.Combine(saveDirectory, Path.GetFileNameWithoutExtension(scriptToDecode) + ".c"));
					fileopen.Close();
				}
				catch (Exception ex)
				{
					Console.WriteLine("Error decompiling script " + ex.Message);
					// Don't return here because ambient_mrsphilips.ysc crashes.
				}
			}

			Console.WriteLine("Directory exported. Time taken: " + (DateTime.Now - Start).ToString());
		}

		static void ExtractNativeTables(string fileName, ScriptFile fileopen)
		{
			Console.WriteLine("Extracting native table...");
			StreamWriter fw = new(File.OpenWrite(fileName + " native table.txt"));
			foreach (var nat in fileopen.X64NativeTable.NativeHashes)
			{
				var temps = nat.ToString("X");
				while (temps.Length < 16)
					temps = "0" + temps;
				fw.WriteLine(temps);
			}

			fw.Flush();
			fw.Close();
		}

		static void HandleParseError(IEnumerable<Error> errs)
		{
			Console.WriteLine("Error");
		}

		public enum IntType
		{
			_int,
			_uint,
			_hex
		}

		public static IntType Find_getINTType()
		{
			var s = Properties.Settings.Default.IntStyle;
			return s.StartsWith("int")
				? (getIntType = IntType._int)
				: s.StartsWith("uint")
				? (getIntType = IntType._uint)
				: s.StartsWith("hex") ? (getIntType = IntType._hex) : (getIntType = IntType._int);
		}

		public static IntType getIntType { get; private set; } = IntType._int;
	}
}
