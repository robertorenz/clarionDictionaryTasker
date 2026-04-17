using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    public class BatchCopyKeysCommand : AbstractMenuCommand
    {
        public override void Run()
        {
            object dict;
            string err;
            if (!DictModel.TryGetOpenDictionary(out dict, out err))
            {
                MessageBox.Show(err, "DCT Addin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new BatchCopyKeysDialog(dict))
            {
                dlg.ShowDialog();
            }
        }
    }
}
