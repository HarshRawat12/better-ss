using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace BetterSS;

// Cropping retains the full canvas; history snapshots share immutable annotation elements.
internal sealed class EditorWindow : Window
{
    private readonly App app;
    private BitmapSource original;
    private readonly string sourcePath;
    private readonly Canvas canvas;
    private readonly List<UIElement> marks = new();
    private sealed record State(BitmapSource Base, UIElement[] Marks, Rect Crop);
    private readonly Stack<State> history = new(), undone = new();
    private Rect cropBounds, cropDraft;
    private readonly CropOverlay cropOverlay = new();
    private readonly Grid imageGrid = new();
    private readonly Canvas cropViewport = new() { ClipToBounds = true };
    private readonly StackPanel shapeMenu = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel cropControls = new();
    private readonly Dictionary<string, Button> shapeChoices = new();
    private State Snapshot() => new(original, marks.ToArray(), cropBounds);
    private void Remember() { history.Push(Snapshot()); undone.Clear(); }
    private void Restore(State state) { original = state.Base; marks.Clear(); marks.AddRange(state.Marks); canvas.Children.Clear(); foreach (var mark in marks) canvas.Children.Add(mark); canvas.Background = new ImageBrush(original); cropBounds = cropDraft = state.Crop; RefreshCrop(); }
    internal void SetCrop(Rect bounds) { bounds.Intersect(new Rect(0, 0, original.PixelWidth, original.PixelHeight)); if (bounds.Width < 3 || bounds.Height < 3 || bounds == cropBounds) return; Remember(); cropBounds = cropDraft = new Rect(Math.Floor(bounds.X), Math.Floor(bounds.Y), Math.Floor(bounds.Width), Math.Floor(bounds.Height)); RefreshCrop(); }
    internal void PreviewCrop(Rect bounds) { bounds.Intersect(new Rect(0, 0, original.PixelWidth, original.PixelHeight)); if (bounds.Width >= 3 && bounds.Height >= 3) { cropDraft = bounds; RefreshCrop(); } }
    internal void ApplyCrop() { if (tool != "Crop") return; if (cropDraft != cropBounds) SetCrop(cropDraft); SelectTool("Pen"); status.Text = $"Crop applied · {cropBounds.Width:0} × {cropBounds.Height:0}. Undo or Ctrl+Z restores the previous crop."; }
    internal void CancelCrop() { cropDraft = cropBounds; SelectTool("Pen"); status.Text = "Crop cancelled."; }
    private void RefreshCrop()
    {
        bool editing = tool == "Crop"; cropOverlay.Bounds = editing ? cropDraft : cropBounds; cropOverlay.Editing = editing; cropOverlay.IsHitTestVisible = editing; cropOverlay.Visibility = editing ? Visibility.Visible : Visibility.Collapsed; cropOverlay.InvalidateVisual();
        Rect visible = editing ? new Rect(0, 0, original.PixelWidth, original.PixelHeight) : cropBounds;
        cropViewport.Width = visible.Width; cropViewport.Height = visible.Height;
        Canvas.SetLeft(imageGrid, -visible.X); Canvas.SetTop(imageGrid, -visible.Y);
    }
    private readonly TextBlock status;
    private readonly Dictionary<string, Button> tools = new();
    private readonly Dictionary<Color, Button> colorButtons = new();
    private readonly WrapPanel colorControls = new();
    private readonly StackPanel shapeControls = new();
    private readonly Grid inkControls = new();
    private Button customColor = null!, fillButton = null!, strokeButton = null!, shapeButton = null!;
    private UIElement? drawing;
    private Point start;
    private string tool = "Pen", nib = "Round", shapeKind = "Rectangle";
    private Color color = Colors.Black, fillColor = Color.FromRgb(52, 120, 246), strokeColor = Colors.Black;
    private double brushSize = 8, strength = 8, fillOpacity = .25, strokeWidth = 5;
    private bool noFill, noStroke;
    private BitmapSource? effectBase;
    private bool busy;
    private readonly bool dark;
    internal Color SelectedColor => color;

    internal EditorWindow(App app, BitmapSource image, string sourcePath)
    {
        cropBounds = cropDraft = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
        this.app = app; original = image; this.sourcePath = sourcePath; dark = UI.Dark(app.Settings);
        UI.SetupWindow(this, "Editor", 1180, 820, dark); MinWidth = 850; MinHeight = 600;
        var root = new DockPanel { Background = UI.Background(dark) };
        var header = new Grid { Margin = new Thickness(20, 13, 20, 13) }; header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition());
        var heading = new StackPanel(); heading.Children.Add(UI.Text("Edit screenshot", 20, UI.Ink(dark), FontWeights.SemiBold));
        var size = UI.Text($"{image.PixelWidth:N0} × {image.PixelHeight:N0} pixels", 11, UI.Muted(dark)); size.Margin = new Thickness(0, 4, 0, 0); heading.Children.Add(size); var identity = new StackPanel { Orientation = Orientation.Horizontal }; var mark = UI.Mark(30); mark.Margin = new Thickness(0, 0, 12, 0); identity.Children.Add(mark); identity.Children.Add(heading); header.Children.Add(identity);
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 660 };
        var undoButton = UI.Button("Undo", Undo, false, dark); UI.Icon(undoButton, "\uE7A7", true); UI.Quiet(undoButton, dark); undoButton.Width = 34; actions.Children.Add(undoButton);
        var redoButton = UI.Button("Redo", Redo, false, dark); UI.Icon(redoButton, "\uE7A6", true); UI.Quiet(redoButton, dark); redoButton.Width = 34; actions.Children.Add(redoButton);
        actions.Children.Add(UI.Button("Copy image", () => { if (!busy) app.Copy(Render()); }, false, dark));
        actions.Children.Add(UI.Button("Extract text", () => { if (!busy) new OcrWindow(app, Render()) { Owner = this }.Show(); }, false, dark));
        actions.Children.Add(UI.Button("Export…", () => { if (!busy) new ExportWindow(app, Render(), sourcePath) { Owner = this }.ShowDialog(); }, true, dark)); Grid.SetColumn(actions, 1); header.Children.Add(actions); var headerSurface = new Border { Child = header, Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(0, 0, 0, .5) }; DockPanel.SetDock(headerSurface, Dock.Top); root.Children.Add(headerSurface);
        status = UI.Text("Choose a tool and draw. The original image stays intact.", 12, UI.Muted(dark)); status.Margin = new Thickness(20, 10, 20, 12); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);

        var controls = new StackPanel { Margin = new Thickness(12), Width = 144 }; var toolsLabel = UI.Text("ANNOTATE", 10, UI.Muted(dark), FontWeights.SemiBold); toolsLabel.Margin = new Thickness(8, 4, 0, 8); controls.Children.Add(toolsLabel);
        foreach (var name in new[] { "Pen", "Highlighter", "Shapes", "Arrow", "Gaussian blur", "Mosaic", "Crop" })
        { string choice = name; var b = UI.Button(name == "Shapes" ? "Shapes ▾" : name, () => { if (choice == "Shapes") ShowShapeMenu(); else SelectTool(choice); }, false, dark); UI.Navigation(b, dark); UI.Select(b, name == tool, dark); UI.Icon(b, name switch { "Pen" => "\uED63", "Highlighter" => "\uE193", "Shapes" => "\uE7C1", "Arrow" => "\uE72A", "Gaussian blur" => "\uE790", "Mosaic" => "\uE80A", _ => "\uE7A8" }); b.Margin = new Thickness(0, 4, 0, 0); controls.Children.Add(b); tools.Add(name, b); if (name == "Shapes") { shapeButton = b; controls.Children.Add(shapeMenu); } }
        foreach (var name in new[] { "Rectangle", "Rounded rectangle", "Ellipse", "Triangle", "Diamond", "Star", "Callout", "Pentagon", "Hexagon" }) { var choice = name; var b = UI.Button(name, () => { shapeKind = choice; SelectTool("Shapes"); foreach (var pair in shapeChoices) UI.Select(pair.Value, pair.Key == shapeKind, dark); }, false, dark); UI.Select(b, name == shapeKind, dark); b.Margin = new Thickness(12, 3, 0, 0); b.Padding = new Thickness(8); b.HorizontalContentAlignment = HorizontalAlignment.Left; shapeChoices.Add(name, b); shapeMenu.Children.Add(b); }
        controls.Children.Add(new Border { Height = .5, Background = UI.Line(dark), Margin = new Thickness(6, 20, 6, 12) });
        var insert = UI.Button("Add image…", AddImage, false, dark); UI.Icon(insert, "\uE8B9"); UI.Navigation(insert, dark); insert.Margin = new Thickness(0, 4, 0, 0); controls.Children.Add(insert);
        var directEdit = UI.Button("Edit original", EditOriginal, false, dark); UI.Icon(directEdit, "\uE8A7"); UI.Navigation(directEdit, dark); directEdit.Margin = new Thickness(0, 4, 0, 0); directEdit.IsEnabled = CanEditOriginal(); directEdit.ToolTip = directEdit.IsEnabled ? "Open this source image in its associated editor" : "This source is not a directly editable image file"; controls.Children.Add(directEdit);
        controls.Margin = new Thickness(10); controls.Width = 140;
        var side = new Border { Child = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = UI.Sidebar(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(0, 0, .5, 0), Width = 174 }; DockPanel.SetDock(side, Dock.Left); root.Children.Add(side);

        var workspace = new DockPanel(); var options = new StackPanel { Margin = new Thickness(16, 10, 16, 10) }; var brushAndColor = new WrapPanel();
        var presetRow = new WrapPanel();
        var nibButtons = new Dictionary<string, Button>();
        foreach (var style in new[] { "Round", "Square", "Dashed" }) { string selected = style; var b = UI.Button(style, () => { nib = selected; foreach (var entry in nibButtons) UI.Select(entry.Value, entry.Key == nib, dark); }, false, dark); UI.Segment(b, dark); UI.Select(b, nib == style, dark); nibButtons.Add(style, b); presetRow.Children.Add(b); }
        var brushLabel = UI.Text("Brush", 12, UI.Muted(dark)); brushLabel.Margin = new Thickness(0, 0, 10, 8); brushAndColor.Children.Add(brushLabel);
        var presets = UI.Segmented(presetRow, dark); presets.Margin = new Thickness(0, 0, 16, 8); brushAndColor.Children.Add(presets);
        foreach (var swatch in new[] { ("Black", Colors.Black), ("White", Colors.White), ("Red", Color.FromRgb(240, 68, 82)), ("Yellow", Color.FromRgb(255, 212, 59)), ("Green", Color.FromRgb(54, 168, 102)), ("Blue", Color.FromRgb(52, 120, 246)) })
        { var selected = swatch.Item2; var button = UI.Button(swatch.Item1, () => SelectColor(selected), false, dark); UI.Swatch(button, selected); button.ToolTip = swatch.Item1; button.Width = 30; button.Height = 30; button.Margin = new Thickness(0, 0, 5, 8); button.Padding = new Thickness(4); colorButtons.Add(selected, button); colorControls.Children.Add(button); }
        customColor = UI.Button("", () => PickColor(color, SelectColor), false, dark); customColor.Margin = new Thickness(0, 0, 0, 8); customColor.Padding = new Thickness(10, 7, 10, 7); colorControls.Children.Add(customColor); brushAndColor.Children.Add(colorControls); options.Children.Add(brushAndColor); SelectColor(color);
        for (int i = 0; i < 2; i++) inkControls.ColumnDefinitions.Add(new ColumnDefinition());
        AddSlider(inkControls, 0, "Brush size", 2, 120, brushSize, "px", v => brushSize = v); AddSlider(inkControls, 1, "Effect strength", 1, 30, strength, "", v => strength = v); options.Children.Add(inkControls);
        var shapeRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; shapeRow.Children.Add(UI.Text("Shape style", 12, UI.Muted(dark)));
        fillButton = UI.Button("Fill", () => PickColor(fillColor, c => { fillColor = c; RefreshShapeButtons(); }), false, dark); fillButton.Margin = new Thickness(12, 0, 8, 0); shapeRow.Children.Add(fillButton);
        strokeButton = UI.Button("Stroke", () => PickColor(strokeColor, c => { strokeColor = c; RefreshShapeButtons(); }), false, dark); shapeRow.Children.Add(strokeButton);
        var fillNone = new CheckBox { Content = "No fill", Foreground = UI.Ink(dark), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; fillNone.Click += (_, _) => { noFill = fillNone.IsChecked == true; fillButton.IsEnabled = !noFill; }; shapeRow.Children.Add(fillNone);
        var strokeNone = new CheckBox { Content = "No stroke", Foreground = UI.Ink(dark), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; strokeNone.Click += (_, _) => { noStroke = strokeNone.IsChecked == true; strokeButton.IsEnabled = !noStroke; }; shapeRow.Children.Add(strokeNone); shapeControls.Children.Add(shapeRow);
        var shapeSliders = new Grid(); for (int i = 0; i < 2; i++) shapeSliders.ColumnDefinitions.Add(new ColumnDefinition());
        AddSlider(shapeSliders, 0, "Fill opacity", 0, 100, 25, "%", v => fillOpacity = v / 100); AddSlider(shapeSliders, 1, "Stroke width", 0, 48, strokeWidth, "px", v => strokeWidth = v); shapeControls.Children.Add(shapeSliders); options.Children.Add(shapeControls); RefreshShapeButtons();
        cropControls.Children.Add(UI.Text("Drag the handles or move the box, then click Apply crop. Cancel keeps the current image.", 12, UI.Muted(dark)));
        var cropActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; cropActions.Children.Add(UI.Button("Cancel", CancelCrop, false, dark)); cropActions.Children.Add(UI.Button("Full image", () => PreviewCrop(new Rect(0, 0, original.PixelWidth, original.PixelHeight)), false, dark)); cropActions.Children.Add(UI.Button("Apply crop", ApplyCrop, true, dark)); cropControls.Children.Add(cropActions); options.Children.Add(cropControls);
        var filters = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; filters.Children.Add(UI.Text("Quick filters", 12, UI.Muted(dark))); foreach (var f in new[] { "Contrast", "B&W", "Sharpen", "Vibrant" }) { var filter = f; var b = UI.Button(filter, () => ApplyFilter(filter), false, dark); b.Margin = new Thickness(10, 0, 0, 0); filters.Children.Add(b); } options.Children.Add(filters);
        var optionsBorder = new Border { Child = options, Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(0, 1, 0, 1) }; DockPanel.SetDock(optionsBorder, Dock.Top); workspace.Children.Add(optionsBorder);
        canvas = new Canvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = new ImageBrush(image) { Stretch = Stretch.Fill }, ClipToBounds = true, Cursor = Cursors.Cross };
        imageGrid.Width = image.PixelWidth; imageGrid.Height = image.PixelHeight; imageGrid.Children.Add(canvas); imageGrid.Children.Add(cropOverlay); cropOverlay.Commit = PreviewCrop; cropViewport.Children.Add(imageGrid);
        var view = new Viewbox { Child = cropViewport, Stretch = Stretch.Uniform, Margin = new Thickness(20) }; workspace.Children.Add(view); root.Children.Add(workspace); Content = root;
        canvas.MouseLeftButtonDown += (_, e) => Begin(e.GetPosition(canvas)); canvas.MouseMove += (_, e) => { if (drawing != null && e.LeftButton == MouseButtonState.Pressed) Move(e.GetPosition(canvas)); }; canvas.MouseLeftButtonUp += async (_, _) => await EndAsync(); canvas.LostMouseCapture += (_, _) => { if (drawing != null && !busy) { canvas.Children.Remove(drawing); drawing = null; } };
        PreviewKeyDown += (_, e) => { if (HandleUndoRedoShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        SyncToolOptions();
    }

    internal bool HandleUndoRedoShortcut(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == 0) return false;
        if (Keyboard.FocusedElement is TextBox) return false;
        if (key == Key.Z) { Undo(); return true; }
        if (key == Key.Y) { Redo(); return true; }
        return false;
    }

    internal void SelectTool(string selected)
    {
        if (busy || drawing != null) return; tool = selected; if (tool == "Crop") cropDraft = cropBounds; foreach (var entry in tools) UI.Select(entry.Value, entry.Key == tool, dark); SyncToolOptions();
        status.Text = tool switch { "Gaussian blur" => "Paint an area to soften it. Effect strength controls Gaussian blur.", "Mosaic" => "Paint an area to pixelate it. Effect strength controls block size.", "Crop" => "Adjust the box, then click Apply crop. Cancel leaves the image unchanged.", "Shapes" => $"Drag to add a {shapeKind.ToLowerInvariant()}. Set fill, stroke, and width above.", _ => "Drag to draw. Adjust brush style above." };
    }
    private void SyncToolOptions() { bool effect = tool is "Gaussian blur" or "Mosaic", shape = tool == "Shapes"; colorControls.Visibility = !effect && !shape && tool != "Crop" ? Visibility.Visible : Visibility.Collapsed; shapeControls.Visibility = shape ? Visibility.Visible : Visibility.Collapsed; inkControls.Visibility = !shape && tool != "Crop" ? Visibility.Visible : Visibility.Collapsed; inkControls.Children[1].Visibility = effect ? Visibility.Visible : Visibility.Collapsed; cropControls.Visibility = tool == "Crop" ? Visibility.Visible : Visibility.Collapsed; if (!shape) shapeMenu.Visibility = Visibility.Collapsed; RefreshCrop(); }
    internal void SelectColor(Color selected) { color = selected; foreach (var swatch in colorButtons) UI.Select(swatch.Value, swatch.Key == color, dark); bool custom = !colorButtons.ContainsKey(color); UI.Select(customColor, custom, dark); customColor.Content = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; status.Text = $"Color #{color.R:X2}{color.G:X2}{color.B:X2} selected for new annotations."; }
    private void PickColor(Color initial, Action<Color> selected) { var picker = new ColorPickerWindow(initial, dark) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }; if (picker.ShowDialog() == true) selected(picker.SelectedColor); }
    private void RefreshShapeButtons() { fillButton.Content = $"Fill #{fillColor.R:X2}{fillColor.G:X2}{fillColor.B:X2}"; strokeButton.Content = $"Stroke #{strokeColor.R:X2}{strokeColor.G:X2}{strokeColor.B:X2}"; }
    private void ShowShapeMenu() { bool open = shapeMenu.Visibility != Visibility.Visible; SelectTool("Shapes"); shapeMenu.Visibility = open ? Visibility.Visible : Visibility.Collapsed; if (open) Motion.Enter(shapeMenu, Motion.Standard, new Point(0, 0)); }
    private void AddSlider(Grid parent, int column, string title, double min, double max, double value, string suffix, Action<double> changed)
    { var panel = new StackPanel { Margin = new Thickness(0, 6, 18, 0) }; var label = UI.Text($"{title} · {value:0}{suffix}", 11, UI.Muted(dark)); panel.Children.Add(label); var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true }; UI.StyleSlider(slider, dark); slider.ValueChanged += (_, _) => { changed(slider.Value); label.Text = $"{title} · {slider.Value:0}{suffix}"; }; panel.Children.Add(slider); Grid.SetColumn(panel, column); parent.Children.Add(panel); }
    internal void Begin(Point p)
    {
        if (tool == "Shapes" && noFill && (noStroke || strokeWidth == 0)) { status.Text = "Enable fill or stroke to draw a visible shape."; return; }
        if (busy) return; start = Clamp(p); bool effect = tool is "Gaussian blur" or "Mosaic"; if (effect) effectBase = RenderFull(); var brush = new SolidColorBrush(color);
        if (tool == "Crop") return;
        else if (tool == "Shapes") drawing = CreateShape();
        else { var cap = nib == "Square" || tool == "Highlighter" ? PenLineCap.Square : PenLineCap.Round; double thickness = tool == "Highlighter" ? Math.Max(14, brushSize) : effect ? Math.Max(16, brushSize) : brushSize; drawing = tool == "Arrow" ? new Path { Stroke = brush, StrokeThickness = brushSize, StrokeLineJoin = PenLineJoin.Miter } : new Polyline { Stroke = effect ? UI.Brush("#888888") : brush, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = cap, StrokeEndLineCap = cap, Points = new PointCollection { start, new Point(start.X + .01, start.Y) } }; drawing.Opacity = effect ? .35 : tool == "Highlighter" ? .22 : 1; if (drawing is Polyline line && nib == "Dashed" && !effect && tool != "Highlighter") line.StrokeDashArray = new DoubleCollection { 2, 2 }; }
        canvas.Children.Add(drawing); canvas.CaptureMouse(); Move(start);
    }
    internal Shape CreateShape()
    { Shape result = shapeKind switch { "Rectangle" => new Rectangle(), "Rounded rectangle" => new Rectangle { RadiusX = 16, RadiusY = 16 }, "Ellipse" => new Ellipse(), _ => new Path { StrokeLineJoin = PenLineJoin.Round } }; result.Stroke = noStroke || strokeWidth == 0 ? null : new SolidColorBrush(strokeColor); result.Fill = noFill ? null : new SolidColorBrush(Color.FromArgb((byte)Math.Round(fillOpacity * 255), fillColor.R, fillColor.G, fillColor.B)); result.StrokeThickness = strokeWidth; return result; }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, original.PixelWidth), Math.Clamp(p.Y, 0, original.PixelHeight));
    internal void Move(Point point)
    {
        Point p = Clamp(point); if (drawing is Polyline line) { line.Points.Add(p); return; }
        if (drawing is Path path && tool == "Arrow") { var v = start - p; if (v.Length < 1) return; v.Normalize(); double head = Math.Max(14, brushSize * 3); var normal = new Vector(-v.Y, v.X); var geometry = new StreamGeometry(); using (var c = geometry.Open()) { c.BeginFigure(start, false, false); c.LineTo(p, true, false); c.BeginFigure(p + v * head + normal * head * .5, false, false); c.LineTo(p, true, false); c.LineTo(p + v * head - normal * head * .5, true, false); } path.Data = geometry; return; }
        if (drawing is Path polygon && tool == "Shapes") { polygon.Data = ShapeGeometry(shapeKind, start, p); return; }
        if (drawing is FrameworkElement box) { Canvas.SetLeft(box, Math.Min(start.X, p.X)); Canvas.SetTop(box, Math.Min(start.Y, p.Y)); box.Width = Math.Abs(p.X - start.X); box.Height = Math.Abs(p.Y - start.Y); }
    }
    private static Geometry ShapeGeometry(string kind, Point a, Point b)
    { double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y), w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y), cx = x + w / 2, cy = y + h / 2; var points = new List<Point>(); if (kind == "Triangle") points.AddRange(new[] { new Point(cx, y), new Point(x + w, y + h), new Point(x, y + h) }); else if (kind == "Diamond") points.AddRange(new[] { new Point(cx, y), new Point(x + w, cy), new Point(cx, y + h), new Point(x, cy) }); else if (kind is "Pentagon" or "Hexagon") { int count = kind == "Pentagon" ? 5 : 6; for (int i = 0; i < count; i++) { double angle = -Math.PI / 2 + i * 2 * Math.PI / count; points.Add(new Point(cx + Math.Cos(angle) * w / 2, cy + Math.Sin(angle) * h / 2)); } } else if (kind == "Star") for (int i = 0; i < 10; i++) { double angle = -Math.PI / 2 + i * Math.PI / 5, radius = i % 2 == 0 ? Math.Min(w, h) / 2 : Math.Min(w, h) / 4.3; points.Add(new Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius)); } else points.AddRange(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(cx + Math.Min(24, w / 4), y + h), new Point(cx, y + h + Math.Min(24, h / 3)), new Point(cx - Math.Min(24, w / 4), y + h), new Point(x, y + h) }); var geometry = new StreamGeometry(); using var c = geometry.Open(); c.BeginFigure(points[0], true, true); c.PolyLineTo(points.GetRange(1, points.Count - 1), true, true); return geometry; }
    internal async Task EndAsync()
    {
        if (drawing == null) return; var finished = drawing; drawing = null; canvas.ReleaseMouseCapture();
        if (tool is not ("Gaussian blur" or "Mosaic")) { Remember(); marks.Add(finished); return; }
        busy = true; status.Text = "Applying effect…"; canvas.Children.Remove(finished);
        try { var line = (Polyline)finished; var shape = new PathGeometry(new[] { new PathFigure(line.Points[0], new[] { new PolyLineSegment(line.Points, true) }, false) }); var mask = shape.GetWidenedPathGeometry(new Pen(Brushes.Black, line.StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }); var area = mask.Bounds; area.Inflate(strength * 3 + 2, strength * 3 + 2); area.Intersect(new Rect(0, 0, original.PixelWidth, original.PixelHeight)); int left = (int)Math.Floor(area.Left), top = (int)Math.Floor(area.Top), width = (int)Math.Ceiling(area.Right) - left, height = (int)Math.Ceiling(area.Bottom) - top; var crop = new WriteableBitmap(new CroppedBitmap(effectBase!, new Int32Rect(left, top, width, height))); crop.Freeze(); var filtered = await Task.Run(() => tool == "Gaussian blur" ? ImageEffects.Gaussian(crop, strength) : ImageEffects.Mosaic(crop, (int)strength * 2)); mask.Transform = new TranslateTransform(-left, -top); var layer = new Image { Source = filtered, Width = width, Height = height, Stretch = Stretch.Fill, Clip = mask }; Canvas.SetLeft(layer, left); Canvas.SetTop(layer, top); Remember(); canvas.Children.Add(layer); marks.Add(layer); status.Text = "Effect applied. Undo is available."; }
        catch (Exception ex) { status.Text = "Couldn't apply effect: " + ex.Message; } finally { effectBase = null; busy = false; }
    }
    private async void ApplyFilter(string filter)
    { if (busy) return; busy = true; status.Text = "Applying " + filter + "…"; try { var flattened = RenderFull(); var adjusted = await Task.Run(() => filter switch { "Contrast" => ImageEffects.Adjust(flattened, contrast: 1.25), "B&W" => ImageEffects.Adjust(flattened, grayscale: true), "Sharpen" => ImageEffects.Adjust(flattened, sharp: true), _ => ImageEffects.Adjust(flattened, contrast: 1.1, saturation: 1.4) }); Remember(); original = adjusted; canvas.Children.Clear(); canvas.Background = new ImageBrush(original); marks.Clear(); status.Text = filter + " applied."; } catch (Exception ex) { status.Text = ex.Message; } finally { busy = false; } }
    private void AddImage()
    { if (busy) return; var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" }; if (dialog.ShowDialog(this) != true) return; try { var bitmap = BitmapDecoder.Create(new Uri(dialog.FileName), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; bitmap.Freeze(); double fit = Math.Min(1, Math.Min(original.PixelWidth * .45 / bitmap.PixelWidth, original.PixelHeight * .45 / bitmap.PixelHeight)); var image = new Image { Source = bitmap, Width = bitmap.PixelWidth * fit, Height = bitmap.PixelHeight * fit, Stretch = Stretch.Uniform }; Canvas.SetLeft(image, (original.PixelWidth - image.Width) / 2); Canvas.SetTop(image, (original.PixelHeight - image.Height) / 2); Remember(); canvas.Children.Add(image); marks.Add(image); status.Text = "Image added. Undo or Ctrl+Z removes it."; } catch (Exception ex) { status.Text = "Couldn't add image: " + ex.Message; } }
    private bool CanEditOriginal() => System.IO.File.Exists(sourcePath) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(System.IO.Path.GetExtension(sourcePath).ToLowerInvariant());
    private void EditOriginal() { if (!CanEditOriginal()) return; try { Process.Start(new ProcessStartInfo(sourcePath) { UseShellExecute = true }); } catch (Exception ex) { status.Text = "Couldn't open the source image: " + ex.Message; } }
    internal void Undo() { if (busy || drawing != null || history.Count == 0) return; undone.Push(Snapshot()); Restore(history.Pop()); }
    internal void Redo() { if (busy || drawing != null || undone.Count == 0) return; history.Push(Snapshot()); Restore(undone.Pop()); }
    internal BitmapSource Render() { var full = RenderFull(); var area = new Int32Rect((int)cropBounds.X, (int)cropBounds.Y, (int)cropBounds.Width, (int)cropBounds.Height); var result = new CroppedBitmap(full, area); result.Freeze(); return result; }
    internal BitmapSource RenderFull() { canvas.UpdateLayout(); var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(canvas) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, original.PixelWidth, original.PixelHeight) }, null, new Rect(0, 0, original.PixelWidth, original.PixelHeight)); var bitmap = new RenderTargetBitmap(original.PixelWidth, original.PixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap; }
}
