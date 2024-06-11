using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
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
    
    private static (ByteAsset, ByteAsset) PadStaticMesh(ByteAsset uasset, ByteAsset uexp)
    {
        var version = new VersionContainer(EGame.GAME_UE4_27);
        
        var uassetArchive = new FByteArchive(uasset.Name, uasset.Bytes, version);
        
        var uexpArchive = new FByteArchive(uexp.Name, uexp.Bytes, version);
        
        var package = new Package(
            uassetArchive,
            uexpArchive,
            (FArchive?)null
        );

        var staticMeshExportIndex = -1;

        for (var i = 0; i < package.ExportMap.Length; i++)
        {
            if (package.ExportMap[i].ClassName == "StaticMesh")
            {
                staticMeshExportIndex = i;
            }
        }

        var staticMesh = (UStaticMesh?)package.GetExport(staticMeshExportIndex);

        var staticMeshSections = new List<FStaticMeshSection>();

        foreach (var lod in staticMesh.RenderData.LODs)
        {
            staticMeshSections.AddRange(lod.Sections);
        }

        var padBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 };

        var uexpBytes = uexp.Bytes.ToList();

        for (var i = 0; i < staticMeshSections.Count; i++)
        {
            var insertionOffset = (int)(staticMeshSections[i].GroundedDataOffset + i * padBytes.Length);
            
            uexpBytes.InsertRange(insertionOffset, padBytes);
        }

        var sizeIncrease = padBytes.Length * staticMeshSections.Count;

        var smObjectExport = package.ExportMap[staticMeshExportIndex];

        var newSerialSize = smObjectExport.SerialSize + sizeIncrease;
        
        var uassetBytes = uasset.Bytes.ToList();
        
        WriteLong(uassetBytes, (int)smObjectExport.SerialSizeOffset, newSerialSize);

        for (var i = staticMeshExportIndex + 1; i < package.ExportMap.Length; i++)
        {
            var objectExport = package.ExportMap[i];
            var serialOffset = objectExport.SerialOffset;
            var newSerialOffset = serialOffset + sizeIncrease;
            WriteLong(uassetBytes, (int)(objectExport.SerialSizeOffset + 8), newSerialOffset);
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

        var (uasset1, uexp1) = PadStaticMesh(uasset, uexp);

        var outputDir = Path.Join(Path.GetDirectoryName(uassetPath), "out");
        Directory.CreateDirectory(outputDir);
        
        File.WriteAllBytes(Path.Join(outputDir, Path.GetFileName(uassetPath)), uasset1.Bytes);
        File.WriteAllBytes(Path.Join(outputDir, Path.GetFileName(uexpPath)), uexp1.Bytes);
    }
}
