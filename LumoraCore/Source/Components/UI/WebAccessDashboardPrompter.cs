// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Web;

namespace Lumora.Core.Components.UI;

// Puts web access questions on this machine's dashboard, in userspace, where only the person at this
// machine sees them and nothing reaches a session world. A closed dash is opened for the question,
// because a prompt nobody can see is a request that silently times out.
//
// Called from any thread. Everything that touches the dash is posted onto the userspace world's own
// update with RunSynchronously and re-checked there, since the dash can go away between the post and
// the run. -xlinka
public sealed class WebAccessDashboardPrompter : IWebAccessPrompter
{
    public bool CanAsk
    {
        get
        {
            var dash = UserspaceDashboard.LocalInstance;
            var world = dash?.World;
            return dash != null && !dash.IsDestroyed && world != null && !world.IsDisposed && world.LocalUser != null;
        }
    }

    public bool Show(WebAccessQuestion question)
    {
        var dash = UserspaceDashboard.LocalInstance;
        var world = dash?.World;
        if (dash == null || dash.IsDestroyed || world == null || world.IsDisposed)
            return false;
        world.RunSynchronously(() => Present(dash, question));
        return true;
    }

    public void Withdraw(WebAccessQuestion question)
    {
        var dash = UserspaceDashboard.LocalInstance;
        var world = dash?.World;
        if (dash == null || world == null || world.IsDisposed)
            return;
        world.RunSynchronously(() =>
        {
            if (dash.IsDestroyed)
                return;
            var dashboard = dash.Dashboard;
            if (dashboard != null && !dashboard.IsDestroyed)
                dashboard.Slot.GetComponent<WebAccessPrompt>()?.Withdraw(question);
        });
    }

    private static void Present(UserspaceDashboard dash, WebAccessQuestion question)
    {
        // Timed out or withdrawn while the post was queued.
        if (!question.IsOpen)
            return;

        var dashboard = dash.IsDestroyed ? null : dash.Dashboard;
        var prompt = dash.World?.LocalUser != null ? WebAccessPrompt.For(dashboard) : null;
        if (prompt == null)
        {
            question.CannotShow();
            return;
        }

        bool opened = !dash.IsOpen.Value;
        if (opened)
            dash.Open();
        prompt.Present(question, opened);
    }
}
