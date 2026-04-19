using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Compare the currently-open dictionary against a *.tasker-snap snapshot.
    // Clarion can only hold one .DCT open at a time, so the workflow is:
    //   1. Save a snapshot of the current dict.
    //   2. Modify the dict (or open a different dict in Clarion).
    //   3. Load the saved snapshot here — tree shows Added / Removed / Changed tables.
    internal class CompareDictionariesDialog : Form
    {
        static readonly Color BgColor      = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor   = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor  = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor   = Color.FromArgb(100, 115, 135);
        static readonly Color AddedColor   = Color.FromArgb(40, 130, 40);
        static readonly Color RemovedColor = Color.FromArgb(170, 40, 40);
        static readonly Color ChangedColor = Color.FromArgb(190, 110, 20);

        readonly object dict;
        Label    lblSnapshot, lblSummary;
        TreeView tree;
        Button   btnExport;
        DictSnapshot snapshot;
        DictSnapshot liveCached;

        public CompareDictionariesDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Compare dictionaries - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 740;
            MinimumSize = new Size(860, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;
            ShowIcon = false; ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Show;

            var header = new Label
            {
                Dock = DockStyle.Top, Height = 48,
                BackColor = HeaderColor, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Compare dictionaries   live: " + DictModel.GetDictionaryName(dict)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            var btnLiveVsLive = new Button { Text = "Compare to another open dict...", Width = 230, Height = 30, Left = 0,   Top = 6, FlatStyle = FlatStyle.System };
            btnLiveVsLive.Click += delegate { CompareToOtherOpenDict(); };
            var btnSave = new Button { Text = "Save current as snapshot...", Width = 210, Height = 30, Left = 240, Top = 6, FlatStyle = FlatStyle.System };
            btnSave.Click += delegate { SaveSnapshot(); };
            var btnLoad = new Button { Text = "Load snapshot && compare...", Width = 210, Height = 30, Left = 460, Top = 6, FlatStyle = FlatStyle.System };
            btnLoad.Click += delegate { LoadAndCompare(); };
            btnExport = new Button { Text = "Export diff as Markdown...",    Width = 210, Height = 30, Left = 680, Top = 6, FlatStyle = FlatStyle.System, Enabled = false };
            btnExport.Click += delegate { ExportMarkdown(); };
            toolbar.Controls.Add(btnLiveVsLive);
            toolbar.Controls.Add(btnSave);
            toolbar.Controls.Add(btnLoad);
            toolbar.Controls.Add(btnExport);

            lblSnapshot = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "Either pick another open .DCT tab, or load a saved *.tasker-snap snapshot."
            };
            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = ""
            };

            tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                HideSelection = false,
                ShowNodeToolTips = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(tree);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(lblSnapshot);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        void CompareToOtherOpenDict()
        {
            var all = DictModel.GetAllOpenDictionaries();
            var others = new List<object>();
            foreach (var d in all) if (!ReferenceEquals(d, dict)) others.Add(d);

            if (others.Count == 0)
            {
                MessageBox.Show(this,
                    "Only one dictionary is open in Clarion right now.\r\n\r\n"
                    + "Open a second .DCT tab (File → Open Dictionary...) and try again, "
                    + "or use \"Load snapshot & compare...\" to compare against a saved snapshot.",
                    "Compare dictionaries", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            object other;
            if (others.Count == 1) other = others[0];
            else
            {
                using (var picker = new OtherDictPickerDialog(others))
                {
                    if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null) return;
                    other = picker.Selected;
                }
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                // Snapshot both sides right now; the diff engine works on snapshots.
                snapshot  = DictSnapshot.CaptureFromLive(other);
                liveCached = DictSnapshot.CaptureFromLive(dict);
                lblSnapshot.Text = "Comparing: "
                    + DictModel.GetDictionaryName(other) + "   =>   "
                    + DictModel.GetDictionaryName(dict) + "   (both live)";
                var diff = DictDiff.Compute(snapshot, liveCached);
                RenderDiff(diff);
                btnExport.Enabled = true;
            }
            finally { Cursor = Cursors.Default; }
        }

        void SaveSnapshot()
        {
            Cursor = Cursors.WaitCursor;
            DictSnapshot snap;
            try { snap = DictSnapshot.CaptureFromLive(dict); }
            finally { Cursor = Cursors.Default; }

            var suggested = (string.IsNullOrEmpty(snap.DictName) ? "dictionary" : snap.DictName)
                          + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tasker-snap";
            using (var dlg = new SaveFileDialog
            {
                Filter = "Dictionary snapshot (*.tasker-snap)|*.tasker-snap|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    snap.Save(dlg.FileName);
                    MessageBox.Show(this,
                        "Snapshot saved:\r\n" + dlg.FileName + "\r\n\r\n" +
                        snap.Tables.Count + " table(s) captured.",
                        "Compare dictionaries", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message,
                        "Compare dictionaries", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void LoadAndCompare()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Dictionary snapshot (*.tasker-snap)|*.tasker-snap|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    snapshot = DictSnapshot.Load(dlg.FileName);
                    lblSnapshot.Text = "Snapshot: " + Path.GetFileName(dlg.FileName)
                        + "   captured " + snapshot.TakenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        + "   from: " + (string.IsNullOrEmpty(snapshot.DictName) ? "?" : snapshot.DictName);
                    Recompute();
                    btnExport.Enabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Load failed: " + ex.Message,
                        "Compare dictionaries", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void Recompute()
        {
            if (snapshot == null) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                liveCached = DictSnapshot.CaptureFromLive(dict);
                var diff = DictDiff.Compute(snapshot, liveCached);
                RenderDiff(diff);
            }
            finally { Cursor = Cursors.Default; }
        }

        void RenderDiff(DictDiff.Result d)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();

            int addedT = d.AddedTables.Count;
            int removedT = d.RemovedTables.Count;
            int changedT = d.ChangedTables.Count;

            lblSummary.Text = string.Format("Tables: +{0} added, -{1} removed, ~{2} changed, ={3} unchanged.",
                addedT, removedT, changedT, d.UnchangedTableCount);

            if (addedT > 0)
            {
                var n = tree.Nodes.Add("Added tables  (" + addedT + ")");
                n.ForeColor = AddedColor;
                foreach (var t in d.AddedTables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var child = n.Nodes.Add(t.Name + "   [fields: " + t.Fields.Count
                        + ", keys: " + t.Keys.Count + ", relations: " + t.Relations.Count + "]");
                    child.ForeColor = AddedColor;
                }
            }
            if (removedT > 0)
            {
                var n = tree.Nodes.Add("Removed tables  (" + removedT + ")");
                n.ForeColor = RemovedColor;
                foreach (var t in d.RemovedTables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var child = n.Nodes.Add(t.Name + "   [fields: " + t.Fields.Count
                        + ", keys: " + t.Keys.Count + ", relations: " + t.Relations.Count + "]");
                    child.ForeColor = RemovedColor;
                }
            }
            if (changedT > 0)
            {
                var n = tree.Nodes.Add("Changed tables  (" + changedT + ")");
                n.ForeColor = ChangedColor;
                foreach (var c in d.ChangedTables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var tn = n.Nodes.Add(c.Name);
                    tn.ForeColor = ChangedColor;
                    AddAttrsNode(tn, c);
                    AddFieldsGroup(tn, c);
                    AddKeysGroup(tn, c);
                    AddRelationsGroup(tn, c);
                }
                n.Expand();
            }

            if (addedT == 0 && removedT == 0 && changedT == 0)
                tree.Nodes.Add("No structural differences.").ForeColor = MutedColor;

            if (tree.Nodes.Count > 0) tree.Nodes[0].Expand();
            tree.EndUpdate();
        }

        static void AddAttrsNode(TreeNode parent, DictDiff.TableChange c)
        {
            if (!c.AttributesChanged) return;
            var n = parent.Nodes.Add("Attributes"); n.ForeColor = ChangedColor;
            if (c.BeforeTable.Prefix != c.AfterTable.Prefix)
                n.Nodes.Add("prefix: \"" + c.BeforeTable.Prefix + "\"  =>  \"" + c.AfterTable.Prefix + "\"").ForeColor = ChangedColor;
            if (c.BeforeTable.Driver != c.AfterTable.Driver)
                n.Nodes.Add("driver: \"" + c.BeforeTable.Driver + "\"  =>  \"" + c.AfterTable.Driver + "\"").ForeColor = ChangedColor;
            if (c.BeforeTable.Description != c.AfterTable.Description)
                n.Nodes.Add("description changed").ForeColor = ChangedColor;
        }

        static void AddFieldsGroup(TreeNode parent, DictDiff.TableChange c)
        {
            if (c.AddedFields.Count == 0 && c.RemovedFields.Count == 0 && c.ChangedFields.Count == 0) return;
            var n = parent.Nodes.Add("Fields");
            if (c.AddedFields.Count > 0)
            {
                var sub = n.Nodes.Add("Added (" + c.AddedFields.Count + ")"); sub.ForeColor = AddedColor;
                foreach (var f in c.AddedFields.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(f.Label + "   " + FieldSig(f)); x.ForeColor = AddedColor;
                }
            }
            if (c.RemovedFields.Count > 0)
            {
                var sub = n.Nodes.Add("Removed (" + c.RemovedFields.Count + ")"); sub.ForeColor = RemovedColor;
                foreach (var f in c.RemovedFields.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(f.Label + "   " + FieldSig(f)); x.ForeColor = RemovedColor;
                }
            }
            if (c.ChangedFields.Count > 0)
            {
                var sub = n.Nodes.Add("Changed (" + c.ChangedFields.Count + ")"); sub.ForeColor = ChangedColor;
                foreach (var ch in c.ChangedFields.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(ch.Label + "   " + FieldSig(ch.Before) + "   =>   " + FieldSig(ch.After));
                    x.ForeColor = ChangedColor;
                }
            }
        }

        static void AddKeysGroup(TreeNode parent, DictDiff.TableChange c)
        {
            if (c.AddedKeys.Count == 0 && c.RemovedKeys.Count == 0 && c.ChangedKeys.Count == 0) return;
            var n = parent.Nodes.Add("Keys");
            if (c.AddedKeys.Count > 0)
            {
                var sub = n.Nodes.Add("Added (" + c.AddedKeys.Count + ")"); sub.ForeColor = AddedColor;
                foreach (var k in c.AddedKeys.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(k.Name + "   " + KeySig(k)); x.ForeColor = AddedColor;
                }
            }
            if (c.RemovedKeys.Count > 0)
            {
                var sub = n.Nodes.Add("Removed (" + c.RemovedKeys.Count + ")"); sub.ForeColor = RemovedColor;
                foreach (var k in c.RemovedKeys.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(k.Name + "   " + KeySig(k)); x.ForeColor = RemovedColor;
                }
            }
            if (c.ChangedKeys.Count > 0)
            {
                var sub = n.Nodes.Add("Changed (" + c.ChangedKeys.Count + ")"); sub.ForeColor = ChangedColor;
                foreach (var ch in c.ChangedKeys.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(ch.Name + "   " + KeySig(ch.Before) + "   =>   " + KeySig(ch.After));
                    x.ForeColor = ChangedColor;
                }
            }
        }

        static void AddRelationsGroup(TreeNode parent, DictDiff.TableChange c)
        {
            if (c.AddedRelations.Count == 0 && c.RemovedRelations.Count == 0 && c.ChangedRelations.Count == 0) return;
            var n = parent.Nodes.Add("Relations");
            if (c.AddedRelations.Count > 0)
            {
                var sub = n.Nodes.Add("Added (" + c.AddedRelations.Count + ")"); sub.ForeColor = AddedColor;
                foreach (var r in c.AddedRelations.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(r.Name + "   -> " + r.RelatedTable); x.ForeColor = AddedColor;
                }
            }
            if (c.RemovedRelations.Count > 0)
            {
                var sub = n.Nodes.Add("Removed (" + c.RemovedRelations.Count + ")"); sub.ForeColor = RemovedColor;
                foreach (var r in c.RemovedRelations.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(r.Name + "   -> " + r.RelatedTable); x.ForeColor = RemovedColor;
                }
            }
            if (c.ChangedRelations.Count > 0)
            {
                var sub = n.Nodes.Add("Changed (" + c.ChangedRelations.Count + ")"); sub.ForeColor = ChangedColor;
                foreach (var ch in c.ChangedRelations.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var x = sub.Nodes.Add(ch.Name + "   " + ch.Before.RelatedTable + "  =>  " + ch.After.RelatedTable);
                    x.ForeColor = ChangedColor;
                }
            }
        }

        static string FieldSig(DictSnapshot.FieldSnap f)
        {
            return f.Type + " / " + f.Size + (string.IsNullOrEmpty(f.Picture) ? "" : " / " + f.Picture);
        }

        static string KeySig(DictSnapshot.KeySnap k)
        {
            var s = string.Join(" + ", k.Components.ToArray());
            var tags = new List<string>();
            if (k.Unique)  tags.Add("unique");
            if (k.Primary) tags.Add("primary");
            return s + (tags.Count > 0 ? "   [" + string.Join(", ", tags.ToArray()) + "]" : "");
        }

        // Small modal picker shown when more than one "other" dict is open.
        sealed class OtherDictPickerDialog : Form
        {
            public object Selected;
            ListBox lb;

            public OtherDictPickerDialog(IList<object> candidates)
            {
                Text = "Pick the OTHER dictionary";
                Width = 560; Height = 360;
                StartPosition = FormStartPosition.CenterParent;
                BackColor = BgColor;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = false; MinimizeBox = false;
                ShowIcon = false; ShowInTaskbar = false;
                MinimumSize = new Size(440, 260);

                var header = new Label
                {
                    Dock = DockStyle.Top, Height = 44,
                    BackColor = HeaderColor, ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 11F),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(16, 0, 0, 0),
                    Text = "Pick the OTHER dictionary to compare against"
                };

                lb = new ListBox
                {
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 10F),
                    IntegralHeight = false
                };
                foreach (var d in candidates) lb.Items.Add(new Wrap(d));
                if (lb.Items.Count > 0) lb.SelectedIndex = 0;
                lb.DoubleClick += delegate { Accept(); };

                var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
                var btnOk = new Button { Text = "OK", Width = 100, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                btnOk.Click += delegate { Accept(); };
                var btnCancel = new Button { Text = "Cancel", Width = 100, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                bottom.Controls.Add(btnOk);
                bottom.Controls.Add(btnCancel);

                Controls.Add(lb);
                Controls.Add(bottom);
                Controls.Add(header);
                AcceptButton = btnOk;
                CancelButton = btnCancel;
            }

            void Accept()
            {
                var sel = lb.SelectedItem as Wrap;
                if (sel == null) return;
                Selected = sel.Dict;
                DialogResult = DialogResult.OK;
                Close();
            }

            sealed class Wrap
            {
                public readonly object Dict;
                public Wrap(object d) { Dict = d; }
                public override string ToString()
                {
                    return DictModel.GetDictionaryName(Dict)
                        + "   —   " + DictModel.GetDictionaryFileName(Dict);
                }
            }
        }

        void ExportMarkdown()
        {
            if (snapshot == null || liveCached == null) return;
            var diff = DictDiff.Compute(snapshot, liveCached);
            var md = DictDiff.RenderMarkdown(snapshot, liveCached, diff);
            using (var dlg = new SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                FileName = "dict-diff-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, md);
                    MessageBox.Show(this, "Saved: " + dlg.FileName,
                        "Compare dictionaries", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message,
                        "Compare dictionaries", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
