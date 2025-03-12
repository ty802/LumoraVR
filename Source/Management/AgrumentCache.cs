using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aquamarine.Source.Logging;
using Godot;

namespace Aquamarine.Source.Management
{
    public partial class AgrumentCache : Node
    {
        private readonly Dictionary<string, string> _cache = new();
        public override void _Ready()
        {
            base._Ready();
            IEnumerator<string> args = OS.GetCmdlineArgs().GetEnumerator() as IEnumerator<string>;
            string key = null;
            StringBuilder value = new();
            do
            {
                if (args.Current.StartsWith("-"))
                {
                    if(!string.IsNullOrEmpty(key))
                    {
                        _cache.Add(key, value.ToString());
                        key = null;
                        value.Clear();
                    }
                    if (args.Current.Contains("="))
                    {
                        string[] split = args.Current.Split('=');
                        key = split[0];
                        if (split.Length > 1)
                            value.Append(split[1]);

                    }
                    else
                        key = args.Current;
                    continue;
                }
                value.Append(args.Current);
            } while (args.MoveNext());
            foreach (var pair in _cache)
            {
                Logger.Debug($"Key: {pair.Key}, Value: {pair.Value}");
            }
        }
    }
}
