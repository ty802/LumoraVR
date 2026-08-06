// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Networking.Sync;

public interface IValidatable
{
    MessageValidity ValidateChange(ValidationContext context);
}

public class ValidationContext
{
    public BinaryMessageBatch Message { get; }

    public User SenderUser => (Message?.SenderUser) ?? null!;

    public SyncElement Element { get; }

    public List<ValidationGroup.Rule> Rules { get; }

    public World World { get; }

    public bool IsAuthority => World?.IsAuthority == true;

    public Dictionary<string, object> Data { get; }

    public ValidationContext(
        BinaryMessageBatch message,
        SyncElement element,
        List<ValidationGroup.Rule> rules,
        World world)
    {
        Message = message;
        Element = element;
        Rules = rules ?? new List<ValidationGroup.Rule>();
        World = world;
        Data = new Dictionary<string, object>();
    }

    public static MessageValidity Accept() => MessageValidity.Valid;

    public static MessageValidity Conflict() => MessageValidity.Conflict;

    public static MessageValidity Ignore() => MessageValidity.Ignore;
}

[System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
public class ValidatedAttribute : System.Attribute
{
    public double? Min { get; set; }

    public double? Max { get; set; }

    public bool AllowNull { get; set; } = true;

    public System.Type ValidatorType { get; set; } = null!;
}
