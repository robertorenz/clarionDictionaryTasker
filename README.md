# clarionDictionaryTasker

A SharpDevelop add-in for the **Clarion 12 IDE** that inspects the currently open dictionary and offers a growing catalogue of read-only and batch tools — without ever parsing the binary `.DCT` file on disk.

## Features

### Browse & navigate
- **Launcher** — tiled home screen with quick access to the main views and the full tools catalogue.
- **Browse tables** — modal list with driver / prefix / field count / description.
- **Show fields** — per-table column view (`Name`, `Type`, `Size`, `Places`, `Picture`, `Heading`, `Prompt`, `Description`, `External name`, ...).
- **Tree view** — hierarchical dictionary → tables → Fields / Keys / Relations / Triggers → leaf properties. Lazy-loaded for large dictionaries.
- **Relations diagram** — auto-laid-out boxes-and-arrows chart.
- **Field inspector** — full reflection dump for schema-tuning / diagnostics.

### Batch operations (write)
- **Batch copy fields** across selected target tables.
- **Batch copy keys**, with automatic component remapping and generated `ExternalName = <TargetTable>_<KeyLabel>`.
- Automatic `.tasker-bak-*` backup of the `.DCT` before any mutation.

### Validation & analysis (read-only)
- **Lint report** — missing primary keys, empty tables, orphaned relations, duplicate keys, undocumented fields.
- **Health dashboard** — totals, top-10 largest tables, driver mix bar chart, relations-per-table histogram.
- **Dead tables** — tables with no relations and no references elsewhere.
- **Duplicate fields** — fields with identical label + type + size appearing on many tables — candidates for extraction.

### Search & navigation
- **Global search** — Ctrl-F across tables, fields, keys, relations, and trigger bodies. Case-insensitive or regex, per-kind filters.
- **Where used** — pick a field on a table → list every key component, relation component, and trigger body that references it.
- **Path finder** — BFS the relation graph to show the shortest undirected path between any two tables, annotated with the relation name at each hop.

### Compare & diff
- **Compare tables** — pick two tables in the current dict, diff their fields + keys side-by-side (Same / Differs / Only-A / Only-B).
- **Compare dictionaries** — save a `*.tasker-snap` snapshot of the current dict, later load it and compare against the live dict (or a completely different dict). Tree view of Added / Removed / Changed tables with field-, key-, and relation-level drill-down. Exportable as Markdown.

### Generation & export
- **SQL DDL export** — live preview window, 5 dialects (SQL Server, PostgreSQL, SQLite, MySQL, MariaDB). Whole dictionary or single table. Remembers the preferred dialect.
- **Model classes** — emit one class/interface per table in **C#** (PascalCase POCOs with XML doc comments) or **TypeScript** (camelCase `export interface`s with JSDoc). Live preview, namespace option, Copy/Save.
- **Markdown documentation** — single-document reference (tables, fields, keys, relations) with optional TOC. Copy or save as `.md`.
- **JSON export** — one table, selected tables, or the whole dictionary.

### Right-click integration
Right-click a table in the **Tables list**, **Tree view**, or **Relations diagram** to jump straight to per-table actions (Show fields, Export SQL DDL, Markdown, ...).

### Tools catalogue
The **Dictionary tools** dialog lists every planned tool, grouped by category. **Bold buttons = implemented**, greyed "(planned)" buttons are placeholders for future work.

## How it works

The Clarion 12 IDE is built on **SharpDevelop 2.1** (`ICSharpCode.SharpDevelop`). Add-ins are a standard .NET 4.0 DLL plus an `.addin` XML manifest dropped into `C:\clarion12\bin\Addins\`.

The add-in reaches the live dictionary model via reflection:

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

Requires Visual Studio with the **.NET Framework 4.0 targeting pack** installed.

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

Restart Clarion 12 and look for the **Dictionary Tasker** toolbar icon (greyed-out until a dictionary is open).

## Settings

User preferences (e.g. preferred SQL dialect) live in:

```
%LOCALAPPDATA%\ClarionDctAddin\settings.txt
```

## File layout

| File | Role |
| --- | --- |
| `ClarionDctAddin.addin` | SharpDevelop manifest — toolbar item, condition evaluators, autostart. |
| `StartupCommand.cs` | Toolbar icon swap / enable state. |
| `DictionaryOpenCondition.cs` | Enables the toolbar icon only when a dictionary is open. |
| `LauncherDialog.cs` | Home screen with tiles. |
| `DictModel.cs` | Reflection helpers that find the live dictionary model. |
| `TableListDialog.cs` | Tabbed modal (Tables / Tree / Relations) with right-click menus. |
| `DictTreeViewPanel.cs` | Hierarchy explorer. |
| `RelationsDiagramPanel.cs` | Custom-painted layered relations diagram. |
| `FieldListDialog.cs` | Per-table field browser. |
| `ToolsDialog.cs` | Tools catalogue (bold = implemented). |
| `LintReportDialog.cs` | Validation findings. |
| `HealthDashboardDialog.cs` | Stats / charts. |
| `DeadTablesDialog.cs` | Tables with no relations. |
| `DuplicateFieldsDialog.cs` | Fields appearing on multiple tables. |
| `GlobalSearchDialog.cs` | Full-dict search across tables/fields/keys/relations/triggers. |
| `WhereUsedDialog.cs` | Find every key / relation / trigger that references a given field. |
| `PathFinderDialog.cs` | Shortest relation path between two tables. |
| `ModelClassesDialog.cs` / `ModelClassesGenerator.cs` | C# + TypeScript model emitter. |
| `CompareTablesDialog.cs` | Side-by-side diff of two tables in one dict. |
| `CompareDictionariesDialog.cs` | Live dict vs. `.tasker-snap` snapshot diff + Markdown export. |
| `DictSnapshot.cs` | Capture + save/load of a dict's structural shape. |
| `DictDiff.cs` | Pure diff of two `DictSnapshot`s. |
| `SqlDdlDialog.cs` / `SqlDdlGenerator.cs` | DDL preview + 5-dialect generator. |
| `MarkdownDialog.cs` / `MarkdownGenerator.cs` | Markdown preview + generator. |
| `BatchCopyFieldsDialog.cs` | Batch field propagation. |
| `BatchCopyKeysDialog.cs` | Batch key propagation with component remap. |
| `JsonExporter.cs` | Dependency-free JSON writer. |
| `Settings.cs` | `%LOCALAPPDATA%` key=value settings. |
| `docs/index.html` | Embedded HTML manual (Help button). |

## Notes

- Tested against Clarion 12.0.13941.
- The add-in targets .NET Framework 4.0 because that's what Clarion 12 loads.

## License

See [LICENSE](LICENSE).
