using System.Collections.Generic;
using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;

namespace FlatClip
{
    /// <summary>
    /// UI strings. English by default; Dutch when the Windows display language is Dutch.
    /// XAML files use {{Key}} placeholders that are filled in before the XAML is parsed.
    /// </summary>
    static class L
    {
        public static bool Dutch { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl";

        /// <summary>Culture for dates and numbers, matching the UI language.</summary>
        public static CultureInfo Culture { get; private set; } = new CultureInfo(Dutch ? "nl-NL" : "en-GB");

        /// <summary>"auto" (follow Windows display language), "en" or "nl".</summary>
        public static void Init(string language)
        {
            switch ((language ?? "auto").ToLowerInvariant())
            {
                case "en": Dutch = false; break;
                case "nl": Dutch = true; break;
                default: Dutch = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl"; break;
            }
            Culture = new CultureInfo(Dutch ? "nl-NL" : "en-GB");
        }

        static readonly Dictionary<string, (string En, string Nl)> S = new Dictionary<string, (string, string)>
        {
            // tray
            ["TrayOpen"] = ("Open", "Openen"),
            ["TrayPause"] = ("Pause recording", "Opnemen pauzeren"),
            ["TraySettings"] = ("Settings…", "Instellingen…"),
            ["TrayExit"] = ("Exit", "Afsluiten"),
            ["TrayPaused"] = ("FlatClip – paused", "FlatClip – gepauzeerd"),
            ["HotkeyInUse"] = ("The hotkey {0} is already in use by another program. Choose a different one in Settings.",
                               "De sneltoets {0} is al in gebruik door een ander programma. Kies een andere via Instellingen."),

            // history items
            ["Image"] = ("Image", "Afbeelding"),
            ["OneFile"] = ("1 file", "1 bestand"),
            ["NFiles"] = ("{0} files", "{0} bestanden"),
            ["Formatted"] = ("formatted", "opmaak"),
            ["NChars"] = ("{0} chars", "{0} tekens"),
            ["JustNow"] = ("just now", "zojuist"),
            ["MinAgo"] = ("{0} min ago", "{0} min geleden"),
            ["Today"] = ("today {0}", "vandaag {0}"),
            ["Yesterday"] = ("yesterday {0}", "gisteren {0}"),

            // popup
            ["SearchPlaceholder"] = ("Search your clipboard history…", "Zoek in je klembordgeschiedenis…"),
            ["Settings"] = ("Settings", "Instellingen"),
            ["TabHistory"] = ("History", "Geschiedenis"),
            ["TabSnippets"] = ("Snippets", "Snippets"),
            ["NewSnippet"] = ("New snippet", "Nieuw snippet"),
            ["HintPaste"] = ("paste", "plakken"),
            ["HintPlain"] = ("plain text", "zonder opmaak"),
            ["HintPin"] = ("pin", "vastzetten"),
            ["HintSaveSnippet"] = ("save as snippet", "als snippet"),
            ["HintNew"] = ("new", "nieuw"),
            ["HintEdit"] = ("edit", "bewerken"),
            ["HintDelete"] = ("delete", "verwijderen"),
            ["PinnedTip"] = ("Pinned – never expires", "Vastgezet – verloopt niet"),
            ["EditTip"] = ("Edit (Ctrl+E)", "Bewerken (Ctrl+E)"),
            ["ConfirmDelete"] = ("Press Shift+Del again to delete", "Nogmaals Shift+Del om te verwijderen"),
            ["OneSnippet"] = ("1 snippet", "1 snippet"),
            ["NSnippets"] = ("{0} snippets", "{0} snippets"),
            ["NofM"] = ("{0} of {1}", "{0} van {1}"),
            ["NoSnippets"] = ("No snippets yet – press Ctrl+N to create one", "Nog geen snippets – Ctrl+N maakt er een"),
            ["NoSnippetsFound"] = ("No snippets found", "Geen snippets gevonden"),
            ["OneItem"] = ("1 item", "1 item"),
            ["NItems"] = ("{0} items", "{0} items"),
            ["OneResult"] = ("1 result", "1 resultaat"),
            ["NResults"] = ("{0} results", "{0} resultaten"),
            ["NothingCopied"] = ("Nothing copied yet", "Nog niets gekopieerd"),
            ["NoResults"] = ("No results", "Geen resultaten"),

            // settings
            ["SettingsTitle"] = ("FlatClip – Settings", "FlatClip – Instellingen"),
            ["Hotkey"] = ("Hotkey", "Sneltoets"),
            ["HotkeyDesc"] = ("Click the field and press the key combination you want.", "Klik in het veld en druk de gewenste toetscombinatie in."),
            ["PressKeys"] = ("Press a key combination…", "Druk op een toetscombinatie…"),
            ["KeepHistory"] = ("Keep history", "Geschiedenis bewaren"),
            ["KeepHistoryDesc"] = ("Items copied longer ago than this are removed automatically. Pinned items are always kept.",
                                   "Items die langer geleden gekopieerd zijn worden automatisch verwijderd. Vastgezette items blijven altijd staan."),
            ["Days"] = ("days", "dagen"),
            ["Theme"] = ("Theme", "Thema"),
            ["ThemeDark"] = ("Dark", "Donker"),
            ["ThemeLight"] = ("Light", "Licht"),
            ["ThemeSystem"] = ("System", "Systeem"),
            ["StartWithWindows"] = ("Start with Windows", "Starten met Windows"),
            ["StartWithWindowsDesc"] = ("FlatClip then runs in the background in the system tray.", "FlatClip draait dan op de achtergrond in het systeemvak."),
            ["ClearHistory"] = ("Clear history", "Geschiedenis wissen"),
            ["Cancel"] = ("Cancel", "Annuleren"),
            ["Save"] = ("Save", "Opslaan"),
            ["NeedModifier"] = ("Use at least one of Ctrl, Alt, Shift or Win (or a function key).",
                                "Gebruik minimaal één van Ctrl, Alt, Shift of Win (of een F-toets)."),
            ["RetentionRange"] = ("Retention must be between 1 and 3650 days.", "De bewaartermijn moet tussen 1 en 3650 dagen liggen."),
            ["HotkeyUnavailable"] = ("The hotkey {0} can't be used; it is probably already in use by another program.",
                                     "De sneltoets {0} kan niet worden gebruikt; waarschijnlijk is hij al in gebruik door een ander programma."),
            ["ClearConfirm"] = ("Are you sure you want to clear the entire history?\n\nPinned items will be kept.",
                                "Weet je zeker dat je de hele geschiedenis wilt wissen?\n\nVastgezette items blijven bewaard."),

            // snippet editor
            ["SnippetTitle"] = ("FlatClip – Snippet", "FlatClip – Snippet"),
            ["EditSnippet"] = ("Edit snippet", "Snippet bewerken"),
            ["Name"] = ("Name", "Naam"),
            ["NameDesc"] = ("Use this to find the snippet: type its name in the search box and press Enter.",
                            "Hiermee vind je het snippet terug: typ de naam in het zoekveld en druk op Enter."),
            ["Content"] = ("Content", "Inhoud"),
            ["Delete"] = ("Delete", "Verwijderen"),
            ["NeedName"] = ("Give the snippet a name.", "Geef het snippet een naam."),
            ["EmptyContent"] = ("The content is empty.", "De inhoud is leeg."),
            ["DeleteSnippetConfirm"] = ("Delete snippet \"{0}\"?", "Snippet \"{0}\" verwijderen?"),
        };

        public static string T(string key) => S.TryGetValue(key, out var s) ? (Dutch ? s.Nl : s.En) : key;

        public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);

        static readonly Regex Placeholder = new Regex(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        /// <summary>Replaces {{Key}} placeholders in XAML source (XML-escaped).</summary>
        public static string Localize(string xaml) => Placeholder.Replace(xaml, m => SecurityElement.Escape(T(m.Groups[1].Value)));
    }
}
