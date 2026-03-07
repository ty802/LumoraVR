// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

/// <summary>
/// Marks component category for editor organization.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ComponentCategoryAttribute : Attribute
{
    public string Category { get; }

    public ComponentCategoryAttribute(string category)
    {
        Category = category;
    }
}