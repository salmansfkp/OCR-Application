using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tesseract;

namespace OCR_Application
{
    public partial class MainWindow : Window
    {
        private readonly string _tesseractDataPath = @"C:\Program Files\Tesseract-OCR\tessdata"; // Update with your Tesseract data path
        private bool isDragging = false;
        private System.Windows.Point startPoint;
        private Window selectionWindow;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position the button in the bottom-right corner
            Left = SystemParameters.PrimaryScreenWidth - Width - 20;
            Top = SystemParameters.PrimaryScreenHeight - Height - 20;

            // Attach MouseLeftButtonDown to the button
            FloatingButton.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; // Prevent Click event interference
                DragMove();
            };
        }

        private void FloatingButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a transparent window for region selection
            selectionWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0)),
                Width = SystemParameters.VirtualScreenWidth, // Use VirtualScreenWidth for multi-monitor
                Height = SystemParameters.VirtualScreenHeight,
                Left = SystemParameters.VirtualScreenLeft, // Account for negative offsets in multi-monitor
                Top = SystemParameters.VirtualScreenTop,
                Topmost = true,
                ShowInTaskbar = false
            };

            var canvas = new Canvas();
            var selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255))
            };
            canvas.Children.Add(selectionRect);
            selectionWindow.Content = canvas;

            selectionWindow.MouseDown += (s, args) =>
            {
                if (args.ChangedButton == MouseButton.Left)
                {
                    isDragging = true;
                    startPoint = args.GetPosition(selectionWindow);
                    Canvas.SetLeft(selectionRect, startPoint.X);
                    Canvas.SetTop(selectionRect, startPoint.Y);
                    selectionRect.Width = 0;
                    selectionRect.Height = 0;
                }
            };

            selectionWindow.MouseMove += (s, args) =>
            {
                if (isDragging)
                {
                    var currentPoint = args.GetPosition(selectionWindow);
                    var width = currentPoint.X - startPoint.X;
                    var height = currentPoint.Y - startPoint.Y;
                    Canvas.SetLeft(selectionRect, width < 0 ? currentPoint.X : startPoint.X);
                    Canvas.SetTop(selectionRect, height < 0 ? currentPoint.Y : startPoint.Y);
                    selectionRect.Width = Math.Abs(width);
                    selectionRect.Height = Math.Abs(height);
                }
            };

            selectionWindow.MouseUp += async (s, args) =>
            {
                if (isDragging && selectionRect.Width > 0 && selectionRect.Height > 0)
                {
                    try
                    {
                        // Get DPI scaling factor
                        var dpi = VisualTreeHelper.GetDpi(this);
                        var rect = new System.Windows.Rect(
                            Canvas.GetLeft(selectionRect) * dpi.DpiScaleX,
                            Canvas.GetTop(selectionRect) * dpi.DpiScaleY,
                            selectionRect.Width * dpi.DpiScaleX,
                            selectionRect.Height * dpi.DpiScaleY);
                        // Close the selection window before processing
                        isDragging = false;
                        selectionWindow.Close();
                        // Debug: Log coordinates
                        System.Diagnostics.Debug.WriteLine($"Adjusted Rect: X={rect.X}, Y={rect.Y}, Width={rect.Width}, Height={rect.Height}");
                        string extractedText = await ExtractTextFromScreenRegion(rect);
                        ShowCopyableTextWindow(extractedText);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error: {ex.Message}", "OCR Error");
                    }
                }
                else
                {
                    isDragging = false;
                    selectionWindow.Close();
                }
            };

            selectionWindow.Show();
        }

        private async Task<string> ExtractTextFromScreenRegion(System.Windows.Rect rect)
        {
            using (var bitmap = new System.Drawing.Bitmap((int)rect.Width, (int)rect.Height))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, new System.Drawing.Size((int)rect.Width, (int)rect.Height));
                }
                // Save bitmap to file for debugging
                string debugPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"debug_capture_{DateTime.Now.Ticks}.png");
                bitmap.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);
                System.Diagnostics.Debug.WriteLine($"Saved debug image to: {debugPath}");
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    stream.Position = 0;
                    using (var engine = new TesseractEngine(_tesseractDataPath, "eng", EngineMode.Default))
                    using (var img = Pix.LoadFromMemory(stream.ToArray()))
                    using (var page = engine.Process(img))
                    {
                        return page.GetText();
                    }
                }
            }
        }
        private void ShowCopyableTextWindow(string text)
        {
            var textWindow = new Window
            {
                Title = "Extracted Text",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };

            var grid = new Grid();
            var textBox = new TextBox
            {
                Text = text,
                IsReadOnly = false,
                AcceptsReturn = true,
                AcceptsTab = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            };
            var copyButton = new Button
            {
                Content = "Copy to Clipboard",
                Width = 120,
                Height = 30,
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            copyButton.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(textBox.Text))
                {
                    Clipboard.SetText(textBox.Text);
                    MessageBox.Show("Text copied to clipboard!", "Success");
                }
            };

            grid.Children.Add(textBox);
            grid.Children.Add(copyButton);
            textWindow.Content = grid;
            textWindow.ShowDialog();
        }
        // P/Invoke for dragging the window
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void DragMoveMarch()
        {
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, 0xA1, 0x2, 0);
        }
    }
}
