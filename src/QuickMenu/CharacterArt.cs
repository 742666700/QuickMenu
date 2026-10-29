using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickMenu;

internal static class CharacterArt
{
    private static readonly SemaphoreSlim PreparationGate = new(1);
    public static async Task<CharacterMotion> Prepare(Func<CharacterMotion> load, CancellationToken token)
    {
        await PreparationGate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(load, token).ConfigureAwait(false); }
        finally { PreparationGate.Release(); }
    }
    internal sealed record Patch(string Base, int X, int Y);
    private static Stream Open(string folder, string file) => Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Characters/{folder}/{file}"))?.Stream
        ?? throw new FileNotFoundException($"Missing character resource: {folder}/{file}");
    public static IReadOnlyDictionary<string, Patch> Patches(string folder)
    {
        try
        {
            using var stream = Open(folder, "patches.json");
            return JsonSerializer.Deserialize<Dictionary<string, Patch>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (IOException) { return new Dictionary<string, Patch>(); }
    }
    private static BitmapSource Read(string folder, string file, int height = 0)
    {
        using var stream = Open(folder, file);
        return Decode(stream, height);
    }
    private static BitmapSource Decode(Stream stream, int height)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.StreamSource = stream; image.CacheOption = BitmapCacheOption.OnLoad;
        if (height > 0) image.DecodePixelHeight = height;
        image.EndInit(); image.Freeze();
        return image;
    }
    public static BitmapSource Load(string folder, string file, int height = 0)
    {
        if (!Patches(folder).TryGetValue(file, out var patch)) return Read(folder, file, height);
        if (Patches(folder).ContainsKey(patch.Base)) throw new InvalidDataException("A patch must refer to a full base picture.");
        var basis = Read(folder, patch.Base);
        var change = Read(folder, file);
        if (patch.X < 0 || patch.Y < 0 || patch.X + change.PixelWidth > basis.PixelWidth || patch.Y + change.PixelHeight > basis.PixelHeight)
            throw new InvalidDataException("Patch outside its base canvas.");
        var stride = basis.PixelWidth * 4;
        var pixels = new byte[stride * basis.PixelHeight];
        new FormatConvertedBitmap(basis, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
        var deltaStride = change.PixelWidth * 4;
        var delta = new byte[deltaStride * change.PixelHeight];
        new FormatConvertedBitmap(change, PixelFormats.Bgra32, null, 0).CopyPixels(delta, deltaStride, 0);
        // Replacement includes alpha: drawing source-over would leave erased base pixels behind.
        for (var y = 0; y < change.PixelHeight; y++) Buffer.BlockCopy(delta, y * deltaStride, pixels, (patch.Y + y) * stride + patch.X * 4, deltaStride);
        BitmapSource composed = BitmapSource.Create(basis.PixelWidth, basis.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        if (height > 0 && height != composed.PixelHeight)
        {
            // Use the same WIC decoder as full pictures. TransformedBitmap rounds some widths differently and
            // keeps its 1200px source alive; a detached decode avoids both seams and retained source pixels.
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(composed)); encoder.Save(stream);
            stream.Position = 0; composed = Decode(stream, height);
        }
        composed.Freeze(); return composed;
    }
    public static long Bytes(IEnumerable<BitmapSource> pictures) => pictures.OfType<BitmapSource>().Distinct().Sum(p => (long)p.PixelWidth * p.PixelHeight * 4);
}
