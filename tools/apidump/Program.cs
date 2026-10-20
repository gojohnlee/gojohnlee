using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// 사용법: apidump <dll> → 모든 타입의 필드/메서드/프로퍼티 시그니처 이름 덤프
var path = args[0];
using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
string TypeName(TypeDefinitionHandle h) {
    var t = md.GetTypeDefinition(h);
    var name = md.GetString(t.Name);
    var ns = md.GetString(t.Namespace);
    if (t.IsNested) return TypeName(t.GetDeclaringType()) + "+" + name;
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}
foreach (var th in md.TypeDefinitions) {
    var t = md.GetTypeDefinition(th);
    var tn = TypeName(th);
    string baseName = "";
    if (!t.BaseType.IsNil) {
        if (t.BaseType.Kind == HandleKind.TypeReference) { var r = md.GetTypeReference((TypeReferenceHandle)t.BaseType); baseName = md.GetString(r.Namespace) + "." + md.GetString(r.Name); }
        else if (t.BaseType.Kind == HandleKind.TypeDefinition) baseName = TypeName((TypeDefinitionHandle)t.BaseType);
        else baseName = "(generic)";
    }
    Console.WriteLine($"T {tn} : {baseName}");
    foreach (var fh in t.GetFields()) { var f = md.GetFieldDefinition(fh); Console.WriteLine($"  F {(f.Attributes.HasFlag(System.Reflection.FieldAttributes.Static)?"static ":"")}{md.GetString(f.Name)}"); }
    foreach (var mh in t.GetMethods()) { var m = md.GetMethodDefinition(mh); var ps = m.GetParameters().Select(p => md.GetString(md.GetParameter(p).Name)).Where(s=>s!=""); Console.WriteLine($"  M {(m.Attributes.HasFlag(System.Reflection.MethodAttributes.Static)?"static ":"")}{(m.Attributes.HasFlag(System.Reflection.MethodAttributes.Virtual)?"virtual ":"")}{md.GetString(m.Name)}({string.Join(",", ps)})"); }
    foreach (var ph in t.GetProperties()) { var p = md.GetPropertyDefinition(ph); Console.WriteLine($"  P {md.GetString(p.Name)}"); }
}
