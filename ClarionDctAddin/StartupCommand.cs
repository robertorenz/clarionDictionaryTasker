using System;
using System.Drawing;
using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    // Runs at /Workspace/Autostart. Two strategies to get our custom icon
    // onto the Dictionary Tasker toolbar button:
    //
    //  1. Try to inject the bitmap into ICSharpCode.Core.ResourceService's
    //     image cache under the name "Icons.24x24.DictTasker". This would
    //     let future resolutions of that name pick up our icon, but in
    //     practice the toolbar was already built before Autostart fires, so
    //     this alone is not enough.
    //
    //  2. Poll the UI with a short-interval Timer, walk every open form's
    //     ToolStrips, and replace the Image on any ToolStripItem whose
    //     ToolTipText matches our button's tooltip. Stops as soon as the
    //     swap succeeds or after ~10 seconds.
    //
    // Both are best-effort — any failure is swallowed so the addin never
    // breaks the IDE over branding.
    public class StartupCommand : AbstractCommand
    {
        const string ButtonTooltip = "Open Dictionary Tasker";

        public override void Run()
        {
            try { EmbeddedAssets.RegisterToolbarIcon(); }
            catch { }

            try { StartPollingForToolbarButton(); }
            catch { }
        }

        static void StartPollingForToolbarButton()
        {
            var img = EmbeddedAssets.Load24Toolbar();
            if (img == null) return;

            var timer = new Timer { Interval = 400 };
            int attempts = 0;
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try { done = TryPatch(img); } catch { }
                if (done || attempts > 25)
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        static bool TryPatch(Image img)
        {
            bool found = false;
            foreach (Form form in Application.OpenForms)
            {
                if (WalkAndPatch(form, img)) found = true;
            }
            return found;
        }

        static bool WalkAndPatch(Control control, Image img)
        {
            bool patched = false;

            var ts = control as ToolStrip;
            if (ts != null)
            {
                foreach (ToolStripItem item in ts.Items)
                {
                    if (string.Equals(item.ToolTipText, ButtonTooltip, StringComparison.Ordinal))
                    {
                        item.Image = img;
                        patched = true;
                    }
                }
            }

            foreach (Control child in control.Controls)
            {
                if (WalkAndPatch(child, img)) patched = true;
            }
            return patched;
        }
    }
}
