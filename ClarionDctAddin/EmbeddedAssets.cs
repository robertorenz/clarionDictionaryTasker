using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace ClarionDctAddin
{
    // Loads embedded icon + background assets from the DLL and (best-effort)
    // registers the 24x24 icon into SharpDevelop's ResourceService so the
    // toolbar button can reference it by name.
    internal static class EmbeddedAssets
    {
        const string IconResourcePath        = "ClarionDctAddin.Resources.dictionarytasker.ico";
        const string BackgroundResourcePath  = "ClarionDctAddin.Resources.dictionarytaskerbig.png";
        public const string ToolbarIconName  = "Icons.24x24.DictTasker";

        static Icon   cachedIcon;
        static Bitmap cachedBackground;
        static Bitmap cachedToolbarBitmap;

        public static Icon LoadIcon()
        {
            if (cachedIcon != null) return cachedIcon;
            try
            {
                using (var s = Open(IconResourcePath))
                    if (s != null) cachedIcon = new Icon(s);
            }
            catch { }
            return cachedIcon;
        }

        public static Bitmap LoadBackground()
        {
            if (cachedBackground != null) return cachedBackground;
            try
            {
                using (var s = Open(BackgroundResourcePath))
                    if (s != null) cachedBackground = new Bitmap(s);
            }
            catch { }
            return cachedBackground;
        }

        public static Bitmap Load24Toolbar()
        {
            if (cachedToolbarBitmap != null) return cachedToolbarBitmap;
            try
            {
                var icon = LoadIcon();
                if (icon != null)
                {
                    using (var sized = new Icon(icon, 24, 24))
                        cachedToolbarBitmap = sized.ToBitmap();
                }
            }
            catch { }
            return cachedToolbarBitmap;
        }

        // Try to inject the toolbar bitmap into ICSharpCode.Core.ResourceService's
        // internal image cache so it can be referenced via icon="Icons.24x24.DictTasker"
        // in the .addin manifest. Best-effort; uses reflection because the exact
        // cache field name is internal to the SD version we're loaded into.
        public static bool RegisterToolbarIcon()
        {
            var bmp = Load24Toolbar();
            if (bmp == null) return false;

            Type rs = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { rs = a.GetType("ICSharpCode.Core.ResourceService", false); }
                catch { rs = null; }
                if (rs != null) break;
            }
            if (rs == null) return false;

            string[] cacheNames = { "loadedImages", "icons", "iconCache", "imageCache", "images" };
            foreach (var name in cacheNames)
            {
                var f = rs.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) continue;
                try
                {
                    var dict = f.GetValue(null) as IDictionary;
                    if (dict != null)
                    {
                        dict[ToolbarIconName] = bmp;
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        static Stream Open(string name)
        {
            var asm = typeof(EmbeddedAssets).Assembly;
            return asm.GetManifestResourceStream(name);
        }
    }
}
