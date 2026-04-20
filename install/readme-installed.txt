Dictionary Tasker — installed
==============================

This folder holds the Clarion add-in DLL + manifest:

  ClarionDctAddin.dll
  ClarionDctAddin.addin

The installer can target Clarion 10, 11, 11.1, and/or 12 — pick any
combination on the "Clarion installation(s)" page (leave a line blank
to skip a version). The same DLL is used for all four IDE versions; it
targets .NET Framework 4.0, which every supported Clarion IDE loads.

Clarion discovers the add-in at IDE startup and the "Dictionary Tasker"
toolbar button will appear greyed-out until you open a .DCT — then it
lights up.

To uninstall, use "Apps & features" in Windows settings and look for
"Dictionary Tasker".

Settings (preferred SQL dialect, Fix keys dropdowns, etc.) are kept in
%LOCALAPPDATA%\ClarionDctAddin\settings.txt and are NOT removed on
uninstall. Delete that file if you want a fully clean slate.
