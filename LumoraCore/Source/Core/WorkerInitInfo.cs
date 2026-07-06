// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lumora.Core;

public sealed class WorkerInitInfo
{
    public FieldInfo[] SyncMemberFields = null!;
    public MethodInfo[] ListedMethods = null!;
    public Dictionary<string, int> ListedMethodNameToIndex = null!;
    public bool[] SyncMemberNonpersistent = null!;
    public bool[] SyncMemberNondrivable = null!;
    public bool[] SyncMemberDontCopy = null!;
    public string[] SyncMemberNames = null!;
    public Dictionary<string, int> SyncMemberNameToIndex = null!;
    public Dictionary<string, List<string>> OldSyncMemberNames = null!;
    public object[] DefaultValues = null!;
    public Type ConnectorType = null!;
    public bool HasUpdateMethods;
    public bool HasAudioUpdateMethod;
    public bool HasAudioConfigurationChangedMethod;
    public bool HasLinkedMethod;
    public bool HasUnlinkedMethod;
    public bool ReceivesAnyWorldEvent;
    public bool[] ReceivesWorldEvent = null!;
    public bool PreserveWithAssets;
    public bool SingleInstancePerSlot;
    public bool DontDuplicate;
    public int DefaultUpdateOrder;
    public bool RegisterGlobally;
}
