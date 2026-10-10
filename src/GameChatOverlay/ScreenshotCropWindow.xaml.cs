using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameChatOverlay;

public partial class ScreenshotCropWindow : Window
{
    private readonly BitmapSource _image;
    private Point _anchor;
    private Rect _selection;
    private bool _dragging;
    public BitmapSource? SelectedImage { get; private set; }

    public ScreenshotCropWindow(BitmapSource image, System.Drawing.Rectangle bounds)
    {
        InitializeComponent();
        _image = image;
        ScreenshotImage.Source = image;
        Loaded += (_, _) =>
        {
            // Use physical screen coordinates, including negative monitor origins and mixed DPI.
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
                bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
            Activate();
            Focus();
        };
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, CaptureSurface.ActualWidth),
        Math.Clamp(point.Y, 0, CaptureSurface.ActualHeight));

    private void Selection_Start(object sender, MouseButtonEventArgs e)
    {
        _anchor = Clamp(e.GetPosition(CaptureSurface));
        _selection = new Rect(_anchor, _anchor);
        _dragging = true;
        CaptureSurface.CaptureMouse();
        UpdateSelection();
        e.Handled = true;
    }

    private void Selection_Move(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _selection = new Rect(_anchor, Clamp(e.GetPosition(CaptureSurface)));
        UpdateSelection();
    }

    private void Selection_End(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _selection = new Rect(_anchor, Clamp(e.GetPosition(CaptureSurface)));
        _dragging = false;
        CaptureSurface.ReleaseMouseCapture();
        UpdateSelection();
        e.Handled = true;
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSelection();

    private void UpdateSelection()
    {
        if (Shade is null || SelectionOutline is null) return;
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, CaptureSurface.ActualWidth, CaptureSurface.ActualHeight)));
        geometry.Children.Add(new RectangleGeometry(_selection));
        Shade.Data = geometry;
        Canvas.SetLeft(SelectionOutline, _selection.Left);
        Canvas.SetTop(SelectionOutline, _selection.Top);
        SelectionOutline.Width = _selection.Width;
        SelectionOutline.Height = _selection.Height;
        SelectionOutline.Visibility = _selection.IsEmpty || _selection.Width == 0 || _selection.Height == 0
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Crop_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (_dragging || _selection.Width < 2 || _selection.Height < 2)
            {
                InstructionsText.Text = (TryFindResource("CropNeedSelection") as string)
                ?? "Select an area before pressing Enter · Esc cancels";
                return;
            }
            double scaleX = _image.PixelWidth / CaptureSurface.ActualWidth;
            double scaleY = _image.PixelHeight / CaptureSurface.ActualHeight;
            int left = Math.Clamp((int)Math.Floor(_selection.Left * scaleX), 0, _image.PixelWidth - 1);
            int top = Math.Clamp((int)Math.Floor(_selection.Top * scaleY), 0, _image.PixelHeight - 1);
            int right = Math.Clamp((int)Math.Ceiling(_selection.Right * scaleX), left + 1, _image.PixelWidth);
            int bottom = Math.Clamp((int)Math.Ceiling(_selection.Bottom * scaleY), top + 1, _image.PixelHeight);
            var cropped = new CroppedBitmap(_image, new Int32Rect(left, top, right - left, bottom - top));
            cropped.Freeze();
            SelectedImage = cropped;
            DialogResult = true;
        }
    }
}
