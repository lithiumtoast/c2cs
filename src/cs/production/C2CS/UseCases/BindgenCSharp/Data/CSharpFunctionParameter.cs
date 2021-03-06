// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

namespace C2CS.UseCases.BindgenCSharp
{
    public record CSharpFunctionParameter : CSharpNode
    {
        public readonly CSharpType Type;

        public CSharpFunctionParameter(
            string name,
            string codeLocationComment,
            CSharpType type)
            : base(name, codeLocationComment)
        {
            Type = type;
        }

        // Required for debugger string with records
        // ReSharper disable once RedundantOverriddenMember
        public override string ToString()
        {
            return base.ToString();
        }
    }
}
