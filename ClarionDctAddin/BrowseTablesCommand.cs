using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    public class BrowseTablesCommand : AbstractMenuCommand
    {
        public override void Run()
        {
            object dict;
            string error;
            if (!DictModel.TryGetOpenDictionary(out dict, out error))
            {
                MessageBox.Show(error, "DCT Addin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new TableListDialog(dict))
            {
                dlg.ShowDialog();
            }
        }
    }
}
