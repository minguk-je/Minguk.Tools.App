using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;

namespace Minguk.Image;

public class FreeImage : MarkupExtension
{
    private static readonly object SyncInstance = new object();
    private static readonly object SyncReturn = new object();
    private static readonly string? ReferencedAssemblyName = Assembly.GetExecutingAssembly().GetName().Name;
    private static FreeImage? _instance;
    private object? _targetObject;
    private object? _targetProperty;

    private Dictionary<string, ImageSource>? _imageSourceCache;
    private Dictionary<string, ImageSource> ImageSourceCache => _imageSourceCache ??= new Dictionary<string, ImageSource>();

    private Dictionary<string, BitmapImage>? _bitmapCache;
    private Dictionary<string, BitmapImage> BitmapCache => _bitmapCache ??= new Dictionary<string, BitmapImage>();

    private Dictionary<string, byte[]>? _byteArrayCache;
    private Dictionary<string, Byte[]> ByteArrayCache => _byteArrayCache ??= new Dictionary<string, Byte[]>();

    public static FreeImage? Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (SyncInstance)
                {
                    _instance = new FreeImage();
                }
            }

            return _instance;
        }
    }

    public string? ResourceName { get; set; }


    // XAML에서 오류 메시지가 발행해서 있어야 함.

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        IProvideValueTarget? target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target == null)
        {
            return this;
        }
        else
        {
            _targetObject = target.TargetObject;
            _targetProperty = target.TargetProperty;
        }

        return CacheImageSource(ResourceName);
        //return CacheBitmapImage(ResourceName);
    }

    public ImageSource? GetImageSource(string path)
    {
        lock (SyncReturn)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            Uri uri = new Uri($"pack://application:,,,/{ReferencedAssemblyName};component/{path}");
            BitmapFrame bitmapFrame = BitmapFrame.Create(uri);
            bitmapFrame.Freeze();
            return bitmapFrame;
        }
    }

    public ImageSource? CacheImageSource(string? path)
    {
        lock (SyncReturn)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (ImageSourceCache.TryGetValue(path, out var source))
                return source;

            try
            {
                Uri uri = new Uri($"pack://application:,,,/{ReferencedAssemblyName};component/{path}");
                BitmapFrame bitmapFrame = BitmapFrame.Create(uri);
                bitmapFrame.Freeze();

                ImageSourceCache.Add(path, bitmapFrame);
                return bitmapFrame;
            }
            catch
            {
                return null;
            }
        }
    }

    public BitmapImage? GetBitmapImage(string path)
    {
        lock (SyncReturn)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            Uri uri = new Uri($"pack://application:,,,/{ReferencedAssemblyName};component/{path}");
            BitmapImage bitmapImage = new BitmapImage(uri);
            bitmapImage.Freeze();
            return bitmapImage;
        }
    }

    public BitmapImage? CacheBitmapImage(string path)
    {
        lock (SyncReturn)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (BitmapCache.TryGetValue(path, out var image))
                return image;

            Uri uri = new Uri($"pack://application:,,,/{ReferencedAssemblyName};component/{path}");
            BitmapImage bitmapImage = new BitmapImage(uri);
            bitmapImage.Freeze();

            BitmapCache.Add(path, bitmapImage);
            return bitmapImage;
        }
    }

    public byte[]? GetByteArray(string path)
    {
        lock (SyncReturn)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var resourceManager = new ResourceManager($"{ReferencedAssemblyName}.g", Assembly.GetExecutingAssembly());
            var stream = resourceManager.GetStream(path);
            if (stream != null)
            {
                MemoryStream memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                byte[] byteArray = memoryStream.ToArray();
                ByteArrayCache.Add(path, byteArray);
                return byteArray;
            }
            else
                return null;
        }
    }

    public byte[]? CacheByteArray(string path)
    {
        lock (SyncReturn)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (ByteArrayCache.TryGetValue(path, out var array))
                return array;

            var resourceManager = new ResourceManager($"{ReferencedAssemblyName}.g", Assembly.GetExecutingAssembly());
            var stream = resourceManager.GetStream(path);
            if (stream != null)
            {
                MemoryStream memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                byte[] byteArray = memoryStream.ToArray();
                ByteArrayCache.Add(path, byteArray);
                return byteArray;
            }
            else
                return null;
        }
    }

    public Stream? GetResourceStream(string resourceName)
    {
        lock (SyncReturn)
        {
            string path = $"pack://application:,,,/{ReferencedAssemblyName};component/{resourceName}";
            Uri uri = new Uri(path);

            StreamResourceInfo? info = System.Windows.Application.GetResourceStream(uri);
            if (info != null)
                return info.Stream;
            else
                return null;
        }
    }

    public Stream? GetContentStream(string resourceName)
    {
        lock (SyncReturn)
        {
            string path = $"pack://application:,,,/{ReferencedAssemblyName};component/{resourceName}";
            Uri uri = new Uri(path);

            StreamResourceInfo? info = System.Windows.Application.GetContentStream(uri);
            if (info != null)
                return info.Stream;
            else
                return null;
        }
    }

    public Stream? GetRemoteStream(string resourceName)
    {
        lock (SyncReturn)
        {
            string path = $"pack://siteoforigin:,,,/{ReferencedAssemblyName};component/{resourceName}";
            Uri uri = new Uri(path);

            StreamResourceInfo? info = System.Windows.Application.GetRemoteStream(uri);
            if (info != null)
                return info.Stream;
            else
                return null;
        }
    }

    public List<string> GetResourceFileList()
    {
        lock (SyncReturn)
        {
            List<string> list = new List<string>();
            var resourceManager = new ResourceManager($"{ReferencedAssemblyName}.g", Assembly.GetExecutingAssembly());
            var resources = resourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);

            if (resources != null)
                foreach (var res in resources)
                    list.Add((string)((DictionaryEntry)res).Key);

            return list;
        }
    }
}
