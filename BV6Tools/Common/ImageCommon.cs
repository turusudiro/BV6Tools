using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using FontStyle = System.Drawing.FontStyle;

namespace ImageCommon;

public static class ImageUtilites
{
    private static string? _fallbackPath;

    private static BitmapImage? _fallbackImage;
    private static readonly Lock _lock = new();

    public static void Initialize(string fallbackPath)
    {
        _fallbackPath = fallbackPath;
    }

    public static BitmapImage CreateQuestionMarkImage()
    {
        if (_fallbackImage == null)
        {
            lock (_lock)
            {
                if (_fallbackImage == null)
                {
                    if (string.IsNullOrEmpty(_fallbackPath))
                    {
                        throw new InvalidOperationException("ImageUtilites must be initialized with a fallback path before use.");
                    }

                    var directory = Path.GetDirectoryName(_fallbackPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    if (!File.Exists(_fallbackPath))
                        CreateQuestionMarkImageFile(_fallbackPath, 231, 87);

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.UriSource = new Uri(_fallbackPath, UriKind.Absolute);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    _fallbackImage = bitmapImage;
                }
            }
        }
        return _fallbackImage;
    }

    private static void CreateQuestionMarkImageFile(string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.Gray);
        using var font = new Font("Arial", 40, FontStyle.Bold);
        using var brush = new SolidBrush(Color.Black);

        const string text = "?";
        var textSize = graphics.MeasureString(text, font);
        var position = new PointF((width - textSize.Width) / 2, (height - textSize.Height) / 2);
        graphics.DrawString(text, font, brush, position);

        bitmap.Save(path, ImageFormat.Png);
    }


    public static BitmapImage LoadImageFromFile(string filePath)
    {
        var bitmapImage = new BitmapImage();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = stream;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }
}