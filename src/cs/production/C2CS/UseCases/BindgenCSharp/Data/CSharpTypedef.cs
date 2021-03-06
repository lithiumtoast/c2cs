// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

namespace C2CS.UseCases.BindgenCSharp
{
    public record CSharpTypedef : CSharpNode
    {
        public CSharpType UnderlyingType;

        public CSharpTypedef(
            string name,
            string locationComment,
            CSharpType underlyingType)
            : base(name, locationComment)
        {
            UnderlyingType = underlyingType;
        }
    }
}
