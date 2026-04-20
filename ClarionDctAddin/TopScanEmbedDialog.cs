using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Launches TopScan.exe and reparents its top-level window into a panel in
    // this dialog via Win32 SetParent, so it appears inline instead of as a
    // separate app window. Window decorations are stripped by Win32Embed so
    // only TopScan's client area (menus, toolbar, grid) shows through. The
    // child process is terminated when the dialog closes.
    internal class TopScanEmbedDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly string tpsPath;

        Panel   host;
        Label   lblStatus;
        Process proc;
        IntPtr  childHwnd;
        Timer   poller;
        int     pollTicks;

        public TopScanEmbedDialog(string tpsPath)
        {
            this.tpsPath = tpsPath;
            BuildUi();
        }

        void BuildUi()
        {
            Text = "TopScan - " + Path.GetFileName(tpsPath);
            Width = 1160; Height = 720;
            MinimumSize = new Size(860, 460);
            StartPosition = FormStartPosition.CenterParent;
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
                Text = "TopScan (embedded)   " + tpsPath
            };

            lblStatus = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 6, 0, 0),
                Text = "Launching TopScan..."
            };

            host = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            host.Resize += delegate { ResizeChild(); };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(host);
            Controls.Add(bottom);
            Controls.Add(lblStatus);
            Controls.Add(header);
            CancelButton = btnClose;

            Shown      += delegate { StartTopScan(); };
            FormClosed += delegate { KillProc(); };
        }

        void StartTopScan()
        {
            var topScan = TopScanLauncher.FindTopScan();
            if (topScan == null)
            {
                lblStatus.Text = "TopScan.exe not found in C:\\clarion12\\bin.";
                return;
            }
            if (string.IsNullOrEmpty(tpsPath) || !File.Exists(tpsPath))
            {
                lblStatus.Text = "TPS file not found: " + tpsPath;
                return;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = topScan,
                    Arguments = "\"" + tpsPath + "\"",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(tpsPath)
                };
                proc = Process.Start(psi);
                try { proc.WaitForInputIdle(4000); } catch { }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Launch failed: " + ex.Message;
                return;
            }

            // TopScan's main HWND isn't guaranteed to exist the instant
            // Process.Start returns, so poll for it for up to ~8s.
            poller = new Timer { Interval = 100 };
            poller.Tick += PollForWindow;
            poller.Start();
        }

        void PollForWindow(object sender, EventArgs e)
        {
            pollTicks++;
            if (proc == null || proc.HasExited)
            {
                poller.Stop();
                lblStatus.Text = "TopScan exited before a window appeared.";
                return;
            }
            var hwnd = Win32Embed.FindMainWindowForProcess(proc.Id);
            if (hwnd != IntPtr.Zero)
            {
                childHwnd = hwnd;
                Win32Embed.MakeChildOf(childHwnd, host.Handle);
                ResizeChild();
                Win32Embed.Show(childHwnd);
                poller.Stop();
                lblStatus.Text = "Embedded (HWND 0x"
                    + childHwnd.ToInt64().ToString("X") + ")   -   "
                    + tpsPath;
            }
            else if (pollTicks > 80)
            {
                poller.Stop();
                lblStatus.Text = "TopScan started but its window did not appear within 8s.";
            }
        }

        void ResizeChild()
        {
            if (childHwnd == IntPtr.Zero) return;
            Win32Embed.Resize(childHwnd, 0, 0, host.ClientSize.Width, host.ClientSize.Height);
        }

        void KillProc()
        {
            try { if (poller != null) poller.Stop(); } catch { }
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    // CloseMainWindow first — polite close. If TopScan ignores
                    // it (e.g. reparented chrome makes WM_CLOSE routing weird),
                    // Kill after a short wait.
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(1500)) proc.Kill();
                }
            }
            catch { }
        }
    }
}
