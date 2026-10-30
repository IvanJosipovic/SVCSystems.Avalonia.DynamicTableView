# SvcSystems.Avalonia.DynamicTableView

[![NuGet](https://img.shields.io/nuget/v/SvcSystems.Avalonia.DynamicTableView.svg?style=flat-square)](https://www.nuget.org/packages/SvcSystems.Avalonia.DynamicTableView)
[![NuGet downloads](https://img.shields.io/nuget/dt/SvcSystems.Avalonia.DynamicTableView.svg?style=flat-square)](https://www.nuget.org/packages/SvcSystems.Avalonia.DynamicTableView)
[![codecov](https://codecov.io/gh/IvanJosipovic/SVCSystems.Avalonia.DynamicTableView/graph/badge.svg)](https://codecov.io/gh/IvanJosipovic/SVCSystems.Avalonia.DynamicTableView)

A read-only Avalonia table driven by DynamicData. Define columns with typed selectors; row members need no reflection metadata.

## Install

```bash
dotnet add package SvcSystems.Avalonia.DynamicTableView
```

## Setup

Include the theme after Avalonia Fluent. Put application overrides after the include. The compiled XAML include supports trim-enabled apps.

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://SvcSystems.Avalonia.DynamicTableView/Themes/DynamicTableViewTheme.axaml" />
</Application.Styles>
```

## Usage

Create a `DynamicTableViewSource` from an `IObservableCache<T,TKey>` and typed columns, then set it as the table's `Source`:

```csharp
using Avalonia.Controls;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView;

var people = new SourceCache<Person, string>(static person => person.Id);
people.AddOrUpdate(new Person[]
{
    new("1", "Ada Lovelace", 36),
    new("2", "Grace Hopper", 85)
});

DynamicTableViewColumn<Person>[] columns =
[
    DynamicTableViewColumn<Person>.Create("name", "Name", static person => person.Name),
    DynamicTableViewColumn<Person>.Create("age", "Age", static person => person.Age)
];

var source = new DynamicTableViewSource<Person, string>(
    people, static person => person.Id, columns);
var table = new DynamicTableView { Source = source };

public sealed record Person(string Id, string Name, int Age);
```

Update rows through the cache (`AddOrUpdate`, `RemoveKey`, or `Edit`). The caller owns and disposes both the cache and source. Use the cache's key selector for stable selection and sorting. Filtering and sorting run on a worker scheduler; row updates and selection restoration run on the UI scheduler.

`SetSort` takes ownership of its descriptor array. Pass a new array for later changes; `SortDescriptors` is read-only.

## Options

- `GridLinesVisibility` defaults to `None`; set it to `Horizontal`, `Vertical`, or `All`. Line color follows Fluent's `TableViewColumnHeaderSeparatorBackground` resource.
- Default text cells expose table-wide defaults: `CellTextWrapping` (`NoWrap`), `CellTextTrimming` (none), and `CellMaxLines` (one line). For example:

```csharp
var table = new DynamicTableView
{
    CellTextWrapping = TextWrapping.Wrap,
    CellTextTrimming = TextTrimming.CharacterEllipsis,
    CellMaxLines = 3
};
```

Each column can override these settings with its nullable `CellTextWrapping`, `CellTextTrimming`, and `CellMaxLines` properties. Null inherits the table setting; `CellMaxLines = 0` means unlimited. For example:

```csharp
columns[0].CellTextWrapping = TextWrapping.Wrap;
columns[0].CellMaxLines = 3;
```

These options affect cells without a custom `CellTemplate`; custom templates control their own text. Use a `Star` or `Pixel` column width to constrain text so it can wrap; `Auto` columns size to their content.
- Column definitions are mutable while the table is attached. `Header`, `CellTemplate`, the cell text options, `IsSearchable`, `CanUserSort`, `CanUserFilter`, `WidthMode`, `Width`, `MinWidth`, `FilterChoices`, and `FilterFlyoutFactory` update attached tables and headers. Make those changes on the UI thread. The table observes the definitions in its current `Source`; a definition with no attached table keeps its metadata and applies it on attach.
- Disabling `CanUserSort` or `CanUserFilter` immediately removes that column's active sort or filter, including a `SetCustomFilter` predicate. Other columns' sort, filter, and search state are kept, and re-enabling does not restore the removed state.
- A column definition can be shared by more than one source. Each source subscribes independently and stops observing the definition when it is removed from that source or disposed.
- Manual column sizing is tracked per table, not per definition. Dragging a divider or restoring a pixel width suppresses `Auto`, `Cells`, and `Header` sizing for that key only in that table; setting `Width`, `WidthMode`, or `MinWidth` releases the suppression. Because the width itself lives on the shared definition, two tables showing the same column start from the same dragged width, but each clamps it to its own effective minimum — including a styled `MinWidth` — so the displayed widths can differ between tables.
- `MinWidth` can also be set from a style, so a theme can own the minimum instead of every definition repeating it. Add a class to the column definition, then target the native `TableViewColumn`:

```xml
<Style Selector="TableViewColumn.narrow">
  <Setter Property="dyn:DynamicTableViewColumnProperties.MinWidth" Value="120" />
</Style>
```

```csharp
column.Classes.Add("narrow");
```

`DynamicTableViewColumn.Classes` is copied to the native column before it attaches, so styles apply normally. A styled minimum takes precedence over `DynamicTableViewColumn.MinWidth`; when no style sets one, the metadata value is used. Changing the styled value updates an attached table.
- Filter flyouts are 280 px wide by default. Override per table:

```xml
<DynamicTableView xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <DynamicTableView.Resources>
    <x:Double x:Key="DynamicTableView.FilterFlyoutWidth">360</x:Double>
  </DynamicTableView.Resources>
</DynamicTableView>
```

The table does not assign actions to Enter, Delete, taps, or double-taps. Add `KeyBinding`s or handle `Tapped` and `DoubleTapped` in the application.

## Sample and benchmarks

Run the sample:

```bash
dotnet run --project samples/SvcSystems.Avalonia.DynamicTableView.Sample
```

Run input benchmarks in Release mode:

```bash
dotnet run -c Release --project benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks -- --filter '*DynamicTableViewInputBenchmarks*' --inProcess
```

The library, tests, and benchmarks target .NET 11. BenchmarkDotNet 0.15.8 does not recognize this runtime in its default out-of-process toolchain; use `--inProcess` with this SDK.

Run each headless UI case in a separate invocation so Avalonia is initialized once on its owning thread. For example:

```bash
for rows in 100 1000 10000; do
  dotnet run -c Release --project benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks -- \
    --filter "*ResizeStarColumns*$rows)" --inProcess --warmupCount 2 --iterationCount 5
done
```
