using Aquamarine.Source.Management;
using Godot;
using System;

namespace Aquamarine.Source.Management.Client.UI;

public partial class SessionInstance : HBoxContainer
{
    private RichTextLabel _detailsText;
    private RichTextLabel _sessionUsersLabel;
    private string _id;
    private WorldsTab _tab;
    private Action _joinAction;
    private bool _isLocal;

    public override void _Ready()
    {
        base._Ready();
        _detailsText = GetNode("%DetailsText") as RichTextLabel;
        _sessionUsersLabel = GetNode("%PlayersText") as RichTextLabel;
    }

    /// <summary>
    /// Updates the session instance with the given session info.
    /// </summary>
    internal void UpdateData(SessionInfo info, WorldsTab tab)
    {
        _id = info.SessionIdentifier;
        _tab = tab;
        _isLocal = false;
#if DEBUG
        _detailsText.Text = info.SessionIdentifier;
#else
        _detailsText.Text = info.Name;
#endif
        if (_sessionUsersLabel != null)
        {
            _sessionUsersLabel.Text = info.Direct ? "Direct session" : "Relay session";
        }
        _joinAction = () =>
        {
            if (!string.IsNullOrEmpty(_id))
            {
                _tab?.joinSession(_id);
            }
        };
    }

    /// <summary>
    /// Updates this instance to represent a local world entry.
    /// </summary>
    internal void UpdateLocal(string title, string subtitle, Action joinAction)
    {
        _id = null;
        _tab = null;
        _isLocal = true;
        _detailsText.Text = title;
        if (_sessionUsersLabel != null)
        {
            _sessionUsersLabel.Text = subtitle;
        }
        _joinAction = joinAction;
    }

    public void OnJoinButtonPressed()
    {
        _joinAction?.Invoke();
    }

    public void OnPortalButtonPressed()
    {
        // Portals for local worlds are not yet implemented.
    }
}
