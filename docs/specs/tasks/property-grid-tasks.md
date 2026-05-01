# PropertyGrid Implementation Tasks

Derived from: `docs/specs/004-property-grid.md`

---

## Phase 1: INPC Foundation — UseObservableTree (General Reactor Infrastructure)

### 1.1 ObservableTreeTracker Core
- [x] Create `ObservableTreeTracker` class with `IDisposable`
- [x] Add `_subscriptions` dictionary (`Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler>`)
- [x] Add `_visiting` HashSet for cycle detection
- [x] Accept `Action requestRerender` in constructor

### 1.2 Reflection Cache
- [x] Create static `ConcurrentDictionary<Type, PropertyInfo[]>` cache (`_inpcPropertyCache`)
- [x] Implement `GetInpcCandidateProperties(Type)` — public instance properties, readable, non-value-type property types

### 1.3 Subscription Walk Algorithm
- [x] Implement `SyncSubscriptions(INotifyPropertyChanged root)`
  - [x] Build `desiredSet` via recursive walk with cycle detection
  - [x] Unsubscribe from objects in `_subscriptions` not in `desiredSet`
  - [x] Subscribe to objects in `desiredSet` not in `_subscriptions`

### 1.4 Nested PropertyChanged Handler
- [x] Implement `OnNestedPropertyChanged(sender, e)`
  - [x] Request re-render via toggle callback
  - [x] Read new property value via reflection
  - [x] If old value was INPC: unsubscribe recursively from old subtree
  - [x] If new value is INPC: subscribe recursively to new subtree

### 1.5 Dispose
- [x] Implement `Dispose()` — unsubscribe all handlers in `_subscriptions`, clear dictionary

### 1.6 UseObservableTree Hook
- [x] Add `UseObservableTree<T>` method to `RenderContext`
  - [x] `UseReducer(false)` for force-render toggle
  - [x] `UseRef<ObservableTreeTracker?>` for persistent tracker
  - [x] `UseEffect` with `source` as dependency — create tracker, sync subscriptions, dispose on cleanup
  - [x] Return source
- [x] Add convenience wrapper in `Component` class (matching existing `UseObservable` pattern)

### 1.7 UseObservableTree Tests
- [x] Test: nested property change triggers re-render
- [x] Test: circular object references don't infinite-loop
- [x] Test: replaced nested INPC object causes resubscribe (old unsubscribed, new subscribed)
- [x] Test: disposal cleans all subscriptions
- [x] Test: source reference change disposes old tracker and creates new one
- [x] Test: non-INPC nested properties are ignored (no crash)

---

## Phase 2: TypeRegistry + Metadata

### 2.1 Data Model Classes
- [x] Create `TypeMetadata` record with `Editor`, `Decompose`, `Compose`, `DisplayName` properties
- [x] Create `PropertyDescriptor` record with `Name`, `DisplayName`, `PropertyType`, `GetValue`, `SetValue`, `Category`, `Description`, `Order`, `IsReadOnly`
- [x] Create `ArrayTypeMetadata` record inheriting `TypeMetadata` with `CreateElement` property

### 2.2 TypeRegistry Class
- [x] Create `TypeRegistry` class with `Dictionary<Type, TypeMetadata>` backing store
- [x] Implement `Register<T>(TypeMetadata)` — fluent registration
- [x] Implement `Resolve(Type)` with fallback chain:
  - [x] Exact match in registry
  - [x] Enum — auto-generated ComboBox editor
  - [x] CLR primitive (`string` → TextField, `bool` → ToggleSwitch, `int`/`long`/`short`/`byte` → NumberBox integer, `float`/`double`/`decimal` → NumberBox decimal)
  - [x] Array/IList<T> → array editor (delegate to ArrayTypeMetadata)
  - [x] Record/class/struct → reflection-based decomposition fallback

### 2.3 Reactor-Specific Attributes
- [x] `PropertyCategoryAttribute(string name)`
- [x] `PropertyDescriptionAttribute(string text)`
- [x] `PropertyDisplayNameAttribute(string name)`
- [x] `PropertyHiddenAttribute`
- [x] `PropertyReadOnlyAttribute`
- [x] `PropertyOrderAttribute(int order)`
- [x] `PropertyEditorAttribute(Type editorType)`

### 2.4 ReflectionTypeMetadataProvider
- [x] Implement `CreateMetadata(Type)` — reflect public properties, read attributes, build TypeMetadata
  - [x] Exclude `[PropertyHidden]` / `[Browsable(false)]` properties
  - [x] Generate `Decompose` function returning `PropertyDescriptor` list
  - [x] Wire `GetValue`/`SetValue` to reflected `PropertyInfo`
  - [x] Cache results per type
- [x] Implement `CreateDescriptor(PropertyInfo, int defaultOrder)` — read all recognized attributes
- [x] Support `System.ComponentModel` fallback: `[Category]`, `[Description]`, `[DisplayName]`, `[Browsable]`, `[ReadOnly]`
- [x] Reactor attributes take precedence over System.ComponentModel when both present

### 2.5 Mutability Detection & Compose Generation
- [x] Detect property mutability: public set = mutable, init-only/no set = immutable
- [x] For immutable types, generate `Compose` function:
  - [x] Look for constructor matching property names (case-insensitive)
  - [x] Call constructor with current values, substitute updated fields
  - [x] Fallback: `Activator.CreateInstance` + init-only setter reflection

### 2.6 TypeRegistry Tests
- [x] Test: explicit registration is returned by Resolve
- [x] Test: primitive types resolve to correct built-in editors
- [x] Test: enum types resolve to ComboBox editor with correct values
- [x] Test: unregistered class falls back to reflection decomposition
- [x] Test: `[PropertyHidden]` / `[Browsable(false)]` properties excluded
- [x] Test: attribute precedence (Reactor attrs override System.ComponentModel)
- [x] Test: Compose generation for immutable record type
- [x] Test: mixed mutability (some properties mutable, some immutable)

---

## Phase 3: Core PropertyGrid Component

### 3.1 PropertyGridElement
- [x] Create `PropertyGridElement` record extending `Element`
  - [x] Required: `Target`, `Registry`
  - [x] Optional: `OnRootChanged`, `Filter`, `ShowSearch`
  - [x] Template overrides: `CategoryTemplate`, `PropertyRowTemplate`, `PropertyLabelTemplate`, `ArrayItemTemplate`, `ArrayToolbarTemplate`
  - [x] Internal `Setters` array

### 3.2 DSL Factory
- [x] Create `PropertyGrid(object target, TypeRegistry registry, Action<object>? onRootChanged = null)` factory method

### 3.3 PropertyGrid Component
- [x] Create `PropertyGridComponent : Component`
- [x] INPC observation: if target is INPC, call `UseObservableTree`
- [x] Property tree construction: read target properties via registry, build ordered list of PropertyDescriptors
- [x] Category grouping: group properties by `Category`, default to "General" if null
- [x] Category expand/collapse state management (per-category `UseState<bool>`)

### 3.4 Default Rendering Templates
- [x] Implement `PropertyGridDefaults.CategoryTemplate` — Expander with VStack children
- [x] Implement `PropertyGridDefaults.PropertyLabelTemplate` — Text with secondary color + tooltip
- [x] Implement `PropertyGridDefaults.PropertyRowTemplate` — FlexRow, label 160px fixed, editor flex-grow, 32px height, indentation

### 3.5 Rendering Pipeline
- [x] For each property: render label via template, resolve editor via registry, compose row via template
- [x] Group rows by category, render each group via CategoryTemplate
- [x] Properties with no category → "General" group at top
- [x] Stable declaration-order sorting within each category

### 3.6 Atomic Property Editing
- [x] Wire primitive editors: TextField for string, ToggleSwitch for bool, NumberBox for numeric types, ComboBox for enums
- [x] Editor `onChange` calls `PropertyDescriptor.SetValue` for mutable properties
- [x] Read-only properties render disabled/non-editable editors

### 3.7 Core Component Tests
- [x] Test: mutable object with primitive properties renders editors
- [x] Test: editing a property calls the setter and mutates the object
- [x] Test: categories group correctly
- [x] Test: read-only properties are non-editable
- [x] Test: `Filter` prop excludes matching properties
- [x] Test: INPC target is observed (external mutation re-renders grid)

---

## Phase 4: Decomposition & Immutable Support

### 4.1 Expandable Composite Properties
- [x] Detect types with `Decompose` in their TypeMetadata
- [x] Render expand/collapse toggle for composite properties
- [x] When expanded, recursively render sub-properties (indented by 16px per level)
- [x] Nested decomposition (composite within composite)

### 4.2 Immutable Edit Propagation
- [x] Maintain path from each leaf editor back to root
- [x] On edit of immutable node, walk up path calling `Compose` at each immutable level
- [x] Stop at nearest mutable ancestor and call `SetValue`
- [x] Mutable ancestor raises `PropertyChanged`

### 4.3 OnRootChanged for Fully Immutable Roots
- [x] If root object itself is immutable, fire `OnRootChanged` callback with newly constructed root
- [x] Parent component uses `UseState` + `onRootChanged: setConfig` pattern

### 4.4 Custom Type Editors
- [x] Types with both `Editor` and `Decompose` show compact editor + expand toggle
- [x] Expanded state shows decomposed sub-properties
- [x] Collapsed state shows inline editor (e.g., Color hex)
- [x] `[PropertyEditor]` attribute support: locate static `CreateEditor` method on referenced type

### 4.5 Decomposition Tests
- [x] Test: composite type expands to show sub-properties
- [x] Test: editing a decomposed sub-property on mutable parent mutates correctly
- [x] Test: immutable nested edit propagates up via Compose chain
- [x] Test: fully immutable root fires OnRootChanged
- [x] Test: type with both Editor and Decompose renders both modes
- [x] Test: multi-level nested immutable propagation (3+ levels deep)

---

## Phase 5: Array Support

### 5.1 Array Decomposition
- [x] Detect array/list types in `TypeRegistry.Resolve` (`T[]`, `IList<T>`, `List<T>`)
- [x] Decompose into indexed child entries
- [x] Each item resolves through registry for its element type

### 5.2 Array UI Components
- [x] Implement `ArrayToolbarTemplate` default — property name, count badge, [+] add button
- [x] Implement `ArrayItemTemplate` default — index, summary (ToString/DisplayName), expand toggle, [up][down][remove] buttons

### 5.3 Array Operations
- [x] **Add**: call `CreateElement` factory (async), append to list
- [x] **Remove**: remove item at index
- [x] **Reorder**: move up / move down within list
- [x] For `T[]` (fixed-size arrays): replace array on parent via property setter
- [x] Hide [+] button when `CreateElement` is null

### 5.4 Array Item Editing
- [x] Expand array item to show its properties (recursive PropertyGrid rendering)
- [x] Item expand/collapse state management
- [x] Observe INPC items: combine `UseCollection` (structure) + `UseObservableTree` per INPC item

### 5.5 Default CreateElement
- [x] For types with parameterless constructor: `Activator.CreateInstance<T>()`
- [x] For types without parameterless constructor: `CreateElement` = null (add disabled)

### 5.6 Array Tests
- [x] Test: array property renders with correct item count
- [x] Test: add operation appends new element
- [x] Test: remove operation removes correct element
- [x] Test: reorder (move up/down) works correctly
- [x] Test: `T[]` replacement via setter after mutation
- [x] Test: array of INPC items — item property change re-renders
- [x] Test: CreateElement null hides add button

---

## Phase 6: Polish

### 6.1 Search/Filter Box
- [x] Implement optional search box (controlled by `ShowSearch` prop)
- [x] Filter properties by display name match (case-insensitive)
- [x] Show matching properties across all categories (flatten if filtering)

### 6.2 Keyboard Navigation
- [x] Tab navigation between editors
- [x] Arrow key navigation between property rows
- [x] Enter to expand/collapse composite properties
- [x] Escape to collapse current expansion

### 6.3 Accessibility
- [x] Set `AutomationId` on all interactive elements
- [x] Associate labels with editors (accessible name)
- [x] Screen reader support for category expand/collapse state
- [x] Array operation buttons have accessible names

### 6.4 Performance
- [x] Evaluate need for virtualization on long property lists
- [x] If needed, implement virtual scrolling for property rows
- [x] Profile and optimize reflection-heavy paths if slow

---

## Summary

| Phase | Task Count |
|-------|-----------|
| Phase 1: INPC Foundation | 22 |
| Phase 2: TypeRegistry + Metadata | 22 |
| Phase 3: Core PropertyGrid | 18 |
| Phase 4: Decomposition & Immutable | 14 |
| Phase 5: Array Support | 17 |
| Phase 6: Polish | 11 |
| **Total** | **104** |
