// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory (https://github.com/lithiumtoast/c2cs) for full license information.

namespace C2CS.Bindgen.ExploreCCode
{
    public readonly struct GenericCodeValue
    {
        public readonly string Name;
        public readonly long Value;

        public GenericCodeValue(
            string name,
            long value)
        {
            Name = name;
            Value = value;
        }
    }
}