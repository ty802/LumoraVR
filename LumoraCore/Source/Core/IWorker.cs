using System;

namespace Lumora.Core;

/// <summary>
/// Interface for all workers in the world (Slots, Components, etc.)
/// </summary>
public interface IWorker : IWorldElement
{
    /// <summary>
    /// Type of this worker
    /// </summary>
    Type WorkerType { get; }
    
    /// <summary>
    /// Whether this worker is persistent
    /// </summary>
    bool IsPersistent { get; }
}
