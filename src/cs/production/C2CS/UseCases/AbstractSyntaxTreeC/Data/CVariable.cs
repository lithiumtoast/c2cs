// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Text.Json.Serialization;

namespace C2CS.UseCases.AbstractSyntaxTreeC
{
    public record CVariable : CNode
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"Record '{Name}': {Type} @ {Location.ToString()}";
        }
    }
}
