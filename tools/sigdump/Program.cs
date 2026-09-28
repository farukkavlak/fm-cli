using System.Reflection;

// Prints the real member signatures of an Il2CppInterop wrapper type.
//
// The dump from Il2CppDumper shows the game's own signatures, but what a plugin can actually
// call is whatever the interop generator emitted, and the two differ: private fields may be
// dropped, spans become Il2CppSystem types, generics may vanish. This loads the interop
// assemblies as metadata only, so it needs no game and no runtime.
//
//   dotnet run --project tools/sigdump -- <assembly.dll> <Type.Full.Name> [member ...]

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var interop = Path.Combine(home, "fm26_bep/BepInEx/interop");
var core = Path.Combine(home, "fm26_bep/BepInEx/core");

if (!Directory.Exists(interop))
{
    Console.Error.WriteLine($"interop assemblies not found at {interop} — run the game once so BepInEx generates them");
    return 1;
}

var paths = Directory.GetFiles(interop, "*.dll")
    .Concat(Directory.GetFiles(core, "*.dll"))
    .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"))
    .ToList();

using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths),
    typeof(object).Assembly.GetName().Name);

// --holders <TypeFullName>: which types across every interop assembly expose a member of
// that type. Answers "where do I get one of these at runtime?" authoritatively.
if (args.Length > 1 && args[0] == "--holders")
{
    var wantedType = args[1];
    foreach (var dll in Directory.GetFiles(interop, "*.dll").OrderBy(x => x))
    {
        Assembly a;
        try { a = mlc.LoadFromAssemblyPath(dll); } catch { continue; }

        Type[] types;
        try { types = a.GetTypes(); } catch { continue; }

        foreach (var ty in types)
        {
            PropertyInfo[] props;
            try { props = ty.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.Instance | BindingFlags.Static); }
            catch { continue; }

            foreach (var pr in props)
            {
                if (pr.PropertyType.FullName?.Contains(wantedType, StringComparison.OrdinalIgnoreCase) != true)
                    continue;
                Console.WriteLine($"{Path.GetFileName(dll)}  {ty.FullName}.{pr.Name}  : {Short(pr.PropertyType)}");
            }
        }
    }
    return 0;
}

var asmName = args.Length > 0 ? args[0] : "SI.Bindable.dll";
var typeName = args.Length > 1 ? args[1] : "SI.Bindable.Bindings";
var wanted = args.Length > 2 ? args[2..] : null;

var asmPath = File.Exists(Path.Combine(interop, asmName))
    ? Path.Combine(interop, asmName)
    : Path.Combine(core, asmName);
var asm = mlc.LoadFromAssemblyPath(asmPath);

var t = asm.GetType(typeName);
if (t == null)
{
    Console.WriteLine($"'{typeName}' not found. Near matches:");
    var leaf = typeName.Split('.').Last();
    foreach (var c in asm.GetTypes()
                 .Where(x => x.FullName?.Contains(leaf, StringComparison.OrdinalIgnoreCase) == true)
                 .Take(25))
        Console.WriteLine("  " + c.FullName);
    return 1;
}

Console.WriteLine($"### {t.FullName}  (base: {t.BaseType?.FullName})\n");

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static;

Console.WriteLine("-- fields");
foreach (var f in t.GetFields(All).Where(f => Match(f.Name)))
    Console.WriteLine($"   {(f.IsStatic ? "static " : "")}{Short(f.FieldType)} {f.Name}");

Console.WriteLine("\n-- properties");
foreach (var p in t.GetProperties(All).Where(p => Match(p.Name)))
    Console.WriteLine($"   {Short(p.PropertyType)} {p.Name}");

Console.WriteLine("\n-- methods");
foreach (var m in t.GetMethods(All).Where(m => Match(m.Name)).OrderBy(m => m.Name))
{
    var ps = string.Join(", ", m.GetParameters().Select(p =>
        (p.ParameterType.IsByRef ? "ref " : "") + Short(p.ParameterType) + " " + p.Name));
    Console.WriteLine($"   {(m.IsStatic ? "static " : "")}{Short(m.ReturnType)} {m.Name}({ps})");
}

return 0;

bool Match(string name) => wanted == null ||
                           wanted.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

static string Short(Type ty)
{
    var n = ty.FullName?.TrimEnd('&') ?? ty.Name;
    // Collapse the assembly-qualified generic arguments that make signatures unreadable.
    var i = n.IndexOf("[[", StringComparison.Ordinal);
    if (i >= 0) n = n[..i] + "<" + n[(i + 2)..].Split(',')[0] + ">";
    return n;
}
