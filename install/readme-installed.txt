Dictionary Tasker — installed
==============================

This folder holds the Clarion 12 add-in DLL + manifest:

  ClarionDctAddin.dll
  ClarionDctAddin.addin

Clarion discovers it at IDE startup and the "Dictionary Tasker" toolbar
button will appear greyed-out until you open a .DCT — then it lights up.

To uninstall, use "Apps & features" in Windows settings and look for
"Dictionary Tasker".

Settings (preferred SQL dialect, Fix keys dropdowns, etc.) are kept in
%LOCALAPPDATA%\ClarionDctAddin\settings.txt and are NOT removed on
uninstall. Delete that file if you want a fully clean slate.
