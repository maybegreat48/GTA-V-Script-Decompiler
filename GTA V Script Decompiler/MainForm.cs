#if OS_WINDOWS
using Decompiler.UI;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Decompiler
{

    public partial class MainForm : Form
    {
        private string FileName = "";
        private ScriptFile OpenFile;
        private Queue<string> CompileList;
        private readonly List<Disassembly> DisassembyWindows = new();
        private readonly FunctionPaneSorter fpnColumnSorter;

        public MainForm()
        {
            InitializeComponent();

            panel1.Size = new Size(0, panel1.Height);

            showArraySizeToolStripMenuItem.Checked = Properties.Settings.Default.ShowArraySize;
            reverseHashesToolStripMenuItem.Checked = Properties.Settings.Default.ReverseHashes;
            declareVariablesToolStripMenuItem.Checked = Properties.Settings.Default.DeclareVariables;
            shiftVariablesToolStripMenuItem.Checked = Properties.Settings.Default.ShiftVariables;
            showFuncPointerToolStripMenuItem.Checked = Properties.Settings.Default.ShowFunctionPointers;
            useMultiThreadingToolStripMenuItem.Checked = Properties.Settings.Default.UseMultithreading;
            includeFunctionPositionToolStripMenuItem.Checked = Properties.Settings.Default.IncludeFunctionPosition;
            includeFunctionHashToolStripMenuItem.Checked = Properties.Settings.Default.IncludeFunctionHash;
            includeNativeNamespaceToolStripMenuItem.Checked = Properties.Settings.Default.ShowNativeNamespace;
            globalAndStructHexIndexingToolStripMenuItem.Checked = Properties.Settings.Default.HexIndex;
            uppercaseNativesToolStripMenuItem.Checked = Properties.Settings.Default.UppercaseNatives;
            isRDR2ToolStripMenuItem.Checked = Properties.Settings.Default.IsRDR2;

            showLineNumbersToolStripMenuItem.Checked = fctb1.ShowLineNumbers = Properties.Settings.Default.LineNumbers;

            ToolStripMenuItem t = null;
            switch (Program.Find_getINTType())
            {
                case Program.IntType._int:
                    t = intToolStripMenuItem;
                    break;
                case Program.IntType._uint:
                    t = uintToolStripMenuItem;
                    break;
                case Program.IntType._hex:
                    t = hexToolStripMenuItem;
                    break;
            }

            fpnColumnSorter = new();
            listView1.ListViewItemSorter = fpnColumnSorter;

            t.Checked = true;
            t.Enabled = false;

            switch (Properties.Settings.Default.LocalizedTextType)
            {
                case 0:
                    disabledToolStripMenuItem.Checked = true;
                    break;
                case 1:
                    gettextStyleToolStripMenuItem.Checked = true;
                    break;
                case 2:
                    commentStyleToolStripMenuItem.Checked = true;
                    break;
            }

            switch (Properties.Settings.Default.EnumDisplayType)
            {
                case 0:
                    disabledToolStripMenuItem1.Checked = true;
                    break;
                case 1:
                    substituteToolStripMenuItem.Checked = true;
                    break;
                case 2:
                    commentToolStripMenuItem.Checked = true;
                    break;
            }
        }

        private void UpdateStatus(string text)
        {
            toolStripStatusLabel1.Text = text;
            Application.DoEvents();
        }

        private void ResetLoadedFile()
        {
            fctb1.Clear();
            listView1.Items.Clear();

            foreach (var dis in DisassembyWindows)
                dis.Dispose();

            DisassembyWindows.Clear();

            OpenFile = null;
            GC.Collect();
        }

        private async void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new()
            {
                Filter = "GTA V Script Files|*.ysc;*.osc;*.dsc;*.psc;*.zsc;*.ysc.full;*.osc.full;*.dsc.full;*.psc.full;*.zsc.full"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                var Start = DateTime.Now;
                FileName = Path.GetFileNameWithoutExtension(ofd.FileName);

                ResetLoadedFile();

                UpdateStatus("Opening script file...");

                var progressBar = new ProgressBar("Decompile File", 1, 2);
                progressBar.Show();

                Enabled = false;

                OpenFile = new ScriptFile(ofd.OpenFile());
                await OpenFile.Decompile(progressBar);

                UpdateStatus("Decompiled script file. Time taken: " + (DateTime.Now - Start).ToString());
                MemoryStream ms = new();

                OpenFile.Save(ms, false);

                listView1.ListViewItemSorter = null;
                listView1.BeginUpdate();

                foreach (var locations in OpenFile.FunctionLines)
                {
                    listView1.Items.Add(new ListViewItem(new string[] { locations.Key.Name, locations.Value.ToString(), locations.Key.Xrefs.ToString() }));
                }

                listView1.EndUpdate();
                listView1.ListViewItemSorter = fpnColumnSorter;

                OpenFile.Close();

                StreamReader sr = new(ms);
                ms.Position = 0;

                UpdateStatus("Loading text in viewer...");

                if (!Debugger.IsAttached) // more weird thread bugs
                    await Task.Run(() => fctb1.Text = sr.ReadToEnd());
                else
                    fctb1.Text = sr.ReadToEnd();

                sr.Close();

                SetFileName(FileName);
                UpdateStatus("Ready. Time taken: " + (DateTime.Now - Start).ToString());

                progressBar.Dispose();

                Enabled = true;
                Focus();
            }
        }

        private async void directoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog fsd = new()
            {
                IsFolderPicker = true
            };
            if (fsd.ShowDialog() == CommonFileDialogResult.Ok)
            {
                await BatchDecompile(fsd.FileName);
            }
        }

        private async Task BatchDecompile(string dirPath)
        {
            CompileList = new Queue<string>();
            var tasks = new List<Task>();

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

            Enabled = false;

            var progressBar = new ProgressBar("Export Directory", 1, CompileList.Count + 1);
            progressBar.Show();

            if (Properties.Settings.Default.UseMultithreading)
            {
                for (var i = 0; i < Environment.ProcessorCount; i++)
                {
                    tasks.Add(Decompile(saveDirectory, progressBar));
                }

                await Task.WhenAll(tasks);
            }
            else
            {
                await Decompile(saveDirectory, progressBar);
            }

            progressBar.Dispose();

            Enabled = true;
            Focus();
            UpdateStatus("Directory exported. Time taken: " + (DateTime.Now - Start).ToString());
        }

        private async Task Decompile(string directory, ProgressBar progressBar)
        {
            while (CompileList.Count > 0)
            {
                string scriptToDecode;

                lock (CompileList)
                {
                    scriptToDecode = CompileList.Dequeue();
                }

                try
                {
                    await Task.Run(async () =>
                    {
                        ScriptFile scriptFile = new(File.OpenRead(scriptToDecode));
                        await scriptFile.Decompile();
                        scriptFile.Save(Path.Combine(directory, Path.GetFileNameWithoutExtension(scriptToDecode) + ".c"));
                        scriptFile.Close();
                    });
                }
                catch (Exception)
                {
                    // MessageBox.Show("Error decompiling script " + Path.GetFileNameWithoutExtension(scriptToDecode) + " - " + ex.Message);
                    throw;
                }

                progressBar.IncrementValue();
            }
        }

        private async void fileToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new()
            {
                Filter = "GTA V Script Files|*.ysc;*.osc;*.dsc;*.psc;*.zsc;*.ysc.full;*.osc.full;*.dsc.full;*.psc.full;*.zsc.full"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                var Start = DateTime.Now;

                var progressBar = new ProgressBar("Export File", 1, 2);
                progressBar.Show();

                Enabled = false;

                ScriptFile file = new(ofd.OpenFile());
                await file.Decompile(progressBar);

                file.Save(Path.Combine(Path.GetDirectoryName(ofd.FileName),
                Path.GetFileNameWithoutExtension(ofd.FileName) + ".c"));
                file.Close();

                progressBar.Dispose();

                Enabled = true;
                Focus();

                UpdateStatus("File saved. Time taken: " + (DateTime.Now - Start).ToString());
            }
        }

        #region Config Options

        private void SetLocalizedTextStyle(int style)
        {
            foreach (ToolStripMenuItem t in showLocalizedTextsToolStripMenuItem.DropDownItems)
            {
                t.Checked = false;
            }

            Properties.Settings.Default.LocalizedTextType = style;
            Properties.Settings.Default.Save();
        }

        private void disabledToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetLocalizedTextStyle(0);
            disabledToolStripMenuItem.Checked = true;
        }

        private void gettextStyleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetLocalizedTextStyle(1);
            gettextStyleToolStripMenuItem.Checked = true;
        }

        private void commentStyleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetLocalizedTextStyle(2);
            commentStyleToolStripMenuItem.Checked = true;
        }

        private void SetEnumDisplayStyle(int style)
        {
            foreach (ToolStripMenuItem t in enumStyleToolStripMenuItem.DropDownItems)
            {
                t.Checked = false;
            }

            Properties.Settings.Default.EnumDisplayType = style;
            Properties.Settings.Default.Save();
            Program.EnumDisplayTypeCache = style;

        }

        private void disabledToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            SetEnumDisplayStyle(0);
            disabledToolStripMenuItem1.Checked = true;
        }

        private void substituteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetEnumDisplayStyle(1);
            substituteToolStripMenuItem.Checked = true;
        }

        private void commentToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetEnumDisplayStyle(2);
            commentToolStripMenuItem.Checked = true;
        }

        private void intstylechanged(object sender, EventArgs e)
        {
            var clicked = (ToolStripMenuItem)sender;
            foreach (ToolStripMenuItem t in intStyleToolStripMenuItem.DropDownItems)
            {
                t.Enabled = true;
                t.Checked = false;
            }

            clicked.Checked = true;
            clicked.Enabled = false;
            Properties.Settings.Default.IntStyle = clicked.Text.ToLower();
            Program.Find_getINTType();
        }

        private void showArraySizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showArraySizeToolStripMenuItem.Checked = !showArraySizeToolStripMenuItem.Checked;
            Properties.Settings.Default.ShowArraySize = showArraySizeToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void reverseHashesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            reverseHashesToolStripMenuItem.Checked = !reverseHashesToolStripMenuItem.Checked;
            Properties.Settings.Default.ReverseHashes = reverseHashesToolStripMenuItem.Checked;
            Program.ShouldReverseHashes = reverseHashesToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void declareVariablesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            declareVariablesToolStripMenuItem.Checked = !declareVariablesToolStripMenuItem.Checked;
            Properties.Settings.Default.DeclareVariables = declareVariablesToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void shiftVariablesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            shiftVariablesToolStripMenuItem.Checked = !shiftVariablesToolStripMenuItem.Checked;
            Properties.Settings.Default.ShiftVariables = shiftVariablesToolStripMenuItem.Checked;
            Program.ShouldShiftVariables = shiftVariablesToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void showLineNumbersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showLineNumbersToolStripMenuItem.Checked = !showLineNumbersToolStripMenuItem.Checked;
            Properties.Settings.Default.LineNumbers = showLineNumbersToolStripMenuItem.Checked;
            fctb1.ShowLineNumbers = showLineNumbersToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void showFuncPointerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showFuncPointerToolStripMenuItem.Checked = !showFuncPointerToolStripMenuItem.Checked;
            Properties.Settings.Default.ShowFunctionPointers = showFuncPointerToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void includeNativeNamespaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            includeNativeNamespaceToolStripMenuItem.Checked = !includeNativeNamespaceToolStripMenuItem.Checked;
            Properties.Settings.Default.ShowNativeNamespace = includeNativeNamespaceToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void globalAndStructHexIndexingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            globalAndStructHexIndexingToolStripMenuItem.Checked = !globalAndStructHexIndexingToolStripMenuItem.Checked;
            Properties.Settings.Default.HexIndex = globalAndStructHexIndexingToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void useMultiThreadingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            useMultiThreadingToolStripMenuItem.Checked = !useMultiThreadingToolStripMenuItem.Checked;
            Properties.Settings.Default.UseMultithreading = useMultiThreadingToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void includeFunctionPositionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            includeFunctionPositionToolStripMenuItem.Checked = !includeFunctionPositionToolStripMenuItem.Checked;
            Properties.Settings.Default.IncludeFunctionPosition = includeFunctionPositionToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void includeFunctionHashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            includeFunctionHashToolStripMenuItem.Checked = !includeFunctionHashToolStripMenuItem.Checked;
            Properties.Settings.Default.IncludeFunctionHash = includeFunctionHashToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void uppercaseNativesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            uppercaseNativesToolStripMenuItem.Checked = !uppercaseNativesToolStripMenuItem.Checked;
            Properties.Settings.Default.UppercaseNatives = uppercaseNativesToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void isRDR2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            isRDR2ToolStripMenuItem.Checked = !isRDR2ToolStripMenuItem.Checked;
            Properties.Settings.Default.IsRDR2 = isRDR2ToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
            Application.Restart();
            Environment.Exit(0);
        }

        #endregion

        #region Function Location

        private bool opening = false;

        private void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (listView1.SelectedItems.Count == 1)
            {
                var num = Convert.ToInt32(listView1.SelectedItems[0].SubItems[1].Text);
                fctb1.Selection = new FastColoredTextBoxNS.Range(fctb1, 0, num, 0, num);
                fctb1.DoSelectionVisible();
            }
        }

        private void listView1_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Determine if clicked column is already the column that is being sorted.
            if (e.Column == fpnColumnSorter.SortColumn)
            {
                // Reverse the current sort direction for this column.
                fpnColumnSorter.Order = fpnColumnSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                fpnColumnSorter.SortColumn = e.Column;
                fpnColumnSorter.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            listView1.Sort();
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            opening = !opening;

            panel1.Size = !opening ? new Size(0, panel1.Size.Height) : new Size(240, panel1.Size.Height);

            columnHeader1.Width = 80;
            columnHeader2.Width = 76;
        }

        #endregion

        private void resetGlobalTypesToolStripMenuItem_Click(object sender, EventArgs e) => Program.GlobalTypeMgr.Reset();

        private void entitiesToolStripMenuItem_Click(object sender, EventArgs e) => Program.Hashes.Export_Entities();

        private void closeToolStripMenuItem_Click(object sender, EventArgs e) => Close();

        private void expandAllBlocksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UpdateStatus("Expanding all blocks...");
            fctb1.ExpandAllFoldingBlocks();
            UpdateStatus("Ready");
        }

        private void collaspeAllBlocksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UpdateStatus("Collasping all blocks...");
            fctb1.CollapseAllFoldingBlocks();
            UpdateStatus("Ready");
        }

        private void fctb1_MouseClick(object sender, MouseEventArgs e)
        {
            opening = false;

            if (e.Button == MouseButtons.Right)
            {
                var pos = fctb1.PointToPosition(fctb1.PointToClient(Cursor.Position));

                if (fctb1.SelectionLength == 0)
                {
                    fctb1.SelectionStart = pos;
                }

                cmsText.Show();
            }
        }

        private void SetFileName(string name)
        {
            if (name == null)
                Text = "GTA V High Level Decompiler";
            if (name.Length == 0)
                Text = "GTA V High Level Decompiler";
            else
            {
                if (name.Contains('.'))
                    name = name.Remove(name.IndexOf('.'));
                Text = "GTA V High Level Decompiler - " + name;
            }
        }

        private void saveCFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new()
            {
                Filter = "Decompiled Script Files|*.c;*.c4;*.sc;*.sch\"",
                FileName = FileName + ".c"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                fctb1.SaveToFile(sfd.FileName, System.Text.Encoding.Default);
                MessageBox.Show("File Saved");
            }
        }

        private void cmsText_Opening(object sender, CancelEventArgs e)
        {
            GetContextItems();

            if (cmsText.Items.Count == 0)
                e.Cancel = true;
        }

        private bool islegalchar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private string GetWordAtCursor()
        {
            var line = fctb1.Lines[fctb1.Selection.Start.iLine];
            if (line.Length == 0)
                return "";
            var pos = fctb1.Selection.Start.iChar;
            if (pos == line.Length)
                return "";
            int min = pos, max = pos;
            while (min > 0)
            {
                if (islegalchar(line[min - 1]))
                    min--;
                else
                    break;
            }

            var len = line.Length;
            while (max < len)
            {
                if (islegalchar(line[max]))
                    max++;
                else
                    break;
            }

            return line[min..max];
        }

        private void GetContextItems()
        {
            var word = GetWordAtCursor();
            cmsText.Items.Clear();
            foreach (ListViewItem lvi in listView1.Items)
            {
                if (lvi.Text == word)
                {
                    cmsText.Items.Add(new ToolStripMenuItem("Goto Declaration (" + lvi.Text + ")", null,
                        new EventHandler(delegate (object o, EventArgs a)
                        {
                            var num = Convert.ToInt32(lvi.SubItems[1].Text);
                            fctb1.Selection = new FastColoredTextBoxNS.Range(fctb1, 0, num, 0, num);
                            fctb1.DoSelectionVisible();
                        }), Keys.F12));

                    cmsText.Items.Add(new ToolStripMenuItem("Disassemble (" + lvi.Text + ")", null,
                        new EventHandler(delegate (object o, EventArgs a)
                        {
                            foreach (var func in OpenFile.Functions)
                            {
                                if (func.Name == word)
                                {
                                    var dis = new Disassembly(func);
                                    dis.Show();
                                    DisassembyWindows.Add(dis);
                                    break;
                                }
                            }
                        }), Keys.F10));
                }
            }
        }

        private void stringsTableToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new()
            {
                Title = "Select location to save string table",
                Filter = "Text files|*.txt|All Files|*.*",
                FileName = (FileName.Contains('.') ? FileName.Remove(FileName.IndexOf('.')) : FileName) + "(Strings).txt"
            };
            if (sfd.ShowDialog() != DialogResult.OK)
                return;
            var sw = File.CreateText(sfd.FileName);
            foreach (var line in OpenFile.GetStringTable())
            {
                sw.WriteLine(line);
            }

            sw.Close();
            MessageBox.Show("File Saved");
        }

        private void nativeTableToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new()
            {
                Title = "Select location to save native table",
                Filter = "Text files|*.txt|All Files|*.*",
                FileName = (FileName.Contains('.') ? FileName.Remove(FileName.IndexOf('.')) : FileName) + "(natives).txt"
            };
            if (sfd.ShowDialog() != DialogResult.OK)
                return;
            var sw = File.CreateText(sfd.FileName);
            foreach (var line in OpenFile.GetNativeTable())
            {
                sw.WriteLine(line);
            }

            sw.Close();
            MessageBox.Show("File Saved");
        }

        private void navigateForwardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!fctb1.NavigateForward())
            {
                MessageBox.Show("Error, cannont navigate forwards anymore");
            }
        }

        private void navigateBackwardsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!fctb1.NavigateBackward())
            {
                MessageBox.Show("Error, cannont navigate backwards anymore");
            }
        }
    }
}
#endif // OS_WINDOWS
