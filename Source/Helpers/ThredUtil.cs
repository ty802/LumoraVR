using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
namespace Aquamarine.Source.Helpers
{
    internal static class ThredUtil
    {
        public static void RunOnNodeSync(this Node GDobject, Action action)
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);
            Action thisAction = () =>
            {
                action();
                semaphore.Release();
            };
            var tree = GDobject.GetTree();
            tree.PhysicsFrame += thisAction;
            semaphore.Wait();
            tree.PhysicsFrame -= thisAction;

        }
        public static async void RunOnNodeAsync(this Node GDobject, Action action)
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);
            Action thisAction = () =>
            {
                action();
                semaphore.Release();
            };
            var tree = GDobject.GetTree();
            tree.PhysicsFrame += thisAction;
            await semaphore.WaitAsync();
            tree.PhysicsFrame -= thisAction;

        }
    }
}
