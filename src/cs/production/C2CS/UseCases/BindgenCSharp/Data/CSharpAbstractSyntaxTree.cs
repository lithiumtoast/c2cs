// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Collections.Immutable;

namespace C2CS.UseCases.BindgenCSharp
{
    public readonly struct CSharpAbstractSyntaxTree
    {
        public readonly ImmutableArray<CSharpFunction> FunctionExterns;

        public readonly ImmutableArray<CSharpFunctionPointer> FunctionPointers;

        public readonly ImmutableArray<CSharpStruct> Structs;

        public readonly ImmutableArray<CSharpTypedef> Typedefs;

        public readonly ImmutableArray<CSharpOpaqueType> OpaqueDataTypes;

        public readonly ImmutableArray<CSharpEnum> Enums;

        public CSharpAbstractSyntaxTree(
            ImmutableArray<CSharpFunction> functionExterns,
            ImmutableArray<CSharpFunctionPointer> functionPointers,
            ImmutableArray<CSharpStruct> structs,
            ImmutableArray<CSharpTypedef> typedefs,
            ImmutableArray<CSharpOpaqueType> opaqueDataTypes,
            ImmutableArray<CSharpEnum> enums)
        {
            FunctionExterns = functionExterns;
            FunctionPointers = functionPointers;
            Structs = structs;
            Typedefs = typedefs;
            OpaqueDataTypes = opaqueDataTypes;
            Enums = enums;
        }
    }
}
