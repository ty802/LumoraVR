using System;
using Godot;

namespace Aquamarine.Source.Management.Data
{
    /// <summary>
    /// Describes a locally hosted world that can be shown in the session browser.
    /// </summary>
    public sealed class LocalWorldInfo
    {
        public LocalWorldInfo(string worldId, string displayName, Texture2D preview, Action joinAction, string description = "")
        {
            WorldId = worldId;
            DisplayName = displayName;
            Preview = preview;
            JoinAction = joinAction ?? throw new ArgumentNullException(nameof(joinAction));
            Description = description;
        }

        /// <summary>
        /// Unique identifier for this world entry.
        /// </summary>
        public string WorldId { get; }

        /// <summary>
        /// Display name shown in the session browser.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Optional description for the world.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Preview image for the world.
        /// </summary>
        public Texture2D Preview { get; }

        /// <summary>
        /// Action to execute when the user selects "Join" on this world.
        /// </summary>
        public Action JoinAction { get; }
    }
}
