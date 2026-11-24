using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views;

/// <summary>The editor's colours are those of the terminal scheme: background, text, selection and syntax.</summary>
public sealed partial class EditorView : UserControl
{
    readonly TextEditor editor;
    readonly TextBox findText, replaceText, lineText;
    readonly TextBlock position, selection, size, wrap;
    readonly FoundRenderer found = new();
    EditorTabViewModel? model;

    public EditorView()
    {
        AvaloniaXamlLoader.Load(this);
        editor = this.FindControl<TextEditor>("Editor")!;
        findText = this.FindControl<TextBox>("FindText")!;
        replaceText = this.FindControl<TextBox>("ReplaceText")!;
        lineText = this.FindControl<TextBox>("LineText")!;
        position = this.FindControl<TextBlock>("PositionText")!;
        selection = this.FindControl<TextBlock>("SelectionText")!;
        size = this.FindControl<TextBlock>("SizeText")!;
        wrap = this.FindControl<TextBlock>("WrapText")!;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.HighlightCurrentLine = true;
        editor.TextArea.TextView.BackgroundRenderers.Add(found);
        // The component's own search panel has patterns without limits; ours takes its place and its keys.
        editor.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(() => { try { editor.SearchPanel?.Uninstall(); } catch (InvalidOperationException) { } });
        editor.TextArea.Caret.PositionChanged += (_, _) => ShowPositionSoon();
        editor.TextArea.SelectionChanged += (_, _) => ShowPositionSoon();
        DataContextChanged += (_, _) => Bind(DataContext as EditorTabViewModel);
        ActualThemeVariantChanged += (_, _) => ApplyColours();
        AddHandler(KeyDownEvent, OnKeys, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        this.FindControl<Button>("FindButton")!.Click += (_, _) => model?.Find.Open(replace: false);
        this.FindControl<Button>("PositionButton")!.Click += (_, _) => model?.Find.OpenLine();
        this.FindControl<Button>("WrapButton")!.Click += (_, _) => Save(p => p with { EditorWrap = !p.EditorWrap });
        followText = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => { followText!.Stop(); model?.Find.FollowText(); }) { IsEnabled = false };
    }

    readonly DispatcherTimer followText;

    void Save(Func<StudioPreferences, StudioPreferences> change)
    {
        if (model?.SavePreferences is { } save) save(change); else Appearance.Apply(change(Appearance.Current));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Appearance.Changed += OnAppearance;
        OnAppearance(Appearance.Current);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Appearance.Changed -= OnAppearance;
        followText.Stop();
    }

    void OnAppearance(StudioPreferences p)
    {
        editor.FontFamily = p.EditorFont.Length > 0 ? new FontFamily(p.EditorFont + "," + Appearance.MonospaceStack) : Appearance.TerminalFontFamily(p);
        editor.FontSize = p.EditorFontSize;
        editor.WordWrap = p.EditorWrap;
        editor.ShowLineNumbers = p.EditorLineNumbers;
        editor.Options.ShowSpaces = p.EditorWhitespace;
        editor.Options.ShowTabs = p.EditorWhitespace;
        editor.Options.IndentationSize = p.EditorTab;
        editor.Options.ConvertTabsToSpaces = p.EditorSpaces;
        model?.FollowSettings();
        wrap.Text = p.EditorWrap ? "自动换行：开" : "自动换行：关";
        ApplyColours();
        ShowPositionSoon();
    }

    void Bind(EditorTabViewModel? next)
    {
        if (model != null)
        {
            model.PropertyChanged -= OnModelChanged;
            model.Document.Changed -= OnTextChanged;
            model.Find.FocusWanted -= OnFocusWanted;
            model.Find.Updated -= OnFound;
            model.Find.Closed -= OnFindClosed;
            model.Find.Selection = null;
            model.Find.Select = null;
        }
        model = next;
        found.Find = next?.Find;
        if (model != null)
        {
            model.PropertyChanged += OnModelChanged;
            model.Document.Changed += OnTextChanged;
            model.Find.FocusWanted += OnFocusWanted;
            model.Find.Updated += OnFound;
            model.Find.Closed += OnFindClosed;
            model.Find.Selection = () => (editor.SelectionStart, editor.SelectionLength);
            model.Find.Select = Go;
        }
        ApplyColours();
        ShowPositionSoon();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(EditorTabViewModel.ColouredLanguage)) ApplyColours(); }

    void OnTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        ShowPositionSoon();
        if (model?.Find.IsOpen == true) { followText.Stop(); followText.Start(); }
    }

    // ---------- find ----------

    void OnFocusWanted() => Dispatcher.UIThread.Post(() =>
    {
        if (model is not { } m || KeyboardFocus.IsElsewhere(this)) return;
        var box = m.Find.IsGoingToLine ? lineText : findText;
        box.Focus();
        box.SelectAll();
    });

    void OnFound() => editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    void OnFindClosed() => Dispatcher.UIThread.Post(FocusText);

    void Go(int offset, int length)
    {
        if (model == null) return;
        offset = Math.Clamp(offset, 0, model.Document.TextLength);
        editor.Select(offset, Math.Clamp(length, 0, model.Document.TextLength - offset));
        var at = model.Document.GetLocation(offset);
        editor.ScrollTo(at.Line, at.Column);
    }

    static readonly bool Mac = OperatingSystem.IsMacOS();
    static KeyModifiers Main => Mac ? KeyModifiers.Meta : KeyModifiers.Control;

    void OnKeys(object? sender, KeyEventArgs e)
    {
        if (model is not { } m) return;
        bool inFind = ReferenceEquals(e.Source, findText) || ReferenceEquals(e.Source, replaceText), inLine = ReferenceEquals(e.Source, lineText);
        bool composing = e.Key == Key.ImeProcessed || e.Source is TextBox box && box.FindDescendantPreedit();
        if (composing) return;
        var find = m.Find;
        if (e.Key == Key.F && e.KeyModifiers == Main || Shortcuts.Matches(Shortcuts.Find, e)) find.Open(replace: false);
        else if (Mac ? e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Alt) : e.Key == Key.H && e.KeyModifiers == KeyModifiers.Control) find.Open(replace: true);
        else if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control) find.OpenLine();
        else if (e.Key == Key.F3 && find.IsOpen || Mac && e.Key == Key.G && e.KeyModifiers.HasFlag(KeyModifiers.Meta) && find.IsOpen) (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? find.PreviousCommand : find.NextCommand).Execute(null);
        else if (e.Key == Key.Escape && (find.IsOpen || find.IsGoingToLine)) find.Close();
        else if (e.Key == Key.Enter && ReferenceEquals(e.Source, findText)) (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? find.PreviousCommand : find.NextCommand).Execute(null);
        else if (e.Key == Key.Enter && ReferenceEquals(e.Source, replaceText)) find.ReplaceCommand.Execute(null);
        else if (e.Key == Key.Enter && inLine) find.GoCommand.Execute(null);
        else return;
        e.Handled = true;
    }

    double turned;

    /// <summary>Cmd / Ctrl + wheel changes the size of the text; a touchpad turns by fractions.</summary>
    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || e.KeyModifiers != Main) return;
        e.Handled = true;
        if (Math.Sign(turned) != Math.Sign(e.Delta.Y)) turned = 0;
        turned += e.Delta.Y;
        if (Math.Abs(turned) < 1) return;
        int by = Math.Sign(turned);
        turned = 0;
        Save(p => p with { EditorFontSize = p.EditorFontSize + by });
    }

    bool positionWanted;

    /// <summary>Not while the text is being changed: caret and selection are still those of the text before.</summary>
    void ShowPositionSoon()
    {
        if (positionWanted) return;
        positionWanted = true;
        Dispatcher.UIThread.Post(() => { positionWanted = false; ShowPosition(); }, DispatcherPriority.Background);
    }

    void ShowPosition()
    {
        if (model == null || !ReferenceEquals(editor.Document, model.Document)) return;
        var caret = editor.TextArea.Caret;
        position.Text = $"行 {caret.Line}，列 {caret.Column}";
        int length = editor.SelectionLength;
        selection.Text = length > 0 ? $"已选 {length} 个字符" + (editor.TextArea.Selection.IsMultiline ? $"，{editor.TextArea.Selection.EndPosition.Line - editor.TextArea.Selection.StartPosition.Line + 1} 行" : "") : "";
        size.Text = $"共 {model.Document.LineCount} 行，{model.Document.TextLength} 个字符";
    }

    /// <summary>Marks what was found, and the current one more.</summary>
    sealed class FoundRenderer : IBackgroundRenderer
    {
        public EditorFindViewModel? Find { get; set; }
        public IBrush? Brush { get; set; }
        public IPen? Pen { get; set; }
        public KnownLayer Layer => KnownLayer.Selection;

        const int LongLine = 1000;
        static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(25);

        /// <summary>Only what is in sight, and for a limited time: a place in a long line is expensive to find.</summary>
        public void Draw(TextView textView, DrawingContext context)
        {
            if (Find is not { IsOpen: true, Matches: { Count: > 0 } matches } find || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var line in textView.VisualLines)
            {
                var (from, to) = InSight(textView, line);
                int low = 0, high = matches.Count;
                while (low < high) { int middle = (low + high) / 2; if (matches[middle].EndOffset < from) low = middle + 1; else high = middle; }
                for (int i = low; i < matches.Count && matches[i].StartOffset <= to; i++)
                {
                    if (matches[i].Length == 0 || i != find.Current && clock.Elapsed > Longest) continue;
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, matches[i]))
                        context.DrawRectangle(Brush, i == find.Current ? Pen : null, new Rect(rect.X, rect.Y, Math.Max(2, rect.Width), rect.Height), 2, 2);
                }
            }
        }

        static (int From, int To) InSight(TextView textView, VisualLine line)
        {
            int first = line.FirstDocumentLine.Offset, last = line.LastDocumentLine.EndOffset;
            if (last - first <= LongLine) return (first, last);
            double top = Math.Max(textView.VerticalOffset, line.VisualTop), bottom = Math.Min(textView.VerticalOffset + textView.Bounds.Height, line.VisualTop + line.Height) - 1;
            int At(double x, double y, int otherwise) => textView.GetPositionFloor(new Point(x, y)) is { } p ? Math.Clamp(textView.Document.GetOffset(p.Location), first, last) : otherwise;
            return (At(textView.HorizontalOffset, top, first), At(textView.HorizontalOffset + textView.Bounds.Width, bottom, last));
        }
    }

    // ---------- colours ----------

    string applied = "";
    EditorTabViewModel? colouredModel;
    GuardedColorizer? colorizer;
    /// <summary>The definition that colours the text now.</summary>
    internal AvaloniaEdit.Highlighting.IHighlightingDefinition? Definition { get; private set; }

    public void FocusText() { if (!KeyboardFocus.IsElsewhere(this)) editor.TextArea.Focus(); }

    /// <summary>In place of <see cref="TextEditor.SyntaxHighlighting"/>, which has no limits.</summary>
    void Colour(AvaloniaEdit.Highlighting.IHighlightingDefinition? definition)
    {
        var transformers = editor.TextArea.TextView.LineTransformers;
        if (colorizer != null) transformers.Remove(colorizer);
        colorizer = null;
        Definition = definition;
        if (definition == null || model is not { } shown) return;
        colorizer = new GuardedColorizer(definition, shown.StopColouring);
        transformers.Insert(0, colorizer);
    }

    void ApplyColours()
    {
        bool dark = ActualThemeVariant != ThemeVariant.Light;
        string scheme = Appearance.Current.TerminalScheme, key = scheme == TerminalSchemes.Auto ? "auto-" + (dark ? "dark" : "light") : scheme;
        string state = key + "|" + (model?.ColouredLanguage.Id ?? "");
        if (state == applied && ReferenceEquals(model, colouredModel)) return; // typing in the settings changes the preferences on every key
        applied = state;
        colouredModel = model;
        var palette = TerminalSchemes.PaletteFor(scheme, dark);
        Color background = Color.Parse(palette.Background!), foreground = Color.Parse(palette.Foreground!);
        editor.Background = new SolidColorBrush(background);
        editor.Foreground = new SolidColorBrush(foreground);
        editor.LineNumbersForeground = new SolidColorBrush(EditorSyntax.Readable(Color.Parse(palette.BrightBlack!), foreground, background, 3));
        editor.TextArea.Caret.CaretBrush = new SolidColorBrush(Color.Parse(palette.Cursor ?? palette.Foreground!));
        editor.TextArea.SelectionBrush = new SolidColorBrush(TerminalSchemes.SelectionOverlay(palette));
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(foreground, 0.06);
        editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        found.Brush = new SolidColorBrush(Color.Parse(palette.Yellow!), 0.32);
        found.Pen = new Pen(new SolidColorBrush(EditorSyntax.Readable(Color.Parse(palette.Yellow!), foreground, background, 3)), 1.5);
        Colour(model == null ? null : EditorSyntax.Definition(model.ColouredLanguage, palette, key));
    }
}
