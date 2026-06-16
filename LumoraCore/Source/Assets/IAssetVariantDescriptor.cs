// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// Describes a specific variant of a URL-loaded asset (e.g. a texture's wrap/mipmap options).
/// Requests for the same URL but different descriptors resolve to different shared instances;
/// requests with equal descriptors share one. Implementations must be immutable value objects
/// with consistent <see cref="object.Equals(object)"/> / <see cref="object.GetHashCode"/> — a
/// <c>record</c> satisfies this. A null descriptor means "the one default variant".
/// </summary>
public interface IAssetVariantDescriptor
{
}
