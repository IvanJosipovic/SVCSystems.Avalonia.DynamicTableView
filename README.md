# SvcSystems.Avalonia.DynamicTableView

[![NuGet](https://img.shields.io/nuget/v/SvcSystems.Avalonia.DynamicTableView.svg?style=flat-square)](https://www.nuget.org/packages/SvcSystems.Avalonia.DynamicTableView)
[![NuGet downloads](https://img.shields.io/nuget/dt/SvcSystems.Avalonia.DynamicTableView.svg?style=flat-square)](https://www.nuget.org/packages/SvcSystems.Avalonia.DynamicTableView)
[![codecov](https://codecov.io/gh/IvanJosipovic/SVCSystems.Avalonia.DynamicTableView/graph/badge.svg)](https://codecov.io/gh/IvanJosipovic/SVCSystems.Avalonia.DynamicTableView)

`DynamicTableView` is a read-only Avalonia table control backed by a DynamicData change stream. The library uses typed delegates for row values and filters, so row members do not need reflection metadata.

## Install

```bash
dotnet add package SvcSystems.Avalonia.DynamicTableView
```

## Sample

Run the sample app from the repository root:

```bash
dotnet run --project samples/SvcSystems.Avalonia.DynamicTableView.Sample
```

## Theme setup

Add the library theme after Avalonia Fluent in the application's compiled XAML. Keep application overrides after this include.

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://SvcSystems.Avalonia.DynamicTableView/Themes/DynamicTableViewTheme.axaml" />
  <!-- application styles can override DynamicTableView templates below -->
</Application.Styles>
```

Use the compiled XAML include in trim-enabled applications. Loading the library theme from a runtime `StyleInclude` in C# uses Avalonia's runtime XAML loader.

## C# usage

Create a caller-owned DynamicData `SourceCache`, define typed columns, and assign the source to a `DynamicTableView`:

```csharp
using Avalonia.Controls;
using DynamicData;
using SvcSystems.Avalonia.DynamicTableView;

public sealed record Person(string Id, string Name, int Age);

public sealed class PeopleWindow : Window
{
    private readonly SourceCache<Person, string> _people;
    private readonly DynamicTableViewSource<Person, string> _source;

    public PeopleWindow()
    {
        _people = new SourceCache<Person, string>(static person => person.Id);
        Person[] initialPeople =
        [
            new("1", "Ada Lovelace", 36),
            new("2", "Grace Hopper", 85)
        ];
        _people.AddOrUpdate(initialPeople);

        DynamicTableViewColumn<Person>[] columns =
        [
            DynamicTableViewColumn<Person>.Create("name", "Name", static person => person.Name),
            DynamicTableViewColumn<Person>.Create("age", "Age", static person => person.Age)
        ];

        _source = new DynamicTableViewSource<Person, string>(
            _people, static person => person.Id, columns);

        Content = new DynamicTableView { Source = _source };
        Closing += (_, _) =>
        {
            _source.Dispose();
            _people.Dispose();
        };
    }
}
```

Update the cache with `AddOrUpdate`, `RemoveKey`, or `Edit` to change rows. The source accepts `IObservableCache<T,TKey>` and subscribes without modifying or disposing it; callers retain ownership of the writable cache. Pass the same key selector used to construct the cache to preserve stable selection identity and sorting. Filtering and sorting run on the worker scheduler; applying bound rows and restoring selection run on the UI scheduler. This replaces the former `FromObservableCollection` API.

`SetSort` takes ownership of its descriptor array without copying it. Do not change that array after passing it to the source; use a new array for subsequent sort changes. `SortDescriptors` exposes a read-only view.

## Benchmarks

Run the BenchmarkDotNet suite in Release mode:

```bash
dotnet run -c Release --project benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks -- --filter '*DynamicTableViewInputBenchmarks*'
```

The benchmark project targets .NET 10 (supported by its BenchmarkDotNet version); the library and tests also target .NET 11. Input benchmarks measure source-cache replacement and selection allocations at 100, 1,000, and 10,000 rows. UI benchmarks measure virtualized scrolling, auto-width updates, fixed-width updates, and moving a column while retaining unaffected native columns. Tables with no automatically sized columns skip the auto-width layout pass. Filter flyout content is created on first open rather than for every header at startup. Compare runs on the same machine and runtime.

`DynamicTableView.GridLinesVisibility` controls separators between cells and headers. It defaults to `None`; use `Horizontal`, `Vertical`, or `All` to show lines. Grid lines use Avalonia Fluent's `TableViewColumnHeaderSeparatorBackground` resource, so they follow the active Fluent theme. Override that Fluent resource in the host application to customize the color.

Filter flyouts default to 280 pixels wide. Override the width on an individual table through its resources:

```xml
<DynamicTableView xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <DynamicTableView.Resources>
    <x:Double x:Key="DynamicTableView.FilterFlyoutWidth">360</x:Double>
  </DynamicTableView.Resources>
</DynamicTableView>
```

## Input and actions

The table does not assign meaning to Enter, Delete, taps, or double-taps. Add Avalonia `KeyBinding` instances to `DynamicTableView.KeyBindings` and subscribe to its native `Tapped` or `DoubleTapped` routed events in the caller. This keeps navigation and row actions specific to the application and lets consumers choose any gestures.
