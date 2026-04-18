using ICSharpCode.Core;

namespace ClarionDctAddin
{
    // Runs once during SharpDevelop startup (via /Workspace/Autostart registration).
    // Registers our embedded 24x24 icon into the resource cache so the toolbar
    // button's icon="Icons.24x24.DictTasker" resolves to our custom image.
    public class StartupCommand : AbstractCommand
    {
        public override void Run()
        {
            try { EmbeddedAssets.RegisterToolbarIcon(); }
            catch { /* non-fatal */ }
        }
    }
}
