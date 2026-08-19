// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Nexus.Protocol;

public class DeltaValidationResult
{
    public bool IsValid { get; set; }
    public string RejectionReason { get; set; } = null!;
    public object CorrectedValue { get; set; } = null!;

    public static DeltaValidationResult Accept()
    {
        return new DeltaValidationResult { IsValid = true };
    }

    public static DeltaValidationResult Reject(string reason, object correctedValue = null!)
    {
        return new DeltaValidationResult
        {
            IsValid = false,
            RejectionReason = reason,
            CorrectedValue = correctedValue
        };
    }
}
