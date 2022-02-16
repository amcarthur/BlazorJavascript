using System.Collections.Immutable;
using System.Text;
using RealGoodApps.BlazorJavascript.CodeGenerator.Models;

namespace RealGoodApps.BlazorJavascript.CodeGenerator
{
    public class CodeGenerator
    {
        private readonly ParsedInfo _parsedInfo;
        private readonly string _outputDirectory;

        public CodeGenerator(
            ParsedInfo parsedInfo,
            string outputDirectory)
        {
            _parsedInfo = parsedInfo;
            _outputDirectory = outputDirectory;
        }

        public void Generate()
        {
            foreach (var interfaceInfo in _parsedInfo.Interfaces)
            {
                var contents = GenerateInterfaceFileContents(interfaceInfo);

                var interfaceOutputPath = Path.Combine(
                    _outputDirectory,
                    $"I{interfaceInfo.Name}.cs");

                if (File.Exists(interfaceOutputPath))
                {
                    throw new Exception($"File already exists: {interfaceOutputPath}");
                }

                File.WriteAllText(interfaceOutputPath, contents);

                var hasPrototype = false;

                foreach (var globalVariable in _parsedInfo.GlobalVariables)
                {
                    if (globalVariable.InlineInterface == null)
                    {
                        continue;
                    }

                    foreach (var property in globalVariable.InlineInterface.Properties)
                    {
                        if (property.Name != "prototype"
                            || property.Type.Single == null
                            || property.Type.Single.Name != interfaceInfo.Name)
                        {
                            continue;
                        }

                        hasPrototype = true;
                        break;
                    }
                }

                if (hasPrototype)
                {
                    var prototypeContents = GeneratePrototypeFileContents(interfaceInfo);

                    var prototypeOutputPath = Path.Combine(
                        _outputDirectory,
                        $"{interfaceInfo.Name}Prototype.cs");

                    if (File.Exists(prototypeOutputPath))
                    {
                        throw new Exception($"File already exists: {prototypeOutputPath}");
                    }

                    File.WriteAllText(prototypeOutputPath, prototypeContents);
                }
            }

            // FIXME: Right now, we know the globalThis is a `Window`, but we might not want to assume this
            //        in the future, especially if this code is used to generate bindings for libraries.
            var windowInterface = _parsedInfo.Interfaces.First(interfaceInfo => interfaceInfo.Name == "Window");
            var allWindowProperties = GetPropertiesFromInterfaceRecursively(windowInterface);
            var allWindowGetters = GetGetAccessorsFromInterfaceRecursively(windowInterface);

            foreach (var globalVariableInfo in _parsedInfo.GlobalVariables)
            {
                // HACK: Let's exclude anything that was already defined in the `Window` interface.
                if (allWindowProperties.Any(property => property.Name == globalVariableInfo.Name)
                    || allWindowGetters.Any(getAccessor => getAccessor.Name == globalVariableInfo.Name))
                {
                    continue;
                }

                var contents = GenerateGlobalVariableFileContents(globalVariableInfo);

                var globalVariableOutputPath = Path.Combine(
                    _outputDirectory,
                    $"{globalVariableInfo.Name}Global.cs");

                if (File.Exists(globalVariableOutputPath))
                {
                    throw new Exception($"File already exists: {globalVariableOutputPath}");
                }

                File.WriteAllText(globalVariableOutputPath, contents);
            }
        }

        private string GenerateInterfaceFileContents(InterfaceInfo interfaceInfo)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("/// <auto-generated />");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.BuiltIns;");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.GlobalVariables;");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.Prototypes;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("namespace RealGoodApps.BlazorJavascript.Interop.Interfaces");
            stringBuilder.AppendLine("{");

            stringBuilder.Append($"{Indent(1)}public interface I{interfaceInfo.Name}");

            stringBuilder.Append(ExtractTypeParametersString(interfaceInfo));

            var extendsList = interfaceInfo.ExtendsList
                .Select(extendTypeInfo => extendTypeInfo)
                .Where(extendTypeInfo => extendTypeInfo.Single != null)
                .Select(GetRenderedTypeName)
                .Append("IJSObject")
                .ToImmutableList();
            stringBuilder.Append($" : {string.Join(", ", extendsList)}");
            stringBuilder.Append(Environment.NewLine);

            stringBuilder.AppendLine(Indent(1) + "{");

            var methods = GetMethodsFromInterface(interfaceInfo, false, ImmutableList<InterfaceInfo>.Empty);

            foreach (var (_, methodInfo) in methods)
            {
                // FIXME: It would be nice to carry over any comments from the TypeScript definitions.
                stringBuilder.Append(Indent(2));
                RenderMethodBeginning(stringBuilder, methodInfo, null);
                stringBuilder.Append(';');
                stringBuilder.Append(Environment.NewLine);
            }

            stringBuilder.AppendLine(Indent(1) + "}");
            stringBuilder.AppendLine("}");

            return stringBuilder.ToString();
        }

        private static string ExtractTypeParametersString(InterfaceInfo interfaceInfo)
        {
            var stringBuilder = new StringBuilder();

            if (interfaceInfo.ExtractTypeParametersResult.TypeParameters.Any())
            {
                stringBuilder.Append('<');

                stringBuilder.Append(string.Join(", ", interfaceInfo.ExtractTypeParametersResult.TypeParameters
                    .Select(typeParameter => typeParameter.Name)));

                stringBuilder.Append('>');
            }

            return stringBuilder.ToString();
        }

        private void RenderMethodBeginning(
            StringBuilder stringBuilder,
            MethodInfo methodInfo,
            InterfaceInfo? prefixInterfaceInfo)
        {
            stringBuilder.Append(GetRenderedTypeName(methodInfo.ReturnType));
            stringBuilder.Append(' ');

            if (prefixInterfaceInfo != null)
            {
                var typeParametersString = ExtractTypeParametersString(prefixInterfaceInfo);
                stringBuilder.Append($"I{prefixInterfaceInfo.Name}{typeParametersString}");
                stringBuilder.Append('.');
            }

            stringBuilder.Append(methodInfo.GetNameForCSharp());

            stringBuilder.Append('(');

            var isFirst = true;

            foreach (var parameterInfo in methodInfo.Parameters)
            {
                if (!isFirst)
                {
                    stringBuilder.Append(", ");
                }

                stringBuilder.Append(GetRenderedTypeName(parameterInfo.Type));
                stringBuilder.Append(' ');
                stringBuilder.Append(parameterInfo.GetNameForCSharp());
                isFirst = false;
            }

            stringBuilder.Append(')');
        }

        private ImmutableList<(InterfaceInfo interfaceInfo, MethodInfo MethodInfo)> GetMethodsFromInterface(
            InterfaceInfo interfaceInfo,
            bool recursive,
            ImmutableList<InterfaceInfo> alreadyProcessedInterfaces)
        {
            var methods = new List<(InterfaceInfo interfaceInfo, MethodInfo MethodInfo)>();

            if (recursive)
            {
                foreach (var extendTypeInfo in interfaceInfo.ExtendsList)
                {
                    if (extendTypeInfo.Single == null)
                    {
                        continue;
                    }

                    var extendInterfaceInfo = _parsedInfo.Interfaces.FirstOrDefault(i => i.Name == extendTypeInfo.Single.Name);

                    if (extendInterfaceInfo == null
                        || alreadyProcessedInterfaces.Any(i => i.Name == extendInterfaceInfo.Name))
                    {
                        continue;
                    }

                    alreadyProcessedInterfaces = alreadyProcessedInterfaces.Add(extendInterfaceInfo);
                    methods.AddRange(GetMethodsFromInterface(extendInterfaceInfo, true, alreadyProcessedInterfaces));
                }
            }

            foreach (var methodInfo in interfaceInfo.Body.Methods)
            {
                // FIXME: We are skipping any methods that are not simple enough for a 1 to 1 translation.
                //        For example, nothing with generics, union types, intersection types, or function parameters.
                if (methodInfo.ExtractTypeParametersResult.TypeParameters.Any())
                {
                    continue;
                }

                if (IsFinalTypeSimpleEnoughToRender(methodInfo.ReturnType))
                {
                    continue;
                }

                if (methodInfo.Parameters.Any(parameterInfo => IsFinalTypeSimpleEnoughToRender(parameterInfo.Type)))
                {
                    continue;
                }

                methods.Add((interfaceInfo, methodInfo));
            }

            return methods.ToImmutableList();
        }

        private ImmutableList<PropertyInfo> GetPropertiesFromInterfaceRecursively(InterfaceInfo interfaceInfo)
        {
            var allProperties = new List<PropertyInfo>();

            foreach (var extendInfo in interfaceInfo.ExtendsList)
            {
                if (extendInfo.Single == null)
                {
                    continue;
                }

                var extendInterfaceInfo = _parsedInfo.Interfaces.FirstOrDefault(i => i.Name == extendInfo.Single.Name);

                if (extendInterfaceInfo == null)
                {
                    continue;
                }

                allProperties.AddRange(GetPropertiesFromInterfaceRecursively(extendInterfaceInfo));
            }

            allProperties.AddRange(interfaceInfo.Body.Properties);
            return allProperties.ToImmutableList();
        }

        private ImmutableList<GetAccessorInfo> GetGetAccessorsFromInterfaceRecursively(InterfaceInfo interfaceInfo)
        {
            var allGetAccessors = new List<GetAccessorInfo>();

            foreach (var extendInfo in interfaceInfo.ExtendsList)
            {
                if (extendInfo.Single == null)
                {
                    continue;
                }

                var extendInterfaceInfo = _parsedInfo.Interfaces.FirstOrDefault(i => i.Name == extendInfo.Single.Name);

                if (extendInterfaceInfo == null)
                {
                    continue;
                }

                allGetAccessors.AddRange(GetGetAccessorsFromInterfaceRecursively(extendInterfaceInfo));
            }

            allGetAccessors.AddRange(interfaceInfo.Body.GetAccessors);
            return allGetAccessors.ToImmutableList();
        }

        private string GenerateGlobalVariableFileContents(GlobalVariableInfo globalVariableInfo)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("/// <auto-generated />");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.Interfaces;");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.Prototypes;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("namespace RealGoodApps.BlazorJavascript.Interop.GlobalVariables");
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine(Indent(1) + $"public class {globalVariableInfo.Name}Global");
            stringBuilder.AppendLine(Indent(1) + "{");
            stringBuilder.AppendLine(Indent(1) + "}");
            stringBuilder.AppendLine("}");

            return stringBuilder.ToString();
        }

        private static string Indent(int levels)
        {
            var indentationBuilder = new StringBuilder();

            for (int level = 1; level <= levels; level++)
            {
                indentationBuilder.Append("    ");
            }

            return indentationBuilder.ToString();
        }

        private string GeneratePrototypeFileContents(InterfaceInfo interfaceInfo)
        {
            var stringBuilder = new StringBuilder();

            var typeParametersString = ExtractTypeParametersString(interfaceInfo);

            stringBuilder.AppendLine("/// <auto-generated />");
            stringBuilder.AppendLine("using System;");
            stringBuilder.AppendLine("using Microsoft.JSInterop;");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.BuiltIns;");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.Extensions;");
            stringBuilder.AppendLine("using RealGoodApps.BlazorJavascript.Interop.Interfaces;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("namespace RealGoodApps.BlazorJavascript.Interop.Prototypes");
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine($"{Indent(1)}public class {interfaceInfo.Name}Prototype{typeParametersString} : I{interfaceInfo.Name}{typeParametersString}, IJSObject");
            stringBuilder.AppendLine(Indent(1) + "{");

            stringBuilder.AppendLine(Indent(2) + $"public {interfaceInfo.Name}Prototype(IJSInProcessRuntime jsInProcessRuntime, IJSObjectReference jsObjectReference)");
            stringBuilder.AppendLine(Indent(2) + "{");
            stringBuilder.AppendLine(Indent(3) + "Runtime = jsInProcessRuntime;");
            stringBuilder.AppendLine(Indent(3) + "ObjectReference = jsObjectReference;");
            stringBuilder.AppendLine(Indent(2) + "}");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine(Indent(2) + "public IJSInProcessRuntime Runtime { get; }");
            stringBuilder.AppendLine(Indent(2) + "public IJSObjectReference ObjectReference { get; }");

            var methods = GetMethodsFromInterface(interfaceInfo, true, ImmutableList<InterfaceInfo>.Empty);

            foreach (var (methodInterfaceInfo, methodInfo) in methods)
            {
                // FIXME: It would be nice to carry over any comments from the TypeScript definitions.
                stringBuilder.Append(Indent(2));
                RenderMethodBeginning(stringBuilder, methodInfo, methodInterfaceInfo);
                stringBuilder.Append(Environment.NewLine);
                stringBuilder.Append(Indent(2) + "{");
                stringBuilder.Append(Environment.NewLine);

                var parametersString = string.Join(", ", methodInfo.Parameters.Select(p => p.GetNameForCSharp()));
                var parametersPrefix = string.IsNullOrWhiteSpace(parametersString) ? string.Empty : ", ";
                var returnRenderedTypeName = GetRenderedTypeName(methodInfo.ReturnType);

                stringBuilder.AppendLine(Indent(3) + $"var propertyObj = this.GetPropertyOfObject(\"{methodInfo.Name}\");");
                stringBuilder.AppendLine(Indent(3) + "var propertyAsFunction = propertyObj as JSFunction;");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine(Indent(3) + "if (propertyAsFunction == null)");
                stringBuilder.AppendLine(Indent(3) + "{");
                stringBuilder.AppendLine(Indent(4) + "throw new InvalidCastException(\"Something went wrong!\");");
                stringBuilder.AppendLine(Indent(3) + "}");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine(Indent(3) + $"var result = propertyAsFunction.Invoke(this{parametersPrefix}{parametersString});");

                if (returnRenderedTypeName != "void")
                {
                    stringBuilder.AppendLine(Indent(3) + $"var resultAsType = result as {returnRenderedTypeName};");
                    stringBuilder.AppendLine();
                    stringBuilder.AppendLine(Indent(3) + "if (resultAsType == null)");
                    stringBuilder.AppendLine(Indent(3) + "{");
                    stringBuilder.AppendLine(Indent(4) + "throw new InvalidCastException(\"Return value is no good.\");");
                    stringBuilder.AppendLine(Indent(3) + "}");
                    stringBuilder.AppendLine();
                    stringBuilder.AppendLine(Indent(3) + "return resultAsType;");
                }

                stringBuilder.Append(Indent(2) + "}");
                stringBuilder.Append(Environment.NewLine);
            }

            stringBuilder.AppendLine(Indent(1) + "}");
            stringBuilder.AppendLine("}");

            return stringBuilder.ToString();
        }

        private bool IsFinalTypeSimpleEnoughToRender(TypeInfo parameterInfoType)
        {
            // FIXME: Eventually, this method shouldn't really exist. It is just used to prevent us from having to handle complex type cases right now.
            var finalTypeInfo = ProcessTypeAliases(parameterInfoType);

            return finalTypeInfo.Single == null
                   || finalTypeInfo.Single.IsUnhandled
                   || finalTypeInfo.Single.TypeArguments.Any()
                   || string.IsNullOrWhiteSpace(finalTypeInfo.Single.Name);
        }

        private string GetRenderedTypeName(TypeInfo typeInfo)
        {
            var finalTypeInfo = ProcessTypeAliases(typeInfo);

            var singleTypeInfo = finalTypeInfo.Single;

            if (singleTypeInfo == null)
            {
                throw new Exception("The type name can not be rendered properly due to complexity.");
            }

            var fullName = new StringBuilder();
            fullName.Append(singleTypeInfo.GetNameForCSharp(_parsedInfo.Interfaces));

            var typeArguments = singleTypeInfo
                .TypeArguments
                .Select(typeArgument => typeArgument.Single)
                .WhereNotNull()
                .ToImmutableList();

            if (typeArguments.Any())
            {
                fullName.Append('<');
                fullName.Append(string.Join(",", typeArguments.Select(typeArgument => typeArgument.GetNameForCSharp(_parsedInfo.Interfaces))));
                fullName.Append('>');
            }
            else
            {
                // There is a possibility that the type we are rendering is actually an interface that has one or more default type parameters.
                var typeAsInterface =
                    _parsedInfo.Interfaces.FirstOrDefault(interfaceInfo => interfaceInfo.Name == singleTypeInfo.Name);

                if (typeAsInterface != null && typeAsInterface.ExtractTypeParametersResult.TypeParameters.Any())
                {
                    fullName.Append('<');

                    foreach (var typeParameter in typeAsInterface.ExtractTypeParametersResult.TypeParameters)
                    {
                        fullName.Append(typeParameter.Default == null
                            ? "IJSObject"
                            : GetRenderedTypeName(typeParameter.Default));
                    }

                    fullName.Append('>');
                }
            }

            return fullName.ToString();
        }

        private TypeInfo ProcessTypeAliases(TypeInfo typeInfo)
        {
            // Anything that looks like a single type is a candidate for being an alias.
            if (typeInfo.Single == null)
            {
                return typeInfo;
            }

            while (true)
            {
                // FIXME: We are assuming that you can only type alias the most simple case for now.
                var typeAlias = _parsedInfo.TypeAliases
                    .FirstOrDefault(typeAlias => typeInfo.Single != null && typeAlias.Name == typeInfo.Single.Name);

                if (typeAlias == null)
                {
                    return typeInfo;
                }

                typeInfo = typeAlias.AliasType;
            }
        }
    }
}
