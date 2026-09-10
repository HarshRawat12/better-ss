using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class EditorRegressionTests
{
    internal static void Run(App app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = new List<string>(); var folder = Path.GetFullPath(".validation/editor-tests"); Directory.CreateDirectory(folder);
        void Check(bool pass, string text) { if (!pass) throw new Exception(text); log.Add("PASS " + text); }
        try
        {
            app.Settings.Theme = "Dark"; System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var sample = SelfTest.SampleImage(); var editor = new EditorWindow(app, sample, "sample.png"); editor.Show(); editor.UpdateLayout();
            Check(Same(sample, editor.Render()), "Initial editor retains pixels");
            Check(!Find<Button>(editor).Any(b => b.Content as string == "Open in Float Edit"), "Advanced editor entry point removed");
            var labels = Find<TextBlock>(editor).Select(t => t.Text).ToArray(); Check(!labels.Any(t => t.StartsWith("Ink opacity") || t.StartsWith("Glow")), "Removed ink opacity and glow controls");
            var shapeButton = Find<Button>(editor).Single(b => b.Content as string == "Shapes ▾"); shapeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Check(Find<Button>(editor).Single(b => b.Content as string == "Star").IsVisible, "Shapes expands options below sidebar button");
            editor.SelectTool("Shapes"); var noFill = Find<CheckBox>(editor).Single(c => c.Content as string == "No fill"); noFill.IsChecked = true; noFill.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Check(editor.CreateShape().Fill == null, "No fill produces outlined shape"); noFill.IsChecked = false; noFill.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var noStroke = Find<CheckBox>(editor).Single(c => c.Content as string == "No stroke"); noStroke.IsChecked = true; noStroke.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Check(editor.CreateShape().Stroke == null, "No stroke produces a fill-only shape"); noStroke.IsChecked = false; noStroke.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            editor.Begin(new Point(40, 40)); editor.Move(new Point(200, 160)); editor.EndAsync().GetAwaiter().GetResult(); var annotated = editor.Render();
            editor.SelectTool("Crop"); editor.PreviewCrop(new Rect(100, 100, 400, 250)); Check(editor.Render().PixelWidth == 960, "Crop preview does not commit before Apply"); editor.CancelCrop(); Check(Same(annotated, editor.Render()), "Cancel crop preserves current image");
            editor.SelectTool("Crop"); editor.PreviewCrop(new Rect(100, 100, 400, 250)); editor.ApplyCrop(); Check(editor.Render().PixelWidth == 400 && editor.Render().PixelHeight == 250, "Apply crop commits chosen bounds");
            editor.HandleUndoRedoShortcut(Key.Z, ModifierKeys.Control); Check(Same(annotated, editor.Render()), "Ctrl+Z undoes applied crop"); editor.HandleUndoRedoShortcut(Key.Y, ModifierKeys.Control); Check(editor.Render().PixelWidth == 400, "Ctrl+Y redoes applied crop");
            editor.UpdateLayout(); Screenshot(editor, Path.Combine(folder, "crop-applied.png")); editor.Close(); log.Add($"Completed {log.Count} checks."); Environment.ExitCode = 0;
        }
        catch (Exception ex) { log.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); File.WriteAllLines(Path.Combine(folder, "results.txt"), log); }
    }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject { if (parent is T match) yield return match; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(parent, i))) yield return child; }
    private static byte[] Bytes(BitmapSource source) { var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var data = new byte[source.PixelWidth * source.PixelHeight * 4]; image.CopyPixels(data, source.PixelWidth * 4, 0); return data; }
    private static bool Same(BitmapSource a, BitmapSource b) => a.PixelWidth == b.PixelWidth && a.PixelHeight == b.PixelHeight && Bytes(a).SequenceEqual(Bytes(b));
    private static void Screenshot(Window window, string path) { var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); Native.SavePng(bitmap, path); }
}
