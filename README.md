# clarionDictionaryTasker

A SharpDevelop add-in for the **Clarion 12 IDE** that inspects the currently open dictionary and offers a growing catalogue of read-only and batch tools — without ever parsing the binary `.DCT` file on disk.

## Features

### Browse & navigate
- **Launcher** — tiled home screen with quick access to the main views and the full tools catalogue.
- **Browse tables** — modal list with driver / prefix / field count / description. Click-to-sort columns (numeric for Fields/Keys, string-insensitive elsewhere), incremental text filter, Driver dropdown, and a live count label. Right-click any row for per-table actions: **Show fields**, **Export to JSON**, **Export SQL DDL**, **Lint this table**, **Fix fields**.
- **Show fields** — per-table column view (`Name`, `Type`, `Size`, `Places`, `Picture`, `Heading`, `Prompt`, `Description`, `External name`, ...).
- **Tree view** — hierarchical dictionary → tables → Fields / Keys / Relations / Triggers → leaf properties. Lazy-loaded for large dictionaries.
- **Relations diagram** — auto-laid-out boxes-and-arrows chart.
- **Field inspector** — full reflection dump for schema-tuning / diagnostics.

### Batch operations (write)
- **Batch copy fields** across selected target tables.
- **Batch copy keys**, with automatic component remapping and generated `ExternalName = <TargetTable>_<KeyLabel>`.
- Automatic `.tasker-bak-*` backup of the `.DCT` before any mutation.

### Validation & analysis (read-only)
- **Lint report** — missing primary keys, empty tables, orphaned relations, duplicate keys, duplicate ExternalName across keys (SQL drivers forbid two indexes with the same name), illegal ExternalName characters (colon from Clarion prefix leak, whitespace, leading digit, chars outside `[A-Za-z0-9_$#.]`), over-length ExternalNames per driver (Postgres 63 / MySQL 64 / MSSQL 128), undocumented fields, malformed pictures (DATE needs `@d*`, numeric `@n*`; LONG/ULONG accept `@n`/`@d`/`@t` since Clarion stores dates as LONG).
- **Fix fields (editable grid)** — the repairable subset of the lint shown in a DataGridView with Description and Picture columns editable inline; live re-check as you type; auto-fill Description from heading / prompt / humanized label; Apply writes through `FieldMutator` with a `.DCT` backup first.
- **Fix keys (editable grid)** — flags empty, duplicate, or illegal ExternalNames; 8 × 2 × 2 = 32 auto-fill combos (style × Owner: Table|Prefix × Key: Label-only|Full), Show filter (All / Blank / Duplicated / Illegal). All four dropdowns persist across sessions.
- **Picture consistency** — flag DATE fields without `@d*`, numerics without `@n*`, STRING with a non-string picture, and labels that appear on many tables with divergent (type, picture) combos.
- **Naming conventions** — tables UPPERCASE, prefixes 2-4 uppercase chars, labels with no whitespace / no digit-start, key-naming convention. Rules togglable at runtime.
- **Health dashboard** — totals, top-10 largest tables, driver mix bar chart, relations-per-table histogram.
- **Dead tables** — tables with no relations and no references elsewhere.
- **Duplicate fields** — fields with identical label + type + size appearing on many tables — candidates for extraction.

### Search & navigation
- **Global search** — Ctrl-F across tables, fields, keys, relations, and trigger bodies. Case-insensitive or regex, per-kind filters.
- **Where used** — pick a field on a table → list every key component, relation component, and trigger body that references it.
- **Path finder** — BFS the relation graph to show the shortest undirected path between any two tables, annotated with the relation name at each hop.

### Compare & diff
- **Compare tables** — pick two tables in the current dict, diff their fields + keys side-by-side (Same / Differs / Only-A / Only-B).
- **Compare dictionaries** — two flows:
  - *Live vs. live:* when more than one `.DCT` tab is open, pick **Compare to another open dict...** — the add-in enumerates every open `DataDictionaryViewContent` in the workbench, shows a picker if >2 are open, and diffs them directly.
  - *Live vs. snapshot:* save a `*.tasker-snap` file now, re-open the dialog later (possibly against a different dict) and **Load snapshot & compare...** to diff the stored structure against the currently-active live dict.

  Either way, the result is a tree of Added / Removed / Changed tables with field-, key-, and relation-level drill-down. Exportable as Markdown.
- **Change-log generator** — pick two `*.tasker-snap` save-points, emit a human-readable Markdown changelog suitable for a release note or a PR description.

### Visualization
- **Export relations map** — standalone SVG with a grid layout (sorted by degree, isolated tables hideable). Self-contained — no external renderer required.

### Refactoring (mutation)
- **Safe rename field** — rename a field's label. Keys and relations follow automatically (they hold object refs); trigger bodies patched via word-boundary regex of `OLDLABEL` and `PREFIX:OLDLABEL`. `.DCT` backed up first.
- **Batch rename (regex)** — regex find/replace across field Label / Description / Heading / Prompt, optionally scoped by table name regex. Preview the plan, then apply with backup.
- **Batch retype fields** — select every field whose label matches a regex, change type / size / picture in one shot. Any field blank means "preserve". Preview + apply with backup.
- **Standard audit pack** — pick a TEMPLATE TABLE that already has the audit fields defined (Guid + CreatedOn/By + ModifiedOn/By + DeletedOn). The tool auto-matches those labels against the template and stamps them onto every selected target table via the proven Batch-copy-fields path. `.DCT` backed up first. Optional Markdown recipe export.

### Enterprise glue
- **Git commit hook** — detects the repo root, shows `git status` for the `.DCT`, seeds a commit message, commits and optionally pushes. Manual rather than auto-on-save, for reliability across Clarion 12 point releases.

### View data (TPS & SQL)
- **View data** — right-click any table and peek at the first N rows in a read-only grid, without leaving the IDE.
  - **SQL** tables (MSSQL) read via direct ADO.NET (`SqlClient`), connection string pulled from the table's `OWNER('…')` and remembered per-dict.
  - **TPS** tables read via the bundled [tps-parse-net](https://github.com/pharmadata/tps-parse-net) library — a managed port of Erik Hooijmeijer's reverse-engineered TPS reader. No `ClaTPS.dll`, no native Clarion runtime: the add-in parses the TPS bytes directly. One local patch (seek to each field's `Offset` before reading) fixes Clarion's `OVER()`-aliased fields.
  - **Embed TopScan** / **Open in TopScan** — SoftVelocity's own TPS viewer, either reparented into the dialog via Win32 `SetParent` or launched as a separate process. Always available as a fallback for edge-case TPS files the parser doesn't know how to decode.

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

With Clarion 12 **closed** (the IDE holds a lock on its loaded DLLs), you have two options:

### Option 1 — installer (for end users)

If [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed on your build
machine, run:

```
install\build-installer.bat
```

which produces `install\dist\DictionaryTasker-Setup-<version>.exe`. Ship that
single `.exe` to a Clarion developer; double-clicking it walks them through
picking the Clarion 12 folder, writes the files into
`<clarion>\bin\Addins\Misc\ClarionDctAddin\`, and registers an uninstaller
under Windows "Apps & features". See `install/README.md` for details.

### Option 2 — deploy.bat (for quick local iteration)

```
deploy.bat
```

copies `ClarionDctAddin.dll` + `ClarionDctAddin.addin` straight to:

```
C:\clarion12\bin\Addins\Misc\ClarionDctAddin\
```

Restart Clarion 12 and look for the **Dictionary Tasker** toolbar icon (greyed-out until a dictionary is open).

## Settings

User preferences (preferred SQL dialect, JSON preview view, Fix keys dropdowns, Tables tab sort, Model classes language + namespace, Naming conventions rule checkboxes, Global search filters, and several more — see the Help manual's *Persisted preferences* section for the full list) live in:

```
%LOCALAPPDATA%\ClarionDctAddin\settings.txt
```

It's a plain key=value text file. Delete a line to reset that preference to its default.

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
| `PictureConsistencyDialog.cs` | Lint pictures against their data types. |
| `NamingConventionsDialog.cs` | Configurable naming-rule violations. |
| `ChangeLogDialog.cs` | Two-snapshot Markdown changelog generator. |
| `RelationsMapExportDialog.cs` | Grid-layout SVG export of the relation graph. |
| `StandardAuditPackDialog.cs` | Preview + Markdown recipe for the audit pack. |
| `SafeRenameFieldDialog.cs` | Rename one field + patch trigger bodies. |
| `BatchRenameDialog.cs` | Regex find/replace over field text properties. |
| `BatchRetypeDialog.cs` | Pattern-match + retype fields in one shot. |
| `FieldMutator.cs` | Shared reflection helper for in-place field property edits. |
| `GitCommitDialog.cs` | Manual git commit / push for the .DCT. |
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
| `ViewDataDialog.cs` | Read-only data grid for SQL/TPS tables, with TopScan embed + external-launch buttons. |
| `SqlTableAccessor.cs` | Direct ADO.NET reader for MSSQL tables (via `SqlClient`). |
| `SqlConnectionPromptDialog.cs` | Per-dict connection-string prompt. |
| `TpsDirectReader.cs` | TPS reader that wraps the bundled `TpsParse/` library (bypasses Clarion's native runtime). |
| `TpsParse/` | Vendored `tps-parse-net` — reverse-engineered managed TPS parser, Apache-2, ~1600 lines. |
| `ClarionFileAccessor.cs` | Reflection-driven accessor for non-TPS file drivers (currently gated to a graceful error). |
| `TopScanEmbedDialog.cs` | Launches TopScan and reparents its HWND into a panel via Win32 `SetParent`. |
| `TopScanLauncher.cs` | Locates / launches `TopScan.exe` externally. |
| `Win32Embed.cs` | Thin `SetParent` / style-bit helper for the embed. |
| `Settings.cs` | `%LOCALAPPDATA%` key=value settings. |
| `docs/index.html` | Embedded HTML manual (Help button). |

## Notes

- Tested against Clarion 12.0.13941.
- The add-in targets .NET Framework 4.0 because that's what Clarion 12 loads.

## Credits

Dictionary Tasker bundles and builds on open-source work. The full list with licence notes is in the in-app help (see the **Credits** section of `docs/index.html`). Briefly:

- **[tps-parse-net](https://github.com/pharmadata/tps-parse-net)** — Adam Burger / pharmadata. The managed TPS reader that powers the **View data** feature for TOPSPEED tables. Apache-2. Source vendored under `ClarionDctAddin/TpsParse/` with one documented local patch (`DataRecord.ParseValues` now seeks to each field's `Offset` before reading, to handle Clarion `OVER()`-aliased fields).
- **[tps-parse](https://github.com/ctrl-alt-dev/tps-parse)** (Java) — Erik Hooijmeijer. The original reverse-engineering of the TopSpeed file format; everything tps-parse-net does traces back to this work. Background writeup: [Liberating data from Clarion TPS files](https://dontpanic.42.nl/2013/01/liberating-data-from-clarion-tps-files.html).
- **SoftVelocity Clarion 12 / TopScan** — the IDE host and the fallback TPS viewer we embed via Win32 `SetParent`.
- **ICSharpCode.SharpDevelop 2.1** — the add-in framework Clarion 12's IDE is built on.

## License

See [LICENSE](LICENSE).
