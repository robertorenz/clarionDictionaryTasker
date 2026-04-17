# clarionDictionaryTasker

Clarion Dictionary Addin with Assistant, Tasker, Viewer, Organization, etc.

A SharpDevelop add-in for the **Clarion 12 IDE** that inspects the currently open dictionary and exports table structures to JSON — without ever parsing the binary `.DCT` file on disk.

## Features

- **Browse tables** — modal list of every table in the open dictionary with driver / prefix / field count / description.
- **Show fields** — per-table column view: `Name`, `Type`, `Size`, `Places`, `Picture`, `Heading`, `Prompt`, `Description`, `External name`, ...
- **Tree view** — full hierarchical explorer: dictionary → tables → Fields / Keys / Relations / Triggers → individual items with their properties as leaves. Lazy-loaded for large dictionaries.
- **Relations diagram** — auto-laid-out boxes-and-arrows chart of parent/child relationships between tables.
- **JSON export** — one table, selected tables, or the whole dictionary. Hand-picked schema for `DDFile` + `DDField` and reflection fallback for other collections.
- **Field inspector** — full reflection dump of any `DDField` for diagnostics / schema tuning.

## How it works

The Clarion 12 IDE is built on **SharpDevelop 2.1** (`ICSharpCode.SharpDevelop`). Add-ins are a standard .NET 4.0 DLL plus an `.addin` XML manifest dropped into `C:\clarion12\bin\Addins\`.

This add-in reaches the live dictionary model via reflection, walking:

```
IViewContent                                              (SharpDevelop active window)
  -> DataDictionaryViewContent.Control                    (SoftVelocity.DataDictionary.Editor)
    -> DCTContent.DCT                                     (public property)
      -> DDDataDictionary                                 (SoftVelocity.DataDictionary)
        -> .Tables                                        -> IEnumerable<DDFile>
          -> .Fields / .Keys / .Relations / .Triggers
```

There is no compile-time reference to any SoftVelocity assembly, so the add-in stays portable across Clarion 12 point-releases as long as the property names hold.

## Build

Requires Visual Studio with the **.NET Framework 4.0 targeting pack** installed (change `<TargetFramework>` in the `.csproj` to `net45` / `net48` if you prefer).

```
dotnet build -c Debug
```

## Deploy

With Clarion 12 **closed** (the IDE holds a lock on its loaded DLLs), run:

```
deploy.bat
```

which copies `ClarionDctAddin.dll` + `ClarionDctAddin.addin` to:

```
C:\clarion12\bin\Addins\Misc\ClarionDctAddin\
```

Restart Clarion 12 and look for the new **Dict Tools** top-level menu next to View.

## Menu entries

| Menu | What it does |
| --- | --- |
| Dict Tools → **Browse tables...** | Open the tabbed dictionary browser. |
| Dict Tools → **Hello (addin loaded)** | Sanity check that the add-in is live. |
| Dict Tools → **Reflection dump (debug)...** | Full reflection dump of the active view — used during development to discover type shapes. |

## File layout

| File | Role |
| --- | --- |
| `ClarionDctAddin.addin` | SharpDevelop manifest — declares menu items & command classes. |
| `HelloCommand.cs` | Trivial command used as a smoke test. |
| `BrowseTablesCommand.cs` | Opens the main dialog. |
| `ListTablesCommand.cs` | Reflection-only debug dump. |
| `DictModel.cs` | Reflection helpers that find the live dictionary model. |
| `TableListDialog.cs` | Tabbed modal (Tables / Tree / Relations). |
| `FieldListDialog.cs` | Per-table field browser with inspector. |
| `DictTreeViewPanel.cs` | Hierarchy explorer. |
| `RelationsDiagramPanel.cs` | Custom-painted layered relations diagram. |
| `JsonExporter.cs` | Dependency-free JSON writer. |

## Notes

- Tested against Clarion 12.0.13941.
- The add-in targets .NET Framework 4.0 because that's what Clarion 12 loads (`<supportedRuntime version="v4.0"/>` in `Clarion.exe.config`). Higher framework versions work if your JIT environment supports them.

## License

See [LICENSE](LICENSE).
