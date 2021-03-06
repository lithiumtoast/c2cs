// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using JetBrains.Annotations;

namespace C2CS
{
    [PublicAPI]
    public class DiagnosticPanic : Diagnostic
    {
        public string? StackTrace { get; }

        public DiagnosticPanic(Exception exception)
            : base(DiagnosticSeverity.Panic)
        {
            Summary = exception.Message;
            StackTrace = exception.StackTrace;
        }

        public override string ToString()
        {
            return $"{GetDiagnosticSeverityShortString()}: {Summary}\n{StackTrace}";
        }
    }
}
