using LibCpp2IL;
using LibCpp2IL.Metadata;
using AssetRipper.Primitives;
// Reads a game's IL2CPP code from disk, no game running, and writes the catalogs identity-check reads
// (enums.txt, events.txt, members.tsv) plus types.tsv (every type, its base, its methods).
//   lipo GameAssembly.dylib -thin arm64 -output GA.dylib
//   dotnet run --project tools/metadump -- GA.dylib global-metadata.dat OUTDIR [unity version]
var (bin, meta, outDir) = (args[0], args[1], args[2]);
var unity = args.Length > 3 ? args[3] : "6000.3.18f1";
Directory.CreateDirectory(outDir);
var ok = LibCpp2IlMain.LoadFromFile(bin, meta, UnityVersion.Parse(unity));
Console.WriteLine($"loaded={ok} metadataVersion={LibCpp2IlMain.MetadataVersion}");
var md = LibCpp2IlMain.TheMetadata;
string Asm(Il2CppTypeDefinition t) => Path.GetFileNameWithoutExtension(t.DeclaringAssembly?.Name ?? "?");
Il2CppTypeDefinition Base(Il2CppTypeDefinition t) { try { return t.BaseType?.baseType; } catch { return null; } }
bool Derives(Il2CppTypeDefinition t, string b) { for (var x = Base(t); x != null; x = Base(x)) if (x.FullName == b) return true; return false; }
using (var types = new StreamWriter(Path.Combine(outDir, "types.tsv")))
using (var enums = new StreamWriter(Path.Combine(outDir, "enums.txt")))
using (var events = new StreamWriter(Path.Combine(outDir, "events.txt")))
using (var members = new StreamWriter(Path.Combine(outDir, "members.tsv")))
foreach (var t in md.typeDefs)
{
    var name = t.FullName;
    types.WriteLine($"{name}\t{Asm(t)}\t{Base(t)?.FullName}");
    foreach (var f in t.Fields ?? []) members.WriteLine($"{name}\t{f.Name}");
    foreach (var p in t.Properties ?? []) members.WriteLine($"{name}\t{p.Name}");
    foreach (var m in t.Methods ?? []) types.WriteLine($"  M\t{name}\t{m.Name}");
    if (t.IsEnumType)
    {
        enums.WriteLine($"{name}  [{Asm(t)}]");
        foreach (var f in t.Fields ?? [])
        {
            if (f.Name == "value__") continue;
            object v = null;
            try { v = f.DefaultValue?.Value; } catch { }
            enums.WriteLine($"    {v,10}  {f.Name}");
        }
    }
    if (Derives(t, "SI.Core.EventBase"))
    {
        events.WriteLine($"{name}  [{Asm(t)}]");
        foreach (var p in t.Properties ?? []) events.WriteLine($"    ? {p.Name}");
    }
}
Console.WriteLine("done");
