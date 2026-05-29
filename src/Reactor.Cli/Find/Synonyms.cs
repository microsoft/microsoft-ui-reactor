#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Find;

internal static partial class Synonyms
{
    private static readonly FrozenDictionary<string, string> _phraseMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app bar button"] = "appbarbutton",
        ["auto suggest"] = "autosuggestbox",
        ["auto suggest box"] = "autosuggestbox",
        ["breadcrumb bar"] = "breadcrumbbar",
        ["calendar date picker"] = "calendardatepicker",
        ["calendar view"] = "calendarview",
        ["check box"] = "checkbox",
        ["color picker"] = "colorpicker",
        ["combo box"] = "combobox",
        ["command bar"] = "commandbar",
        ["command bar flyout"] = "commandbarflyout",
        ["command host"] = "commandhost",
        ["content dialog"] = "contentdialog",
        ["content flyout"] = "contentflyout",
        ["context menu"] = "contextmenu",
        ["dark mode"] = "theme",
        ["data grid"] = "datagrid",
        ["date picker"] = "datepicker",
        ["drop down"] = "dropdown",
        ["drop down button"] = "dropdownbutton",
        ["error boundary"] = "errorboundary",
        ["flip view"] = "flipview",
        ["form field"] = "formfield",
        ["global state"] = "context",
        ["grid view"] = "gridview",
        ["hyperlink button"] = "hyperlinkbutton",
        ["info badge"] = "infobadge",
        ["info bar"] = "infobar",
        ["infinite scroll"] = "infinite",
        ["items repeater"] = "itemsrepeater",
        ["items view"] = "itemsview",
        ["list box"] = "listbox",
        ["list view"] = "listview",
        ["map control"] = "mapcontrol",
        ["master detail"] = "masterdetail",
        ["media player"] = "mediaplayerelement",
        ["menu bar"] = "menubar",
        ["menu flyout"] = "menuflyout",
        ["navigation host"] = "navigationhost",
        ["navigation view"] = "navigationview",
        ["number box"] = "numberbox",
        ["password box"] = "passwordbox",
        ["person picture"] = "personpicture",
        ["pips pager"] = "pipspager",
        ["progress bar"] = "progressbar",
        ["progress ring"] = "progressring",
        ["pull to refresh"] = "pulltorefresh",
        ["radio button"] = "radiobutton",
        ["radio buttons"] = "radiobuttons",
        ["rating control"] = "ratingcontrol",
        ["rich edit"] = "richeditbox",
        ["rich edit box"] = "richeditbox",
        ["rich text"] = "richtextblock",
        ["scroll view"] = "scrollview",
        ["scroll viewer"] = "scrollviewer",
        ["selector bar"] = "selectorbar",
        ["semantic zoom"] = "semanticzoom",
        ["sidebar nav"] = "sidebar",
        ["split button"] = "splitbutton",
        ["split view"] = "splitview",
        ["swipe control"] = "swipecontrol",
        ["tab view"] = "tabview",
        ["teaching tip"] = "teachingtip",
        ["text block"] = "textblock",
        ["text field"] = "textfield",
        ["theme switch"] = "theme",
        ["time picker"] = "timepicker",
        ["title bar"] = "titlebar",
        ["toggle button"] = "togglebutton",
        ["toggle switch"] = "toggleswitch",
        ["tree view"] = "treeview",
        ["use callback"] = "usecallback",
        ["use collection"] = "usecollection",
        ["use context"] = "usecontext",
        ["use custom hook"] = "customhook",
        ["use effect"] = "useeffect",
        ["use memo"] = "usememo",
        ["use mutation"] = "usemutation",
        ["use navigation"] = "usenavigation",
        ["use reducer"] = "usereducer",
        ["use ref"] = "useref",
        ["use resource"] = "useresource",
        ["use state"] = "usestate",
        ["use validation"] = "usevalidationcontext",
        ["validation context"] = "usevalidationcontext",
        ["virtual list"] = "virtuallist",
        ["web view"] = "webview2",
        ["wrap grid"] = "wrapgrid"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string[]> _synonymMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["accordion"] = ["expander"],
        ["alert"] = ["infobar", "contentdialog"],
        ["appbar"] = ["commandbar"],
        ["autocomplete"] = ["autosuggestbox"],
        ["avatar"] = ["personpicture"],
        ["badge"] = ["infobadge"],
        ["banner"] = ["infobar"],
        ["breadcrumbs"] = ["breadcrumbbar"],
        ["btn"] = ["button"],
        ["button"] = ["button"],
        ["card"] = ["card", "border"],
        ["carousel"] = ["flipview"],
        ["chart"] = ["linechart", "barchart", "areachart"],
        ["checkbox"] = ["checkbox"],
        ["chip"] = ["togglebutton"],
        ["collapse"] = ["expander"],
        ["counter"] = ["usestate"],
        ["datagrid"] = ["datagrid"],
        ["datepicker"] = ["calendardatepicker", "datepicker"],
        ["dialog"] = ["contentdialog"],
        ["div"] = ["flexrow", "flexcolumn", "vstack", "hstack"],
        ["divider"] = ["divider", "rectangle"],
        ["drawer"] = ["navigationview", "splitview"],
        ["dropdown"] = ["combobox"],
        ["elem"] = ["element"],
        ["errorhandling"] = ["errorboundary"],
        ["expander"] = ["expander"],
        ["fetch"] = ["useresource"],
        ["flex"] = ["flexrow", "flexcolumn"],
        ["flexbox"] = ["flexrow", "flexcolumn"],
        ["flexcol"] = ["flexcolumn"],
        ["flyout"] = ["flyout", "contentflyout"],
        ["form"] = ["formfield", "usevalidationcontext"],
        ["grid"] = ["grid", "gridview"],
        ["header"] = ["heading", "subtitle"],
        ["hook"] = ["usestate", "useeffect", "usereducer"],
        ["image"] = ["image"],
        ["img"] = ["image"],
        ["infinite"] = ["useinfiniteresource"],
        ["input"] = ["textfield", "numberbox"],
        ["label"] = ["caption", "formfield"],
        ["layout"] = ["vstack", "hstack", "flexrow", "grid"],
        ["link"] = ["hyperlinkbutton"],
        ["list"] = ["listview", "foreach"],
        ["loader"] = ["progressring", "progressbar"],
        ["menu"] = ["menuflyout", "menubar"],
        ["modal"] = ["contentdialog", "dialog"],
        ["nav"] = ["navigationview", "usenavigation"],
        ["notification"] = ["infobar", "teachingtip"],
        ["pager"] = ["pipspager"],
        ["password"] = ["passwordbox"],
        ["picker"] = ["combobox", "calendardatepicker"],
        ["popup"] = ["flyout", "contentdialog"],
        ["progress"] = ["progressring", "progressbar"],
        ["query"] = ["useresource"],
        ["radio"] = ["radiobuttons"],
        ["reducer"] = ["usereducer"],
        ["richtext"] = ["richtextblock", "richeditbox"],
        ["scroll"] = ["scrollview"],
        ["search"] = ["autosuggestbox"],
        ["select"] = ["combobox"],
        ["sidebar"] = ["navigationview", "splitview"],
        ["slider"] = ["slider"],
        ["snackbar"] = ["infobar", "teachingtip"],
        ["span"] = ["textblock"],
        ["spinner"] = ["progressring"],
        ["split"] = ["splitview", "splitbutton"],
        ["state"] = ["usestate", "usereducer"],
        ["stepper"] = ["numberbox"],
        ["switch"] = ["toggleswitch"],
        ["table"] = ["datagrid", "listview"],
        ["tabs"] = ["tabview", "pivot"],
        ["text"] = ["textblock", "textfield"],
        ["textarea"] = ["textbox", "richeditbox"],
        ["timepicker"] = ["timepicker"],
        ["toast"] = ["infobar"],
        ["toggle"] = ["toggleswitch", "togglebutton"],
        ["toolbar"] = ["commandbar"],
        ["tooltip"] = ["tooltipservice"],
        ["tree"] = ["treeview"],
        ["txt"] = ["textblock", "textfield"],
        ["typeahead"] = ["autosuggestbox"],
        ["usecallback"] = ["usecallback"],
        ["usecontext"] = ["usecontext"],
        ["useeffect"] = ["useeffect"],
        ["usememo"] = ["usememo"],
        ["usereducer"] = ["usereducer"],
        ["useref"] = ["useref"],
        ["usestate"] = ["usestate"],
        ["video"] = ["mediaplayerelement"],
        ["webview"] = ["webview2"]
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string CollapsePhrase(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var collapsed = query.ToLowerInvariant();
        foreach (var (phrase, token) in _phraseMap)
        {
            collapsed = Regex.Replace(
                collapsed,
                $@"\b{Regex.Escape(phrase)}\b",
                token,
                RegexOptions.CultureInvariant);
        }

        return collapsed;
    }

    public static string[] Expand(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        var normalized = term.ToLowerInvariant();
        return _synonymMap.TryGetValue(normalized, out var expanded)
            ? expanded
            : [normalized];
    }

    public static string[] ProcessQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Tokenize(CollapsePhrase(query))
            .Where(term => !StopWords.IsStopWord(term))
            .SelectMany(Expand)
            .Where(term => !StopWords.IsStopWord(term))
            .ToArray();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in TokenRegex().Matches(text.ToLowerInvariant()))
        {
            if (match.Value.Length > 0)
            {
                yield return match.Value;
            }
        }
    }

    [GeneratedRegex("[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
