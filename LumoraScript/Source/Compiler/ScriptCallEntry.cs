// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Wasm;

namespace Lumora.Script;

// One Call Script Function node: the runtime import that starts its call, which takes the node's await
// number, the word of the flow it runs in, then the lanes of its inputs (the target first), and the Function
// it calls, by name and ports.
public sealed record ScriptCallEntry(string NodeId, string Import, WasmFuncType ImportType, string Function, FunctionSignature Signature,
    NodeOverload Overload);
