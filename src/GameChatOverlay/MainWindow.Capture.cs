using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GameChatOverlay;

public partial class MainWindow
{
    private bool _captureInProgress;

    private async void Capture_Click(object sender, RoutedEventArgs e) => await CaptureScreenAsync();
    private async void CropCapture_Click(object sender, RoutedEventArgs e) => await CaptureScreenAsync(selectRegion: true);

    private static bool IsCaptureTarget(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window) ||
            !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) return false;
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return processId != 0 && processId != (uint)Environment.ProcessId;
    }

    private static Rectangle CaptureBounds(IntPtr target, Rectangle fallback, out bool capturingWindow)
    {
        capturingWindow = false;
        if (IsCaptureTarget(target))
        {
            // Extended frame bounds are physical pixels and exclude invisible resize borders.
            bool found = NativeMethods.DwmGetWindowAttribute(target, 9, out var rect,
                Marshal.SizeOf<NativeMethods.WindowRect>()) == 0 || NativeMethods.GetWindowRect(target, out rect);
            if (found && rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                var bounds = Rectangle.Intersect(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                    Forms.SystemInformation.VirtualScreen);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    capturingWindow = true;
                    return bounds;
                }
            }
        }
        return fallback;
    }

    private static byte[] CapturePng(Rectangle bounds)
    {
        // Thread-pool threads must use physical coordinates just like DWM's window bounds.
        IntPtr previousDpi = NativeMethods.SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally { if (previousDpi != IntPtr.Zero) NativeMethods.SetThreadDpiAwarenessContext(previousDpi); }
    }

    private static BitmapSource DecodeCapture(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }

    private async Task CopyCaptureAsync(BitmapSource image)
    {
        // Chromium accepts the registered PNG format; keep Bitmap for other Windows apps.
        using var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        var data = new System.Windows.DataObject();
        data.SetImage(image);
        data.SetData("PNG", png);
        // Another application can briefly hold the clipboard open. Retry on the UI's STA thread.
        for (int attempt = 0; ; attempt++)
        {
            if (_exiting) return;
            try
            {
                png.Position = 0;
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (COMException) when (attempt < 4) { await Task.Delay(80); }
        }
    }

    private async Task CaptureScreenAsync(bool selectRegion = false)
    {
        if (_exiting || _captureInProgress) return;
        _captureInProgress = true;
        CaptureButton.IsEnabled = false;
        CropCaptureButton.IsEnabled = false;
        IntPtr target = IsVisible ? _previousWindow : NativeMethods.ForegroundRoot;
        Rectangle fallback = Forms.Screen.FromHandle(IsCaptureTarget(target) ? target : _handle).Bounds;
        string message;
        bool error = false;
        try
        {
            HideBrowserPopups();
            Hide();
            if (IsCaptureTarget(target)) NativeMethods.SetForegroundWindow(target);
            // Let the desktop/game repaint after all Agripa windows have disappeared.
            await Task.Delay(300);
            if (_exiting) return;
            Rectangle bounds = CaptureBounds(target, fallback, out bool capturingWindow);
            byte[] png = await Task.Run(() => CapturePng(bounds));
            if (_exiting) return;
            BitmapSource image = DecodeCapture(png);
            if (selectRegion)
            {
                var selector = new ScreenshotCropWindow(image, bounds) { Owner = this };
                if (selector.ShowDialog() != true || selector.SelectedImage is null)
                {
                    if (!_exiting) SetStatus(Text("CropCancelled"));
                    return;
                }
                image = selector.SelectedImage;
            }
            await CopyCaptureAsync(image);
            message = selectRegion
                ? Text("CropCopied")
                : capturingWindow
                ? Text("WindowCopied")
                : Text("MonitorCopied");
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or IOException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            message = Text("CaptureError");
            error = true;
        }
        finally
        {
            _captureInProgress = false;
            if (!_exiting)
            {
                CaptureButton.IsEnabled = true;
                CropCaptureButton.IsEnabled = true;
                if (IsCaptureTarget(target)) _previousWindow = target;
                Show();
                WindowState = WindowState.Normal;
                if (_clickThrough)
                {
                    if (_previousWindow != IntPtr.Zero && NativeMethods.IsWindow(_previousWindow))
                        NativeMethods.SetForegroundWindow(_previousWindow);
                }
                else
                {
                    Activate();
                    NativeMethods.SetForegroundWindow(_handle);
                }
                RestoreBrowserPopups();
                if (!_compactMode) _ = Dispatcher.BeginInvoke(FocusInput, DispatcherPriority.Input);
            }
        }
        if (!_exiting) SetStatus(message, error);
    }
}
