// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// The datamodel gate lives in LumoraWarden, an assembly BELOW the engine that references nothing from
// it. Keeping the policy underneath means the engine takes a hard dependency on it and cannot be built
// without it, and nothing inside the engine can reach in and rewrite the rules it is gated by.
// These aliases keep the engine-side names, so every existing call site reads exactly as it did while
// the decision itself now sits out of reach. Add an alias here rather than renaming call sites. -xlinka
global using DataModelPermissionAction = Lumora.Warden.PermissionAction;
global using DataModelPermissionSurface = Lumora.Warden.PermissionSurface;
global using DataModelPermissionResult = Lumora.Warden.PermissionResult;
global using DataModelAccessClass = Lumora.Warden.PermissionAccessClass;
global using DataModelPermissionRole = Lumora.Warden.PermissionRole;
global using DataModelPermissionRequest = Lumora.Warden.PermissionRequest;
global using IDataModelPermissionRule = Lumora.Warden.IPermissionRule;
global using WorldMode = Lumora.Warden.WorldMode;
global using DataModelPermissionToggle = Lumora.Warden.PermissionToggle;
global using DataModelPermissionDomain = Lumora.Warden.PermissionDomain;
global using DataModelDenialKind = Lumora.Warden.PermissionDenialKind;
global using DataModelViolationResponse = Lumora.Warden.PermissionViolationResponse;
global using IDataModelCopyProtection = Lumora.Warden.IPermissionCopyProtection;
