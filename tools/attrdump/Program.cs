using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// 사용법: attrdump <dll> <TypeFullName...> → 해당 타입 필드의 특성(attribute) 이름과 인자
using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
string AttrName(CustomAttribute a) {
    if (a.Constructor.Kind == HandleKind.MemberReference) {
        var mr = md.GetMemberReference((MemberReferenceHandle)a.Constructor);
        if (mr.Parent.Kind == HandleKind.TypeReference) { var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent); return md.GetString(tr.Name); }
    } else if (a.Constructor.Kind == HandleKind.MethodDefinition) {
        var m = md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor);
        return md.GetString(md.GetTypeDefinition(m.GetDeclaringType()).Name);
    }
    return "?";
}
foreach (var th in md.TypeDefinitions) {
    var t = md.GetTypeDefinition(th);
    var full = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
    if (!args.Skip(1).Contains(full)) continue;
    Console.WriteLine("T " + full);
    foreach (var fh in t.GetFields()) {
        var f = md.GetFieldDefinition(fh);
        var attrs = f.GetCustomAttributes().Select(h => { var a = md.GetCustomAttribute(h); var blob = md.GetBlobBytes(a.Value); return AttrName(a) + "[" + BitConverter.ToString(blob) + "]"; }).ToList();
        if (attrs.Count > 0) Console.WriteLine("  " + md.GetString(f.Name) + " : " + string.Join(", ", attrs));
    }
}
