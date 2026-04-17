using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    public class HelloCommand : AbstractMenuCommand
    {
        public override void Run()
        {
            MessageBox.Show(
                "Clarion DCT Addin loaded successfully.\r\nPhase 1 OK.",
                "DCT Addin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
