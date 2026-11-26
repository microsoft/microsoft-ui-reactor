using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

internal enum PageKind { Type, Group, Hook, ElementArea, FactoryArea, ModifierArea, CategoryIndex, RootIndex }

/// <summary>One page the reference will write, before it is rendered.</summary>
internal sealed class PlannedPage
{
    public required PageKind Kind { get; init; }

    /// <summary>The category folder, or <c>null</c> for the root index.</summary>
    public RefCategory? Category { get; init; }
    public required string Stem { get; init; }
    public required string Title { get; init; }

    /// <summary>The short name used in index tables and the sidebar, for example
    /// "Buttons" for the page titled "Factories: Buttons".</summary>
    public string Label { get => _label ?? Title; init => _label = value; }
    private string? _label;

    /// <summary>True for a modifier page that serves particular controls rather than
    /// a purpose: a control area, or the fallback area for modifiers that several controls share.</summary>
    public bool IsControlModifierPage => Kind == PageKind.ModifierArea && (Area is not null || ModifierArea?.Match.Count == 0);

    public string RelativePath => Category is null ? "reference/index.md" : $"reference/{Category.Id}/{Stem}.md";

    /// <summary>The types the page documents: one for a type page, several for a
    /// group or element area page.</summary>
    public List<ApiType> Types { get; } = new();

    /// <summary>Nested types shown as sections under their outer type.</summary>
    public Dictionary<string, List<ApiType>> Nested { get; } = new(StringComparer.Ordinal);

    /// <summary>The members of each type that the page shows. Members that moved to a
    /// hook, factory or modifier page are left out.</summary>
    public Dictionary<string, List<ApiMember>> TypeMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Hook overloads, factory methods or modifier methods.</summary>
    public List<ApiMember> Members { get; } = new();

    /// <summary>Hook overloads on <c>Component</c> that only forward to <c>RenderContext</c>.</summary>
    public List<ApiMember> Forwarders { get; } = new();

    public RefArea? Area { get; init; }
    public RefModifierArea? ModifierArea { get; init; }
    public RefGroup? Group { get; init; }

    /// <summary>Guide page slugs from <c>reference-map.yaml</c>, in first-seen order.</summary>
    public List<string> GuidePages { get; } = new();

    public void AddGuidePages(IEnumerable<string>? pages)
    {
        if (pages is null) return;
        foreach (var p in pages)
            if (!string.IsNullOrWhiteSpace(p) && !GuidePages.Contains(p)) GuidePages.Add(p);
    }

    public override string ToString() => RelativePath;
}

/// <summary>Where every type and member goes, decided before any page is rendered.</summary>
internal sealed class ReferencePlan
{
    public required ApiModel Model { get; init; }
    public required ReferenceMap Map { get; init; }
    public List<PlannedPage> Pages { get; } = new();
    public Dictionary<string, PlannedPage> PageByTypeDocId { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, PlannedPage> PageByMemberDocId { get; } = new(StringComparer.Ordinal);

    /// <summary>Members that have no section of their own (for example the generated
    /// <c>Descriptor</c> field of an element). A link to one goes to its type.</summary>
    public HashSet<string> HiddenMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Types that no rule placed. They get no page.</summary>
    public List<ApiType> Uncategorized { get; } = new();

    /// <summary>The control area of each element record.</summary>
    public Dictionary<string, RefArea> ElementAreas { get; } = new(StringComparer.Ordinal);

    /// <summary>Factory methods that return each element record.</summary>
    public Dictionary<string, List<ApiMember>> FactoriesByElement { get; } = new(StringComparer.Ordinal);

    /// <summary>Modifiers written for one element record (not for every element).</summary>
    public Dictionary<string, List<ApiMember>> ModifiersByElement { get; } = new(StringComparer.Ordinal);

    public IEnumerable<PlannedPage> PagesIn(RefCategory category) =>
        Pages.Where(p => p.Category == category && p.Kind != PageKind.CategoryIndex);
}

/// <summary>
/// Decides the page layout of the API reference (spec 041 §10.4): one page per type,
/// one page per hook, and area pages for element records, factory methods and
/// modifiers. The rules come from <c>docs/_pipeline/reference-map.yaml</c>.
/// </summary>
internal static class ReferencePlanner
{
    internal const string RenderContextDocId = "T:Microsoft.UI.Reactor.Core.RenderContext";
    internal const string ComponentDocId = "T:Microsoft.UI.Reactor.Core.Component";
    internal const string ComponentOfTDocId = "T:Microsoft.UI.Reactor.Core.Component`1";
    internal const string FactoriesDocId = "T:Microsoft.UI.Reactor.Factories";

    private static readonly Regex HookName = new("^Use[A-Z]", RegexOptions.CultureInvariant);
    private static readonly string[] HookOwners = { RenderContextDocId, ComponentDocId, ComponentOfTDocId };

    internal static readonly RefArea OtherArea = new("other", "Other", "Controls that do not fit one of the other areas.",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    internal static readonly RefModifierArea OtherModifierArea = new("other", "Other",
        "Modifiers that do not fit one of the other areas.", Array.Empty<string>(), Array.Empty<string>());

    private enum Role { None, Hook, Factory, Modifier }

    public static ReferencePlan Plan(ApiModel model, ReferenceMap map)
    {
        var plan = new ReferencePlan { Model = model, Map = map };
        var elementBaseDocId = "T:" + map.ElementBase;
        var hooksCategory = map.Categories.FirstOrDefault(c => c.Layout == "hooks");
        var elementsCategory = map.Categories.FirstOrDefault(c => c.Layout == "elements");
        var factoriesCategory = map.Categories.FirstOrDefault(c => c.Layout == "factories");
        var modifiersCategory = map.Categories.FirstOrDefault(c => c.Layout == "modifiers");

        // ── Element records ──────────────────────────────────────────────
        var elementCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool IsElement(string? docId)
        {
            if (docId is null || docId == elementBaseDocId) return false;
            if (elementCache.TryGetValue(docId, out var known)) return known;
            elementCache[docId] = false; // cycle guard
            var result = false;
            if (model.TypesByDocId.TryGetValue(docId, out var t) && t.BaseDocId is { } b)
                result = b == elementBaseDocId || IsElement(b);
            elementCache[docId] = result;
            return result;
        }

        // ── Type categories ──────────────────────────────────────────────
        // null means uncategorized; ExcludedCategory keeps a type and its members out.
        var typeCategory = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var t in model.Types.OrderBy(t => Depth(t)).ThenBy(t => t.DocId, StringComparer.Ordinal))
        {
            if (t.DeclaringType is { } outer && typeCategory.TryGetValue(outer.DocId, out var oc) && oc == ReferenceMap.ExcludedCategory)
            {
                typeCategory[t.DocId] = ReferenceMap.ExcludedCategory;
                continue;
            }
            string? cat = map.OverrideCategory(t.DocId);
            if (string.IsNullOrEmpty(cat) && elementsCategory is not null && IsElement(t.DocId)) cat = elementsCategory.Id;
            if (string.IsNullOrEmpty(cat)) cat = map.Resolve(t.DocId)?.Category;
            if (string.IsNullOrEmpty(cat)) cat = null;
            if (cat is not null && cat != ReferenceMap.ExcludedCategory && map.FindCategory(cat) is null) cat = null;
            typeCategory[t.DocId] = cat;
        }

        bool Excluded(ApiType t) => typeCategory.TryGetValue(t.DocId, out var c) && c == ReferenceMap.ExcludedCategory;

        // ── Element areas ────────────────────────────────────────────────
        RefArea? AreaForName(string key) =>
            map.Areas.FirstOrDefault(a => a.Elements.Any(g => ReferenceMap.PrefixMatches(key, g)));
        RefArea ElementAreaOf(ApiType t) =>
            plan.ElementAreas.TryGetValue(t.DocId, out var a) ? a : AreaForName(AreaKey(t.Name)) ?? FindArea(map, "other") ?? OtherArea;

        foreach (var t in model.Types)
        {
            if (elementsCategory is not null && typeCategory[t.DocId] == elementsCategory.Id && IsElement(t.DocId))
                plan.ElementAreas[t.DocId] = AreaForName(AreaKey(t.Name)) ?? FindArea(map, "other") ?? OtherArea;
        }

        // ── Member roles ─────────────────────────────────────────────────
        var hooks = new SortedDictionary<string, List<ApiMember>>(StringComparer.Ordinal);
        var factories = new SortedDictionary<string, List<ApiMember>>(StringComparer.Ordinal);
        var modifiers = new SortedDictionary<string, List<ApiMember>>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        Role RoleOf(ApiMember m)
        {
            var owner = m.DeclaringType;
            if (m.Kind == ApiMemberKind.Method && HookName.IsMatch(m.Name) && hooksCategory is not null)
            {
                if (HookOwners.Contains(owner.DocId)) return Role.Hook;
                if (m.IsExtension && m.ExtendedTypeDocIds.Any(id => HookOwners.Contains(id))) return Role.Hook;
                if (!model.FromMetadata && owner.Name.EndsWith("Extensions", StringComparison.Ordinal)) return Role.Hook;
            }
            if (factoriesCategory is not null && m.Kind == ApiMemberKind.Method)
            {
                if (owner.DocId == FactoriesDocId) return Role.Factory;
                // Other packages add their own `Factories` class (for example
                // Microsoft.UI.Reactor.Advanced.Factories, issue #1721).
                if (owner.Kind == ApiTypeKind.StaticClass && owner.Name is "Factories" && m.IsStatic && !m.IsExtension)
                    return Role.Factory;
                if (owner.Kind == ApiTypeKind.StaticClass && owner.Name.EndsWith("Dsl", StringComparison.Ordinal) && m.IsStatic && !m.IsExtension)
                    return Role.Factory;
            }
            if (modifiersCategory is not null && m.Kind == ApiMemberKind.Method && m.IsExtension
                && m.ReturnType is { } rt && rt != "void"
                && m.ExtendedTypeDocIds.Any(id => id == elementBaseDocId || IsElement(id) || IsRichText(id))
                && ReturnsChainable(m))
                return Role.Modifier;
            return Role.None;
        }

        // A modifier returns an element (or rich-text block) so the call can be
        // chained: the receiver's own type (`T Margin<T>(this T el)`), or an element
        // type. `Task EvaluateAsync(this Element …)` or a query such as
        // `GetValidation` stays on its declaring type's page.
        bool ReturnsChainable(ApiMember m) =>
            (m.Params.Count > 0 && m.Params[0].Type.TrimEnd('?') == m.ReturnType!.TrimEnd('?'))
            || m.ReturnDocId is { } r && (r == elementBaseDocId || IsElement(r) || IsRichText(r));

        foreach (var t in model.Types)
        {
            if (Excluded(t)) continue;
            foreach (var m in t.Members)
            {
                switch (RoleOf(m))
                {
                    case Role.Hook: Add(hooks, m.Name, m); claimed.Add(m.DocId); break;
                    case Role.Factory: Add(factories, m.Name, m); claimed.Add(m.DocId); break;
                    case Role.Modifier: Add(modifiers, m.Name, m); claimed.Add(m.DocId); break;
                }
                // The generated control descriptor is plumbing for the reconciler.
                if (m.Name == "Descriptor" && m.IsStatic && m.Kind is ApiMemberKind.Field or ApiMemberKind.Property && IsElement(t.DocId))
                    plan.HiddenMembers.Add(m.DocId);
            }
        }

        // ── Pages: reserve area and group stems first ────────────────────
        var stems = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> StemsOf(RefCategory c)
        {
            if (!stems.TryGetValue(c.Id, out var s))
                stems[c.Id] = s = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index" };
            return s;
        }
        PlannedPage NewPage(PageKind kind, RefCategory category, string stem, string title,
            RefArea? area = null, RefModifierArea? modArea = null, RefGroup? group = null, string? label = null)
        {
            StemsOf(category).Add(stem);
            var page = new PlannedPage { Kind = kind, Category = category, Stem = stem, Title = title, Label = label ?? title, Area = area, ModifierArea = modArea, Group = group };
            plan.Pages.Add(page);
            return page;
        }

        // Element area pages.
        if (elementsCategory is not null)
        {
            var byArea = plan.ElementAreas
                .Select(kv => (Type: model.TypesByDocId[kv.Key], Area: kv.Value))
                .GroupBy(x => x.Area.Id, StringComparer.Ordinal);
            foreach (var g in OrderAreas(map, byArea.Select(g => g.First().Area)))
            {
                var types = byArea.First(x => x.Key == g.Id).Select(x => x.Type).OrderBy(t => t.DisplayName, StringComparer.Ordinal).ThenBy(t => t.DocId, StringComparer.Ordinal).ToList();
                var page = NewPage(PageKind.ElementArea, elementsCategory, g.Id, "Elements: " + g.Title, area: g, label: g.Title);
                page.AddGuidePages(g.GuidePages);
                foreach (var t in types)
                {
                    page.Types.Add(t);
                    page.TypeMembers[t.DocId] = t.Members.Where(m => !claimed.Contains(m.DocId)).ToList();
                    plan.PageByTypeDocId[t.DocId] = page;
                    foreach (var m in t.Members.Concat(t.EnumValues)) plan.PageByMemberDocId.TryAdd(m.DocId, page);
                }
            }
        }

        // Factory area pages.
        if (factoriesCategory is not null)
        {
            var factoryArea = new Dictionary<string, RefArea>(StringComparer.Ordinal);
            foreach (var (name, overloads) in factories)
            {
                var area = map.Areas.FirstOrDefault(a => a.Factories.Any(g => ReferenceMap.PrefixMatches(name, g)))
                    ?? overloads.OrderBy(o => o.Params.Count).ThenBy(o => o.DocId, StringComparer.Ordinal)
                        .Select(o => o.ReturnDocId)
                        .Where(id => id is not null && plan.ElementAreas.ContainsKey(id))
                        .Select(id => plan.ElementAreas[id!])
                        .FirstOrDefault()
                    ?? (overloads.Select(o => o.ReturnDocId).FirstOrDefault(id => id is not null && model.TypesByDocId.ContainsKey(id) && IsElement(id)) is { } rid
                        ? ElementAreaOf(model.TypesByDocId[rid]) : null)
                    ?? FindArea(map, "other") ?? OtherArea;
                factoryArea[name] = area;
            }
            foreach (var area in OrderAreas(map, factoryArea.Values))
            {
                var page = NewPage(PageKind.FactoryArea, factoriesCategory, area.Id, "Factories: " + area.Title, area: area, label: area.Title);
                page.AddGuidePages(area.GuidePages);
                foreach (var (name, overloads) in factories)
                {
                    if (factoryArea[name].Id != area.Id) continue;
                    foreach (var m in SortOverloads(overloads))
                    {
                        page.Members.Add(m);
                        plan.PageByMemberDocId[m.DocId] = page;
                        if (m.ReturnDocId is { } r && IsElement(r)) Add(plan.FactoriesByElement, r, m);
                    }
                }
            }
        }

        // Modifier area pages.
        if (modifiersCategory is not null)
        {
            // A modifier area without match globs collects what no other area takes.
            var fallbackMod = map.ModifierAreas.FirstOrDefault(a => a.Match.Count == 0) ?? OtherModifierArea;
            var modPage = new Dictionary<string, (RefModifierArea? Mod, RefArea? Area)>(StringComparer.Ordinal);
            foreach (var (name, overloads) in modifiers)
            {
                var mod = map.ModifierAreas.FirstOrDefault(a => a.Match.Any(g => ReferenceMap.PrefixMatches(name, g)));
                if (mod is not null) { modPage[name] = (mod, null); continue; }
                var extended = overloads.SelectMany(o => o.ExtendedTypeDocIds).Distinct(StringComparer.Ordinal).ToList();
                RefArea? area = null;
                if (extended.Count > 0 && !extended.Contains(elementBaseDocId))
                {
                    var areas = extended.Select(id => model.TypesByDocId.TryGetValue(id, out var et) ? AreaForName(AreaKey(et.Name)) : null).ToList();
                    if (areas.All(a => a is not null) && areas.Select(a => a!.Id).Distinct(StringComparer.Ordinal).Count() == 1 && areas[0]!.Id != "other")
                        area = areas[0];
                }
                modPage[name] = area is not null ? (null, area) : (fallbackMod, null);
            }
            var keys = new List<(string Id, RefModifierArea? Mod, RefArea? Area)>();
            foreach (var a in map.ModifierAreas)
                if (a != fallbackMod && modPage.Values.Any(v => v.Mod == a)) keys.Add((a.Id, a, null));
            if (modPage.Values.Any(v => v.Mod == fallbackMod)) keys.Add((fallbackMod.Id, fallbackMod, null));
            foreach (var a in OrderAreas(map, modPage.Values.Where(v => v.Area is not null).Select(v => v.Area!)))
                keys.Add((a.Id, null, a));
            foreach (var key in keys)
            {
                var control = key.Mod is null || key.Mod.Match.Count == 0;
                var title = (control ? "Control modifiers: " : "Modifiers: ") + (key.Mod?.Title ?? key.Area!.Title);
                var page = NewPage(PageKind.ModifierArea, modifiersCategory, key.Id, title, area: key.Area, modArea: key.Mod,
                    label: key.Mod?.Title ?? key.Area!.Title);
                page.AddGuidePages(key.Mod?.GuidePages ?? key.Area!.GuidePages);
                foreach (var (name, overloads) in modifiers)
                {
                    var v = modPage[name];
                    if (key.Mod is not null ? v.Mod != key.Mod : v.Area != key.Area) continue;
                    foreach (var m in SortOverloads(overloads))
                    {
                        page.Members.Add(m);
                        plan.PageByMemberDocId[m.DocId] = page;
                        foreach (var id in m.ExtendedTypeDocIds)
                            if (IsElement(id)) Add(plan.ModifiersByElement, id, m);
                    }
                }
            }
        }

        // Type group pages (types and hooks layouts).
        var groupPages = new Dictionary<(string Cat, string Group), PlannedPage>();
        PlannedPage? GroupPageFor(ApiType t, RefCategory c)
        {
            if (c.Layout is not ("types" or "hooks")) return null;
            var group = c.Groups.FirstOrDefault(g => g.Match.Count > 0 && g.Match.Any(glob => GroupMatches(t, glob)));
            if (group is null && t.Kind is ApiTypeKind.Enum or ApiTypeKind.Delegate && t.DeclaringType is null)
            {
                var autoId = t.Kind == ApiTypeKind.Enum ? "enums" : "delegates";
                group = c.Groups.FirstOrDefault(g => g.Id == autoId)
                    ?? (t.Kind == ApiTypeKind.Enum
                        ? new RefGroup("enums", "Enums", "The enumerations in this category.", Array.Empty<string>(), Array.Empty<string>())
                        : new RefGroup("delegates", "Delegates", "The delegate types in this category.", Array.Empty<string>(), Array.Empty<string>()));
            }
            if (group is null) return null;
            if (!groupPages.TryGetValue((c.Id, group.Id), out var page))
            {
                page = new PlannedPage { Kind = PageKind.Group, Category = c, Stem = group.Id, Title = group.Title, Group = group };
                groupPages[(c.Id, group.Id)] = page;
            }
            return page;
        }

        // Decide the destination of every remaining type before stems are handed out,
        // so group ids are reserved ahead of type names.
        var typePlacement = new List<(ApiType Type, RefCategory Category, PlannedPage? Group)>();
        foreach (var t in model.Types.OrderBy(t => Depth(t)).ThenBy(t => t.DocId, StringComparer.Ordinal))
        {
            var catId = typeCategory[t.DocId];
            if (catId == ReferenceMap.ExcludedCategory) continue;
            if (plan.PageByTypeDocId.ContainsKey(t.DocId)) continue; // element records
            var remaining = t.Members.Where(m => !claimed.Contains(m.DocId)).ToList();
            bool emptyHolder = remaining.Count == 0 && t.EnumValues.Count == 0
                && (t.Kind == ApiTypeKind.StaticClass || t.IsSynthetic
                    || (!model.FromMetadata && t.Name.EndsWith("Extensions", StringComparison.Ordinal)));
            if (emptyHolder) continue;
            if (catId is null) { plan.Uncategorized.Add(t); continue; }
            var category = map.FindCategory(catId)!;
            typePlacement.Add((t, category, t.DeclaringType is null ? GroupPageFor(t, category) : null));
        }

        foreach (var page in groupPages.Values
            .OrderBy(p => map.Categories.ToList().IndexOf(p.Category!))
            .ThenBy(p => GroupOrder(p), StringComparer.Ordinal))
        {
            if (!typePlacement.Any(x => x.Group == page)) continue;
            StemsOf(page.Category!).Add(page.Stem);
            plan.Pages.Add(page);
        }

        // Hook pages.
        if (hooksCategory is not null)
        {
            foreach (var (name, overloads) in hooks)
            {
                var stem = UniqueStem(StemsOf(hooksCategory), name, null);
                var page = NewPage(PageKind.Hook, hooksCategory, stem, name);
                var primary = overloads.Where(o => !IsComponentSide(o)).ToList();
                foreach (var m in SortHookOverloads(overloads))
                {
                    var forwards = IsComponentSide(m) && primary.Any(p => HookKey(p) == HookKey(m));
                    (forwards ? page.Forwarders : page.Members).Add(m);
                    plan.PageByMemberDocId[m.DocId] = page;
                    page.AddGuidePages(map.Resolve(m.DocId)?.GuidePages);
                }
                if (page.Members.Count == 0)
                {
                    page.Members.AddRange(page.Forwarders);
                    page.Forwarders.Clear();
                }
            }
        }

        // Type pages, groups and nested types.
        foreach (var (t, category, group) in typePlacement)
        {
            var remaining = t.Members.Where(m => !claimed.Contains(m.DocId)).ToList();
            PlannedPage page;
            if (t.DeclaringType is { } outer && plan.PageByTypeDocId.TryGetValue(outer.DocId, out var outerPage)
                && outerPage.Kind is PageKind.Type or PageKind.Group)
            {
                page = outerPage;
                var owner = page.Kind == PageKind.Type ? page.Types[0].DocId : RootOf(t, page).DocId;
                Add(page.Nested, owner, t);
            }
            else if (group is not null)
            {
                page = group;
                page.Types.Add(t);
            }
            else
            {
                var stem = UniqueStem(StemsOf(category), t.Name, t);
                page = NewPage(PageKind.Type, category, stem, t.DisplayName);
                page.Types.Add(t);
            }
            page.TypeMembers[t.DocId] = remaining;
            page.AddGuidePages(map.Resolve(t.DocId)?.GuidePages);
            plan.PageByTypeDocId[t.DocId] = page;
            foreach (var m in remaining.Concat(t.EnumValues)) plan.PageByMemberDocId[m.DocId] = page;
        }

        // Members of uncategorized or element types that a role did not claim still
        // need a link target when their type has a page.
        foreach (var t in model.Types)
        {
            if (!plan.PageByTypeDocId.TryGetValue(t.DocId, out var page)) continue;
            foreach (var m in t.Members) plan.PageByMemberDocId.TryAdd(m.DocId, page);
        }

        // Index pages: one per category that has pages, then the root.
        foreach (var c in map.Categories)
        {
            if (!plan.Pages.Any(p => p.Category == c)) continue;
            var index = new PlannedPage { Kind = PageKind.CategoryIndex, Category = c, Stem = "index", Title = c.Title };
            index.AddGuidePages(c.GuidePages);
            plan.Pages.Add(index);
        }
        plan.Pages.Add(new PlannedPage { Kind = PageKind.RootIndex, Category = null, Stem = "index", Title = "API reference" });
        return plan;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>An element record name without its <c>Element</c> or <c>ElementBase</c> suffix.</summary>
    internal static string AreaKey(string typeName)
    {
        if (typeName.EndsWith("ElementBase", StringComparison.Ordinal) && typeName.Length > "ElementBase".Length)
            return typeName[..^"ElementBase".Length];
        if (typeName.EndsWith("Element", StringComparison.Ordinal) && typeName.Length > "Element".Length)
            return typeName[..^"Element".Length];
        return typeName;
    }

    private static bool IsRichText(string docId) =>
        docId.StartsWith("T:Microsoft.UI.Reactor.Core.RichText", StringComparison.Ordinal);

    /// <summary>A group glob with a dot matches the full type name; one without a dot
    /// matches the simple name.</summary>
    private static bool GroupMatches(ApiType t, string glob) =>
        glob.Contains('.')
            ? ReferenceMap.PrefixMatches(t.DocId[2..], glob)
            : ReferenceMap.PrefixMatches(t.Name, glob);

    private static RefArea? FindArea(ReferenceMap map, string id) =>
        map.Areas.FirstOrDefault(a => a.Id == id);

    /// <summary>Areas in the order the map declares them, with <c>other</c> last.</summary>
    private static List<RefArea> OrderAreas(ReferenceMap map, IEnumerable<RefArea> used)
    {
        var set = used.ToList();
        var result = map.Areas.Where(a => set.Any(u => u.Id == a.Id)).ToList();
        foreach (var u in set)
            if (!result.Any(a => a.Id == u.Id)) result.Add(u);
        return result.OrderBy(a => a.Id == "other" ? 1 : 0).ToList();
    }

    private static string GroupOrder(PlannedPage p)
    {
        var groups = p.Category!.Groups;
        var i = groups.ToList().FindIndex(g => g.Id == p.Group!.Id);
        return (i < 0 ? 999 : i).ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + p.Stem;
    }

    private static int Depth(ApiType t)
    {
        var d = 0;
        for (var o = t.DeclaringType; o is not null; o = o.DeclaringType) d++;
        return d;
    }

    private static ApiType RootOf(ApiType t, PlannedPage page)
    {
        var o = t.DeclaringType!;
        while (o.DeclaringType is not null && !page.Types.Contains(o)) o = o.DeclaringType;
        return o;
    }

    private static bool IsComponentSide(ApiMember m) =>
        m.DeclaringType.DocId is ComponentDocId or ComponentOfTDocId
        || (m.IsExtension && m.ExtendedTypeDocIds.Any(id => id is ComponentDocId or ComponentOfTDocId)
            && !m.ExtendedTypeDocIds.Contains(RenderContextDocId));

    /// <summary>The overload shape without the <c>this</c> parameter, so an extension on
    /// <c>Component</c> and the matching method on <c>RenderContext</c> compare equal.</summary>
    internal static string HookKey(ApiMember m) =>
        m.Name + "`" + m.TypeParameters.Count + "(" + string.Join(",", m.Params.Where(p => p.Modifier != "this").Select(p => p.Type)) + ")";

    private static IEnumerable<ApiMember> SortHookOverloads(IEnumerable<ApiMember> overloads) =>
        overloads
            .OrderBy(o => IsComponentSide(o) ? 1 : 0)
            .ThenBy(o => o.Params.Count(p => p.Modifier != "this"))
            .ThenBy(o => o.TypeParameters.Count)
            .ThenBy(o => o.DocId, StringComparer.Ordinal);

    internal static IEnumerable<ApiMember> SortOverloads(IEnumerable<ApiMember> overloads) =>
        overloads
            .OrderBy(o => o.Params.Count(p => p.Modifier != "this"))
            .ThenBy(o => o.TypeParameters.Count)
            .ThenBy(o => o.DocId, StringComparer.Ordinal);

    /// <summary>A file stem that is unique in its folder, ignoring case (Windows file
    /// names and site routes are case-insensitive).</summary>
    internal static string UniqueStem(HashSet<string> used, string name, ApiType? type)
    {
        var candidates = new List<string> { name };
        if (type is not null)
        {
            if (type.TypeParameters.Count > 0) candidates.Add(name + "-" + type.TypeParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var lastNs = type.Namespace.Split('.').LastOrDefault();
            if (!string.IsNullOrEmpty(lastNs)) candidates.Add(name + "-" + lastNs);
        }
        foreach (var c in candidates)
        {
            if (used.Add(c)) return c;
        }
        for (var i = 2; ; i++)
        {
            var c = name + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(c)) return c;
        }
    }

    private static void Add<TKey, TValue>(IDictionary<TKey, List<TValue>> dict, TKey key, TValue value)
    {
        if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<TValue>();
        list.Add(value);
    }
}
