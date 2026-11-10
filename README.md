# SvcSystems.Avalonia.DynamicTableView

[![NuGet](https://img.shields.io/nuget/v/SvcSystems.Avalonia.DynamicTableView.svg?style=flat-square)](https://www.nuget.org/packages/SvcSystems.Avalonia.DynamicTableView)
[![NuGet downloads](https://img.shields.io/nuget/dt/SvcSystems.Avalonia.DynamicTableView.svg?style=flat-square)](https://www.nuget.org/packages/SvcSystems.Avalonia.DynamicTableView)
[![codecov](https://codecov.io/gh/IvanJosipovic/SVCSystems.Avalonia.DynamicTableView/graph/badge.svg)](https://codecov.io/gh/IvanJosipovic/SVCSystems.Avalonia.DynamicTableView)

A read-only Avalonia table backed by DynamicData, with typed column selectors and virtualized rows.

## Features

- Sort, filter, search, resize, and reorder columns.
- Choose native Avalonia `GridLength` sizing: Auto (a 1* share in `TableView`), pixel, or weighted Star. Widths do not fit header or cell content.
- Preserve selection and capture or restore column layout, filters, sorting, and scroll position.
- Customize cell templates, text wrapping, grid lines, and column minimum widths.
- Use typed selectors without reflection-based row discovery.

## Install

```bash
dotnet add package SvcSystems.Avalonia.DynamicTableView
```

Add the compiled theme after `FluentTheme` in `App.axaml`. Because the theme is compiled into the `DynamicTableViewTheme` class, include it as an element rather than loading the `.axaml` file with `StyleInclude`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:fluent="using:Avalonia.Themes.Fluent"
             xmlns:table="using:SvcSystems.Avalonia.DynamicTableView"
             x:Class="MyApp.App">
  <Application.Styles>
    <fluent:FluentTheme />
    <table:DynamicTableViewTheme />
  </Application.Styles>
</Application>
```

## Quick Start

Create a cache, define columns, and assign the source to a table:

```csharp
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

Add the table to your view and update rows through the cache with `AddOrUpdate`, `RemoveKey`, or `Edit`. Dispose the source and cache when they are no longer needed.

## Customize

Configure column sizing and table appearance in code:

```csharp
using Avalonia.Media;
using Avalonia.Controls;

columns[0].Width = new GridLength(1, GridUnitType.Star);
columns[0].MinWidth = 120;
columns[0].MaxWidth = 360;
columns[0].CellTextWrapping = TextWrapping.Wrap;

table.GridLinesVisibility = DynamicTableViewGridLinesVisibility.All;
table.CellMaxLines = 3;
```

Column text options override table defaults for cells without a custom `CellTemplate`. Use `CellMaxLines = 0` for unlimited lines.

To override a column's minimum or maximum width from a style, assign a class before the column is added to the table:

```csharp
columns[0].Classes.Add("wide-column");
var source = new DynamicTableViewSource<Person, string>(
    people, static person => person.Id, columns);
var table = new DynamicTableView { Source = source };
```

Column classes are copied to the native columns and stay synchronized when changed while the table is attached. Changes made while detached are applied when it reattaches.

```xml
<Style Selector="TableViewColumn.wide-column">
  <Setter Property="MinWidth" Value="120" />
  <Setter Property="MaxWidth" Value="360" />
</Style>
```

Native `TableViewColumn` style values take precedence over `DynamicTableViewColumn.MinWidth` and `MaxWidth` metadata.

Column widths use Avalonia `GridLength`: `GridLength.Auto` (the default) participates as a `1*` share in `TableView`, `new GridLength(120)` sets a pixel width, and `new GridLength(2, GridUnitType.Star)` sets a weighted share. Width is not measured to fit header or cell text.

## Save And Restore

```csharp
var state = table.CaptureState();
table.RestoreState(state);
```

Keyboard shortcuts and tap actions are application-owned: use Avalonia `KeyBindings`, `Tapped`, or `DoubleTapped` to add them.

## Sample

Explore XAML-first and code-first examples in the [sample project](samples/SvcSystems.Avalonia.DynamicTableView.Sample/README.md):

```bash
dotnet run --project samples/SvcSystems.Avalonia.DynamicTableView.Sample
```
