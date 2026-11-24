using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Forterm.Studio.Services;
using Iciclecreek.Terminal;
using Porta.Pty;
using XTerm.Options;
using XTerm.Selection;

namespace Forterm.Studio.Controls;

/// <summary>The workspace owns the terminal's lifetime: switching tabs detaches and
/// re-attaches the visual without ending the PTY. The workspace disposes it explicitly.</summary>
public sealed partial class StudioTerminalControl : TerminalControl
{
    public static readonly Color DarkBackground = Color.Parse("#00161C");
    public static readonly Color LightBackground = Color.Parse("#FBFBFB");

    protected override Type StyleKeyOverride => typeof(TerminalControl);

    public StudioTerminalControl()
    {
        AutoStartOnLoad = false;
        Process = "";
        FontSize = Appearance.Current.TerminalFontSize;
        FontFamily = Appearance.TerminalFontFamily(Appearance.Current);
        Appearance.Changed += OnAppearance;
        AllowWindowOps = false;
        BufferSize = Appearance.Current.Scrollback;
        CopyFilter = PasteGuard.Copied;
        // "会话记录" writes its file from the reader, not from the UI thread.
        OutputReceivedOnReadTask = true;
        Options = new TerminalOptions
        {
            ReflowOnResize = false,
            ClipboardReadEnabled = false,
            ClipboardWriteEnabled = false,
            KittyNotificationsEnabled = false,
            SixelEnabled = false,
            KittyGraphicsEnabled = false,
            Theme = TerminalSchemes.PaletteFor(Appearance.Current.TerminalScheme, appDark: true),
        };
        ApplyVariant();
        ApplyCursor(Appearance.Current);
        ActualThemeVariantChanged += (_, _) => ApplyVariant();
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPressHandled, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
    }

    // ---------- mouse and shortcuts ----------

    public event Action? FindRequested;

    bool menuArmed, selecting;
    SelectionRange? selectionAtPress;

    /// <summary>A program that tracks the mouse gets the clicks; Shift takes them back, as for selecting.</summary>
    bool ProgramWantsMouse(KeyModifiers modifiers) =>
        Terminal.MouseTrackingMode != XTerm.Input.MouseTrackingMode.None && !(modifiers.HasFlag(KeyModifiers.Shift) && !Terminal.ShiftEscape);

    SelectionRange? Selected => templateApplied && Terminal is { } t && t.Selection.TryGetSelection(out var range) ? range : null;
    public bool HasSelection => Selected != null;

    /// <summary>Where the cursor is. FullScreen: a program has the whole screen (vim, less, tmux). AtPrompt: it stands
    /// behind what looks like the prompt of a shell, and nothing follows. Output: how much has been printed so far. A
    /// terminal that was never shown does not know: its output is still waiting.</summary>
    public (bool FullScreen, bool AtPrompt, long Output) ReadPlace()
    {
        if (!templateApplied || View is not { Terminal: not null } view) return (false, false, -1);
        var line = view.ReadCursorLine();
        var row = line.Rows[line.Row];
        string before = string.Concat(line.Rows.Take(line.Row).SelectMany(r => r).Concat(row.Take(line.Column))), after = string.Concat(row.Skip(line.Column).Concat(line.Rows.Skip(line.Row + 1).SelectMany(r => r)));
        return (line.Alternate, after.Trim().Length == 0 && LooksLikePrompt(before), line.Written);
    }

    /// <summary>"root@web:~# ", "[deploy@web ~]$ ", "bash-5.1$ ", "web% ", "web /etc # " — not "[Y/n] ", "Password: ", a
    /// continued line ("> ", "db-# "), a question that ends in "#", a percentage or a bar of "####". Rather no Enter at
    /// a prompt than one at a question.</summary>
    internal static bool LooksLikePrompt(string before)
    {
        string t = before.Trim();
        if (t.Length == 0 || t[^1] is not ('$' or '#' or '%')) return false;
        if (t.Length == 1) return true;
        if (t.Contains('?')) return false;
        string head = t[..^1];
        char previous = head[^1];
        // psql says what is not finished before its "#": "db-#", "db'#", "db(#", "db$#"; "=#" is its empty line.
        if (previous == ' ') return head.TrimEnd().Split(' ')[^1] is ['~' or '/', ..];
        if (!char.IsLetterOrDigit(previous) && previous is not (']' or ')' or '~' or '/' or '_' or '=' or ':') || t[^1] == '%' && char.IsDigit(previous)) return false;
        // One word ("host:~$", "Router(config)#"), brackets around it ("[root@web my dir]#"), or who and where it is.
        return !head.Contains(' ') || previous == ']' && head[0] == '[' && head.Contains('@') || WhoAndWhere().IsMatch(head);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(\([^)]{1,60}\) ?)?[\w.-]{1,64}@[\w.-]{1,255}[: ]~?(/.{0,400})?$", System.Text.RegularExpressions.RegexOptions.NonBacktracking)]
    private static partial System.Text.RegularExpressions.Regex WhoAndWhere();

    TerminalView? View => this.FindDescendantOfType<TerminalView>();

    /// <summary>Unless the user is in another window: what is typed there must not reach a shell.</summary>
    public void FocusTerminal() { if (!Services.KeyboardFocus.IsElsewhere(this)) View?.Focus(); }

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        menuArmed = selecting = false;
        // Not the scroll bar: dragging it selects nothing.
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<TerminalView>(includeSelf: true) is not { Terminal: not null } view) return;
        selecting = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed && !ProgramWantsMouse(e.KeyModifiers);
        selectionAtPress = Selected;
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || ProgramWantsMouse(e.KeyModifiers)) return;
        if (!Appearance.Current.RightClickMenu) return; // the view copies, or pastes through PasteFilter
        e.Handled = true; // not copy/paste: the menu opens on release
        menuArmed = true;
        view.Focus();
    }

    /// <summary>After the view handled the press: it dropped the old selection, or selected a word. Dragging
    /// over the same text again is a new selection too.</summary>
    void OnPressHandled(object? sender, PointerPressedEventArgs e)
    {
        if (selecting && Selected != selectionAtPress) selectionAtPress = null;
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || !selecting) return;
        selecting = false;
        if (Appearance.Current.CopyOnSelect && Selected is { } now && now != selectionAtPress) _ = CopyAsync();
    }

    /// <summary>The menu belongs to a right click that was not given to the program. The menu key goes to
    /// the program like every other key.</summary>
    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (!menuArmed) e.Handled = true;
        menuArmed = false;
    }

    void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (!Services.Shortcuts.Matches(Services.Shortcuts.Find, e) || FindRequested == null) return;
        e.Handled = true;
        FindRequested();
    }

    /// <summary>Selects the whole buffer (what the program printed, including scrollback).</summary>
    public void SelectAll()
    {
        if (!templateApplied || Terminal is not { } terminal) return;
        terminal.Selection.SelectAll();
        Refresh();
    }

    // ---------- appearance ----------

    Highlighter? highlighter;
    string appliedCursor = "";

    void ApplyCursor(StudioPreferences preferences)
    {
        // Only a real change: a program may have set its own cursor (DECSCUSR).
        string key = preferences.CursorStyle + preferences.CursorBlink;
        if (key == appliedCursor) return;
        appliedCursor = key;
        CursorStyle = Appearance.CursorStyles.First(c => c.Key == preferences.CursorStyle).Style;
        CursorBlink = preferences.CursorBlink;
    }

    void ApplyHighlight(ThemeOptions palette)
    {
        var next = Highlighter.For(Appearance.Current, appliedScheme, palette);
        if (ReferenceEquals(next, highlighter)) return;
        highlighter = next;
        HighlightProvider = next == null ? null : next.Match;
    }

    string appliedScheme = "";
    bool templateApplied;

    /// <summary>Applies the "终端配色" scheme: "跟随主题" follows the light/dark variant, a fixed scheme
    /// keeps its own colours. Foreground, cursor and selection are set here as local values because the
    /// vendor template defaults them to white-on-black and pushes its foreground into the emulator.
    /// Only a real change is applied: re-applying would discard colours a program set (OSC 4/10/11).</summary>
    void ApplyVariant()
    {
        // Null while detached (another tab is shown): that is not "dark". The application's variant keeps
        // a hidden terminal on its colours, including those a program set.
        var variant = (ThemeVariant?)ActualThemeVariant ?? Avalonia.Application.Current?.ActualThemeVariant;
        bool dark = variant != ThemeVariant.Light;
        string scheme = Appearance.Current.TerminalScheme;
        string key = scheme == TerminalSchemes.Auto ? "auto-" + (dark ? "dark" : "light") : scheme;
        var palette = TerminalSchemes.PaletteFor(scheme, dark);
        if (key == appliedScheme) { ApplyHighlight(palette); return; }
        appliedScheme = key;
        ApplyHighlight(palette);
        Color Parse(string? c, Color fallback) => c is null ? fallback : Color.Parse(c);
        // Theme first (it re-seeds the whole palette), then the control's own colours.
        if (Options is { } options) options.Theme = palette;
        if (templateApplied && Terminal is { Options: { } live }) live.Theme = palette;
        Background = new SolidColorBrush(Parse(palette.Background, dark ? DarkBackground : LightBackground));
        Foreground = new SolidColorBrush(Parse(palette.Foreground, dark ? Colors.White : Colors.Black));
        CursorColor = Parse(palette.Cursor, dark ? Colors.White : Colors.Black);
        SelectionBrush = new SolidColorBrush(TerminalSchemes.SelectionOverlay(palette));
        var (match, current) = TerminalSchemes.FindOverlays(palette);
        SearchHighlightBrush = new SolidColorBrush(match);
        SearchCurrentBrush = new SolidColorBrush(current);
    }

    IPtyConnection? pending;

    void OnAppearance(StudioPreferences preferences)
    {
        FontSize = preferences.TerminalFontSize;
        var family = Appearance.TerminalFontFamily(preferences);
        if (!Equals(FontFamily, family)) FontFamily = family;
        ApplyVariant();
        ApplyCursor(preferences);
    }

    /// <summary>Stops following preference changes, then releases the terminal.</summary>
    public new void Dispose()
    {
        Appearance.Changed -= OnAppearance;
        base.Dispose();
    }

    /// <summary>Attaches now if the control has its template, otherwise as soon as it gets one.
    /// A workspace that is not on screen (another tab selected, batch connect) has no template
    /// yet; its PTY output waits in the connection's bounded read queue until it is shown.</summary>
    public void AttachWhenReady(IPtyConnection connection)
    {
        try { AttachConnection(connection); }
        catch (InvalidOperationException) { pending = connection; }
    }

    public bool HasPendingConnection => pending != null;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // The emulator exists now: later scheme changes go straight to its live options.
        templateApplied = true;
        // Without this guard the upstream detach hook cancels the PTY reader when a tab switch
        // removes the control from the visual tree. Released in the workspace's Dispose.
        BeginReparent();
        if (pending is { } connection)
        {
            pending = null;
            AttachConnection(connection);
        }
    }
}
