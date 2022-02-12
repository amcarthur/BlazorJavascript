import * as fs from "fs";
import * as ts from "typescript";
import {SourceFile, SyntaxKind} from "typescript";

const inputTypeDefinitions = [
    'lib.dom.d',
];

const isPrettyPrint = process.argv.indexOf('--pretty') !== -1;
const isDebugMode = process.argv.indexOf('--debug') !== -1;

const getCircularReplacer = () => {
    const seen = new WeakSet();
    return (key: any, value: any) => {
        if (typeof value === "object" && value !== null) {
            if (seen.has(value)) {
                return;
            }
            seen.add(value);
        }
        return value;
    };
};

fs.mkdirSync('output', {recursive: true});

interface ConstructorInfo {
    returnType: TypeInfo;
    parameters: ParameterInfo[];
}

interface ParameterInfo {
    name: string;
    isOptional: boolean;
    type: TypeInfo;
}

interface SingleTypeInfo {
    name: string;
    typeArguments: SingleTypeInfo[];
}

interface TypeInfo {
    list: SingleTypeInfo[];
}

interface GlobalVariableInfo {
    name: string;
    hasPrototype: boolean;
    constructors: ConstructorInfo[];
    properties: PropertyInfo[];
}

interface PropertyInfo {
    name: string;
    isReadonly: boolean;
    type: TypeInfo;
}

interface TypeParameter {
    name: string;
    constraint: TypeInfo | null;
}

interface MethodInfo {
    name: string;
    typeParameters: TypeParameter[];
    returnType: TypeInfo;
    parameters: ParameterInfo[];
}

interface InterfaceInfo {
    name: string;
    extendsList: string[];
    properties: PropertyInfo[];
    methods: MethodInfo[];
}

interface ParsedInfo {
    globalVariables: GlobalVariableInfo[];
    interfaces: InterfaceInfo[];
}

function extractTypeArguments(typeNode: ts.TypeReferenceNode): SingleTypeInfo[] {
    const typeArguments: SingleTypeInfo[] = [];

    if (!!typeNode.typeArguments) {
        typeNode.typeArguments.forEach(typeArgument => {
            typeArguments.push(extractSingleTypeInfo(typeArgument));
        });
    }

    return typeArguments;
}
function extractSingleTypeInfo(typeNode: ts.TypeNode): SingleTypeInfo {
    if (ts.isTypeReferenceNode(typeNode)
        && ts.isIdentifier(typeNode.typeName)) {
        return {
            name: typeNode.typeName.text,
            typeArguments: extractTypeArguments(typeNode)
        };
    }

    if (ts.isArrayTypeNode(typeNode)) {
        const typeName = extractSingleTypeInfo(typeNode.elementType);
        return {
            name: `${typeName}[]`,
            typeArguments: []
        };
    }

    if (typeNode.kind === ts.SyntaxKind.NumberKeyword) {
        return {
            name: "number",
            typeArguments: []
        };
    }

    if (typeNode.kind === ts.SyntaxKind.StringKeyword) {
        return {
            name: "string",
            typeArguments: []
        };
    }

    if (typeNode.kind === ts.SyntaxKind.BooleanKeyword) {
        return {
            name: "boolean",
            typeArguments: []
        };
    }

    if (ts.isLiteralTypeNode(typeNode)) {
        if (typeNode.literal.kind == ts.SyntaxKind.NullKeyword) {
            return {
                name: "null",
                typeArguments: []
            };
        }

        return {
            name: "unhandled_literal",
            typeArguments: []
        };
    }

    return {
        name: "unhandled",
        typeArguments: []
    };
}

function extractTypeInfo(typeNode: ts.TypeNode): TypeInfo {
    if (ts.isUnionTypeNode(typeNode)) {
        const singleTypeInfoList: SingleTypeInfo[] = [];

        typeNode.types.forEach(unionTypeChild => {
            singleTypeInfoList.push(extractSingleTypeInfo(unionTypeChild));
        });

        return {
            list: singleTypeInfoList,
        }
    }

    return {
        list: [extractSingleTypeInfo(typeNode)],
    };
}

function isParameterOptional(parameterDeclaration: ts.ParameterDeclaration): boolean {
    return !!parameterDeclaration.questionToken
        && parameterDeclaration.questionToken.kind == ts.SyntaxKind.QuestionToken
}

function extractProperties(members: ts.NodeArray<ts.TypeElement>) {
    const properties: PropertyInfo[] = [];

    members.forEach(member => {
        if (!ts.isPropertySignature(member) || !ts.isIdentifier(member.name) || !member.type) {
            return;
        }

        // FIXME: Ignore properties that are named "prototype" to make our life easier.
        //        There might be a better way to do this.
        if (member.name.text === "prototype") {
            return;
        }

        let isReadonly = false;

        if (!!member.modifiers) {
            member.modifiers.forEach(modifier => {
                if (modifier.kind === ts.SyntaxKind.ReadonlyKeyword) {
                    isReadonly = true;
                }
            });
        }

        properties.push({
            name: member.name.text,
            type: extractTypeInfo(member.type),
            isReadonly: isReadonly,
        });
    });

    return properties;
}

function extractParameters(member: ts.NodeArray<ts.ParameterDeclaration>): ParameterInfo[] {
    const parameters: ParameterInfo[] = [];

    member.forEach(parameter => {
        if (!ts.isIdentifier(parameter.name) || !parameter.type) {
            return;
        }

        parameters.push({
            name: parameter.name.text,
            isOptional: isParameterOptional(parameter),
            type: extractTypeInfo(parameter.type),
        });
    });

    return parameters;
}

inputTypeDefinitions.forEach(inputTypeDefinition => {
    const inputPath = `node_modules/typescript/lib/${inputTypeDefinition}.ts`;
    const outputPath = `output/${inputTypeDefinition}.json`;

    console.log(`Dumping AST for "${inputPath}" to "${outputPath}"...`);

    const sourceFile: ts.SourceFile = ts.createSourceFile(
        'x.ts',
        fs.readFileSync(inputPath, {encoding:'utf8', flag:'r'}),
        ts.ScriptTarget.Latest
    );

    const parsedInfo: ParsedInfo = {
        globalVariables: [],
        interfaces: [],
    };

    if (isDebugMode) {
        (parsedInfo as any).raw = sourceFile;
    }

    sourceFile.statements.forEach(statement => {
        if (ts.isInterfaceDeclaration(statement)
            && ts.isIdentifier(statement.name)) {
            const extendsList: string[] = [];

            if (!!statement.heritageClauses) {
                statement.heritageClauses.forEach(heritageClause => {
                    if (heritageClause.token !== ts.SyntaxKind.ExtendsKeyword) {
                        console.error("Heritage clause detected without extends keyword.");
                        return;
                    }

                    heritageClause.types.forEach(type => {
                        if (!ts.isIdentifier(type.expression)) {
                            return;
                        }

                        extendsList.push(type.expression.text);
                    });
                });
            }

            const methods: MethodInfo[] = [];

            statement.members.forEach(member => {
                if (!ts.isMethodSignature(member) || !ts.isIdentifier(member.name) || !member.type) {
                    return;
                }

                const typeParameters: TypeParameter[] = [];
                let anyConstraintsAreNotSimple = false;

                if (!!member.typeParameters) {
                    member.typeParameters.forEach(typeParameter => {
                        if (!ts.isIdentifier(typeParameter.name)) {
                            return;
                        }

                        // FIXME: For now, we are ignoring defaults for type parameters.
                        //        We might be able to emulate this with subclassing: https://stackoverflow.com/a/707788.
                        let constraint: TypeInfo | null = null;

                        if (!!typeParameter.constraint) {
                            if (!ts.isTypeReferenceNode(typeParameter.constraint)) {
                                anyConstraintsAreNotSimple = true;
                                return;
                            }

                            constraint = extractTypeInfo(typeParameter.constraint);
                        }

                        typeParameters.push({
                            name: typeParameter.name.text,
                            constraint: constraint,
                        });
                    });
                }

                // FIXME: Any function that has something like this is ignored for now:
                //        foo<K extends keyof Bar>(type: K)
                //        In the future, we should consider replacing the generic and parameters with a string.
                if (anyConstraintsAreNotSimple) {
                    return;
                }

                methods.push({
                    name: member.name.text,
                    parameters: extractParameters(member.parameters),
                    typeParameters: typeParameters,
                    returnType: extractTypeInfo(member.type),
                });
            });

            const interfaceInfo: InterfaceInfo = {
                name: statement.name.text,
                extendsList: extendsList,
                properties: extractProperties(statement.members),
                methods: methods,
            };

            parsedInfo.interfaces.push(interfaceInfo);
            return;
        }

        if (ts.isVariableStatement(statement)) {
            statement.declarationList.declarations.forEach(declaration => {
                if (!ts.isVariableDeclaration(declaration)
                    || !declaration.type
                    || !ts.isTypeLiteralNode(declaration.type)
                    || !ts.isIdentifier(declaration.name)) {
                    return;
                }

                let hasPrototype = false;

                const declarationName = declaration.name.text;
                const constructors: ConstructorInfo[] = [];

                declaration.type.members.forEach(member => {
                    if (ts.isPropertySignature(member)
                        && ts.isIdentifier(member.name)
                        && member.name.text === "prototype"
                        && member.type
                        && ts.isTypeReferenceNode(member.type)
                        && ts.isIdentifier(member.type.typeName)
                        && member.type.typeName.text === declarationName) {
                        hasPrototype = true;
                        return;
                    }

                    if (ts.isConstructSignatureDeclaration(member) && member.type) {
                        constructors.push({
                            returnType: extractTypeInfo(member.type),
                            parameters: extractParameters(member.parameters),
                        });

                        return;
                    }
                });

                parsedInfo.globalVariables.push({
                    name: declarationName,
                    hasPrototype: hasPrototype,
                    constructors: constructors,
                    properties: extractProperties(declaration.type.members),
                });
            });
        }
    });

    fs.writeFileSync(outputPath, JSON.stringify(parsedInfo, getCircularReplacer(), isPrettyPrint ? 2 : undefined));
});
