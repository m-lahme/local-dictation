using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalDictation;

// Renders the screenshots in README.md from the real UI with neutral English sample data:
// dotnet NativeAudit.dll --readme-images <scratch data dir> writes docs/images/*.png.
internal static class ReadmeImages
{
    public static void Run(string root, string data)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; string output = Path.Combine(root, "docs", "images"); Directory.CreateDirectory(output);
        string previous = L.Language; L.Language = "en";
        try
        {
            Storage.Write(Path.Combine(data, "history.json"), History());
            foreach (string theme in new[] { "dark", "light" })
            {
                // Off screen, so the mouse pointer cannot leave a hover highlight in the picture.
                var window = new MainWindow(new Settings { Theme = theme, Rules = Rules() }, data, x => { }, x => { }, (entry, engine) => { }) { Width = 1040, Height = 790, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                window.Show(); window.SetStatus(SidebarStatus.Ready("whisper"));
                window.ShowHistory(); Pump(300); Save(window, 1.5, Path.Combine(output, "history-" + theme + ".png"));
                window.Height = 600; window.ShowDictionary(); Pump(300); Save(window, 1.5, Path.Combine(output, "dictionary-" + theme + ".png"));
                window.Close();
                Save(Overlays(theme), 2, Path.Combine(output, "overlay-" + theme + ".png"));
            }
        }
        finally { L.Language = previous; app.Shutdown(); }
    }

    static List<HistoryEntry> History()
    {
        DateTime now = DateTime.Today.AddHours(14).AddMinutes(20);
        HistoryEntry Entry(int minutesAgo, int seconds, string engine, string text, string status = "complete", bool review = false, bool audio = false) => new HistoryEntry { Id = Guid.NewGuid().ToString("N"), Time = now.AddMinutes(-minutesAgo), DurationMs = seconds * 1000 + 400, Engine = engine, RawText = text, Text = text, Status = status, ReviewRecommended = review, AudioFile = audio ? "sample.wav" : "", AudioExpiresAtUtc = audio ? DateTime.UtcNow.AddMinutes(56).AddSeconds(30) : null };
        return new List<HistoryEntry>
        {
            Entry(4, 12, "whisper", "Thanks for the quick reply! I went through the draft and left a few comments on the second section. Overall it looks great, so let's aim to ship it on Thursday.", audio: true),
            Entry(18, 9, "whisper", "Reminder for tomorrow:\r\n• Move the stand-up to 10:30\r\n• Invite the design team\r\n• Book the small meeting room"),
            Entry(41, 8, "parakeet", "The build passes locally, but the Windows job in CI fails while restoring NuGet packages. I'll check the cache settings and the lock file."),
            Entry(73, 5, "whisper", "the quarterly report is attached let me know if anything is missing", review: true),
            Entry(126, 6, "whisper", "Here's the link to the updated design spec. The color tokens are in the second tab.", "not-inserted"),
            Entry(190, 7, "whisper", "Please add JSON export to the settings page and keep the existing CSV option for now."),
        };
    }

    static List<ReplacementRule> Rules() => new List<ReplacementRule>
    {
        new ReplacementRule { Source = "Jason", Target = "JSON", CaseSensitive = true },
        new ReplacementRule { Source = "git hub", Target = "GitHub" },
        new ReplacementRule { Source = "dot net", Target = ".NET" },
        new ReplacementRule { Source = "type script", Target = "TypeScript" },
        new ReplacementRule { Source = "post gress", Target = "PostgreSQL" },
        new ReplacementRule { Source = "cube control", Target = "kubectl" },
        new ReplacementRule { Source = "vs code", Target = "VS Code" },
        new ReplacementRule { Source = "teh", Target = "the", Whole = false },
        new ReplacementRule { Source = "my email", Target = "alex@example.com" },
    };

    // The overlay states side by side at their real size. The transcribing wave is captured where its crest is clearly visible.
    static Border Overlays(string theme)
    {
        bool dark = Theme.IsDark(theme); Brush text = new SolidColorBrush(dark ? Color.FromRgb(205, 207, 214) : Color.FromRgb(52, 54, 60));
        var states = new (AppState State, double Level, double Time, string Label)[] { (AppState.Idle, 0, 1.2, "Ready"), (AppState.Recording, .85, 1.2, "Recording"), (AppState.Transcribing, 0, 2 * Math.PI - .8, "Transcribing"), (AppState.Success, 0, 1.2, "Inserted"), (AppState.Review, 0, 1.2, "Inserted, please review"), (AppState.Error, 0, 1.2, "Error") };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var state in states) row.Children.Add(Cell(Pill(theme, state.State, state.Level, state.Time), state.Label, text));
        var board = new StackPanel(); board.Children.Add(row);
        var copied = Cell(Pill(theme, AppState.Copied, 0, 1.2), "Not inserted: the text is on the clipboard", text); copied.Margin = new Thickness(0, 16, 0, 0); board.Children.Add(copied);
        var frame = new Border { Child = board, Background = new SolidColorBrush(dark ? Color.FromRgb(13, 14, 17) : Color.FromRgb(247, 246, 243)), Padding = new Thickness(14, 18, 14, 14), CornerRadius = new CornerRadius(10) };
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); frame.Arrange(new Rect(frame.DesiredSize)); return frame;
    }

    static FrameworkElement Cell(BitmapSource pill, string label, Brush text)
    {
        var cell = new StackPanel { Margin = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center };
        cell.Children.Add(new Image { Source = pill, Width = pill.Width, Height = pill.Height, HorizontalAlignment = HorizontalAlignment.Center });
        cell.Children.Add(new TextBlock { Text = label, Foreground = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        return cell;
    }

    static BitmapSource Pill(string theme, AppState state, double level, double time)
    {
        var overlay = new OverlayWindow(); overlay.ApplyTheme(theme); overlay.Show(); overlay.Snapshot(state, level, time); Pump(80);
        var bitmap = Render(overlay, 4); overlay.Close(); return bitmap;
    }

    static BitmapSource Render(FrameworkElement view, double scale)
    {
        view.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale), (int)Math.Ceiling(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view); bitmap.Freeze(); return bitmap;
    }

    // A window renders at its outer size, so it is cropped to its content area.
    static void Save(FrameworkElement view, double scale, string path)
    {
        BitmapSource bitmap = Render(view, scale);
        if (view is Window window && window.Content is FrameworkElement content) bitmap = new CroppedBitmap(bitmap, new Int32Rect(0, 0, Math.Min(bitmap.PixelWidth, (int)Math.Round(content.ActualWidth * scale)), Math.Min(bitmap.PixelHeight, (int)Math.Round(content.ActualHeight * scale))));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        Console.WriteLine("wrote " + path);
    }

    static void Pump(int ms)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
