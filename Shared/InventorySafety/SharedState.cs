using System;
using System.Collections.Generic;
namespace RunicAutomation
{
    // Store only framework types here: each DLL has its own copy of our CLR types.
    internal static class SharedState
    {
        private const string Key = "Runic.InventorySafety.Embedded.v1";
        internal static readonly object Sync = string.Intern(Key);
        internal static T Get<T>(string name, Func<T> create) where T : class
        {
            lock (Sync)
            {
                var state = AppDomain.CurrentDomain.GetData(Key) as Dictionary<string, object>;
                if (state == null)
                {
                    state = new Dictionary<string, object>();
                    AppDomain.CurrentDomain.SetData(Key, state);
                }
                if (!state.TryGetValue(name, out object value)) state[name] = value = create();
                return (T)value;
            }
        }
    }
}
