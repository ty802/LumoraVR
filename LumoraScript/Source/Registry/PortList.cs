// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Lumora.Script;

// A node's ports. A list behind an interface boxed its enumerator on every foreach, and the editor walks the
// ports of every node of an open graph each frame; this one's foreach allocates nothing. -xlinka
public sealed class PortList : IReadOnlyList<NodePort>
{
    private readonly NodePort[] _ports;

    public PortList(IEnumerable<NodePort> ports)
    {
        _ports = ports.ToArray();
    }

    public int Count => _ports.Length;

    public NodePort this[int index] => _ports[index];

    public Enumerator GetEnumerator() => new(_ports);

    IEnumerator<NodePort> IEnumerable<NodePort>.GetEnumerator() => ((IEnumerable<NodePort>)_ports).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _ports.GetEnumerator();

    public struct Enumerator
    {
        private readonly NodePort[] _ports;
        private int _index;

        internal Enumerator(NodePort[] ports)
        {
            _ports = ports;
            _index = -1;
        }

        public bool MoveNext() => ++_index < _ports.Length;

        public NodePort Current => _ports[_index];
    }
}
