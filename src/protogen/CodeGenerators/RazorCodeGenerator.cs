using Google.Protobuf.Reflection;
using ProtoBuf;
using ProtoBuf.Reflection;
using RazorLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;

namespace protogen.CodeGenerators
{  
    public class RazorCodeGenerator : CodeGenerator
    {
        public delegate string TypeGenerationDelegate(DescriptorProto proto, int ident = 0);
        public delegate string EnumGenerationDelegate(EnumDescriptorProto proto, int ident = 0);
        public delegate string GetCodeNamespaceDelegate(FileDescriptorProto proto);
        public class ModelBase
        {
            public TypeGenerationDelegate GenerateType { get; set; }
            public EnumGenerationDelegate GenerateEnum { get; set; }

            public GetCodeNamespaceDelegate GetCodeNamespace { get; set; }

            /// <summary>
            /// 对字符串中的连续大写字母序列进行特殊处理。
            /// 例如： "DEFAULT" -> "Default", "LevelFUND" -> "LevelFund"
            /// </summary>
            /// <param name="part">要处理的字符串部分</param>
            /// <returns>处理后的字符串</returns>
            private static string ProcessPart(string part)
            {
                if (string.IsNullOrEmpty(part))
                {
                    return string.Empty;
                }

                var resultBuilder = new StringBuilder();
                for (int i = 0; i < part.Length; i++)
                {
                    if(i == 0 && char.IsLower(part[i]))
                    {
                        resultBuilder.Append(char.ToUpper(part[i]));
                    }
                    // 查找连续两个或以上的大写字母序列
                    else if (char.IsUpper(part[i]) && i + 1 < part.Length && char.IsUpper(part[i + 1]))
                    {
                        int startIndex = i;
                        // 向前查找，直到序列结束
                        while (i + 1 < part.Length && char.IsUpper(part[i + 1]))
                        {
                            i++;
                        }

                        // 提取整个大写序列（例如 "FUND" 或 "DEFAULT"）
                        string upperSequence = part.Substring(startIndex, i - startIndex + 1);

                        // 首字母大写，其余转为小写
                        resultBuilder.Append(char.ToUpper(upperSequence[0]));
                        resultBuilder.Append(upperSequence.Substring(1).ToLower());
                    }
                    else
                    {
                        // 如果不是连续大写序列的一部分，则直接附加字符
                        resultBuilder.Append(part[i]);
                    }
                }

                return resultBuilder.ToString();
            }

            public string ToUpperCamel(string identifier)
            {
                if (string.IsNullOrEmpty(identifier))
                {
                    return identifier;
                }

                var parts = identifier.Split('_');
                var resultBuilder = new StringBuilder();

                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        resultBuilder.Append(ProcessPart(part));
                    }
                }

                return resultBuilder.ToString();
            }
            public string ToLowerCamel(string val)
            {
                if (!string.IsNullOrEmpty(val))
                    return ToUpperCamel(val).Substring(0, 1).ToLower() + val.Substring(1);
                return val;
            }

            public bool TryGetOptionValue<T>(DescriptorProto proto, int fieldNumber, out T value)
            {
                if (proto.Options == null)
                {
                    value = default;
                    return false;
                }
                return Extensible.TryGetValue(proto.Options, fieldNumber, out value);
            }

            WireType GetWireTypeByType(FieldDescriptorProto.Type type)
            {
                WireType wireType = WireType.Varint;
                switch (type)
                {
                    case FieldDescriptorProto.Type.TypeMessage:
                    case FieldDescriptorProto.Type.TypeBytes:
                    case FieldDescriptorProto.Type.TypeString:
                        wireType = WireType.String;
                        break;
                    case FieldDescriptorProto.Type.TypeFloat:
                    case FieldDescriptorProto.Type.TypeFixed32:
                    case FieldDescriptorProto.Type.TypeSfixed32:
                        wireType = WireType.Fixed32;
                        break;
                    case FieldDescriptorProto.Type.TypeDouble:
                    case FieldDescriptorProto.Type.TypeFixed64:
                    case FieldDescriptorProto.Type.TypeSfixed64:
                        wireType = WireType.Fixed64;
                        break;
                }
                return wireType;
            }
            public string MakeTag(FieldDescriptorProto proto, bool pack = true)
            {
                int fieldNumber = proto.Number;
                WireType wireType;
                if (proto.label == FieldDescriptorProto.Label.LabelRepeated)
                {
                    wireType = GetWireTypeByType(proto.type);
                    switch (wireType)
                    {
                        case WireType.String:
                            break;
                        default:
                            if (pack)
                            {
                                //packed repeated field
                                wireType = WireType.String;
                            }
                            break;
                    }
                }
                else
                {
                    wireType = GetWireTypeByType(proto.type);
                }
                return ((uint)(fieldNumber << 3) | (uint)wireType).ToString();
            }

            public bool IsMap(DescriptorProto proto)
            {
                if (proto.FullyQualifiedName.StartsWith($"{GetFullQualifiedName(proto.Parent)}.Map") && proto.Name.EndsWith("Entry"))
                    return true;
                else
                    return false;
            }

            public bool IsFieldMap(FieldDescriptorProto proto)
            {
                if (proto.ResolvedType != null && proto.TypeName.StartsWith($"{GetFullQualifiedName(proto.ResolvedType.Parent)}.Map") && proto.TypeName.EndsWith("Entry"))
                    return true;
                else
                    return false;
            }

            public bool IsRepeated(FieldDescriptorProto proto)
            {
                return proto.label == FieldDescriptorProto.Label.LabelRepeated;
            }
            public bool IsRequired(FieldDescriptorProto proto)
            {
                return proto.label == FieldDescriptorProto.Label.LabelRequired;
            }
            public bool IsOptional(FieldDescriptorProto proto)
            {
                return proto.label == FieldDescriptorProto.Label.LabelOptional;
            }
            public FieldDescriptorProto GetMapFieldType(FieldDescriptorProto proto, bool isKey)
            {
                DescriptorProto type = (DescriptorProto)proto.ResolvedType;
                return isKey ? type.Fields[0] : type.Fields[1];
            }

            public string GetMessageTypeName(DescriptorProto proto)
            {
                (string package, string name) = GetPackageNameAndNamespace(proto, null);
                string res;
                if (package == ".")
                {
                    if (proto.FullyQualifiedName.StartsWith("."))
                        res = name + "." + proto.FullyQualifiedName.Substring(1);
                    else
                        res = name + "." + proto.FullyQualifiedName;
                }
                else
                    res = proto.FullyQualifiedName.Replace(package, name);
                if (res.StartsWith("."))
                    res = res.Substring(1);
                return res;
            }
            public string GetMessageTypeName(FieldDescriptorProto proto, FileDescriptorProto parentFile)
            {
                (string package, string name) = GetPackageNameAndNamespace(proto.ResolvedType, parentFile);
                string res;
                if (package == ".")
                {
                    if (proto.TypeName.StartsWith("."))
                        res = proto.TypeName.Substring(1);
                    else
                        res = proto.TypeName;
                    res = res.Replace(".", ".Types.");
                }
                else
                {
                    res = proto.TypeName.Replace(package, "");
                    if (res.StartsWith("."))
                        res = res.Substring(1);
                    res = res.Replace(".", ".Types.");
                    res = name+ "." + res;
                }
                if (res.StartsWith("."))
                    res = res.Substring(1);
                Console.WriteLine($"GetMessageTypeName0 {res}");

                return res;
            }

            public FileDescriptorSet GetFileDescriptorSet(FileDescriptorProto proto)
            {
                return proto.Parent;
            }

            (string package, string name) GetPackageNameAndNamespace(IType proto, FileDescriptorProto parentFile)
            {
                string package = "";
                string name = "";
                IType cur = proto;
                IType topType = null;
                while (cur != null)
                {
                    if (cur.Parent is FileDescriptorProto)
                    {
                        topType = cur;
                        break;
                    }
                    cur = cur.Parent;
                }
                if (topType != null)
                {
                    FileDescriptorProto fdp = (FileDescriptorProto)topType.Parent;
                    package = fdp.Package;
                    if (parentFile != fdp)
                    {
                        if (GetCodeNamespace != null)
                            name = GetCodeNamespace(fdp);
                        else
                        {
                            name = fdp.Options.CsharpNamespace;
                            if (string.IsNullOrEmpty(name))
                                name = fdp.Parent.DefaultPackage;
                        }
                    }
                }
                if (!package.StartsWith("."))
                    package = "." + package;
                return (package, name);
            }

            string GetFullQualifiedName(IType proto)
            {
                string name = "";
                IType cur = proto;
                while (cur != null)
                {
                    if (cur is DescriptorProto pProto)
                    {
                        if (!string.IsNullOrEmpty(name))
                            name = pProto.Name + "." + name;
                        else
                            name = pProto.Name;
                    }
                    else if (cur is EnumDescriptorProto eProto)
                    {
                        if (!string.IsNullOrEmpty(name))
                            name = eProto.Name + "." + name;
                        else
                            name = eProto.Name;
                    }
                    else if (cur is FileDescriptorProto fProto)
                    {
                        if (!string.IsNullOrEmpty(name))
                            name = fProto.Package + "." + name;
                        else
                            name = fProto.Package;
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                    cur = cur.Parent;
                }
                if (!name.StartsWith("."))
                    name = "." + name;
                return name;
            }
        }
        public class SingleFileModel : ModelBase
        {
            public FileDescriptorProto File { get; set; }
        }
        public class GlobalFileModel : ModelBase
        {
            public FileDescriptorSet Files { get; set; }
        }
        public class OneOfModel
        {
            public string Name { get; set; }
            public List<FieldDescriptorProto> Fields { get; set; }
        }
        public class TypeModel : ModelBase
        {
            public DescriptorProto TypeInfo { get; set; }

            public FileDescriptorProto FileInfo { get; set; }

            public List<OneOfModel> OneOfInfo { get; set; }
            public int CurrentIdent { get; set; }

            public string GetMessageTypeName(FieldDescriptorProto proto)
            {
                return GetMessageTypeName(proto, FileInfo);
            }

            public bool TryGetOptionValue<T>(int fieldNumber, out T value)
            {
                return TryGetOptionValue(TypeInfo, fieldNumber, out value);
            }

            public DescriptorProto TryFindMessageByOptionValue<T>(FileDescriptorProto file, int fieldNumber, T value)
            {
                foreach(var i in file.MessageTypes)
                {
                    var res = TryFindMessageByOptionValueSub(i, fieldNumber, value);
                    if (res != null) return res;
                }
                return null;
            }

            DescriptorProto TryFindMessageByOptionValueSub<T>(DescriptorProto proto, int fieldNumber, T value)
            {
                if (TryGetOptionValue(proto, fieldNumber, out T newVal) && newVal.Equals(value))
                    return proto;
                foreach(var i in proto.NestedTypes)
                {
                    var res = TryFindMessageByOptionValueSub(i, fieldNumber, value);
                    if(res != null) 
                        return res;
                }
                return null;




            }
        }
        public class EnumModel : ModelBase
        {
            public EnumDescriptorProto EnumInfo { get; set; }
            public int CurrentIdent { get; set; }
        }

        RazorLightEngine engine; 
        string templatePath;
        string typeTemplate;
        string enumTemplate;
        string fileTemplate;
        public RazorCodeGenerator()
        {
            engine = new RazorLightEngineBuilder()
            // required to have a default RazorLightProject type,
            // but not required to create a template from string.
            .UseEmbeddedResourcesProject(typeof(SingleFileModel))
            .SetOperatingAssembly(typeof(SingleFileModel).Assembly)
            .UseMemoryCachingProvider()
            .Build();

        }
        public override string Name => nameof(RazorCodeGenerator);

        public override IEnumerable<CodeFile> Generate(FileDescriptorSet set, NameNormalizer normalizer = null, Dictionary<string, string> options = null)
        {
            string extension = null;
            string ignorePackage = null;
            string globalConfigs = null;
            List<(string FileName, string TemplateName, string TemplateContent)> globalTemplates = new List<(string, string, string)>();
            options?.TryGetValue("template_path", out templatePath);
            options?.TryGetValue("file_extension", out extension);
            options?.TryGetValue("ignore_package", out ignorePackage);
            options?.TryGetValue("global_codegen", out globalConfigs);
            if (extension == null)
            {
                extension = "";
            }
            if (!string.IsNullOrEmpty(templatePath) && Directory.Exists(templatePath))
            {
                string filePath = $"{templatePath}/type.tmpl";
                if(File.Exists(filePath))
                {
                    typeTemplate = File.ReadAllText(filePath);
                }
                filePath = $"{templatePath}/enum.tmpl";
                if (File.Exists(filePath))
                {
                    enumTemplate = File.ReadAllText(filePath);
                }
                filePath = $"{templatePath}/file.tmpl";
                if (File.Exists(filePath))
                {
                    fileTemplate = File.ReadAllText(filePath);
                }
            }
            if (!string.IsNullOrEmpty(globalConfigs))
            {
                string[] files = globalConfigs.Split(',');
                foreach (var f in files)
                {
                    string[] kv = f.Split(':');
                    if (kv.Length > 1)
                    {
                        string tf = $"{templatePath}/{kv[1]}";
                        if (File.Exists(tf))
                        {
                            string content = File.ReadAllText(tf);
                            globalTemplates.Add((kv[0], Path.GetFileNameWithoutExtension(kv[1]), content));
                        }
                    }
                }
            }
            List<CodeFile> codes = new List<CodeFile>();
            bool hasTemplate = false;
            if(!string.IsNullOrEmpty(fileTemplate) && !string.IsNullOrEmpty(typeTemplate) && !string.IsNullOrEmpty(enumTemplate))
            {
                hasTemplate= true;
                foreach(var i in set.Files)
                {
                    Console.WriteLine(i);
                    if (i.EnumTypes.Count == 0 && i.MessageTypes.Count == 0)
                        continue;
                    if (!string.IsNullOrEmpty(ignorePackage) && i.Package.StartsWith(ignorePackage))
                        continue;
                    if (i.Options == null)
                        i.Options = new Google.Protobuf.Reflection.FileOptions();
                    if (string.IsNullOrEmpty(i.Options.CsharpNamespace))
                        i.Options.CsharpNamespace = set.DefaultPackage;
                    string path = Path.GetDirectoryName(i.Name);
                    string fn = Path.GetFileNameWithoutExtension(i.Name);
                    codes.Add(new CodeFile(Path.Combine(path, fn + extension), GenerateSingleFileCode(i)));
                }
            }
            foreach(var i in globalTemplates)
            {
                hasTemplate = true;
                string fn = Path.GetFileNameWithoutExtension(i.FileName);
                codes.Add(new CodeFile(fn + extension, GenerateGlobalFileCode(set, i.TemplateName, i.TemplateContent)));
            }

            if (!hasTemplate)
            {
                throw new ArgumentException("Please specify razor template path by using 'template_path' parameter");
            }
            return codes;
        }

        void InitializeModel(ModelBase model)
        {
            model.GenerateType = GenerateTypeCode;
            model.GenerateEnum = GenerateEnumCode;
        }

        FileDescriptorProto FindFileProto(DescriptorProto proto)
        {
            IType cur = proto;
            do
            {
                cur = cur.Parent;
                if (cur is FileDescriptorProto fdp)
                    return fdp;
            } while (cur != null);
            return null;
        }

        TypeModel MakeTypeModel(DescriptorProto proto, int ident)
        {
            TypeModel typeModel = new TypeModel();
            InitializeModel(typeModel);
            typeModel.CurrentIdent = ident;
            typeModel.TypeInfo = proto;
            typeModel.FileInfo = FindFileProto(proto);
            typeModel.OneOfInfo = new List<OneOfModel>();
            bool hasOneOf = false;
            foreach(var i in proto.OneofDecls)
            {
                hasOneOf = true;
                OneOfModel model = new OneOfModel();
                model.Name = i.Name;
                model.Fields = new List<FieldDescriptorProto>();
                typeModel.OneOfInfo.Add(model);
            }
            if (hasOneOf)
            {
                foreach(var i in proto.Fields)
                {
                    if (i.ShouldSerializeOneofIndex())
                    {
                        var model = typeModel.OneOfInfo[i.OneofIndex];
                        model.Fields.Add(i);
                    }
                }
            }
            return typeModel;
        }

        EnumModel MakeEnumModel(EnumDescriptorProto proto, int ident)
        {
            EnumModel enumModel = new EnumModel();
            InitializeModel(enumModel);
            enumModel.CurrentIdent = ident;
            enumModel.EnumInfo = proto;
            return enumModel;
        }

        SingleFileModel MakeSingleFileModel(FileDescriptorProto proto)
        {
            SingleFileModel model = new SingleFileModel();
            InitializeModel(model);
            model.File = proto;
            return model;
        }

        GlobalFileModel MakeGlobalFileModel(FileDescriptorSet set)
        {
            GlobalFileModel model = new GlobalFileModel();
            InitializeModel(model);
            model.Files = set;
            return model;
        }

        string GenerateCode(string templateName, string template, ModelBase model, int ident)
        {
            var task = engine.CompileRenderStringAsync(templateName, template, model);

            try
            {
                task.Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                throw new Exception("Generate failed");
            }
            var res = task.Result;
            if (ident > 0)
            {
                StringReader sr = new StringReader(res);
                StringBuilder final = new StringBuilder();
                while (sr.Peek() > 0)
                {
                    for (int i = 0; i < ident; i++)
                    {
                        final.Append("    ");
                    }
                    final.AppendLine(sr.ReadLine());
                }
                return final.ToString();
            }
            else
                return res;
        }

        string GenerateGlobalFileCode(FileDescriptorSet set, string templateName, string templateContent)
        {
            return GenerateCode(templateName, templateContent!, MakeGlobalFileModel(set), 0);
        }

        string GenerateSingleFileCode(FileDescriptorProto proto)
        {
            return GenerateCode("FileTemplate", fileTemplate!, MakeSingleFileModel(proto), 0);
        }

        string GenerateTypeCode(DescriptorProto proto, int ident = 0)
        {
            return GenerateCode("TypeTemplate", typeTemplate!, MakeTypeModel(proto, ident), ident);
        }

        string GenerateEnumCode(EnumDescriptorProto proto, int ident = 0)
        {
            return GenerateCode("EnumTemplate", enumTemplate!, MakeEnumModel(proto, ident), ident);
        }

        
    }
}
