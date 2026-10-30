# TableView Improvement Plan

## Goal and decisions

Retain Avalonia TableView unless the required behavior cannot be implemented reliably through its supported interfaces. Preserve row virtualization, typed selectors, selection identity, and persisted state. A manually resized column becomes Pixel; other Star columns keep their weights and redistribute normally. Review behavior changes separately from refactoring.

Outcome: all five steps are resolved. The library keeps its `TableView` subclass, with the architectural rationale, deferred risks, and revisit triggers recorded in section 5. Along the way three real defects were found and fixed — Direct routed events subscribed as Bubble, hidden columns still accepting resize input, and sort state missing from the automation tree — and the benchmark harness was repaired so section 4 could produce a baseline.

## 1. Evaluate native extension points

Status: Complete. Native grip and minimum-width investigation completed; retain the custom resize path. A real-control native integration is deferred until native resizing can meet the reverse-drag contract. The resulting architectural decision is recorded in section 5.

- Compare resize, width layout, header input, reordering, selection, templates, and state against the exact pinned Avalonia version. Done against 12.1.4; see the source comparison below.
- Prototype native resizing in an isolated test: observe native Width changes and synchronize metadata and state, without modifying unrelated Star columns. Done; see the proof of concept.
- Test minimum widths, last and single columns, hidden columns, scrolling, capture loss, repeated drags, state restore, and both sample tabs. Compare native grip hit area and interaction with sorting, filtering, and reordering. Done for minimum widths, capture loss, and reverse drags under the native prototype; the equivalent scenarios are covered against the production custom path in section 3.
- Decide whether native resize can meet the contract. If it cannot, isolate and document the custom interaction boundary and consider an upstream issue or contribution. Do not remove the current implementation until the comparison passes. Decided: it cannot meet the reverse-drag contract; the custom path is retained and its boundary documented.
- Record the decisions and evidence here; repeat the comparison when upgrading Avalonia. Recorded. Re-running this comparison is a stated trigger in section 5.

### Avalonia 12.1.4 source comparison

Pinned dependency: `Avalonia` 12.1.4 in `src/SvcSystems.Avalonia.DynamicTableView/SvcSystems.Avalonia.DynamicTableView.csproj`. The following links are version-tagged upstream implementation, not a guarantee of behavior in later releases:

- [TableView](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/TableView.cs): exposes `Columns` and `CanUserResizeColumns`; its column-change and layout invalidation methods are internal. It creates TableViewRow containers and rebuilds realized rows when columns change.
- [TableViewColumn](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/TableViewColumn.cs): exposes `HeaderTemplate`, `HeaderTheme`, `CellTemplate`, `CellTheme`, `Width`, `IsVisible`, `CanUserResize`, and read-only `ActualWidth`. A width change resets actual widths via the table. There is no column `MinWidth` property in this implementation.
- [TableViewColumnHeader](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/TableViewColumnHeader.cs): subscribes to `PART_Resizer` Thumb drag events and sets the owning column's Width to pixels. Its drag handler is private; it clamps to the resizer's width, not the dynamic column's configured minimum.
- [TableViewLayoutHelper](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/Presenters/TableViewLayoutHelper.cs): internal layout distributes the remaining viewport width by Star weights and treats Auto as 1*. It recalculates all actual widths together.
- [TableViewCellsPresenter](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/Presenters/TableViewCellsPresenter.cs): rows recycle, but columns within a row are not virtualized.

The library currently disables native resizing per column, hides the native resizer in its theme, and intercepts pointer input to implement resizing, reordering, and selection dragging. Native resizing is a promising candidate, but matching custom minimum widths, metadata/state synchronization, visual hit targets, and gesture interactions must be demonstrated before replacing it. The upstream source checked here does not establish a supported native column-reordering or sorting API; verify those separately before drawing a final conclusion.

### Native resize proof of concept

`Native_table_resize_updates_width_and_preserves_other_star_columns` exercises a plain Avalonia TableView using a headless pointer drag on `PART_Resizer`. On 12.1.4, the dragged Star column becomes Pixel and grows by the drag distance, the other column stays Star, and `TableViewColumn.WidthProperty` raises a change notification. The solution build and this focused test passed.

This verifies an observable notification for synchronization, not a complete integration: the test does not enforce DynamicTableView minimum widths, sync its metadata or captured state, or cover the existing custom header and input behavior. The native handler clamps to its grip width, so minimum-width enforcement needs an explicit design and regression tests. The following minimum-width prototype tests whether that policy can preserve the existing resize interaction.

A guarded observer prototype clamps native Width to 100 and copies it to column metadata. The headless test moves the pointer 350 pixels left, then 30 pixels right: the pointer is still left of the clamped divider and the width remains 100. A further move past the divider increases the width. All three drag events arrive, and pointer capture stays on the same grip descendant; capture loss is not the cause. The reverse event still reports a negative native drag delta. A comparison without the minimum clamp shows a positive reverse delta and width recovery when layout can follow the pointer.

In Avalonia 12.1.4, [Thumb](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/Primitives/Thumb.cs) computes each delta from the pointer position relative to the grip and its press position; [TableViewColumnHeader](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Controls/TableViewColumnHeader.cs) applies that delta to ActualWidth. After the observer clamps Width and layout places the grip at the minimum, the pointer remains beyond that grip, so reversing within the overshoot still yields a negative delta. The existing custom handler instead uses successive pointer positions, allowing an immediate reverse at the minimum. The observer approach therefore cannot preserve that behavior using native width notifications alone. Retain the production custom resize path; do not proceed with the real-control native integration on this basis. This is a limitation of the tested API approach, not evidence that all possible native extensions are impossible. The headless result does not establish behavior on every platform. CaptureState/RestoreState, scrolling, sorting and reordering under a native integration remain untested.

## 2. Column configuration and lifecycle

Status: Complete. Source and column replacement now clear stale manual-sizing keys; focused headless tests cover reuse of a key after both transitions. Header changes update attached visuals and queue header-width measurement; cell template changes update attached cells and are picked up after reattachment. Sort and filter capabilities update attached headers; disabling either removes the active keyed sort or filter, including custom keyed filters. Searchability changes rebuild an active search predicate.

Capability-driven query removal is scoped to the disabled column: `ColumnOnSortCapabilityChanged` rewrites the sort list without that key, and `ColumnOnFilterCapabilityChanged` removes only that key from both `_filters` and `_customFilters`, leaving other columns' keyed state and scope filters intact. Verification: `Disabling_column_capabilities_removes_only_that_columns_active_query` (unit) and `Changing_column_capabilities_updates_header_and_clears_query` (headless) assert that the surviving sort and filter belong to the other column and that re-enabling does not restore the removed ones.

Header subscriptions follow the attached lifetime. `DynamicTableViewHeader` subscribes to `HeaderChanged`, `SortCapabilityChanged`, `FilterCapabilityChanged`, and `FilterConfigurationChanged` in `OnAttachedToVisualTree` and unsubscribes on detach; `UpdateCapabilities` also detaches the click handler, hides an open flyout, and clears or rebuilds the attached flyout. `DynamicTableViewSource.SynchronizeColumnSubscriptions` compares observed columns against `Columns` on every collection change and unsubscribes in `Dispose`, so a definition shared by two sources only keeps the subscription of the source that still contains it. Verification: `Column_query_notifications_follow_source_membership_and_disposal`, `Disabling_filter_capability_hides_an_open_flyout_and_its_filter`, and `Column_capabilities_changed_while_detached_apply_after_reattachment`.

Full suite: 250 tests passed. Sizing metadata and filter configuration now notify as well.

Width and minimum widths notify through `DynamicTableViewColumn.WidthOptionsChanged`. `WidthMode`, `Width`, and `MinWidth` now validate (finite, non-negative) and raise the event; an attached `DynamicTableView` observes the definitions of its current source only while attached and re-applies the initial width, drops any manual-size key, and re-queues Star sizing and auto-width measurement. Metadata changed while detached is applied in `OnAttachedToVisualTree` via `ApplyWidthMetadataToNativeColumns`, so a pending change is not lost. `RestoreState` and `ResizeColumnAtDivider` write through `SetColumnWidthMetadata`, which suppresses re-entrancy so an internal write does not look like a caller edit. Verification: `Changing_width_metadata_updates_the_attached_column_while_visible`, `Pending_width_metadata_change_is_used_when_the_table_attaches`, and `Changing_width_metadata_overrides_a_restored_manual_width`.

Filter configuration notifies through `DynamicTableViewColumn.FilterConfigurationChanged`; `FilterChoices` and `FilterFlyoutFactory` raise it, attached headers rebuild the attached flyout and close an open one, and the active filter descriptor is intentionally preserved. Verification: `Changing_filter_choices_rebuilds_the_next_flyout_and_keeps_active_filter` and `Changing_filter_factory_rebuilds_the_next_custom_flyout`.

Audit: text wrapping, trimming, maximum lines, header text, cell template, searchability, sort/filter capabilities, sizing metadata, and filter configuration all notify now. Sizing changes are applied by an attached table rather than the definition itself, so a definition with no attached table simply keeps its metadata. Definitions can also be shared by sources, so subscriptions and cleanup account for more than one owner. The user selected immediate removal of the corresponding active descriptor when a sort or filter affordance is disabled.

Superseded: the original audit recorded `MinWidth` as plain metadata with no style path. The user rejected that, because a theme cannot own a per-column minimum when every definition must repeat it. `MinWidth` is now also settable from a style through the attached `DynamicTableViewColumnProperties.MinWidthProperty` on the native `TableViewColumn`, targeted by a class copied from `DynamicTableViewColumn.Classes`. A styled value takes precedence over the metadata value, and a change to it re-applies sizing on an attached table. Verification: `Style_can_set_a_column_minimum_width` and `Metadata_minimum_still_applies_when_no_style_sets_one`. Full suite: 250 tests passed.

### Manual sizing state: ownership decision

`_manuallySizedColumns` is a per-`DynamicTableView` set of keys, not per-source or per-definition state. The decision is to keep it that way, because "the user dragged this divider on this table" is a property of that table's viewport, not of the shared metadata:

- Cleared on `AttachSource` (a different source has different columns and its own saved widths), on a column-collection `Reset`, and per key when that key is removed. Re-adding a key therefore starts unsuppressed, which is why auto sizing resumes instead of silently sticking.
- Written by `ResizeColumnAtDivider` and by `RestoreState` for pixel columns, and cleared by `ApplyWidthMetadata` so an explicit metadata edit supersedes a captured drag.
- Two tables sharing one source and one definition keep independent suppression sets. A resize in one table writes pixel width into the shared definition, which both tables then observe, so they agree; but restoring a manual width in one table does not suppress auto sizing in the other.

That last point is the one meaningful asymmetry: shared metadata converges on width, while per-table suppression is intentionally independent. It is verified by `Resizing_a_shared_column_definition_keeps_attached_tables_in_sync` (both tables adopt the dragged width and agree with captured state, other columns untouched) and `Header_change_restores_auto_sizing_in_every_table_sharing_the_definition` (a metadata change clears suppression in both). Key reuse across source replacement, column removal, and clearing is covered by `Replacing_source_does_not_suppress_auto_sizing_for_reused_key`, `Removing_column_does_not_suppress_auto_sizing_for_reused_key`, and `Clearing_columns_does_not_suppress_auto_sizing_for_a_reused_key`.

### Section 2 checklist

- Define which column properties support runtime changes and implement notifications and synchronization for that contract, including header, templates, width, minimum, sorting, and filtering where applicable. Done for header, cell template, cell text options, searchability, sort/filter capabilities, sizing metadata, and filter configuration.
- Scope manual sizing state to the current source and column lifetime; test source replacement, clearing, and removing/re-adding a key. Done: scoped per table by design, with the transitions above covered.
- Preserve saved state and document ownership of mutable definitions and UI-thread requirements. `README.md` now documents the mutable property set, the UI-thread requirement, the disable-immediately-clears behavior, shared-definition ownership, and per-table manual sizing.
- Add focused unit and headless tests for each supported update and lifecycle transition. Done for the notifications above.

## 3. Interaction and compatibility coverage

Status: Complete for the automation-checkable surface. Capability-driven header interactions, detach/reattach behavior for headers, minimum-width clamping with an in-gesture reverse, pointer capture loss, resize after horizontal scrolling, single-column resize, hidden-column hit testing, and sort-state automation text are covered. Manual screen-reader and platform checks remain a release task rather than a code gap.

### Defect found: Direct routed events were subscribed as Bubble

`DynamicTableView` subscribed to `InputElement.PointerCaptureLostEvent` and `InputElement.PointerExitedEvent` with `RoutingStrategies.Bubble`. In Avalonia 12.1.4 both are registered as `RoutingStrategies.Direct` ([InputElement.cs](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Base/Input/InputElement.cs)), and `Interactive.BuildEventRoute` only walks ancestors for Tunnel/Bubble events. A Direct event raised on the table therefore never reached a Bubble subscription, so `OnGridPointerCaptureLost` and `OnGridPointerExited` were dead code: a lost capture left the resize gesture active, and leaving the table left the resize hover class and cursor set. Both subscriptions now use `RoutingStrategies.Direct`. Verification: `Losing_pointer_capture_ends_the_resize_gesture` and `Leaving_the_table_clears_the_resize_hover_state`; reverting either subscription to Bubble makes the corresponding test fail.

The headless mouse device does not raise `PointerCaptureLost` when capture is released programmatically, so the capture-loss test raises the routed event directly on the table, which is also the only way a Direct event reaches the table's own handler.

### Defect found: hidden columns still accepted resize input

`TryHitTestDivider` skipped a column using `grip.IsVisible`. A collapsed `TableViewColumnHeader` leaves the grip's own `IsVisible` set to `true`, so a hidden column still owned a live divider in the cached hit-test list: hovering its old position set the resize cursor and pointer-over class, and a drag starting there resized a column that was not on screen. The check now uses `IsEffectivelyVisible`, which accounts for the collapsed header. Verification: `Hidden_column_is_skipped_by_the_resize_hit_test` covers hover, a neighboring drag that must not touch the hidden width, and re-showing the column so the divider works again; reverting to `IsVisible` makes it fail.

### Accessibility

The filter button already had an accessible name. Sort state did not: it was conveyed only by an arrow `PathIcon`, and `DynamicTableViewResources.SortAccessibleName` was unused dead code. `DynamicTableViewHeader` now exposes `SortAccessibleText`, set from the new `SortAscendingAccessibleName` / `SortDescendingAccessibleName` resources and cleared when the column is unsorted, and the sort indicator in the control theme binds `AutomationProperties.Name` to it. Verification: `Sort_state_is_exposed_to_automation_and_cleared_when_unsorted` asserts empty text when unsorted, the ascending and descending strings after each sort, and that the indicator hides and clears when the sort is removed; removing the template binding makes it fail.

- Cover single and hidden columns, horizontal scrolling, reorder, minimum constraints, repeated and reverse drags, pointer capture loss, restore, and detach/reattach. Done.
- Cover keyboard and automation behavior, filter/sort interactions, and alternative themes/templates; distinguish supported customization from private template assumptions. Done for the interactions above plus sort-state automation text. Customization relies on the public control-theme and template-binding surface, not private template internals.
- Verify accessibility semantics and focus behavior with appropriate automation and manual platform checks. Automation coverage is in place for the filter button name and sort state; screen-reader narration and platform-specific focus order still need a manual pass on each shipping platform.
- Run the required solution build before tests, then focused and full suites; document platform gaps. Done: `dotnet build --tl:off -clp:ErrorsOnly` precedes every test run, and 250 tests pass. All UI results are headless; no physical-platform verification has been recorded.

## 4. Performance measurement

Status: Harness repaired; the first trustworthy baseline is recorded below. The remaining open work is coverage breadth, not tooling.

### Root cause of the broken benchmark run

BenchmarkDotNet 0.15.8 (the latest stable release) does not recognize .NET 11: it maps the pinned runtime to `RuntimeMoniker.NotRecognized`, and `DotNetSdkValidator.ValidateCoreSdks` calls `RuntimeMonikerExtensions.GetRuntimeVersion`, which throws `NotImplementedException: GetRuntimeVersion not implemented for NotRecognized`. That is a hard failure during job validation, so no benchmark could run — the earlier in-process workaround only appeared to work because it skipped the out-of-process validator.

Two attempted fixes and why they were rejected:

- `CoreRuntime.CreateForNewVersion("net11.0", ...)` compiled on 0.15.8 but still failed: the second parameter is a display name, not a framework moniker, and the validator still resolves the runtime moniker before the job is used.
- Pinning the `net10.0` target was rejected because the library's primary target is `net11.0`; measuring a different runtime than the library ships on would misrepresent it.

The fix is `BenchmarkDotNet` 0.16.0-preview.2, which adds `CoreRuntime.Core11_0`. The preview is required because no stable release supports .NET 11. `BenchmarkDotNetConfiguration` names that runtime explicitly rather than relying on detection of the pinned RC build, so the job does not depend on runtime-version sniffing. This is a deliberate prerelease dependency on a benchmark-only project; revisit it when a stable BenchmarkDotNet release supports .NET 11.

Two operational notes: a stale `.bdn` working directory makes the generated build fail with `CS0016` on the library's XML doc path, and deleting that directory fixes it; and `--job` on the command line overrides the configured job, so the runtime must be supplied by the config rather than the CLI.

### Recorded baseline

`DynamicTableViewUiBenchmarks.AutoWidthAfterSourceUpdate`, ShortRun, out-of-process, 20 columns, headless Fluent theme, Ubuntu 26.04.1 (Xeon E5-2690), .NET 11.0.0-rc.3:

| Rows | Mean | Error | Allocated |
|------|------|-------|-----------|
| 100 | 33.38 ms | 7.840 ms | 4.93 MB |
| 1,000 | 39.01 ms | 105.785 ms | 4.92 MB |
| 10,000 | 34.48 ms | 19.073 ms | 4.93 MB |

The flat mean and constant allocation across a 100x row increase is the meaningful result: the auto-width pass measures only realized cells, so it is bounded by the viewport rather than the source size. That is evidence virtualization holds for this path, and it matches the design intent. The error bars are wide because ShortRun uses three iterations; treat the ordering and the flatness as the signal, not the absolute milliseconds.

### Post-extraction comparison

Re-measured after the composition extraction moved this path into `ColumnWidthCalculator`, `StarColumnWidths`, and `TextMeasurer`. Same machine, same OS, same runtime, same ShortRun job, same 20-column source:

| Rows | Mean before | Mean after | Allocated before | Allocated after |
|------|-------------|------------|------------------|-----------------|
| 100 | 33.38 ms | 34.75 ms | 4.93 MB | 4.92 MB |
| 1,000 | 39.01 ms | 40.48 ms | 4.92 MB | 4.92 MB |
| 10,000 | 34.48 ms | 34.71 ms | 4.93 MB | 4.92 MB |

Allocation is unchanged, which is the claim the extraction had to preserve: the star loop still runs without scratch storage and the sizing types add none. The mean moved by 1.4 ms at 100 rows and 1.5 ms at 1,000, both inside the recorded error bars (23.284 ms and 71.763 ms respectively), so the difference is not resolvable with this job. The conclusion supported here is narrow and sufficient: the extraction introduced no measurable regression on the measured path. It does not show an improvement, and it is not evidence about paths without a baseline.

### Star layout: removing the per-frame recompute

`OnLayoutUpdated` ran the star redistribution on every layout pass, which includes every scroll frame. The pass wrote `native.Width`, so it also invalidated layout and could feed another pass. Scrolling cannot change a star width, so the work was wasted; the user-visible symptom was star columns settling after a scroll.

The pass now compares a key of its three real inputs — the width budget left after fixed columns take their realized width, the column count, and a revision bumped by width-metadata and collection changes — and skips the redistribution and writes when the key is unchanged. Measured on the shrinking-viewport test: 18 layout passes produced 8 recomputes, one per actual viewport change.

Avalonia cannot enforce this itself. `TableViewLayoutHelper.UpdateActualWidths` distributes the star budget purely proportionally with no minimum awareness, so two star columns with a minimum above their share both render below it. The redistribution is therefore required, and the minimum cannot be delegated to the platform.

### Star layout: removing the below-minimum flash

The previous loop wrote `native.Width` as each column was resolved, so a constrained column could be arranged below its minimum for one frame and then snap to it. `StarColumnWidths.ComputeWidths` now resolves every width into a buffer and the caller applies them in one pass, so no width below the minimum is ever written. Verification: `Star_columns_never_apply_a_width_below_their_minimum_while_the_viewport_shrinks` asserts that every pixel width applied while shrinking from 900 to 200 is at or above the minimum, in addition to the existing `ActualWidth >= MinWidth` assertions.

- Establish representative row and column counts, update rates, viewport sizes, themes, and interaction workloads. Row counts and a 20-column case are covered; update rates, viewport sizes, and themes are not yet varied.
- Use existing benchmarks and profiling to measure auto-width passes, realized-cell traversal, layout invalidation, resize/reorder input, allocations, and UI responsiveness. Include many-column scenarios because native cells are not column-virtualized. Auto width is measured; scrolling, column moves, state restore, and star layout have benchmarks but no recorded baseline.
- Record baseline environment and results; optimize only measured bottlenecks, then repeat the same measurements and retain regression coverage. The first baseline is recorded above; no bottleneck has been identified that justifies an optimization yet.

## 5. Architecture decision

Status: Decided. The record below keeps the single subclass, defers both the native-resize integration and a second `TableView`-derived control, and schedules one targeted extraction.

### Options compared

| Option | Correctness | Maintenance | Performance | Accessibility | Platform risk |
|--------|-------------|-------------|-------------|---------------|---------------|
| **Keep the subclass** (chosen) | Only option proven against the full retained-feature test suite | One 1066-line control with six clustered responsibilities | Bounded by realized cells; virtualization confirmed on the auto-width path | Automation names for filter and sort state now exist; rest unverified | Relies on public Avalonia surface plus two upstream template part names |
| **Native extension points** | Cannot meet the reverse-drag contract (section 1) | Lowest churn when upstream adds APIs | Native layout, but no DynamicTableView minimum widths | Inherits Avalonia's own semantics | Best long-term, blocked today |
| **Add a second `TableView`-derived control** | Preserves behavior only if the interaction seam is factored out first; no behavior gain by itself | Does not shrink the class — converts private fields into a `protected` contract; adds a second public type to document and test | Neutral | Neutral | Splits exposure: display type avoids pointer/template coupling, interaction type keeps it |
| **Extract adapters/composition** | Preserves behavior if done incrementally under the existing tests | Splits responsibilities; adds indirection | Neutral if hot paths stay allocation-free | Neutral | Reduces exposure to template internals over time |
| **Vendor/fork Avalonia controls** | Full control over header input and layout | Tracks upstream fixes, license, upgrades, and per-platform testing by hand | Could optimize column layout | Would own all accessibility work | Worst: every Avalonia release becomes a merge |

### Evaluating a second `TableView`-derived control

The idea: split `DynamicTableView` into a base type that owns columns, source, state, and width, and a derived type that owns the pointer interaction, so consumers can pick a read-only table without paying for resize/reorder/selection-drag code — and so the 1066-line class splits along an existing seam.

**What the code already supports.** The seam exists in `DynamicTableView` today: the `TableView` subclass is the only base type, and the interaction cluster (pointer pressed/moved/released/capture-lost/exited, `TryHitTestDivider`, `RefreshResizeHeaders`, `UpdateRowSelectionDrag`) is already contiguous and separable from the state/width cluster. Nothing in the library prevents a split.

**What blocks a clean split, concretely.** The split is not free, because four things are typed against the concrete class rather than a base:

- `DynamicTableViewHeader` holds `private readonly DynamicTableView _owner` and calls six members on it: `GetNativeColumn`, `UpdateHeaderState`, `NotifyColumnHeaderChanged`, `NotifyHeaderTemplateApplied`, `MakeFilterFlyout`, `SortColumn`.
- `DynamicTableViewCell` holds `private readonly DynamicTableView _table`.
- Both types are constructed with the concrete type in their public constructors.
- The theme's 17 styles are keyed to `TableView.dynamic-table-view`, and `StyleKeyOverride` returns `typeof(TableView)`.

So a split must first introduce a base contract (interface or abstract base) covering those six header calls plus the cell's needs, then retype the two controls, then decide whether the base shares the same style key or needs a second theme entry. That is a genuine refactor with its own regression surface, not a mechanical extraction.

**The correctness trap.** `DynamicTableView` is `sealed`. A base/derived pair means unsealing it and converting private fields (`_nativeColumns`, `_manuallySizedColumns`, `_observedWidthColumns`, `_cellSamples`, the resize and selection-drag fields) into `protected` or internal surface. Every one of those is currently free to change; making it `protected` freezes it as API for the derived type and, if the base is public, for consumers. The repository's rules require every public API to be documented and tested, so a `protected` surface is a real cost, not a formality.

**Behavioral evidence against doing it now.** The defects found in section 3 live *in the interaction cluster* — Direct-event routing and hidden-column hit testing. Splitting that cluster into a derived type while it is still being corrected would have meant fixing both halves and verifying the seam, and would have obscured which type owned the bug. There is also no measured problem to solve: the auto-width baseline is flat from 100 to 10,000 rows, so nothing in the current design is demonstrably too slow or too coupled for consumers.

**What it would actually buy.** Only a smaller read-only surface for consumers who never resize. It would not reduce total code, would not improve measured performance, and would not by itself satisfy the single-responsibility concern — the base would still hold source attach, column sync, width/star layout, auto-width, and state capture, which is most of the 1066 lines.

**Conclusion.** A second derived control is a reasonable future shape, but it is a refactor of the same cluster that is currently the source of defects, and its only benefit is surface-area reduction. The cheaper first move is the extraction already scheduled in the deferred risks: pull width/star/auto-width into `ColumnWidthController` as a *composition* unit. That shrinks the class without adding a public type or freezing a `protected` contract, and if the interaction cluster still warrants its own type afterwards, the header/cell retyping happens once, against a class that is already smaller. *Trigger to revisit:* the interaction cluster changes again, or a consumer asks for a read-only table without resize/reorder.

### Decision

**Keep the single subclass.** Every retained behavior — custom resize with immediate reverse at the minimum, header reorder, selection drag, keyed state capture/restore, capability-driven query cleanup, and the live-update notification contract — is implemented and regression-tested against `TableView`. The native-resize option is disproven for the reverse-drag contract, not merely unexplored, so replacing the custom interaction now would regress tested behavior. A fork is unjustified: no blocker has been found that the public surface cannot express. A second `TableView`-derived control is deferred rather than adopted: it reduces consumer surface area but requires unsealing the class, freezing private state as `protected` API, and retyping the header and cell against a new base contract, all inside the cluster that is still the source of defects.

The evidence that the subclass is the right shape, rather than merely the incumbent: 250 tests cover the retained surface, all UI results are headless, and the one measured performance path shows the design goal holds (auto-width stays flat from 100 to 10,000 rows because only realized cells are measured).

### Composition extraction (completed)

The follow-up question was whether the library should own more of its logic by composing instead of inheriting. The answer is inheritance for the control and composition for everything the control delegates. Avalonia seals the part that matters: `TableView` is `ListBox`-derived and exposes four public properties, while `TableViewLayoutHelper`, the header and cell presenters, and every column-attach and cell-rebuild method are `internal`. Replacing the base would mean rewriting the header row, the cell presenter, row-container recycling, and actual-width propagation — a net loss of ownership, not a gain.

The class was instead reduced by extracting cohesive units, with no public surface change except one deliberate promotion:

| Extracted | Kind | Owns |
|---|---|---|
| `ColumnWidthCalculator` | static, pure | Initial width, effective minimum (styled over metadata), star weight and star width |
| `StarColumnWidths` | static, pure | The constrained-minimum redistribution loop over available width |
| `DynamicTableViewStateSerializer` | static, pure | State capture, restore ordering, and restored-width clamping |
| `TextMeasurer` | instance | The off-tree `TextBlock` used for auto-width measurement |
| `ResizeHeaderCache` | instance | Realized headers and grips, staleness detection, divider hit testing |
| `ColumnResizeController` | instance | The resize gesture: begin/move/end, pointer ownership, hover feedback |

Allocation behavior is unchanged: the star loop still runs without scratch storage, and the two static sizing types add no allocation to the layout path. `DynamicTableView` keeps the side effects (moving columns, writing width metadata, capturing the pointer) so the extracted types stay testable without a visual tree.

`ResizeColumnAtDivider` stays `internal`. It is the operation the drag performs, so the resize contract is tested directly rather than only through a simulated pointer gesture, and the test project reaches it through an `InternalsVisibleTo` entry in the library `.csproj`. That keeps the member out of the shipped public surface — a host driving resize from a keyboard shortcut would need a new public member and its own API decision, which is not made here. Verification: `Resize_column_at_divider_grows_and_shrinks_the_column` and `Resize_column_at_divider_ignores_unknown_key_and_disabled_resizing`. Full suite: 250 tests passed; both `net10.0` and `net11.0` build with no warnings.

### Deferred risks

These are accepted, not resolved. Each has a stated trigger for revisiting.

1. **Upstream template coupling.** The library styles `PART_Resizer` and `ScrollViewer#PART_ScrollViewer > DockPanel#ContentPanel` — upstream Avalonia template internals, not public API. A rename in a future Avalonia release silently breaks the resize-grip hiding and content-panel padding. *Trigger:* re-run the section 3 interaction tests on every Avalonia upgrade; if either style stops matching, adapt or raise upstream.
2. **`DynamicTableView.cs` size and responsibilities.** Addressed by the composition extraction recorded below. The control dropped from 1,117 to 947 lines with the width math, state mapping, star layout, text measurement, resize gesture, and header cache moved out. What remains is source attach/column sync, header/sort/filter coordination, selection drag, and visual-tree lookup. *Trigger for more:* the interaction cluster changes again for a third time, or a consumer asks for a read-only table that omits resize, reorder, and selection drag.
3. **Headless-only verification.** No physical-platform run is recorded, so platform-specific input, focus order, and screen-reader narration are unproven. *Trigger:* manual pass per shipping platform before a stable release.
4. **Benchmark coverage is one path deep.** Auto-width has a baseline; scrolling, reorder, state restore, and star layout have benchmarks but no recorded numbers. *Trigger:* record baselines before optimizing any of those paths.
5. **Prerelease benchmark dependency.** `BenchmarkDotNet` 0.16.0-preview.2 is required because no stable release supports .NET 11. *Trigger:* move to stable when it supports .NET 11.

### Conditions for revisiting this decision

- Avalonia publishes supported column-resize minimums, header drag-reorder, or a per-column `MinWidth`; then re-run the section 1 comparison rather than assuming the old result holds.
- The interaction cluster changes again for a third time, which would make extraction — or a second derived control — cheaper than continued patching.
- A consumer asks for a read-only table that omits resize, reorder, and selection drag; then evaluate the derived split against the already-shrunk class.
- A platform-specific defect appears that the subclass cannot fix through public API.
- Any upstream template part name the library styles disappears.

### Record of what was decided against

- Replacing the custom resize path with native resizing: rejected on evidence in section 1 — the guarded-observer prototype cannot reverse within the minimum-width overshoot.
- Forking Avalonia controls: rejected as unjustified cost; no blocker requires it.
- Splitting into a second `TableView`-derived control: deferred, not rejected. The seam exists, but a clean split requires a base contract for the six header calls and the cell, retyping both controls, unsealing `DynamicTableView`, and freezing private fields as `protected` API — all inside the interaction cluster that is currently the source of defects, for a benefit limited to surface-area reduction. The composition extraction (`ColumnWidthController`) is the cheaper first step and is already scheduled above.
