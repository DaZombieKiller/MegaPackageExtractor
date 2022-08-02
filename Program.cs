using System.Text;
using System.IO.Compression;
using System.Runtime.InteropServices;

unsafe
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Usage: {0} <path to MegaPackage.dat> [output directory]", Path.GetFileNameWithoutExtension(Environment.ProcessPath));
        return;
    }

    if (!File.Exists(args[0]))
    {
        Console.Error.WriteLine("Error: MegaPackage.dat does not exist at given location.");
        return;
    }

    using var input = File.OpenRead(args[0]);
    var gameRoot    = Path.GetDirectoryName(Path.GetDirectoryName(input.Name)) ?? "";

    // User can override destination path instead of implied parent directory of MegaPackage.dat
    if (args.Length > 1)
        gameRoot = args[1];

    MegaPackageHeader header;
    input.ReadExactly(new(&header, sizeof(MegaPackageHeader)));

    if (header.Magic != (('M' << 24) | ('E' << 16) | ('G' << 8) | 'A'))
    {
        Console.Error.WriteLine("Error: Invalid MegaPackage header.");
        return;
    }

    var entries = new MegaPackageEntry[ReadCompactInt(input)];
    var names   = new string[entries.Length];

    for (int i = 0; i < entries.Length; i++)
    {
        names[i] = ReadUnString(input);
        input.ReadExactly(MemoryMarshal.AsBytes(entries.AsSpan(i, 1)));
    }

    var blocks = new MegaPackageBlock[ReadCompactInt(input)];
    input.ReadExactly(MemoryMarshal.AsBytes(blocks.AsSpan()));

    int compressedLength;
    input.ReadExactly(new(&compressedLength, sizeof(int)));

    var origin = input.Position;
    var data   = new byte[blocks.Length * 4096];

    for (int i = 0; i < blocks.Length; i++)
    {
        input.Position = origin + blocks[i].Offset;
        using var ms   = new MemoryStream(blocks[i].Length);
        ms.SetLength(blocks[i].Length);
        input.ReadExactly(ms.GetBuffer(), 0, blocks[i].Length);

        using var ds = new ZLibStream(ms, CompressionMode.Decompress);
        ds.ReadAtLeast(data.AsSpan(4096 * i, 4096), 4096, false);
    }

    for (int i = 0; i < entries.Length; i++)
    {
        var output = GetOutputDirectory(Path.GetExtension(names[i]));
        var folder = Path.Combine(gameRoot, output);
        var path   = Path.Combine(folder, names[i]);
        Directory.CreateDirectory(folder);

        using (var fs = File.OpenWrite(path))
        {
            fs.SetLength(entries[i].Length);
            fs.Write(data, entries[i].Offset, entries[i].Length);
        }

        var fileTime = ((long)entries[i].HighDateTime << 32) | entries[i].LowDateTime;
        File.SetLastWriteTimeUtc(path, DateTime.FromFileTimeUtc(fileTime));
    }
}

static string GetOutputDirectory(string extension)
{
    if (extension.EndsWith("_DFX", StringComparison.OrdinalIgnoreCase))
        return "Sounds";

    return extension.ToUpperInvariant() switch
    {
        ".U"   => "System",
        ".DTX" => "Textures",
        ".DMX" => "SkinMeshes",
        ".DFX" => "Sounds",
        ".CVP" => "Sounds",
        ".DSM" => "StaticMeshes",
        ".DPS" => "Particles",
        ".XML" => "PhysicsAssets",
        ".DCT" => "Music/Vis",
        _      => "System",
    };
}

static int ReadCompactInt(Stream source)
{
    byte b    = (byte)source.ReadByte();
    int sign  = b & 0x80;
    int value = b & 0x3F;

    if ((b & 0x40) != 0)
    {
        int shift = 6;

        do
        {
            if (shift > 27)
                break;

            b      = (byte)source.ReadByte();
            value |= (b & 0x7F) << shift;
            shift += 7;

        } while ((b & 0x80) != 0);
    }

    return sign != 0 ? -value : value;
}

static string ReadUnString(Stream source)
{
    int length = ReadCompactInt(source);
    var buffer = new byte[length];
    source.ReadExactly(buffer);
    var index = Array.IndexOf<byte>(buffer, 0);

    if (index != -1)
        length = index;

    return Encoding.UTF8.GetString(buffer, 0, length);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct MegaPackageHeader
{
    public int Magic;   // 'A', 'G', 'E', 'M'
    public int Version; // 0
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct MegaPackageEntry
{
    public int Unknown0; // Always 0, version?
    public int Offset;
    public int Length;
    public uint HighDateTime;
    public uint LowDateTime;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct MegaPackageBlock
{
    public int Offset;
    public ushort Length;
}
