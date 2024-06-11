# PadSM (Pad Static Mesh)

Small script that can make a cooked UE4.27 static mesh asset compatible with the game Grounded.

Specifically, the script modifies the .uexp file by inserting the extra bytes the game expects
in each FStaticMeshSection, and then the script updates the export sizes/offsets accordingly
in the .uasset file.

## Requirements

PadSM requires you to have **.NET 8.0 or later installed**. If you don't already have it, you
can [download it here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime)
(select the **Windows Desktop x64** version).

## Usage

```
PadSM.exe /Path/To/StaticMesh.uasset
```

## Example

TODO

## License

PadSM is licensed under Apache License 2.0. It uses the third-party library CUE4Parse, which is also
licensed under Apache License 2.0.
