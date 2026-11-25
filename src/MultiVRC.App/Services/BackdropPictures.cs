using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class BackdropPictures(LauncherState state)
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    public string FolderOf(Instance instance) => Path.Combine(state.Paths.InstanceDir(instance.Id), "backgrounds");

    private string CacheFolder => Path.Combine(state.Paths.Root, "background-cache");

    public static string VRChatFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VRChat");

    public List<string> Own(Instance instance) => Find(FolderOf(instance), recursive: false).OrderBy(File.GetCreationTimeUtc).ToList();

    public List<string> VRChatPhotos() => Find(VRChatFolder, recursive: true).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();

    public List<string> For(Instance instance) => instance.BackgroundMode switch
    {
        BackgroundMode.Pictures => Own(instance),
        BackgroundMode.VRChatPhotos => VRChatPhotos(),
        _ => [],
    };

    private static IEnumerable<string> Find(string folder, bool recursive)
    {
        if (!Directory.Exists(folder)) return [];
        try
        {
            return Directory.EnumerateFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch (Exception) { return []; }
    }

    public void Add(Instance instance, IEnumerable<string> paths)
    {
        var folder = FolderOf(instance);
        Directory.CreateDirectory(folder);
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var target = Path.Combine(folder, name + ext);
            for (var n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{name} ({n}){ext}");
            File.Copy(path, target);
        }
    }

    public void Remove(Instance instance, string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) == Path.GetFullPath(FolderOf(instance)) && File.Exists(path)) File.Delete(path);
    }

    public bool MigrateOldPicture(Instance instance)
    {
        if (instance.BackgroundFile is not { } file) return false;
        var old = Path.Combine(state.Paths.InstanceDir(instance.Id), file);
        if (File.Exists(old))
        {
            Directory.CreateDirectory(FolderOf(instance));
            File.Move(old, Path.Combine(FolderOf(instance), file), overwrite: true);
            if (instance.BackgroundMode == BackgroundMode.Server) instance.BackgroundMode = BackgroundMode.Pictures;
        }
        instance.BackgroundFile = null;
        return true;
    }

    public Task<string?> CssAsync(string path) => Task.Run(() => (Scaled(path, 1920) ?? Raw(path)) is { } uri ? $"url(\"{uri}\") center / cover no-repeat" : null);

    public Task<string?> ThumbnailAsync(string path) => Task.Run(() => Scaled(path, 320) ?? Raw(path));

    private static string? Raw(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 8 << 20) return null;
            var type = info.Extension.ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/jpeg" };
            return $"data:{type};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (Exception) { return null; }
    }

    private string? Scaled(string path, int width)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{width}")))[..20];
            var cached = Path.Combine(CacheFolder, key + ".jpg");
            if (!File.Exists(cached))
            {
                Directory.CreateDirectory(CacheFolder);
                using var source = Image.FromFile(path);
                var scale = Math.Min(1.0, (double)width / source.Width);
                var w = Math.Max(1, (int)(source.Width * scale));
                var h = Math.Max(1, (int)(source.Height * scale));
                using var bitmap = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, 0, 0, w, h);
                }
                var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                var temp = cached + ".tmp";
                bitmap.Save(temp, jpeg, parameters);
                File.Move(temp, cached, overwrite: true);
            }
            return "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(cached));
        }
        catch (Exception) { return null; }
    }
}
