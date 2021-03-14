// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory (https://github.com/lithiumtoast/c2cs) for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using C2CS.Bindgen.ExploreCCode;
using ClangSharp.Interop;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace C2CS.Bindgen.TranspileCCodeToCSharp
{
	internal sealed class CSharpCodeGenerator
	{
		private readonly string _libraryName;
		private readonly Dictionary<CXCursor, string> _cSharpNamesByClangCursor = new();

		public CSharpCodeGenerator(string libraryName)
		{
			_libraryName = libraryName;
		}

		public ClassDeclarationSyntax CreatePInvokeClass(string name, ImmutableArray<MemberDeclarationSyntax> members)
		{
			var newMembers = new List<MemberDeclarationSyntax>();

			var libraryNameField = FieldDeclaration(
					VariableDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)))
						.WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("LibraryName"))
							.WithInitializer(
								EqualsValueClause(LiteralExpression(
									SyntaxKind.StringLiteralExpression,
									Literal(_libraryName)))))))
				.WithModifiers(
					TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ConstKeyword)));

			newMembers.Add(libraryNameField);
			newMembers.AddRange(members);

			const string comment = @"
//-------------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by the following tool:
//        https://github.com/lithiumtoast/c2cs
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ReSharper disable All
//-------------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;";

			var commentFormatted = comment.TrimStart() + "\r\n";

			var result = ClassDeclaration(name)
				.AddModifiers(
					Token(SyntaxKind.PublicKeyword),
					Token(SyntaxKind.StaticKeyword),
					Token(SyntaxKind.UnsafeKeyword),
					Token(SyntaxKind.PartialKeyword))
				.WithMembers(List(
					newMembers))
				.WithLeadingTrivia(Comment(commentFormatted))
				.Format();

			return result;
		}

		public MethodDeclarationSyntax CreateExternMethod(CFunctionExtern functionExtern)
		{
			var functionName = functionExtern.Name;
			var functionReturnTypeName = functionExtern.ReturnType.Name;
			var functionReturnType = ParseTypeName(functionReturnTypeName);
			var functionCallingConvention = CSharpCallingConvention(functionExtern.CallingConvention);
			var functionParameters = functionExtern.Parameters;

			var cSharpMethod = MethodDeclaration(functionReturnType, functionName)
				.WithDllImportAttribute(functionCallingConvention)
				.WithModifiers(TokenList(
					Token(SyntaxKind.PublicKeyword),
					Token(SyntaxKind.StaticKeyword),
					Token(SyntaxKind.ExternKeyword)))
				.WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

			var parameters = CreateMethodParameters(functionParameters);

			cSharpMethod = cSharpMethod
				.AddParameterListParameters(parameters.ToArray())
				.WithLeadingTrivia(CInfoComment(functionExtern.Info));

			return cSharpMethod;
		}

		public MemberDeclarationSyntax CreateFunctionPointer(CFunctionPointer functionPointer)
		{
			var cSharpStruct = StructDeclaration(functionPointer.Name)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(
					LayoutKind.Explicit, functionPointer.Type.Layout.Size, functionPointer.Type.Layout.Alignment);

			var cSharpFieldName = SanitizeIdentifierName("Pointer");
			var cSharpVariable = VariableDeclarator(Identifier(cSharpFieldName));
			var cSharpFieldType = ParseTypeName("void*");
			var cSharpField = FieldDeclaration(VariableDeclaration(cSharpFieldType)
				.WithVariables(SingletonSeparatedList(cSharpVariable)));

			cSharpField = cSharpField.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(0, functionPointer.Type.Layout.Size, 0);

			cSharpStruct = cSharpStruct
				.WithMembers(new SyntaxList<MemberDeclarationSyntax>(cSharpField))
				.WithLeadingTrivia(CInfoComment(functionPointer.Info));

			return cSharpStruct;
		}

		private static SyntaxTrivia CInfoComment(CInfo info)
		{
			var kind = info.Kind;
			var location = info.Location;

			var comment =
				$"// {kind} @ {location.FileName}:{location.FileLine} {location.DateTime}";

			return Comment(comment);
		}

		// public FieldDeclarationSyntax CreateConstant(CXCursor enumConstant)
		// {
		// 	var variableDeclarationCSharp = CreateConstantVariable(enumConstant);
		// 	var constFieldDeclarationCSharp = FieldDeclaration(variableDeclarationCSharp)
		// 		.WithModifiers(
		// 			TokenList(
		// 				Token(SyntaxKind.PublicKeyword),
		// 				Token(SyntaxKind.ConstKeyword)));
		//
		// 	return constFieldDeclarationCSharp;
		// }

		public EnumDeclarationSyntax CreateEnum(CEnum @enum)
		{
			var cSharpEnumType = @enum.Type.Name;
			var cSharpEnum = EnumDeclaration(@enum.Name)
				.AddModifiers(Token(SyntaxKind.PublicKeyword))
				.AddBaseListTypes(SimpleBaseType(ParseTypeName(cSharpEnumType)));

			var cSharpEnumMembers = ImmutableArray.CreateBuilder<EnumMemberDeclarationSyntax>(@enum.Values.Length);
			foreach (var cEnumValue in @enum.Values)
			{
				var cSharpEqualsValueClause = CreateEqualsValueClause(cEnumValue.Value, cSharpEnumType);
				var cSharpEnumMember = EnumMemberDeclaration(cEnumValue.Name)
					.WithEqualsValue(cSharpEqualsValueClause);

				cSharpEnumMembers.Add(cSharpEnumMember);
			}

			return cSharpEnum.AddMembers(cSharpEnumMembers.ToArray())
				.WithLeadingTrivia(CInfoComment(@enum.Info));
		}

		public StructDeclarationSyntax CreateStruct(CStruct cStruct)
		{
			var structName = cStruct.Name;
			var structSize = cStruct.Type.Layout.Size;
			var structAlignment = cStruct.Type.Layout.Alignment;

			var cSharpStruct = StructDeclaration(structName)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(LayoutKind.Explicit, structSize, structAlignment);

			var cSharpStructMembers = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

			foreach (var cStructField in cStruct.Fields)
			{
				var cSharpStructField = CreateStructField(cStructField, out var needsWrap);
				cSharpStructMembers.Add(cSharpStructField);

				if (!needsWrap)
				{
					continue;
				}

				var cSharpStructFieldWrappedMethod = CreateStructFieldWrapperMethod(cStruct.Name, cStructField);
				cSharpStructMembers.Add(cSharpStructFieldWrappedMethod);
			}

			cSharpStruct = cSharpStruct
				.AddMembers(cSharpStructMembers.ToArray())
				.WithLeadingTrivia(CInfoComment(cStruct.Info));
			return cSharpStruct;
		}

		private FieldDeclarationSyntax CreateStructField(
			CStructField cStructField,
			out bool needsWrap)
		{
			FieldDeclarationSyntax result;

			if (cStructField.Type.IsArray)
			{
				result = CreateStructFieldFixedArray(cStructField, out needsWrap);
			}
			else
			{
				result = CreateStructFieldNormal(cStructField);
				needsWrap = false;
			}

			return result;
		}

		private static FieldDeclarationSyntax CreateStructFieldNormal(CStructField cStructField)
		{
			var cSharpFieldName = SanitizeIdentifierName(cStructField.Name);
			var cSharpVariable = VariableDeclarator(Identifier(cSharpFieldName));
			var cSharpFieldType = ParseTypeName(cStructField.Type.Name);
			var cSharpField = FieldDeclaration(VariableDeclaration(cSharpFieldType)
				.WithVariables(SingletonSeparatedList(cSharpVariable)));

			cSharpField = cSharpField.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(cStructField.Offset, cStructField.Type.Layout.Size, cStructField.Padding);

			return cSharpField;
		}

		private FieldDeclarationSyntax CreateStructFieldFixedArray(
			CStructField cStructField,
			out bool needsWrap)
		{
			var cSharpFieldName = SanitizeIdentifierName(cStructField.Name);
			var cSharpFieldType = ParseTypeName(cStructField.Type.Name);
			VariableDeclaratorSyntax cSharpVariable;

			var isValidFixedType = IsValidCSharpTypeSpellingForFixedBuffer(cStructField.Type.Name);
			if (isValidFixedType)
			{
				var arraySize = cStructField.Type.Layout.Size / cStructField.Type.Layout.Alignment;
				cSharpVariable = VariableDeclarator(Identifier(cSharpFieldName))
					.WithArgumentList(
						BracketedArgumentList(
							SingletonSeparatedList(
								Argument(
									LiteralExpression(
										SyntaxKind.NumericLiteralExpression,
										Literal(arraySize))))));

				needsWrap = false;
			}
			else
			{
				var typeTokenSyntaxKind = cStructField.Type.Layout.Alignment switch
				{
					1 => SyntaxKind.ByteKeyword,
					2 => SyntaxKind.UShortKeyword,
					4 => SyntaxKind.UIntKeyword,
					8 => SyntaxKind.ULongKeyword,
					_ => throw new ArgumentException("Invalid field alignment.")
				};

				cSharpFieldType = PredefinedType(Token(typeTokenSyntaxKind));
				cSharpVariable = VariableDeclarator(Identifier($"_{cStructField.Name}"))
					.WithArgumentList(BracketedArgumentList(SingletonSeparatedList(
						Argument(
							BinaryExpression(
								SyntaxKind.DivideExpression,
								LiteralExpression(
									SyntaxKind.NumericLiteralExpression,
									Literal(cStructField.Type.Layout.Size)),
								LiteralExpression(
									SyntaxKind.NumericLiteralExpression,
									Literal(cStructField.Type.Layout.Alignment)))))));

				needsWrap = true;
			}

			var cSharpField = FieldDeclaration(VariableDeclaration(cSharpFieldType)
				.WithVariables(SingletonSeparatedList(cSharpVariable)));

			cSharpField = cSharpField
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(cStructField.Offset, cStructField.Type.Layout.Size, cStructField.Padding)
				.AddModifiers(Token(SyntaxKind.FixedKeyword))
				.WithSemicolonToken(Token(TriviaList(), SyntaxKind.SemicolonToken, TriviaList(
					Comment($"/* original type is `{cStructField.Type.OriginalName}` */"))));

			return cSharpField;
		}

		public StructDeclarationSyntax CreateOpaqueStruct(COpaqueType cOpaqueType)
		{
			var cSharpStruct = StructDeclaration(cOpaqueType.Name)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(LayoutKind.Explicit, cOpaqueType.Type.Layout.Size, cOpaqueType.Type.Layout.Alignment);

			var cSharpFieldType = ParseTypeName("IntPtr");
			var cSharpFieldVariable = VariableDeclarator(Identifier("Handle"));
			var cSharpField = FieldDeclaration(
					VariableDeclaration(cSharpFieldType)
						.WithVariables(SingletonSeparatedList(cSharpFieldVariable)))
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(0, cOpaqueType.Type.Layout.Size, 0);
			cSharpStruct = cSharpStruct
				.AddMembers(cSharpField)
				.WithLeadingTrivia(CInfoComment(cOpaqueType.Info));

			return cSharpStruct;
		}

		public MemberDeclarationSyntax CreateForwardStruct(CForwardType cForwardType)
		{
			var cSharpStruct = StructDeclaration(cForwardType.Name)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(LayoutKind.Explicit, cForwardType.Type.Layout.Size, cForwardType.Type.Layout.Alignment);

			var cSharpFieldType = ParseTypeName(cForwardType.UnderlyingType.Name);
			var cSharpFieldVariable = VariableDeclarator(Identifier("Data"));
			var cSharpField = FieldDeclaration(
					VariableDeclaration(cSharpFieldType)
						.WithVariables(SingletonSeparatedList(cSharpFieldVariable)))
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(0, cForwardType.Type.Layout.Size, 0);
			cSharpStruct = cSharpStruct
				.AddMembers(cSharpField)
				.WithLeadingTrivia(CInfoComment(cForwardType.Info));

			return cSharpStruct;
		}

		private ImmutableArray<ParameterSyntax> CreateMethodParameters(ImmutableArray<CFunctionParameter> functionParameters)
		{
			var cSharpMethodParameters = ImmutableArray.CreateBuilder<ParameterSyntax>();
			var cSharpMethodParameterNames = new HashSet<string>();

			foreach (var clangFunctionParameter in functionParameters)
			{
				// var clangDisplayName = ClangDisplayName(clangFunctionParameter);
				var cSharpMethodParameterName = SanitizeIdentifierName(clangFunctionParameter.Name);

				while (cSharpMethodParameterNames.Contains(cSharpMethodParameterName))
				{
					var numberSuffixMatch = Regex.Match(cSharpMethodParameterName, "\\d$");
					if (numberSuffixMatch.Success)
					{
						var parameterNameWithoutSuffix = cSharpMethodParameterName.Substring(0, numberSuffixMatch.Index);
						cSharpMethodParameterName = ParameterNameUniqueSuffix(parameterNameWithoutSuffix, numberSuffixMatch.Value);
					}
					else
					{
						cSharpMethodParameterName = ParameterNameUniqueSuffix(cSharpMethodParameterName, string.Empty);
					}
				}

				cSharpMethodParameterNames.Add(cSharpMethodParameterName);
				var cSharpMethodParameter = CreateMethodParameter(clangFunctionParameter, cSharpMethodParameterName);
				cSharpMethodParameters.Add(cSharpMethodParameter);
			}

			return cSharpMethodParameters.ToImmutable();

			static string ParameterNameUniqueSuffix(string parameterNameWithoutSuffix, string parameterSuffix)
			{
				if (parameterSuffix == string.Empty)
				{
					return parameterNameWithoutSuffix + "2";
				}

				var parameterSuffixNumber = int.Parse(parameterSuffix, NumberStyles.Integer, CultureInfo.InvariantCulture);
				parameterSuffixNumber += 1;
				var parameterName = parameterNameWithoutSuffix + parameterSuffixNumber;
				return parameterName;
			}
		}

		private ParameterSyntax CreateMethodParameter(CFunctionParameter functionParameter, string parameterName)
		{
			var methodParameter = Parameter(Identifier(parameterName));
			if (functionParameter.IsReadOnly)
			{
				methodParameter = methodParameter.WithAttribute("In");
			}

			var typeName = functionParameter.Type.Name;
			var type = ParseTypeName(typeName);
			methodParameter = methodParameter.WithType(type);

			return methodParameter;
		}

		private static EqualsValueClauseSyntax CreateEqualsValueClause(long value, string type)
		{
			// ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
			var literalToken = type switch
			{
				"int" => Literal((int)value),
				"uint" => Literal((uint)value),
				_ => throw new NotImplementedException($"The syntax kind is not yet supported: {type}.")
			};

			return EqualsValueClause(
				LiteralExpression(SyntaxKind.NumericLiteralExpression, literalToken));
		}

		// private VariableDeclarationSyntax CreateConstantVariable(CXCursor clangEnumConstant)
		// {
		// 	var clangType = clangEnumConstant.Type;
		// 	var cSharpType = GetCSharpType(clangType);
		//
		// 	var cSharpTypeString = cSharpType.ToFullString();
		// 	var literalSyntaxToken = cSharpTypeString switch
		// 	{
		// 		"short" => Literal((short)clangEnumConstant.EnumConstantDeclValue),
		// 		"int" => Literal((int)clangEnumConstant.EnumConstantDeclValue),
		// 		_ => throw new NotImplementedException(
		// 			$@"The enum constant literal expression is not yet supported: {cSharpTypeString}.")
		// 	};
		//
		// 	var cSharpName = ClangName(clangEnumConstant);
		// 	var variable = VariableDeclaration(cSharpType)
		// 		.WithVariables(
		// 			SingletonSeparatedList(
		// 				VariableDeclarator(
		// 						Identifier(cSharpName))
		// 					.WithInitializer(
		// 						EqualsValueClause(LiteralExpression(
		// 							SyntaxKind.NumericLiteralExpression, literalSyntaxToken)))));
		//
		// 	return variable;
		// }

		private static MethodDeclarationSyntax CreateStructFieldWrapperMethod(string structName, CStructField cStructField)
		{
			var cSharpMethodName = SanitizeIdentifierName(cStructField.Name);
			var cSharpFieldName = $"_{cStructField.Name}";
			var cSharpStructTypeName = ParseTypeName(structName);
			var cSharpFieldType = ParseTypeName(cStructField.Type.Name);

			var body = Block(SingletonList<StatementSyntax>(FixedStatement(
				VariableDeclaration(PointerType(cSharpStructTypeName))
					.WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("@this"))
						.WithInitializer(EqualsValueClause(
							PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ThisExpression()))))),
				Block(
					LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
						.WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("pointer"))
							.WithInitializer(EqualsValueClause(CastExpression(
								PointerType(cSharpFieldType),
								PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ElementAccessExpression(
										MemberAccessExpression(
											SyntaxKind.PointerMemberAccessExpression,
											IdentifierName("@this"),
											IdentifierName(cSharpFieldName)))
									.WithArgumentList(BracketedArgumentList(SingletonSeparatedList(
										Argument(LiteralExpression(
											SyntaxKind.NumericLiteralExpression,
											Literal(0))))))))))))),
					LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
						.WithVariables(SingletonSeparatedList(VariableDeclarator(
								Identifier("pointerOffset"))
							.WithInitializer(EqualsValueClause(
								IdentifierName("index")))))),
					ReturnStatement(RefExpression(PrefixUnaryExpression(
						SyntaxKind.PointerIndirectionExpression,
						ParenthesizedExpression(BinaryExpression(
							SyntaxKind.AddExpression,
							IdentifierName("pointer"),
							IdentifierName("pointerOffset"))))))))));

			return MethodDeclaration(RefType(cSharpFieldType), cSharpMethodName)
				.WithModifiers(TokenList(
					Token(SyntaxKind.PublicKeyword)))
				.WithParameterList(ParameterList(SingletonSeparatedList(
					Parameter(Identifier("index"))
						.WithType(PredefinedType(Token(SyntaxKind.IntKeyword)))
						.WithDefault(EqualsValueClause(LiteralExpression(
							SyntaxKind.NumericLiteralExpression,
							Literal(0)))))))
				.WithBody(body);
		}

		private static CXType GetClangBaseType(CXType typeClang)
		{
			switch (typeClang.TypeClass)
			{
				case CX_TypeClass.CX_TypeClass_Pointer:
				{
					var pointeeType = GetClangBaseType(typeClang.PointeeType);
					return pointeeType;
				}

				case CX_TypeClass.CX_TypeClass_Typedef:
				{
					var underlyingType = GetClangBaseType(typeClang.Declaration.TypedefDeclUnderlyingType);
					return underlyingType;
				}

				case CX_TypeClass.CX_TypeClass_Elaborated:
				{
					var elaboratedType = GetClangBaseType(typeClang.NamedType);
					return elaboratedType;
				}

				case CX_TypeClass.CX_TypeClass_ConstantArray:
				{
					var elementType = GetClangBaseType(typeClang.ArrayElementType);
					return elementType;
				}

				case CX_TypeClass.CX_TypeClass_Enum:
				case CX_TypeClass.CX_TypeClass_Record:
				case CX_TypeClass.CX_TypeClass_Builtin:
				{
					return typeClang;
				}

				case CX_TypeClass.CX_TypeClass_FunctionProto:
				{
					// TODO: Function pointers
					return default;
				}

				default:
					throw new NotImplementedException();
			}
		}

		// private TypeSyntax GetCSharpType(CXType clangType, CXType? baseClangType = null)
		// {
		// 	if (_cSharpNamesByClangCursor.TryGetValue(clangType.Declaration, out var cSharpName))
		// 	{
		// 		return ParseTypeName(cSharpName);
		// 	}
		//
		// 	if (baseClangType == null)
		// 	{
		// 		baseClangType = GetClangBaseType(clangType);
		// 	}
		//
		// 	string baseClangTypeSpelling;
		// 	// TODO: Function pointers
		// 	if (baseClangType.Value == default)
		// 	{
		// 		baseClangTypeSpelling = "void";
		// 	}
		// 	else
		// 	{
		// 		baseClangTypeSpelling = ClangTypeToCSharpTypeString(baseClangType.Value);
		// 	}
		//
		// 	if (clangType.Declaration.IsAnonymous)
		// 	{
		// 		cSharpName = ClangName(clangType.Declaration);
		// 	}
		// 	else
		// 	{
		// 		var cSharpType = clangType.TypeClass switch
		// 		{
		// 			CX_TypeClass.CX_TypeClass_Pointer when baseClangType.Value == default => PointerType(ParseTypeName(baseClangTypeSpelling)),
		// 			CX_TypeClass.CX_TypeClass_Typedef when baseClangType.Value == default => PointerType(ParseTypeName(baseClangTypeSpelling)),
		// 			CX_TypeClass.CX_TypeClass_Pointer => PointerType(GetCSharpType(clangType.PointeeType, baseClangType)),
		// 			CX_TypeClass.CX_TypeClass_Typedef => ParseTypeName(clangType.Spelling.CString
		// 				.Replace("const ", string.Empty).Trim()),
		// 			CX_TypeClass.CX_TypeClass_Enum => ParseTypeName(baseClangTypeSpelling.Replace("enum ", string.Empty)
		// 				.Trim()),
		// 			CX_TypeClass.CX_TypeClass_Record => ParseTypeName(baseClangTypeSpelling.Replace("struct ", string.Empty)
		// 				.Trim()),
		// 			CX_TypeClass.CX_TypeClass_Elaborated => GetCSharpType(clangType.NamedType, baseClangType),
		// 			CX_TypeClass.CX_TypeClass_Builtin => ParseTypeName(ClangTypeToCSharpTypeString(baseClangType!.Value)),
		// 			CX_TypeClass.CX_TypeClass_ConstantArray => ParseTypeName(baseClangTypeSpelling),
		// 			_ => throw new NotImplementedException()
		// 		};
		//
		// 		return cSharpType;
		// 	}
		//
		// 	return ParseTypeName(cSharpName);
		// }

		private static bool IsValidCSharpTypeSpellingForFixedBuffer(string typeString)
		{
			return typeString switch
			{
				"bool" => true,
				"byte" => true,
				"char" => true,
				"short" => true,
				"int" => true,
				"long" => true,
				"sbyte" => true,
				"ushort" => true,
				"uint" => true,
				"ulong" => true,
				"float" => true,
				"double" => true,
				_ => false
			};
		}

		// private string ClangName(CXCursor cursor)
		// {
		// 	if (_cSharpNamesByClangCursor.TryGetValue(cursor, out var name))
		// 	{
		// 		return name;
		// 	}
		//
		// 	if (cursor.IsInSystem())
		// 	{
		// 		var systemUnderlyingType = cursor.Type.CanonicalType;
		// 		if (systemUnderlyingType.TypeClass == CX_TypeClass.CX_TypeClass_Builtin)
		// 		{
		// 			name = ClangTypeToCSharpTypeString(systemUnderlyingType);
		// 		}
		// 		else
		// 		{
		// 			throw new NotImplementedException();
		// 		}
		// 	}
		// 	else if (cursor.IsAnonymous)
		// 	{
		// 		var fieldName = string.Empty;
		// 		var parent = cursor.SemanticParent;
		// 		parent.VisitChildren((child, _) =>
		// 		{
		// 			if (child.kind == CXCursorKind.CXCursor_FieldDecl && child.Type.Declaration == cursor)
		// 			{
		// 				fieldName = child.Spelling.CString;
		// 			}
		// 		});
		//
		// 		if (cursor.kind == CXCursorKind.CXCursor_UnionDecl)
		// 		{
		// 			name = $"Anonymous_Union_{fieldName}";
		// 		}
		// 		else
		// 		{
		// 			name = $"Anonymous_Struct_{fieldName}";
		// 		}
		// 	}
		// 	else
		// 	{
		// 		name = cursor.Spelling.CString;
		// 		if (string.IsNullOrEmpty(name))
		// 		{
		// 			var clangType = cursor.Type;
		// 			if (clangType.kind == CXTypeKind.CXType_Pointer)
		// 			{
		// 				clangType = clangType.PointeeType;
		// 			}
		//
		// 			name = clangType.Spelling.CString;
		// 		}
		// 	}
		//
		// 	_cSharpNamesByClangCursor.Add(cursor, name);
		// 	return name;
		// }

		private static string SanitizeIdentifierName(string name)
		{
			var result = name;

			if (name == "lock" || name == "string" || name == "base" || name == "ref")
			{
				result = $"@{name}";
			}

			return result;
		}

		public void AddSystemType(CXCursor cursor, string name)
		{
			// var type = cursor.Type;
			// var underlyingSystemType = type.CanonicalType;
			// if (underlyingSystemType.TypeClass == CX_TypeClass.CX_TypeClass_Builtin)
			// {
			// 	var cSharpName = ClangTypeToCSharpTypeString(underlyingSystemType);
			// 	_cSharpNamesByClangCursor.Add(cursor, cSharpName);
			// }
			// else
			// {
			// 	if (name == "FILE")
			// 	{
			// 		_cSharpNamesByClangCursor.Add(cursor, "IntPtr");
			// 	}
			// 	else
			// 	{
			// 		throw new NotImplementedException();
			// 	}
			// }
		}

		private static CallingConvention CSharpCallingConvention(CFunctionCallingConvention callingConvention)
		{
			return callingConvention switch
			{
				CFunctionCallingConvention.Unknown => CallingConvention.Winapi,
				CFunctionCallingConvention.C => CallingConvention.Cdecl,
				_ => throw new ArgumentOutOfRangeException(nameof(callingConvention), callingConvention, null)
			};
		}
	}
}
