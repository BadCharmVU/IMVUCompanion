using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MakeIcon <input.png> <output.ico>");
    return 1;
}

string pngPath = Path.GetFullPath(args[0]);
string icoPath = Path.GetFullPath(args[1]);
if (!File.Exists(pngPath))
{
    Console.Error.WriteLine("Input not found: " + pngPath);
    return 1;
}

using var src = new Bitmap(pngPath);
int[] sizes = [16, 32, 48, 256];
using var ms = new MemoryStream();
using var writer = new BinaryWriter(ms);

writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);

var imageData = new byte[sizes.Length][];
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    using var resized = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(resized))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, s, s);
    }
    using var pngMs = new MemoryStream();
    resized.Save(pngMs, ImageFormat.Png);
    imageData[i] = pngMs.ToArray();
}

int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    writer.Write((byte)(s == 256 ? 0 : (byte)s));
    writer.Write((byte)(s == 256 ? 0 : (byte)s));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)imageData[i].Length);
    writer.Write((uint)offset);
    offset += imageData[i].Length;
}

foreach (var chunk in imageData)
    writer.Write(chunk);

Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
File.WriteAllBytes(icoPath, ms.ToArray());
Console.WriteLine("Wrote " + icoPath);
return 0;