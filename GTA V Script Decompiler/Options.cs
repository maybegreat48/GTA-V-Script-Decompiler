using CommandLine;

namespace Decompiler
{
	class Options
	{
		[Value(0, MetaName = "input file", HelpText = "Input file to be processed.", Required = false)]
		public string FileName { get; set; }

		[Option('r', "recursive", Default = false, HelpText = "Decompile files recursively.")]
		public bool Recursive { get; set; }

		[Option('n', "native_tables", HelpText = "Don't extract native tables.")]
		public bool DontExtractNativeTables { get; set; }

		[Option('v', "verbose", HelpText = "Show which file is currently being decompiled.")]
		public bool Verbose { get; set; }
	}
}