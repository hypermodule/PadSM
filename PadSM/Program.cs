using System.Reflection;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace PadSM;

internal record ByteAsset(string Name, byte[] Bytes)
{
    public static ByteAsset Read(string path) => new(path, File.ReadAllBytes(path));
}

public class Program
{
    private static void WriteLong(List<byte> bytes, int offset, long value)
    {
        var longBytes = BitConverter.GetBytes(value);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(longBytes);
        }

        for (var i = 0; i < longBytes.Length; i++)
        {
            bytes[offset + i] = longBytes[i];
        }
    }

    private static TypeMappings GetUsmapForGrounded2()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("PadSM.GenericUE5_4.usmap");
        using var memoryStream = new MemoryStream();
        stream!.CopyTo(memoryStream);
        var data = memoryStream.ToArray();
        var parser = new UsmapParser(data);
        return parser.Mappings!;
    }

    private static Package ParseAsset(ByteAsset uasset, ByteAsset uexp)
    {
        try
        {
            // Try parsing as Grounded asset
            var versions = new VersionContainer(EGame.GAME_UE4_27);
            
            var uassetArchive = new FByteArchive(uasset.Name, uasset.Bytes, versions);
            var uexpArchive = new FByteArchive(uexp.Name, uexp.Bytes, versions);

            return new Package(uassetArchive, uexpArchive, (FArchive?)null);
        }
        catch (ParserException)
        {
            // Try parsing as Grounded 2 asset (needs .usmap file)
            var versions = new VersionContainer(EGame.GAME_UE5_4);
            
            var uassetArchive = new FByteArchive(uasset.Name, uasset.Bytes, versions);
            var uexpArchive = new FByteArchive(uexp.Name, uexp.Bytes, versions);
            var usmap = GetUsmapForGrounded2();
            
            return new Package(uassetArchive, uexpArchive, (FArchive?)null, mappings: usmap, useLazySerialization: false);
        }
    }
    
    private static (ByteAsset, ByteAsset) PadStaticMesh(ByteAsset uasset, ByteAsset uexp)
    {
        var package = ParseAsset(uasset, uexp);

        // Find the StaticMesh export (main export)
        var staticMeshExportIndex = -1;

        for (var i = 0; i < package.ExportMap.Length; i++)
        {
            if (package.ExportMap[i].ClassName == "StaticMesh")
            {
                staticMeshExportIndex = i;
            }
        }

        var staticMesh = (UStaticMesh?)package.GetExport(staticMeshExportIndex);

        // Find its StaticMeshSections
        var staticMeshSections = new List<FStaticMeshSection>();

        foreach (var lod in staticMesh.RenderData.LODs)
        {
            staticMeshSections.AddRange(lod.Sections);
        }

        // Insert padding bytes into each section
        var padBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 };

        var uexpBytes = uexp.Bytes.ToList();

        for (var i = 0; i < staticMeshSections.Count; i++)
        {
            var insertionOffset = (int)(staticMeshSections[i].GroundedDataOffset + i * padBytes.Length);
            
            uexpBytes.InsertRange(insertionOffset, padBytes);
        }

        // Update export table with new size of main export
        var sizeIncrease = padBytes.Length * staticMeshSections.Count;

        var mainExport = package.ExportMap[staticMeshExportIndex];

        var newSerialSize = mainExport.SerialSize + sizeIncrease;
        
        var uassetBytes = uasset.Bytes.ToList();
        
        WriteLong(uassetBytes, (int)mainExport.OffsetOfSerialSize, newSerialSize);

        // Update export table with new offsets for any subsequent exports
        for (var i = staticMeshExportIndex + 1; i < package.ExportMap.Length; i++)
        {
            var objectExport = package.ExportMap[i];
            var serialOffset = objectExport.SerialOffset;
            var newSerialOffset = serialOffset + sizeIncrease;
            WriteLong(uassetBytes, (int)(objectExport.OffsetOfSerialSize + 8), newSerialOffset);
        }
        
        // Update bulk data start offset
        if (package.Summary.BulkDataStartOffset > 0)
        {
            var newBulkDataStartOffset = package.Summary.BulkDataStartOffset + sizeIncrease;
            WriteLong(uassetBytes, (int)package.Summary.OffsetOfBulkDataStartOffset, newBulkDataStartOffset);
        }

        return (new ByteAsset(uasset.Name, uassetBytes.ToArray()), new ByteAsset(uexp.Name, uexpBytes.ToArray()));
    }

    public static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: PadmSM UASSET_FILE");
            Environment.Exit(1);
        }

        var uassetPath = args[0];
        var uexpPath = Path.ChangeExtension(uassetPath, ".uexp");
        
        var uasset = ByteAsset.Read(uassetPath);
        var uexp = ByteAsset.Read(uexpPath);

        var (newUasset, newUexp) = PadStaticMesh(uasset, uexp);
        
        File.Move(uassetPath, Path.ChangeExtension(uassetPath, ".uasset.bak"));
        File.Move(uexpPath, Path.ChangeExtension(uexpPath, ".uexp.bak"));
        
        File.WriteAllBytes(uassetPath, newUasset.Bytes);
        File.WriteAllBytes(uexpPath, newUexp.Bytes);
    }
}
