using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalDictation
{
    public static class Brand
    {
        public static UIElement Logo(double size) { var grid = new Grid { Width = size, Height = size, Background = Brushes.Transparent }; grid.Children.Add(new Border { CornerRadius = new CornerRadius(size * .28), Background = Theme.Brush(Color.FromRgb(250, 248, 243)) }); double[] h = { .20, .45, .58, .32, .40 }, y = { .40, .275, .21, .34, .30 }; for (int i = 0; i < 5; i++) grid.Children.Add(new Border { Width = size * .09, Height = size * h[i], CornerRadius = new CornerRadius(size * .045), Background = Theme.Brush(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(size * (.17 + i * .14), size * y[i], 0, 0) }); return grid; }
        public static BitmapSource Image(int size) { var view = Logo(size); view.Measure(new Size(size, size)); view.Arrange(new Rect(0, 0, size, size)); view.UpdateLayout(); var b = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); b.Render(view); b.Freeze(); return b; }
        public static void SaveAssets(string dir) { Directory.CreateDirectory(dir); var images = new System.Collections.Generic.List<byte[]>(); int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 }; foreach (int size in sizes) { var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(Image(size))); using (var m = new MemoryStream()) { e.Save(m); images.Add(m.ToArray()); File.WriteAllBytes(Path.Combine(dir, "logo-" + size + ".png"), m.ToArray()); } } using (var f = new BinaryWriter(File.Create(Path.Combine(dir, "icon.ico")))) { f.Write((ushort)0); f.Write((ushort)1); f.Write((ushort)sizes.Length); int offset = 6 + 16 * sizes.Length; for (int i = 0; i < sizes.Length; i++) { f.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); f.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); f.Write((byte)0); f.Write((byte)0); f.Write((ushort)1); f.Write((ushort)32); f.Write(images[i].Length); f.Write(offset); offset += images[i].Length; } foreach (var bytes in images) f.Write(bytes); } }
    }
}
