# Engineering Guide (AGENTS)

This document defines the architectural and coding rules for this standalone Avalonia control library.

## 0) Pre-requisites
.NET SDK installed based on global.json version

## 1) Core principles (non-negotiable)

### SOLID (strict)
- Single Responsibility: every class has exactly one reason to change.
- Open/Closed: extend behavior via composition and interfaces; avoid modifying
  stable code paths when adding features.
- Liskov Substitution: derived types must be safely substitutable without
  altering expected behavior or contract.
- Interface Segregation: prefer small, focused interfaces; avoid "god" interfaces.
- Dependency Inversion: depend on abstractions; wire concrete types in the
  composition root only.

## 2) Architecture

### Projects
- `src/SvcSystems.Avalonia.DynamicTableView` contains the standalone control library.
- `tests/SvcSystems.Avalonia.DynamicTableView.Tests` contains unit and headless UI tests.
- `samples/SvcSystems.Avalonia.DynamicTableView.Sample` demonstrates XAML-first and code-first use.
- `benchmarks/SvcSystems.Avalonia.DynamicTableView.Benchmarks` contains isolated benchmarks.
- The library may depend on Avalonia and DynamicData only; it must not depend on application, sample, test, or benchmark projects.

## 3) Avalonia UI best practices (aligned with Avalonia codebase)

References:
- https://github.com/AvaloniaUI/Avalonia
- https://docs.avaloniaui.net

### Views and styling
- Use XAML for layout and visuals; avoid creating controls in code.
- Define styles and resources in dedicated resource dictionaries and merge them
  in `App.axaml` to keep styling consistent and maintainable.
- Prefer `StaticResource` for immutable resources and `DynamicResource` when
  runtime updates are required.

### Data binding
- Use compiled bindings only (no reflection bindings) with explicit `x:DataType` on
  all binding scopes (views, DataTemplates, control themes, and resources).
- Keep bindings one-way unless user input must update the ViewModel.
- Use `DataTemplates` or a `ViewLocator` (custom, non-reflection) for view lookup.

### Custom controls
- Use `StyledProperty` only for values that must participate in styling.
- Prefer `DirectProperty` for non-styled properties to avoid extra overhead.
- For best UI/UX, prefer custom control creation or re-templating using control themes
  instead of CRUD-style UI.

## 4) Performance (required)

- Prefer allocation-free APIs: `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`,
  `ValueTask`, `ArrayPool<T>`, and `System.Buffers`.
- Use SIMD (`System.Numerics.Vector<T>` or hardware intrinsics) where it provides
  measurable wins and keeps code maintainable.
- Avoid LINQ in hot paths; use loops and pre-sized collections.
- Minimize boxing, virtual dispatch in tight loops, and avoid unnecessary
  allocations in render/update loops.
- Profile before and after optimizations; document expected gains.

## 5) Reflection and source generation (required)

- Avoid reflection whenever possible.
- Prefer source generators (incremental generators required) before any reflection-based
  approach.
- If reflection is the only viable option, ask the user explicitly before introducing it.

## 6) Testing and validation

References:
- https://github.com/AvaloniaUI/Avalonia
- https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform

- All production code must be covered by unit tests; xUnit is required for unit testing.
- UI tests must use Avalonia Headless (xUnit integration) and follow the headless testing guidance and helpers for input simulation.
- Cover all public API and retained control behavior with focused unit or headless UI tests.
- Run `dotnet build --tl:off -clp:ErrorsOnly` before tests.

## 7) Code conventions

- No code-behind event handlers.
- Avoid static state (except truly immutable constants).
- Prefer explicit types where clarity is improved; avoid `var` in public APIs.
- All public APIs must be documented and unit-tested.
- No, 'hacks' or weird workarounds, if you think you have to, ask for guidance
