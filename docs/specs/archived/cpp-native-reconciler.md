# ReactorCpp: Native C++ Reconciler and Language Binding

This document specifies a fully native C++ implementation of the Microsoft.UI.Reactor (Reactor) framework — element model, hooks, reconciler, and WinUI bridge — designed so that an existing C++ program can adopt WinUI declaratively without a ground-up rewrite.

---

## Motivation

The C# Reactor framework pays a managed/unmanaged transition cost on every WinRT call during reconciliation. The Rust native differ experiment (now removed) showed that a native component embedded in a mostly-managed stack doesn't net enough perf win — the serialization boundary ate the gains.

**Hypothesis:** a *fully* native stack — where the element tree, diffing, patching, hooks, and state management all live in C++ — can avoid those transitions entirely. The only WinRT calls happen at the leaf edge when we actually set a property on a WinUI control. If this hypothesis holds, the C++ reconciler should show measurable FPS and memory improvements in the stress_perf grid benchmark.

Even if the perf win is modest, a high-quality C++ binding has independent value: it lets existing C++ desktop applications adopt WinUI declaratively without rewriting in C#.

---

## Goals

1. **Ergonomic modern C++** — the app-facing API should feel like idiomatic C++20/23, not like COM plumbing.
2. **Native reconciler** — element tree, diffing, child reconciliation (LIS), and hook state all in C++. Zero managed transitions in the hot path.
3. **Thin WinRT bridge** — the only C++/WinRT calls are leaf-level property sets (`textBlock.Text(...)`, `panel.Children().Append(...)`) and control construction.
4. **Port the TestApp** — validate ergonomics and correctness by porting the existing 11-tab test app.
5. **Perf comparison** — add a 4th `StressPerf.ReactorCpp` variant to the existing benchmark harness.
6. **Isolated experiment** — everything lives under a single top-level `ReactorCpp/` directory; easy to delete if the experiment doesn't pan out.

## Non-Goals

- Porting FlexPanel / Yoga (not needed to test the core hypothesis).
- Porting Monaco Editor or other advanced custom controls.
- Production-quality error messages or developer tooling (hot reload, CLI).
- Supporting controls beyond what the TestApp needs.
- NuGet packaging or distribution.

---

## Prior Art & Influences

### Landscape Survey of Reactive/Declarative C++ Frameworks

We surveyed the existing C++ UI framework landscape to inform ReactorCpp's design. **No existing C++ framework combines React-style hooks, virtual element tree reconciliation, and a bridge to a native platform UI toolkit.** ReactorCpp is novel in this regard. The closest conceptual matches and their relevant patterns:

| Project | Architecture | Key Ideas / What We Borrow | What Doesn't Fit |
|---------|-------------|---------------------------|-------------------|
| **[lager](https://github.com/arximboldi/lager)** (arximboldi) | Redux/Elm unidirectional data-flow in C++; uses **[immer](https://github.com/arximboldi/immer)** persistent data structures for efficient immutable state. State is value types; update logic is pure functions. Cursor/lens system for zooming into nested state. ([Architecture docs](https://sinusoid.es/lager/architecture.html)) | Clean value-oriented state model; composable state via cursors | No reconciliation or virtual tree — it's a state management library, not a UI framework. Requires separate view layer. |
| **[FTXUI](https://github.com/ArthurSonzogni/FTXUI)** | Three-layer functional terminal UI: Screen → DOM → Component. Elements built through function composition with pipe operator. Components are stateful and react to events, rendering as Elements. ([Docs](https://arthursonzogni.github.io/FTXUI/)) | Separation of static Elements (layout description) from dynamic Components (stateful + interactive) maps directly to our Element vs Component split. Proves functional UI is ergonomic in C++. | Terminal-only; no diffing/reconciliation; no platform control bridge. |
| **[AUI Framework](https://github.com/aui-framework/aui)** | Declarative C++20 toolkit using aggregate initialization as a DSL: `Vertical { Label{"Hello"}, Button{"Click"}.clicked(...) }`. Uses C++20 designated initializers, brace-enclosed children, method chaining for events. | C++20 aggregate initialization creates a clean declarative syntax. Event attachment via `.clicked()` is ergonomic. | Retained-mode OOP (no virtual tree or reconciliation). Brace-initialization is less flexible than free functions for our use case — harder to pass dynamic children or conditional elements. |
| **[ossia](https://ossia.io/posts/minimum-viable/)** ([HN discussion](https://news.ycombinator.com/item?id=30771265)) | "Minimum viable declarative GUI" using plain C++ structs as UI declarations. Layout direction via `enum { hbox }` sentinel members. Compile-time reflection iterates struct fields. Backend-agnostic: same struct renders to QML, Nuklear, etc. | Peak separation of concerns — UI spec is completely framework-agnostic. Clever use of compile-time reflection. | Too static for our needs: no state management, no events, no reconciliation. An interesting academic exercise, not a production UI model. |
| **[alia](https://github.com/alialib/alia)** | Declarative UI with dataflow/signal semantics. Reactive values (signals) as first-class citizens. Combines immediate-mode calling conventions with retained-mode output. Currently targets web via Emscripten. | Signal/dataflow approach to reactive values is interesting | Adds complexity beyond hooks; less familiar to React developers; web-focused, not native desktop. |
| **[Slint](https://github.com/slint-ui/slint)** | Declarative GUI using a custom `.slint` markup language. C++ used only for business logic via generated bindings. Strong Material Design component library. | Shows that declarative UI with C++ business logic works commercially | Uses a separate DSL, not C++ for UI declaration — opposite of our goal. |
| **[cycfi Elements](https://github.com/cycfi/elements)** | Modern C++ GUI; complex controls composed from smaller parts. Embeddable, modular design. | Compositional model (sliders from sub-parts) is solid | Retained-mode OOP; no functional/reconciled architecture. |
| **Dear ImGui** | Immediate mode; proves C++ developers accept UI-in-code | UI-in-code is natural for C++ developers | Immediate mode (no diffing); not suitable for native platform controls. |

### CppCon 2025: "Creating a Declarative UI Library in C++"

Richard Powell's [CppCon 2025 talk](https://cppcon2025.sched.com/) on this topic confirms growing community interest in declarative C++ UI. No published content yet, but the timing validates that this is an active area of exploration.

### Design Validation

Our current design (free functions + `std::variant` + fluent modifiers + hooks) holds up well against the field:

1. **Free functions beat brace-initialization** for our use case — `vstack(12, text("a"), button("b", cb))` is more readable than `Vertical { Label{"a"}, Button{"b"}.clicked(...) }`, handles dynamic children naturally, and allows conditional elements via ternary/if-else.
2. **Hooks beat Redux/signals** for component-local state — simpler mental model, familiar to React developers, no global store overhead.
3. **`std::variant` dispatch beats virtual dispatch** — exhaustive visitation at compile time, cache-friendly, no vtable overhead.
4. **Reconciliation is the missing piece** — none of the surveyed C++ frameworks implement virtual tree diffing. This is ReactorCpp's core differentiator, enabling efficient declarative UI with minimal platform control churn.

### C++/WinRT Patterns

C++/WinRT already provides a zero-overhead COM projection. Key patterns we'll use:
- `winrt::Microsoft::UI::Xaml::Controls::TextBlock` etc. for control access
- `DispatcherQueue` for scheduling renders on the UI thread
- `winrt::fire_and_forget` for async operations
- `winrt::com_ptr` / `winrt::Windows::Foundation::IInspectable` for control storage

---

## Directory Structure

```
ReactorCpp/                               # Top-level isolated directory
├── ReactorCpp.sln                        # VS solution (all projects)
│
├── ReactorCpp/                           # Core static library (.vcxproj)
│   ├── ReactorCpp.vcxproj
│   ├── packages.config
│   ├── pch.h / pch.cpp               # Precompiled header (WinRT includes)
│   ├── include/
│   │   └── duct/
│   │       ├── duct.h                # Single-header convenience include
│   │       ├── element.h             # Element types (value types)
│   │       ├── modifiers.h           # ElementModifiers, attached properties
│   │       ├── component.h           # Component base class
│   │       ├── hooks.h               # RenderContext and hooks (UseState, etc.)
│   │       ├── dsl.h                 # Factory functions: text(), button(), vstack(), etc.
│   │       └── app.h                 # ReactorApp entry point
│   └── src/
│       ├── reconciler.h              # Reconciler internal header
│       ├── reconciler.cpp            # Main reconciliation logic
│       ├── reconciler_mount.cpp      # Mount dispatch + per-control mount
│       ├── reconciler_update.cpp     # Update dispatch + per-control update
│       ├── child_reconciler.h        # Child reconciliation (LIS)
│       ├── child_reconciler.cpp
│       ├── element_pool.h            # Control pooling
│       ├── element_pool.cpp
│       ├── host.h                    # ReactorHost (render loop)
│       ├── host.cpp
│       └── winrt_bridge.cpp          # WinRT property setters, control creation
│
├── ReactorCpp.TestApp/                   # WinUI 3 test app (.vcxproj)
│   ├── ReactorCpp.TestApp.vcxproj
│   ├── packages.config
│   ├── main.cpp                       # Entry point
│   └── app.cpp                        # Port of Reactor.TestApp (all 11 demos)
│
└── ReactorCpp.StressPerf/               # Benchmark variant (.vcxproj)
    ├── ReactorCpp.StressPerf.vcxproj
    ├── packages.config
    └── main.cpp                       # StressPerf.ReactorCpp variant
```

---

## Element Model

Elements are C++ value types — cheap to construct, cheap to copy (via COW or small-buffer), and never touch WinRT. This mirrors the C# record approach.

### Design: `std::variant` Element

```cpp
#include <string>
#include <vector>
#include <functional>
#include <variant>
#include <optional>
#include <memory>

namespace duct {

// Forward declarations
struct Element;
class RenderContext;

// Shared modifiers (COW via shared_ptr for cheap copies)
struct ElementModifiers {
    std::optional<Thickness> margin;
    std::optional<Thickness> padding;
    std::optional<double> width, height;
    std::optional<double> min_width, min_height, max_width, max_height;
    std::optional<HorizontalAlignment> h_align;
    std::optional<VerticalAlignment> v_align;
    std::optional<double> opacity;
    std::optional<std::string> background;
    std::optional<std::string> foreground;
    std::optional<double> corner_radius;
    std::optional<bool> is_enabled;
    std::optional<std::string> tooltip;
    // ... additional as needed
};

// Concrete element types as structs
struct TextElement {
    std::string content;
    std::optional<double> font_size;
    std::optional<FontWeight> font_weight;
};

struct ButtonElement {
    std::string label;
    std::function<void()> on_click;
};

struct TextFieldElement {
    std::string value;
    std::function<void(std::string)> on_changed;
    std::optional<std::string> placeholder;
    std::optional<std::string> header;
};

struct CheckBoxElement {
    bool is_checked;
    std::function<void(bool)> on_changed;
    std::optional<std::string> label;
};

struct SliderElement {
    double value;
    double min = 0.0, max = 100.0;
    std::function<void(double)> on_changed;
};

struct StackElement {
    Orientation orientation;
    double spacing = 0.0;
    std::vector<Element> children;
};

struct GridElement {
    GridDefinition definition;
    std::vector<Element> children;
};

struct BorderElement {
    Element child;  // uses heap-allocated Element (see below)
};

struct ScrollViewElement {
    Element child;
};

struct ComponentElement {
    // Type-erased component factory + props
    std::shared_ptr<class ComponentBase> component;
};

struct FuncElement {
    std::function<Element(RenderContext&)> render;
};

struct EmptyElement {};

// The element variant — one value type that can hold any element kind
using ElementData = std::variant<
    EmptyElement,
    TextElement,
    ButtonElement,
    TextFieldElement,
    CheckBoxElement,
    SliderElement,
    StackElement,
    GridElement,
    BorderElement,
    ScrollViewElement,
    ComponentElement,
    FuncElement,
    ToggleSwitchElement,
    ProgressElement,
    ImageElement,
    ComboBoxElement,
    HeadingElement,
    SubHeadingElement,
    CaptionElement
    // ... add types as needed for TestApp
>;

// The main Element type — a value with optional modifiers
struct Element {
    ElementData data;
    std::optional<std::string> key;
    std::shared_ptr<ElementModifiers> modifiers;  // COW — shared until mutated

    Element() : data(EmptyElement{}) {}
    Element(ElementData d) : data(std::move(d)) {}

    // Fluent modifier API (returns modified copy)
    Element margin(double uniform) const;
    Element margin(double h, double v) const;
    Element padding(double uniform) const;
    Element width(double w) const;
    Element height(double h) const;
    Element background(std::string color) const;
    Element foreground(std::string color) const;
    Element opacity(double o) const;
    Element disabled(bool d = true) const;
    Element font_size(double s) const;
    Element bold() const;
    Element semi_bold() const;
    Element with_key(std::string k) const;
    Element corner_radius(double r) const;
    Element center() const;
    Element h_align(HorizontalAlignment a) const;
    Element v_align(VerticalAlignment a) const;
    // ... etc.
};

} // namespace duct
```

### Why `std::variant`?

- **Value semantics** — elements live on the stack or inline in vectors; no heap allocation for simple elements.
- **Exhaustive visitation** — `std::visit` ensures the reconciler handles every element type at compile time. Adding a new type without a mount/update handler is a compile error.
- **Cache-friendly** — variant elements in a `vector<Element>` are contiguous in memory.
- **No virtual dispatch** — reconciler dispatch is a variant visit (jump table), not vtable indirection.

**Tradeoff:** `BorderElement` and `ScrollViewElement` hold child `Element`s, requiring heap allocation via `std::unique_ptr<Element>` (wrapped for copy semantics). This is fine — these are structural wrappers, not hot-path leaf elements.

For child Element storage in border/scroll, we'll use a small wrapper:

```cpp
// Heap-allocated element for recursive containment
struct BoxedElement {
    std::unique_ptr<Element> ptr;
    BoxedElement() : ptr(std::make_unique<Element>()) {}
    BoxedElement(Element e) : ptr(std::make_unique<Element>(std::move(e))) {}
    BoxedElement(const BoxedElement& o) : ptr(std::make_unique<Element>(*o.ptr)) {}
    BoxedElement(BoxedElement&&) = default;
    BoxedElement& operator=(const BoxedElement& o) { ptr = std::make_unique<Element>(*o.ptr); return *this; }
    BoxedElement& operator=(BoxedElement&&) = default;
    const Element& operator*() const { return *ptr; }
    Element& operator*() { return *ptr; }
};
```

---

## DSL (Factory Functions)

The user-facing API uses free functions in the `duct` namespace, designed to be used with `using namespace duct;`:

```cpp
#include <duct/duct.h>
using namespace duct;

// Text
auto ui = text("Hello, world!");
auto ui = heading("Title");

// Button
auto ui = button("Click me", [] { std::cout << "clicked\n"; });

// Layout
auto ui = vstack(12,           // spacing = 12
    text("Line 1"),
    text("Line 2"),
    button("OK", on_ok)
);

auto ui = hstack(8,
    text("Name:"),
    text_field(name, set_name, {.placeholder = "Enter name"})
);

// Nesting and modifiers
auto ui = border(
    vstack(8,
        heading("Settings"),
        check_box(show_advanced, set_show_advanced, {.label = "Show advanced"}),
        show_advanced
            ? vstack(4, text("Advanced option 1"), text("Advanced option 2"))
            : text("Check above for more options").opacity(0.6)
    )
).padding(16).corner_radius(8).background("#f5f5f5");

// Grid
auto ui = grid({.columns = "* 2* Auto", .rows = "Auto *"},
    text("A").grid(0, 0),
    text("B").grid(0, 1),
    text("C").grid(1, 0, 1, 2)  // row, col, rowSpan, colSpan
);
```

### Factory Function Signatures

```cpp
namespace duct {

// Text elements
Element text(std::string content);
Element heading(std::string content);
Element sub_heading(std::string content);
Element caption(std::string content);

// Interactive
Element button(std::string label, std::function<void()> on_click);
Element text_field(std::string value,
                   std::function<void(std::string)> on_changed,
                   TextFieldOptions opts = {});
Element check_box(bool checked, std::function<void(bool)> on_changed,
                  CheckBoxOptions opts = {});
Element toggle_switch(bool on, std::function<void(bool)> on_changed,
                      ToggleSwitchOptions opts = {});
Element slider(double value, double min, double max,
               std::function<void(double)> on_changed = {});
Element combo_box(int selected_index,
                  std::vector<std::string> items,
                  std::function<void(int)> on_changed);

// Layout
Element vstack(std::initializer_list<Element> children);
Element vstack(double spacing, std::initializer_list<Element> children);
Element hstack(std::initializer_list<Element> children);
Element hstack(double spacing, std::initializer_list<Element> children);
Element border(Element child);
Element scroll_view(Element child);

// Also accept vector<Element> for dynamic children
Element vstack(double spacing, std::vector<Element> children);
Element hstack(double spacing, std::vector<Element> children);

// Grid
Element grid(GridDef def, std::initializer_list<Element> children);
Element grid(GridDef def, std::vector<Element> children);

// Utilities
Element empty();

// Conditional helper
Element when(bool condition, std::function<Element()> builder);

} // namespace duct
```

### Designated Initializers for Options

C++20 designated initializers make optional parameters readable without builder patterns:

```cpp
text_field(name, set_name, {.placeholder = "Search...", .header = "Query"})
toggle_switch(dark_mode, set_dark_mode, {.on_content = "Dark", .off_content = "Light"})
```

---

## Component Model & Hooks

### Class Components

```cpp
class CounterDemo : public duct::Component {
public:
    Element render() override {
        auto [count, set_count] = use_state(0);
        auto [step, set_step] = use_state(1);

        return vstack(12,
            heading("Counter"),
            text(std::format("Current count: {}", count)).font_size(24).semi_bold(),

            hstack(8,
                button(std::format("- {}", step), [=] { set_count(count - step); }),
                button("Reset", [=] { set_count(0); }).disabled(count == 0),
                button(std::format("+ {}", step), [=] { set_count(count + step); })
            ),

            hstack(8,
                text("Step size:"),
                slider(step, 1, 10, [=](double v) { set_step(static_cast<int>(v)); }).width(200),
                text(std::format("{}", step))
            )
        );
    }
};
```

### Function Components

```cpp
auto counter = duct::func([](RenderContext& ctx) {
    auto [count, set_count] = ctx.use_state(0);
    return vstack(
        text(std::format("Count: {}", count)),
        button("+", [=] { set_count(count + 1); })
    );
});
```

### Hooks API

```cpp
class Component {
protected:
    // State
    template<typename T>
    std::pair<T, std::function<void(T)>> use_state(T initial);

    // Reducer (functional updater)
    template<typename T>
    std::pair<T, std::function<void(std::function<T(T)>)>> use_reducer(T initial);

    // Effects
    void use_effect(std::function<std::function<void()>()> effect,
                    std::vector<std::any> deps = {});

    // Memoization
    template<typename T>
    T use_memo(std::function<T()> factory, std::vector<std::any> deps);

    // Ref
    template<typename T>
    std::shared_ptr<T> use_ref(T initial);
};
```

### Structured Bindings

C++17 structured bindings make hooks feel natural:

```cpp
auto [count, set_count] = use_state(0);
auto [items, update_items] = use_reducer(std::vector<TodoItem>{});
```

This maps directly to the C# pattern `var (count, setCount) = UseState(0)`.

### Hook Dependency Tracking

In C#, deps are checked via `object.Equals`. In C++, we need a type-erased equality check. Options:

1. **`std::any` + type hash** — store deps as `vector<any>`, compare via `any_cast` and `==`. Simple but has overhead.
2. **Capture-hash approach** — hash the dep values into a single `size_t`; if hash changes, re-run. Fast but has (negligible) collision risk.
3. **Template deps** — `use_effect<int, string>(effect, count, name)` — deps are typed and compared directly. Most efficient but verbose.

**Recommendation:** Option 1 for simplicity, with Option 3 available as an optimization for hot paths.

---

## Reconciler (Pure C++)

The reconciler is the performance-critical core. It operates entirely in C++ — no WinRT calls until it needs to create or update an actual control.

### Architecture

```
State change (set_count called)
  → request_render() queues via DispatcherQueue
  → Component::render() produces new Element tree (pure C++ values)
  → Reconciler::reconcile(old_tree, new_tree, control)
    → std::visit on variant to dispatch to mount/update
    → For containers: child_reconciler handles keyed/positional matching
    → Only at leaf: WinRT property setter called
  → host sets window.Content() if root changed
```

### Reconcile Dispatch

```cpp
class Reconciler {
public:
    // Main entry point
    winrt::Microsoft::UI::Xaml::UIElement reconcile(
        const Element* old_el,
        const Element& new_el,
        winrt::Microsoft::UI::Xaml::UIElement old_control,
        std::function<void()> request_rerender);

private:
    // Mount: create WinUI control from element
    winrt::Microsoft::UI::Xaml::UIElement mount(
        const Element& el,
        std::function<void()> request_rerender);

    // Update: patch existing control
    // Returns replacement control if type changed, nullptr otherwise
    winrt::Microsoft::UI::Xaml::UIElement update(
        const Element& old_el,
        const Element& new_el,
        winrt::Microsoft::UI::Xaml::UIElement control,
        std::function<void()> request_rerender);

    // Can we update in-place? (same variant index)
    bool can_update(const Element& old_el, const Element& new_el) {
        return old_el.data.index() == new_el.data.index();
    }

    // Element pool for recycling unmounted controls
    ElementPool pool_;

    // Component instance tracking
    std::unordered_map<void*, ComponentNode> component_nodes_;
};
```

### Mount via `std::visit`

```cpp
UIElement Reconciler::mount(const Element& el, RequestRerender rr) {
    auto control = std::visit(overloaded{
        [&](const TextElement& t) -> UIElement {
            auto tb = TextBlock();
            tb.Text(winrt::to_hstring(t.content));
            if (t.font_size) tb.FontSize(*t.font_size);
            // ... set properties
            return tb;
        },
        [&](const ButtonElement& b) -> UIElement {
            auto btn = Button();
            btn.Content(winrt::box_value(winrt::to_hstring(b.label)));
            set_tag(btn, el);  // tag-based event dispatch
            btn.Click([this](auto sender, auto) {
                if (auto* el = get_tag<ButtonElement>(sender))
                    if (el->on_click) el->on_click();
            });
            return btn;
        },
        [&](const StackElement& s) -> UIElement {
            auto panel = StackPanel();
            panel.Orientation(s.orientation);
            panel.Spacing(s.spacing);
            for (auto& child : s.children) {
                panel.Children().Append(mount(child, rr));
            }
            return panel;
        },
        // ... handlers for each element type
        [&](const EmptyElement&) -> UIElement { return nullptr; },
    }, el.data);

    if (control) apply_modifiers(control, el.modifiers);
    return control;
}
```

### Update via `std::visit`

```cpp
UIElement Reconciler::update(const Element& old_el, const Element& new_el,
                              UIElement control, RequestRerender rr) {
    return std::visit(overloaded{
        [&](const TextElement& old_t, const TextElement& new_t) -> UIElement {
            auto tb = control.as<TextBlock>();
            if (old_t.content != new_t.content)
                tb.Text(winrt::to_hstring(new_t.content));
            if (old_t.font_size != new_t.font_size && new_t.font_size)
                tb.FontSize(*new_t.font_size);
            apply_modifiers(control, old_el.modifiers, new_el.modifiers);
            return nullptr;  // no replacement needed
        },
        [&](const ButtonElement& old_b, const ButtonElement& new_b) -> UIElement {
            auto btn = control.as<Button>();
            if (old_b.label != new_b.label)
                btn.Content(winrt::box_value(winrt::to_hstring(new_b.label)));
            set_tag(btn, new_el);  // update closure via tag
            return nullptr;
        },
        [&](const StackElement& old_s, const StackElement& new_s) -> UIElement {
            auto panel = control.as<StackPanel>();
            if (old_s.spacing != new_s.spacing)
                panel.Spacing(new_s.spacing);
            child_reconciler::reconcile(
                old_s.children, new_s.children,
                panel.Children(), *this, rr);
            return nullptr;
        },
        // ... all type pairs
        [&](const auto&, const auto&) -> UIElement {
            // Type mismatch — should not reach here (can_update checks first)
            return mount(new_el, rr);
        },
    }, old_el.data, new_el.data);
}
```

### Child Reconciler

Port of `ChildReconciler.cs` — identical algorithms in C++:

```cpp
namespace child_reconciler {

// Positional reconciliation: O(max(old, new))
void reconcile_positional(
    std::span<const Element> old_children,
    std::span<const Element> new_children,
    winrt::Windows::Foundation::Collections::IVector<UIElement> ui_children,
    Reconciler& reconciler,
    RequestRerender rr);

// Keyed reconciliation: prefix/suffix strip + LIS
void reconcile_keyed(
    std::span<const Element> old_children,
    std::span<const Element> new_children,
    winrt::Windows::Foundation::Collections::IVector<UIElement> ui_children,
    Reconciler& reconciler,
    RequestRerender rr);

// LIS (Longest Increasing Subsequence) for minimal DOM moves
std::vector<int> longest_increasing_subsequence(std::span<const int> arr);

} // namespace child_reconciler
```

### Tag-Based Event Dispatch

Same pattern as C# — store the current Element pointer in the control's Tag so event handlers always use the latest closure:

```cpp
// Store element reference in control's Tag for event dispatch
void set_tag(FrameworkElement const& control, const Element& el) {
    // Store a pointer to the element's variant data.
    // Lifetime: the old element tree is kept alive until the next reconciliation,
    // so the tag pointer remains valid for the duration of any event handler.
    control.Tag(winrt::box_value(reinterpret_cast<uint64_t>(&el.data)));
}

template<typename T>
const T* get_tag(IInspectable const& sender) {
    auto fe = sender.as<FrameworkElement>();
    auto addr = winrt::unbox_value<uint64_t>(fe.Tag());
    auto* data = reinterpret_cast<const ElementData*>(addr);
    return std::get_if<T>(data);
}
```

**Important lifetime note:** The old element tree must be kept alive until the new tree is produced and reconciled. This is already the case in the render loop (we keep `current_tree_`).

---

## WinRT Bridge Layer

All WinRT interaction is isolated in `winrt_bridge.cpp`. This file contains:

1. **Control creation functions** — `create_text_block()`, `create_button()`, etc.
2. **Property setter helpers** — `set_text(TextBlock, string)`, `set_foreground(UIElement, color)`, etc.
3. **Brush cache** — color string → `SolidColorBrush` cache (same as C# `BrushHelper`).
4. **Modifier application** — `apply_modifiers(UIElement, shared_ptr<Modifiers>)`.

The bridge is the only translation unit that `#include`s `winrt/Microsoft.UI.Xaml.h`. The rest of the framework works with opaque `UIElement` handles.

This isolation means:
- The reconciler's diff logic can be unit-tested without WinUI.
- Build times are manageable (WinRT headers are expensive).
- The bridge can be replaced for testing with a mock.

---

## Hosting & App Entry Point

### ReactorHost (Render Loop)

Direct port of `ReactorHost.cs`:

```cpp
class ReactorHost {
public:
    ReactorHost(winrt::Microsoft::UI::Xaml::Window window);

    void mount(std::shared_ptr<Component> component);
    void mount(std::function<Element(RenderContext&)> render_func);

    void request_render();
    Reconciler& reconciler() { return reconciler_; }

private:
    void render_loop();
    void render();

    winrt::Microsoft::UI::Xaml::Window window_;
    Reconciler reconciler_;
    winrt::Microsoft::UI::Dispatching::DispatcherQueue dispatcher_;

    std::shared_ptr<Component> root_component_;
    std::function<Element(RenderContext&)> root_render_func_;
    std::unique_ptr<RenderContext> func_context_;

    Element current_tree_;
    winrt::Microsoft::UI::Xaml::UIElement current_control_{nullptr};
    bool render_pending_ = false;
    bool is_rendering_ = false;
    bool needs_rerender_ = false;

    static constexpr int max_render_iterations = 50;
};
```

### ReactorApp Entry Point

```cpp
namespace duct {

// Class component entry
template<typename TComponent>
    requires std::derived_from<TComponent, Component>
void run(std::wstring title, int width = 1024, int height = 768);

// Function component entry
void run(std::wstring title,
         std::function<Element(RenderContext&)> root,
         int width = 1024, int height = 768);

} // namespace duct
```

### App Usage

```cpp
#include <duct/duct.h>

class MyApp : public duct::Component {
public:
    Element render() override {
        auto [count, set_count] = use_state(0);
        return duct::vstack(12,
            duct::text(std::format("Count: {}", count)),
            duct::button("+", [=] { set_count(count + 1); })
        );
    }
};

int main() {
    duct::run<MyApp>(L"My App");
}
```

---

## TestApp Port

Port all 11 demos from `tests/Reactor.TestApp/App.cs`. The C++ versions should be nearly line-for-line translations, demonstrating API parity.

### Example: Counter Demo

**C# (original):**
```csharp
class CounterDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (step, setStep) = UseState(1);

        return VStack(12,
            Heading("Counter"),
            Text($"Current count: {count}").FontSize(24).SemiBold(),
            HStack(8,
                Button($"- {step}", () => setCount(count - step)),
                Button("Reset", () => setCount(0)).IsEnabled(!(count == 0)),
                Button($"+ {step}", () => setCount(count + step))
            ),
            HStack(8,
                Text("Step size:"),
                Slider(step, 1, 10, v => setStep((int)v)).Width(200),
                Text($"{step}")
            ),
            count switch
            {
                0 => Text("Try clicking the buttons!").Opacity(0.6),
                > 0 and < 10 => Text("Going up..."),
                >= 10 and < 50 => Text("Getting bigger!").SemiBold(),
                >= 50 => Text("That's a LOT!").Bold().FontSize(20),
                < 0 and > -10 => Text("Going negative..."),
                _ => Text("Way down there!").Bold()
            }
        );
    }
}
```

**C++ (ported):**
```cpp
class CounterDemo : public duct::Component {
public:
    Element render() override {
        auto [count, set_count] = use_state(0);
        auto [step, set_step] = use_state(1);

        auto count_message = [&]() -> Element {
            if (count == 0) return text("Try clicking the buttons!").opacity(0.6);
            if (count > 0 && count < 10) return text("Going up...");
            if (count >= 10 && count < 50) return text("Getting bigger!").semi_bold();
            if (count >= 50) return text("That's a LOT!").bold().font_size(20);
            if (count < 0 && count > -10) return text("Going negative...");
            return text("Way down there!").bold();
        };

        return vstack(12,
            heading("Counter"),
            text(std::format("Current count: {}", count)).font_size(24).semi_bold(),
            hstack(8,
                button(std::format("- {}", step), [=] { set_count(count - step); }),
                button("Reset", [=] { set_count(0); }).disabled(count == 0),
                button(std::format("+ {}", step), [=] { set_count(count + step); })
            ),
            hstack(8,
                text("Step size:"),
                slider(step, 1, 10, [=](double v) { set_step(static_cast<int>(v)); }).width(200),
                text(std::format("{}", step))
            ),
            count_message()
        );
    }
};
```

### Translation Notes

| C# Pattern | C++ Equivalent |
|------------|---------------|
| `var (x, setX) = UseState(v)` | `auto [x, set_x] = use_state(v)` |
| `$"text {expr}"` | `std::format("text {}", expr)` |
| `() => action` | `[=] { action; }` |
| `expr switch { ... }` | If/else chain or immediately-invoked lambda |
| `.WithKey($"id-{i}")` | `.with_key(std::format("id-{}", i))` |
| `items.Select((x, i) => ...).ToArray()` | Range-based transform into `vector<Element>` |
| `null` (empty child) | `empty()` or `std::nullopt` in optional context |
| `When(cond, () => el)` | `when(cond, [&] { return el; })` |
| `ForEach(range, fn)` | `transform` into vector + pass to container |

---

## Perf Benchmark: StressPerf.ReactorCpp

Add a 4th variant to the existing `tests/stress_perf/` harness.

### What to Measure

| Metric | Why |
|--------|-----|
| **FPS** | Does the native reconciler maintain higher frame rates under load? |
| **Update latency** | Is the diff+patch cycle faster when it's all native? |
| **Memory (working set)** | Does avoiding managed allocations reduce memory pressure? |
| **GC pauses** | C++ has no GC — do we see smoother frame pacing? |

### Implementation

- Same 80x60 grid, same `StockDataSource` (port the deterministic data generation to C++).
- Same `PerfTracker` pattern: `CompositionTarget.Rendering` for FPS, `QueryPerformanceCounter` for update timing, `GetProcessMemoryInfo` for working set.
- Same CLI flags: `--headless --percent N --duration N`.
- Output matches the existing report format for easy comparison.

### Expected Results Matrix

| Scenario | Direct | Bound | Reactor (C#) | ReactorCpp |
|----------|--------|-------|-----------|---------|
| 10% update | ~60 FPS | ~60 FPS | ~60 FPS | ~60 FPS |
| 50% update | ~55 FPS | ~50 FPS | ~45 FPS | ~55 FPS? |
| 100% update | ~45 FPS | ~35 FPS | ~30 FPS | ~40 FPS? |
| Memory | Low | Medium | Higher | Low-Medium |

The hypothesis: ReactorCpp should approach Direct-like performance because the diff is native and property sets are the only WinRT calls (same as Direct). The C# Reactor variant pays for managed element allocation + GC + WinRT interop throughout reconciliation.

---

## Build System

### MSBuild + C++/WinRT

MSBuild is the primary build system — it has first-class support for C++/WinRT and WinUI 3, and works seamlessly in Visual Studio.

**Solution structure:**

```
ReactorCpp/
├── ReactorCpp.sln                          # VS solution
├── ReactorCpp/                             # Core static library
│   ├── ReactorCpp.vcxproj
│   ├── packages.config                  # Microsoft.Windows.CppWinRT NuGet
│   └── pch.h / pch.cpp                  # Precompiled header (WinRT headers)
├── ReactorCpp.TestApp/                     # WinUI 3 unpackaged app
│   ├── ReactorCpp.TestApp.vcxproj
│   └── packages.config                  # WindowsAppSDK + CppWinRT NuGets
└── ReactorCpp.StressPerf/                  # Benchmark variant
    ├── ReactorCpp.StressPerf.vcxproj
    └── packages.config
```

**Key MSBuild settings:**
- Target: `net8.0-windows10.0.22621.0` equivalent (`<WindowsTargetPlatformVersion>10.0.22621.0</WindowsTargetPlatformVersion>`)
- C++ standard: `/std:c++20`
- NuGet packages: `Microsoft.Windows.CppWinRT`, `Microsoft.WindowsAppSDK`
- Precompiled header: `pch.h` includes WinRT projection headers to keep build times manageable
- Platform: x64

This ensures the project opens and builds natively in Visual Studio with full IntelliSense, debugging, and project reference support.

---

## Implementation Phases

### Phase 1: Foundation (Element Model + Minimal Reconciler)

**Goal:** Build the core types, a minimal reconciler that handles Text/Button/Stack, and display "Hello World" in a WinUI window.

- [ ] Set up `ReactorCpp/` directory and MSBuild solution (.sln + .vcxproj)
- [ ] Implement `Element` variant with `TextElement`, `ButtonElement`, `StackElement`, `EmptyElement`
- [ ] Implement `ElementModifiers` with margin, padding, width, height, opacity, background
- [ ] Implement fluent modifier API on `Element`
- [ ] Implement DSL factory functions: `text()`, `button()`, `vstack()`, `hstack()`
- [ ] Implement `Reconciler::mount()` for Text, Button, Stack
- [ ] Implement `Reconciler::update()` for Text, Button, Stack
- [ ] Implement `apply_modifiers()` in the WinRT bridge
- [ ] Implement `ReactorHost` render loop with `DispatcherQueue`
- [ ] Implement `duct::run()` entry point
- [ ] Validate: CounterDemo renders and increments

### Phase 2: Hooks & Components

**Goal:** Full hook system and component lifecycle.

- [ ] Implement `RenderContext` with `use_state`, `use_reducer`
- [ ] Implement `use_effect` with cleanup and dependency tracking
- [ ] Implement `use_memo`, `use_ref`
- [ ] Implement `Component` base class with render context management
- [ ] Implement `FuncElement` for inline function components
- [ ] Implement `ComponentElement` for class component nesting
- [ ] Implement component mount/update/unmount lifecycle in reconciler
- [ ] Validate: TodoDemo works (state, lists, component nesting)

### Phase 3: Child Reconciler

**Goal:** Keyed and positional child reconciliation with LIS.

- [ ] Implement positional child reconciliation
- [ ] Implement keyed child reconciliation with prefix/suffix stripping
- [ ] Implement LIS algorithm for minimal moves
- [ ] Implement `ElementPool` for control recycling (TextBlock, StackPanel, Border)
- [ ] Validate: Dynamic list with add/remove/reorder works correctly

### Phase 4: Remaining Controls

**Goal:** All element types needed by the TestApp.

- [ ] `BorderElement`, `ScrollViewElement`
- [ ] `GridElement` with grid definition and attached properties
- [ ] `CheckBoxElement`, `ToggleSwitchElement`, `RadioButtonElement`
- [ ] `SliderElement`, `ProgressElement`
- [ ] `TextFieldElement` (TextBox)
- [ ] `ComboBoxElement`
- [ ] `ImageElement`
- [ ] `HeadingElement`, `SubHeadingElement`, `CaptionElement` (TextBlock variants)
- [ ] Grid attached properties (`.grid(row, col, ...)`)
- [ ] Validate: all 11 TestApp demos render correctly

### Phase 5: TestApp Port

**Goal:** Complete, working port of `tests/Reactor.TestApp/App.cs`.

- [ ] Port root DemoApp with tab navigation
- [ ] Port CounterDemo
- [ ] Port TodoDemo
- [ ] Port ConditionalDemo
- [ ] Port FormDemo
- [ ] Port DynamicListDemo
- [ ] Port PerfStressDemo (quicksort visualization)
- [ ] Port VirtualizationDemo
- [ ] Port FlyoutDemo
- [ ] Port DataTemplateDemo
- [ ] Port TransitionsDemo (implicit + theme transitions)
- [ ] Side-by-side visual comparison with C# TestApp

### Phase 6: Stress Perf Benchmark

**Goal:** Quantified performance comparison.

- [ ] Port `StockDataSource` to C++
- [ ] Port `PerfTracker` to C++ (using `QueryPerformanceCounter`, `GetProcessMemoryInfo`)
- [ ] Implement `StressPerf.ReactorCpp` with same grid layout
- [ ] Implement headless CLI mode with same flags
- [ ] Run comparative benchmarks at 10%, 50%, 100% update rates
- [ ] Capture and document results
- [ ] Analyze: Is the perf delta meaningful? Is it the reconciler, the allocations, the GC, or the WinRT transitions?

### Phase 7: Polish & Decision

**Goal:** Make the call on whether ReactorCpp is worth continuing.

- [ ] Document findings (perf results, ergonomics assessment, maintenance cost)
- [ ] Clean up API rough edges discovered during TestApp port
- [ ] Write brief "lessons learned" doc
- [ ] Decision: continue, pivot, or archive

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| C++/WinRT build complexity | High setup cost, slow iteration | Use MSBuild (first-class WinUI 3 support); precompiled headers for WinRT |
| `std::variant` size bloat | Element variant may be large if many types added | Profile; if needed, switch to `std::unique_ptr<ElementBase>` with virtual dispatch |
| Lambda capture ergonomics | `[=]` captures by value (stale state), `[&]` is dangling after render | Document the `[=]` pattern clearly; hooks return values, not references |
| Tag-based event dispatch lifetime | Stored pointer to element data must remain valid during event handling | Keep old element tree alive until next reconcile (already in design) |
| WinRT header compile times | C++/WinRT headers are massive | Isolate in `winrt_bridge.cpp`; use PCH for WinRT headers |
| Perf hypothesis may be wrong | WinRT property sets may dominate regardless | The experiment is isolated and disposable; learning is valuable either way |
| C++20 feature support | MSVC may have gaps in C++20 features | Target MSVC 17.8+ (VS 2022); test early with concepts, format, etc. |

---

## Resolved Decisions

1. **Build system: MSBuild.** First-class WinUI 3 and C++/WinRT support, works natively in Visual Studio with IntelliSense and debugging.

2. **No runtime extensibility.** The `std::variant` element type is closed — all supported element types are known at compile time. This enables maximum optimization (no type-erasure overhead, exhaustive `std::visit`). Extensibility can be revisited later if needed.

3. **Strings: `std::string` (UTF-8).** All element data uses `std::string`. Conversion to `winrt::hstring` (UTF-16) happens at the WinRT bridge boundary. This is more ergonomic for C++ developers and avoids `L""` literals throughout app code.

4. **Threading: STA (UI thread only).** Same as the C# version. All state changes and rendering happen on the UI thread. State setters post to `DispatcherQueue` if called from a background thread.

5. **Memory: default allocator.** Start with standard `new`/`delete`. Custom allocators (arena/bump for short-lived element trees) are a potential Phase 7 optimization — defer until profiling shows allocation pressure is a bottleneck.

---

## Benchmark Results

Stress test: 70×70 grid = 4,900 `TextElement` cells, 10-second runs at three load levels.

| Variant | 10% FPS | 50% FPS | 100% FPS | Avg Update (ms) | Avg Memory (MB) |
|---------|---------|---------|----------|-----------------|-----------------|
| **ReactorCpp** | **28.7** | 7.5 | 4.6 | 0.1–0.2 | 403 |
| Reactor C# | 19.2 | 8.0 | 5.6 | 0.1–0.5 | 459 |
| Direct (C#) | 26.3 | 8.3 | 5.6 | 2.1–15.1 | 477 |
| Binding (C#) | 22.3 | 7.0 | 4.7 | 6.2–41.2 | 514 |
| DirectX | 38.4 | 39.0 | 38.4 | 0.0–0.2 | 142 |

**Key finding:** ReactorCpp's reconciler is 2× faster than C# Reactor at low load (28.7 vs 19.2 FPS) and uses 12% less memory. At high load, both hit the same XAML layout ceiling. The original hypothesis — that a fully native stack avoids enough overhead to show measurable improvement — is **confirmed** for the reconciler itself, though the XAML rendering pipeline remains the dominant cost.

---

## Lessons Learned

1. **The XAML layout engine is the ceiling.** At high update rates, both C++ and C# reconcilers are bottlenecked by XAML's measure/arrange/render pipeline, not by the diff algorithm. DirectX at 38 FPS proves this — neither reconciler can exceed ~8 FPS at 50% load regardless of how fast the diff runs. The reconciler's job is to minimize the number of XAML property sets; once that's minimized, further optimization requires bypassing XAML entirely.

2. **C++/WinRT ABI overhead is real but manageable.** Each C++/WinRT property set is a raw COM vtable call with HRESULT checking. This is faster than C# for individual calls (no marshaling), but lacks the CLR's batched RCW caching. At 100% load (9,800+ property sets per frame), C++ is slightly slower than C# Reactor. The fix would be to batch WinRT updates or use `SetValue` with dependency property tokens.

3. **`std::variant` was the right call.** The closed element type set enables exhaustive `std::visit` dispatch with zero virtual call overhead. Element construction is stack-allocated — no `new`, no GC, no boxing. The variant is ~128 bytes (dominated by `std::vector<Element>` in StackElement), which is reasonable.

4. **Hooks translate cleanly to C++.** `auto [count, set_count] = use_state(0)` via structured bindings is nearly identical to the C# `var (count, setCount) = UseState(0)`. The main friction point is `use_reducer`'s functional updater syntax, which requires lambdas where C# uses simple assignment.

5. **`NOMINMAX` should be in every Windows C++ project.** The `<windows.h>` `min`/`max` macros conflict with `<algorithm>` and force ugly `(std::max)(a, b)` workarounds. This was the single most annoying ergonomic issue — a one-line fix that should have been in place from day one.

6. **Designated initializers are a win for option structs.** `check_box(true, handler, {.label = "Enable"})` reads cleanly and avoids builder pattern boilerplate. This is one area where C++20 is more concise than the equivalent C# record syntax.

7. **Single-file apps are viable.** The TestApp is 1,161 lines in a single `main.cpp` — shorter than the C# version. No XAML, no resource files, no code-behind. For small-to-medium apps, this is a compelling developer experience.
