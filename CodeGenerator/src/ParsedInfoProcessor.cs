using System.Text;
using RealGoodApps.BlazorJavascript.CodeGenerator.Models;
using RealGoodApps.BlazorJavascript.CodeGenerator.Models.Processed;
using RealGoodApps.ValueImmutableCollections;

namespace RealGoodApps.BlazorJavascript.CodeGenerator
{
    public class ParsedInfoProcessor
    {
        private readonly ParsedInfo _parsedInfo;

        public ParsedInfoProcessor(ParsedInfo parsedInfo)
        {
            _parsedInfo = parsedInfo;
        }

        public ProcessedInfo Process()
        {
            var interfaceItems = new List<ProcessedInterfaceInfo>();
            var classItems = new List<ProcessedClassInfo>();

            var allInterfaces = new List<InterfaceInfo>();
            var globalsDefined = GetGlobalsDefinedOutsideOfGlobalThisInterface();

            foreach (var interfaceInfo in _parsedInfo.Interfaces)
            {
                allInterfaces.Add(interfaceInfo);
            }

            foreach (var globalDefined in globalsDefined)
            {
                if (allInterfaces.Any(i => i.Name == globalDefined.InterfaceInfo.Name))
                {
                    continue;
                }

                allInterfaces.Add(globalDefined.InterfaceInfo);
            }

            foreach (var interfaceInfo in allInterfaces)
            {
                interfaceItems.Add(ProcessInterface(interfaceInfo, globalsDefined));
                classItems.Add(ProcessClass(interfaceInfo, globalsDefined));
            }

            return new ProcessedInfo(
                new ProcessedInterfacesInfo(interfaceItems.ToValueImmutableList()),
                new ProcessedClassesInfo(classItems.ToValueImmutableList()));
        }

        private string GetRenderedTypeName(
            TypeInfo typeInfo,
            SymbolParent parent,
            ValueImmutableList<TypeParameter>? symbolTypeParameters,
            TypeInfo? fallbackType)
        {
            if (typeInfo.Array != null)
            {
                var fullArrayName = new StringBuilder();
                fullArrayName.Append("IJSArray");

                fullArrayName.Append('<');
                fullArrayName.Append(GetRenderedTypeName(
                    typeInfo.Array,
                    parent,
                    symbolTypeParameters,
                    null));
                fullArrayName.Append('>');

                return fullArrayName.ToString();
            }

            if (typeInfo.Function != null)
            {
                // FIXME: Add variants for parameter types and return type.
                return "JSFunction";
            }

            if (typeInfo.Single == null)
            {
                if (fallbackType == null)
                {
                    return "IJSObject";
                }

                return GetRenderedTypeName(fallbackType, parent, symbolTypeParameters, null);
            }

            return GetRenderedSingleTypeName(
                typeInfo.Single,
                parent,
                symbolTypeParameters);
        }

        private string GetRenderedSingleTypeName(
            SingleTypeInfo singleTypeInfo,
            SymbolParent parent,
            ValueImmutableList<TypeParameter>? symbolTypeParameters)
        {
            if (singleTypeInfo.Name == null
                || singleTypeInfo.Name == "any"
                || singleTypeInfo.Name == "void"
                || singleTypeInfo.Name == "null")
            {
                return "IJSObject";
            }

            if (singleTypeInfo.Name == "undefined")
            {
                return "JSUndefined";
            }

            if (singleTypeInfo.Name == "boolean")
            {
                return "JSBoolean";
            }

            if (singleTypeInfo.Name == "string")
            {
                return "JSString";
            }

            if (singleTypeInfo.Name == "number")
            {
                return "JSNumber";
            }

            var singleTypeInterface = _parsedInfo.Interfaces.FirstOrDefault(interfaceInfo => interfaceInfo.Name == singleTypeInfo.Name);

            if (singleTypeInterface == null)
            {
                if (symbolTypeParameters != null
                    && symbolTypeParameters.Any(symbolTypeParameter => singleTypeInfo.Name == symbolTypeParameter.Name))
                {
                    return singleTypeInfo.Name;
                }

                for (var typeParameterIndex = 0;
                     typeParameterIndex < parent.OwnerInterface.ExtractTypeParametersResult.TypeParameters.Count;
                     typeParameterIndex++)
                {
                    var typeParameter = parent.OwnerInterface.ExtractTypeParametersResult.TypeParameters[typeParameterIndex];

                    if (singleTypeInfo.Name != typeParameter.Name)
                    {
                        continue;
                    }

                    if (parent.Parent == null || parent.TypeArguments == null)
                    {
                        return singleTypeInfo.Name;
                    }

                    return GetRenderedTypeName(
                        parent.TypeArguments[typeParameterIndex],
                        parent.Parent,
                        null, // We intentionally drop the symbol here, since it is only useful for type parameters on a symbol.
                        null);
                }

                // FIXME: This might actually be better handled with some sort of support for other type mixins.
                //        This is an example of a type that led to here:
                //            type Record<K extends keyof any, T> = {
                //                [P in K]: T;
                //            };
                return "IJSObject";
            }

            var fullName = new StringBuilder();

            fullName.Append($"I{singleTypeInterface.Name}");

            var typeArguments = singleTypeInfo
                .TypeArguments
                .WhereNotNull()
                .ToValueImmutableList();

            if (typeArguments.Any())
            {
                fullName.Append('<');
                fullName.Append(string.Join(",", typeArguments.Select((typeArgument, typeArgumentIndex) => GetRenderedTypeName(
                    typeArgument,
                    parent,
                    symbolTypeParameters,
                    singleTypeInterface.ExtractTypeParametersResult.TypeParameters[typeArgumentIndex].Constraint))));
                fullName.Append('>');
            }

            return fullName.ToString();
        }

        private ProcessedInterfaceInfo ProcessInterface(
            InterfaceInfo interfaceInfo,
            ValueImmutableList<GlobalDefinedOutsideOfGlobalThisInterface> globalsDefinedOutside)
        {
            var genericCarrots = string.Empty;

            if (interfaceInfo.ExtractTypeParametersResult.TypeParameters.Any())
            {
                genericCarrots = $"<{string.Join(",", interfaceInfo.ExtractTypeParametersResult.TypeParameters.Select(_ => string.Empty))}>";
            }

            var parent = SymbolParent.Root(interfaceInfo);

            var processedTypeParameters = ProcessTypeParameters(
                interfaceInfo.ExtractTypeParametersResult.TypeParameters,
                parent);

            var processedExtendsList = new List<ProcessedExtendsChainItemInfo>();

            foreach (var extendItem in interfaceInfo.ExtendsList)
            {
                processedExtendsList.Add(new ProcessedExtendsChainItemInfo(
                    new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        extendItem,
                        parent,
                        null,
                        null))));
            }

            // We only want symbols that come from the interface body, ignoring the extends list.
            var symbols = GetSymbolsFromInterface(
                parent,
                false);

            var processedSymbols = GetProcessedSymbols(
                symbols,
                parent,
                globalsDefinedOutside);

            return new ProcessedInterfaceInfo(
                $"I{interfaceInfo.Name}",
                processedTypeParameters,
                $"{interfaceInfo.Name}{genericCarrots}",
                new ProcessedExtendsChainInfo(processedExtendsList.ToValueImmutableList()),
                processedSymbols.Constructors,
                processedSymbols.Methods,
                processedSymbols.Properties,
                processedSymbols.Indexers);
        }

        private ProcessedClassInfo ProcessClass(
            InterfaceInfo interfaceInfo,
            ValueImmutableList<GlobalDefinedOutsideOfGlobalThisInterface> globalsDefinedOutside)
        {
            var root = SymbolParent.Root(interfaceInfo);

            var processedTypeParameters = ProcessTypeParameters(
                interfaceInfo.ExtractTypeParametersResult.TypeParameters,
                root);

            var symbols = GetSymbolsFromInterface(
                root,
                true);

            var groupedSymbols = symbols
                .GroupBy(s => s.Parent)
                .ToValueImmutableList();

            var implementations = new List<ProcessedClassImplementationInfo>();

            foreach (var symbolGrouping in groupedSymbols)
            {
                var processedSymbols = GetProcessedSymbols(
                    symbols,
                    symbolGrouping.Key,
                    globalsDefinedOutside);

                var prefixStringBuilder = new StringBuilder();

                prefixStringBuilder.Append($"I{symbolGrouping.Key.OwnerInterface.Name}");

                if (symbolGrouping.Key.OwnerInterface.ExtractTypeParametersResult.TypeParameters.Any())
                {
                    prefixStringBuilder.Append('<');

                    if (symbolGrouping.Key.TypeArguments != null)
                    {
                        prefixStringBuilder.Append(string.Join(", ", symbolGrouping.Key
                            .TypeArguments
                            .Select(typeArgument => GetRenderedTypeName(
                                typeArgument,
                                symbolGrouping.Key,
                                null,
                                null))));
                    }
                    else
                    {
                        prefixStringBuilder.Append(string.Join(", ", symbolGrouping.Key.OwnerInterface.ExtractTypeParametersResult.TypeParameters
                            .Select(typeParameter => typeParameter.Name)));
                    }

                    prefixStringBuilder.Append('>');
                }

                implementations.Add(new ProcessedClassImplementationInfo(
                    prefixStringBuilder.ToString(),
                    new ProcessedConstructorsInfo(processedSymbols.Constructors.Items.ToValueImmutableList()),
                    new ProcessedMethodsInfo(processedSymbols.Methods.Items.ToValueImmutableList()),
                    new ProcessedPropertiesInfo(processedSymbols.Properties.Items.ToValueImmutableList()),
                    new ProcessedIndexersInfo(processedSymbols.Indexers.Items.ToValueImmutableList())));
            }

            var deduplicatedImplementations = DeduplicateImplementations(implementations);

            return new ProcessedClassInfo(
                $"{interfaceInfo.Name}",
                $"I{interfaceInfo.Name}",
                processedTypeParameters,
                new ProcessedClassImplementationsInfo(deduplicatedImplementations.ToValueImmutableList()));
        }

        private ValueImmutableList<ProcessedClassImplementationInfo> DeduplicateImplementations(
            List<ProcessedClassImplementationInfo> implementations)
        {
            var finalImplementations = new List<ProcessedClassImplementationInfo>();

            foreach (var implementation in implementations)
            {
                if (finalImplementations.Any(i => i.Prefix == implementation.Prefix))
                {
                    continue;
                }

                finalImplementations.Add(implementation);
            }

            return finalImplementations.ToValueImmutableList();
        }

        public sealed record ProcessedSymbols(
            ProcessedConstructorsInfo Constructors,
            ProcessedMethodsInfo Methods,
            ProcessedPropertiesInfo Properties,
            ProcessedIndexersInfo Indexers);

        private ProcessedSymbols GetProcessedSymbols(
            ValueImmutableList<SymbolInfo> symbols,
            SymbolParent parent,
            ValueImmutableList<GlobalDefinedOutsideOfGlobalThisInterface> globalsDefinedOutside)
        {
            var constructorSymbols = symbols
                .Where(s => s.Parent == parent && s.ConstructorInfo != null)
                .GroupBy(s => new
                {
                    ParameterCount = s.ConstructorInfo?.Parameters.Count ?? 0,
                    TypeParameterCount = s.ConstructorInfo?.ExtractTypeParametersResult.TypeParameters.Count ?? 0,
                })
                .ToValueImmutableList();

            var processedConstructors = new List<ProcessedConstructorInfo>();

            foreach (var overloadGrouping in constructorSymbols)
            {
                var individualSymbols = overloadGrouping
                    .Select(s => s.ConstructorInfo)
                    .WhereNotNull()
                    .ToValueImmutableList();

                // Figure out a way to make all the overloads have the same conforming parameter types.
                var conformingParameters = new List<ParameterInfo>();

                for (var parameterIndex = 0; parameterIndex < overloadGrouping.Key.ParameterCount; parameterIndex++)
                {
                    var parameterConforms = individualSymbols
                        .Select(s => s.Parameters[parameterIndex].Type)
                        .ToList()
                        .DistinctSafeSlow()
                        .ToValueImmutableList()
                        .Count == 1;

                    // FIXME: This might lead to some odd parameter names if there is a situation where the names do not conform, but the types do.
                    if (parameterConforms)
                    {
                        conformingParameters.Add(individualSymbols.First().Parameters[parameterIndex]);
                    }
                    else
                    {
                        conformingParameters.Add(individualSymbols.First().Parameters[parameterIndex] with
                        {
                            Type = TypeInfo.AnyType,
                        });
                    }
                }

                var conformingTypeParameters = new List<TypeParameter>();

                for (var typeParameterIndex = 0; typeParameterIndex < overloadGrouping.Key.TypeParameterCount; typeParameterIndex++)
                {
                    var constraintConforms = individualSymbols
                        .Select(s => s.ExtractTypeParametersResult.TypeParameters[typeParameterIndex].Constraint)
                        .ToList()
                        .DistinctSafeSlow()
                        .ToValueImmutableList()
                        .Count == 1;

                    // FIXME: This might lead to some odd parameter names if there is a situation where the names do not conform, but the types do.
                    if (constraintConforms)
                    {
                        conformingTypeParameters.Add(individualSymbols.First().ExtractTypeParametersResult.TypeParameters[typeParameterIndex]);
                    }
                    else
                    {
                        conformingTypeParameters.Add(individualSymbols.First().ExtractTypeParametersResult.TypeParameters[typeParameterIndex] with
                        {
                            Constraint = null,
                        });
                    }
                }

                var returnTypeConforms = individualSymbols
                    .Select(s => s.ReturnType)
                    .DistinctSafeSlow()
                    .ToValueImmutableList()
                    .Count == 1;

                var conformingReturnType = returnTypeConforms ? individualSymbols.First().ReturnType : TypeInfo.AnyType;

                processedConstructors.Add(new ProcessedConstructorInfo(
                    new ProcessedReturnTypeInfo(new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        conformingReturnType,
                        parent,
                        conformingTypeParameters.ToValueImmutableList(),
                        null))),
                    "construct",
                    ProcessTypeParameters(
                        conformingTypeParameters.ToValueImmutableList(),
                        parent),
                    ProcessParameters(
                        conformingParameters.ToValueImmutableList(),
                        parent,
                        conformingTypeParameters.ToValueImmutableList())));
            }

            var methodSymbols = symbols
                .Where(s => s.Parent == parent && s.MethodInfo != null)
                .GroupBy(s => new
                {
                    Name = s.MethodInfo?.Name ?? string.Empty,
                    ParameterCount = s.MethodInfo?.Parameters.Count ?? 0,
                    TypeParameterCount = s.MethodInfo?.ExtractTypeParametersResult.TypeParameters.Count ?? 0,
                })
                .ToValueImmutableList();

            var processedMethods = new List<ProcessedMethodInfo>();

            foreach (var overloadGrouping in methodSymbols)
            {
                var individualSymbols = overloadGrouping
                    .Select(s => s.MethodInfo)
                    .WhereNotNull()
                    .ToValueImmutableList();

                // Figure out a way to make all the overloads have the same conforming parameter types.
                var conformingParameters = new List<ParameterInfo>();

                for (var parameterIndex = 0; parameterIndex < overloadGrouping.Key.ParameterCount; parameterIndex++)
                {
                    var parameterConforms = individualSymbols
                        .Select(s => s.Parameters[parameterIndex].Type)
                        .ToList()
                        .DistinctSafeSlow()
                        .ToValueImmutableList()
                        .Count == 1;

                    // FIXME: This might lead to some odd parameter names if there is a situation where the names do not conform, but the types do.
                    if (parameterConforms)
                    {
                        conformingParameters.Add(individualSymbols.First().Parameters[parameterIndex]);
                    }
                    else
                    {
                        conformingParameters.Add(individualSymbols.First().Parameters[parameterIndex] with
                        {
                            Type = TypeInfo.AnyType,
                        });
                    }
                }

                var conformingTypeParameters = new List<TypeParameter>();

                for (var typeParameterIndex = 0; typeParameterIndex < overloadGrouping.Key.TypeParameterCount; typeParameterIndex++)
                {
                    var constraintConforms = individualSymbols
                        .Select(s => s.ExtractTypeParametersResult.TypeParameters[typeParameterIndex].Constraint)
                        .ToList()
                        .DistinctSafeSlow()
                        .ToValueImmutableList()
                        .Count == 1;

                    // FIXME: This might lead to some odd parameter names if there is a situation where the names do not conform, but the types do.
                    if (constraintConforms)
                    {
                        conformingTypeParameters.Add(individualSymbols.First().ExtractTypeParametersResult.TypeParameters[typeParameterIndex]);
                    }
                    else
                    {
                        conformingTypeParameters.Add(individualSymbols.First().ExtractTypeParametersResult.TypeParameters[typeParameterIndex] with
                        {
                            Constraint = null,
                        });
                    }
                }

                var returnTypeConforms = individualSymbols
                    .Select(s => s.ReturnType)
                    .DistinctSafeSlow()
                    .ToValueImmutableList()
                    .Count == 1;

                var conformingReturnType = returnTypeConforms ? individualSymbols.First().ReturnType : TypeInfo.AnyType;

                processedMethods.Add(new ProcessedMethodInfo(
                    new ProcessedReturnTypeInfo(new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        conformingReturnType,
                        parent,
                        conformingTypeParameters.ToValueImmutableList(),
                        null))),
                    ReservedKeywords.SanitizeName(overloadGrouping.Key.Name),
                    overloadGrouping.Key.Name,
                    ProcessTypeParameters(
                        conformingTypeParameters.ToValueImmutableList(),
                        parent),
                    ProcessParameters(
                        conformingParameters.ToValueImmutableList(),
                        parent,
                        conformingTypeParameters.ToValueImmutableList())));
            }

            var propertySymbols = symbols
                .Where(s => s.Parent == parent && s.PropertyInfo != null)
                .ToValueImmutableList();

            var processedProperties = new List<ProcessedPropertyInfo>();

            foreach (var propertySymbol in propertySymbols)
            {
                if (propertySymbol.PropertyInfo == null)
                {
                    continue;
                }

                processedProperties.Add(new ProcessedPropertyInfo(
                    new ProcessedReturnTypeInfo(new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        propertySymbol.PropertyInfo.Type,
                        parent,
                        null,
                        null))),
                    ReservedKeywords.SanitizeName(propertySymbol.PropertyInfo.Name),
                    propertySymbol.PropertyInfo.Name,
                    propertySymbol.PropertyInfo.IsReadonly ? ProcessedPropertyMode.GetterOnly : ProcessedPropertyMode.GetterAndSetter));
            }

            if (parent.OwnerInterface.Name == GetGlobalThisInterfaceName())
            {
                foreach (var globalDefinedOutside in globalsDefinedOutside)
                {
                    processedProperties.Add(new ProcessedPropertyInfo(
                        new ProcessedReturnTypeInfo(new ProcessedTypeReferenceInfo($"I{globalDefinedOutside.InterfaceInfo.Name}")),
                        globalDefinedOutside.GlobalVariableInfo.Name,
                        globalDefinedOutside.GlobalVariableInfo.Name,
                        ProcessedPropertyMode.GetterOnly));
                }
            }

            var accessorSymbols = symbols
                .Where(s => s.Parent == parent && (s.GetAccessorInfo != null || s.SetAccessorInfo != null))
                .GroupBy(s => s.GetAccessorInfo != null
                    ? s.GetAccessorInfo.Name
                    : (s.SetAccessorInfo != null ? s.SetAccessorInfo.Name : string.Empty))
                .ToValueImmutableList();

            foreach (var accessorGrouping in accessorSymbols)
            {
                var individualSymbols = accessorGrouping.ToValueImmutableList();

                var accessorTypes = individualSymbols
                    .Select(s => s.GetAccessorInfo != null
                        ? s.GetAccessorInfo.ReturnType
                        : s.SetAccessorInfo?.Parameters.FirstOrDefault()?.Type)
                    .WhereNotNull()
                    .ToValueImmutableList();

                var accessorTypeConforms = accessorTypes
                    .DistinctSafeSlow()
                    .ToValueImmutableList()
                    .Count == 1;

                var conformingType = accessorTypeConforms && accessorTypes.Count > 0
                    ? accessorTypes.First()
                    : TypeInfo.AnyType;

                ProcessedPropertyMode mode;

                var hasGetter = individualSymbols.Any(s => s.GetAccessorInfo != null);
                var hasSetter = individualSymbols.Any(s => s.SetAccessorInfo != null);

                if (hasGetter && hasSetter)
                {
                    mode = ProcessedPropertyMode.GetterAndSetter;
                }
                else if (hasGetter && !hasSetter)
                {
                    mode = ProcessedPropertyMode.GetterOnly;
                }
                else if (hasSetter && !hasGetter)
                {
                    mode = ProcessedPropertyMode.SetterOnly;
                }
                else
                {
                    continue;
                }

                processedProperties.Add(new ProcessedPropertyInfo(
                    new ProcessedReturnTypeInfo(new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        conformingType,
                        parent,
                        null,
                        null))),
                    ReservedKeywords.SanitizeName(accessorGrouping.Key),
                    accessorGrouping.Key,
                    mode));
            }

            var indexerSymbols = symbols
                .Where(s => s.Parent == parent && s.IndexerInfo != null)
                .ToValueImmutableList();

            var processedIndexers = new List<ProcessedIndexerInfo>();

            foreach (var indexerSymbol in indexerSymbols)
            {
                if (indexerSymbol.IndexerInfo == null)
                {
                    continue;
                }

                processedIndexers.Add(new ProcessedIndexerInfo(
                    new ProcessedReturnTypeInfo(new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        indexerSymbol.IndexerInfo.ReturnType,
                        parent,
                        null,
                        null))),
                    new ProcessedParameterInfo(
                        new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                            indexerSymbol.IndexerInfo.IndexType,
                            parent,
                            null,
                            null)),
                        ReservedKeywords.SanitizeName(indexerSymbol.IndexerInfo.IndexName)),
                    indexerSymbol.IndexerInfo.IsReadonly ? ProcessedIndexerMode.GetterOnly : ProcessedIndexerMode.GetterAndSetter));
            }

            return new ProcessedSymbols(
                new ProcessedConstructorsInfo(processedConstructors.ToValueImmutableList()),
                new ProcessedMethodsInfo(processedMethods.ToValueImmutableList()),
                new ProcessedPropertiesInfo(processedProperties.ToValueImmutableList()),
                new ProcessedIndexersInfo(processedIndexers.ToValueImmutableList()));
        }

        private ProcessedTypeParametersInfo ProcessTypeParameters(
            ValueImmutableList<TypeParameter> extractTypeParameters,
            SymbolParent parent)
        {
            // FIXME: Is there a way to exclude type parameters that actually never got used in a return type or a parameter type?
            //        For example, see IPromise in the generated code.
            var processedTypeParameters = new List<ProcessedTypeParameterInfo>();

            foreach (var typeParameter in extractTypeParameters)
            {
                ProcessedTypeParameterConstraintInfo? processedConstraintInfo = null;

                if (typeParameter.Constraint != null)
                {
                    var renderedConstraintName = GetRenderedTypeName(
                        typeParameter.Constraint,
                        parent,
                        extractTypeParameters, // FIXME: Is this right? Should we pass null here instead?
                        null);

                    // FIXME: This is a little hacky, but I'm fine with it for now. I think it'd be cool if we didn't
                    //        to do this here, though. Maybe this could move to the time simplification step.
                    if (renderedConstraintName != "IJSObject")
                    {
                        processedConstraintInfo = new ProcessedTypeParameterConstraintInfo(renderedConstraintName);
                    }
                }

                processedTypeParameters.Add(new ProcessedTypeParameterInfo(
                    typeParameter.Name,
                    processedConstraintInfo));
            }

            return new ProcessedTypeParametersInfo(processedTypeParameters.ToValueImmutableList());
        }

        private ProcessedParametersInfo ProcessParameters(
            ValueImmutableList<ParameterInfo> parameters,
            SymbolParent parent,
            ValueImmutableList<TypeParameter>? symbolTypeParameters)
        {
            var processedParameters = new List<ProcessedParameterInfo>();

            foreach (var parameter in parameters)
            {
                processedParameters.Add(new ProcessedParameterInfo(
                    new ProcessedTypeReferenceInfo(GetRenderedTypeName(
                        parameter.Type,
                        parent,
                        symbolTypeParameters,
                        null)),
                    ReservedKeywords.SanitizeName(parameter.Name)));
            }

            return new ProcessedParametersInfo(processedParameters.ToValueImmutableList());
        }

        private ValueImmutableList<GlobalDefinedOutsideOfGlobalThisInterface> GetGlobalsDefinedOutsideOfGlobalThisInterface()
        {
            var globalThisInterface = _parsedInfo.Interfaces.First(interfaceInfo => interfaceInfo.Name == GetGlobalThisInterfaceName());
            var allSymbols = GetSymbolsFromInterface(
                SymbolParent.Root(globalThisInterface),
                true);

            var allProperties = allSymbols
                .Select(symbolInfo => symbolInfo.PropertyInfo)
                .WhereNotNull()
                .ToValueImmutableList();

            var allWindowGetters = allSymbols
                .Select(symbolInfo => symbolInfo.GetAccessorInfo)
                .WhereNotNull()
                .ToValueImmutableList();

            var result = new List<GlobalDefinedOutsideOfGlobalThisInterface>();

            foreach (var globalVariableInfo in _parsedInfo.GlobalVariables)
            {
                // HACK: Let's exclude anything that was already defined in the `Window` interface.
                if (allProperties.Any(propertyDetails => propertyDetails.Name == globalVariableInfo.Name)
                    || allWindowGetters.Any(getAccessor => getAccessor.Name == globalVariableInfo.Name))
                {
                    continue;
                }

                if (globalVariableInfo.InlineInterface != null)
                {
                    result.Add(new GlobalDefinedOutsideOfGlobalThisInterface(
                        globalVariableInfo,
                        new InterfaceInfo(
                            $"{globalVariableInfo.Name}Global",
                            new ExtractTypeParametersResult(
                                ValueImmutableList.Create<TypeParameter>(),
                                false),
                            ValueImmutableList.Create<TypeInfo>(),
                            globalVariableInfo.InlineInterface)));

                    continue;
                }

                if (globalVariableInfo.Type == null)
                {
                    continue;
                }

                var globalInterfaceType = _parsedInfo.Interfaces.FirstOrDefault(i => i.Name == globalVariableInfo.Type.Single?.Name);

                if (globalInterfaceType == null)
                {
                    continue;
                }

                result.Add(new GlobalDefinedOutsideOfGlobalThisInterface(
                    globalVariableInfo,
                    globalInterfaceType));
            }

            return result.ToValueImmutableList();
        }

        private string GetGlobalThisInterfaceName()
        {
            // FIXME: Right now, we know the globalThis is a `Window`, but we might not want to assume this
            //        in the future, especially if this code is used to generate bindings for libraries.
            return "Window";
        }

        private ValueImmutableList<SymbolInfo> GetSymbolsFromInterface(
            SymbolParent parent,
            bool isRecursive)
        {
            var symbols = new List<SymbolInfo>();

            if (isRecursive)
            {
                foreach (var extendTypeInfo in parent.OwnerInterface.ExtendsList)
                {
                    if (extendTypeInfo.Single == null)
                    {
                        continue;
                    }

                    var extendInterfaceInfo =
                        _parsedInfo.Interfaces.FirstOrDefault(i => i.Name == extendTypeInfo.Single.Name);

                    if (extendInterfaceInfo == null)
                    {
                        continue;
                    }

                    var nextParent = new SymbolParent(
                        extendInterfaceInfo,
                        extendTypeInfo.Single.TypeArguments,
                        parent);

                    symbols.AddRange(GetSymbolsFromInterface(
                        nextParent,
                        true));
                }
            }

            var interfaceBodyInfo = parent.OwnerInterface.Body;

            symbols.AddRange(interfaceBodyInfo
                .Methods
                .Select(methodInfo => SymbolInfo.From(parent, methodInfo))
                .ToValueImmutableList());

            symbols.AddRange(interfaceBodyInfo
                .Constructors
                .Select(constructorInfo => SymbolInfo.From(parent, constructorInfo))
                .ToValueImmutableList());

            symbols.AddRange(interfaceBodyInfo
                .Properties
                .Select(propertyInfo => SymbolInfo.From(parent, propertyInfo))
                .ToValueImmutableList());

            symbols.AddRange(interfaceBodyInfo
                .GetAccessors
                .Select(getAccessorInfo => SymbolInfo.From(parent, getAccessorInfo))
                .ToValueImmutableList());

            symbols.AddRange(interfaceBodyInfo
                .SetAccessors
                .Select(setAccessorInfo => SymbolInfo.From(parent, setAccessorInfo))
                .ToValueImmutableList());

            symbols.AddRange(interfaceBodyInfo
                .Indexers
                .Select(indexerInfo => SymbolInfo.From(parent, indexerInfo))
                .ToValueImmutableList());

            return symbols.ToValueImmutableList();
        }
    }
}
