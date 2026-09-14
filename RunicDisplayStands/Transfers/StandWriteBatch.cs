using System;
using System.Collections.Generic;
using System.Linq;

namespace RunicDisplayStands
{
    // Prepare every byte before changing a stand. Only local ZDO setters run in
    // the commit; visual RPCs run later and cannot turn a committed move into a failure.
    internal sealed class StandWriteBatch
    {
        private readonly ZNetView _view;
        private readonly ZDO _zdo;
        private readonly List<Action> _writes = new List<Action>();
        private readonly List<Action> _undo = new List<Action>();
        private readonly List<Func<bool>> _checks = new List<Func<bool>>();
        internal StandWriteBatch(ZNetView view) { _view = view; _zdo = view.GetZDO(); }

        internal void Int(int key, int value)
        {
            int old = _zdo.GetInt(key, 0);
            _writes.Add(() => _zdo.Set(key, value));
            _undo.Add(() => _zdo.Set(key, old));
            _checks.Add(() => _zdo.GetInt(key, 0) == value);
        }
        internal void ClearLegacy(int key)
        {
            string old = _zdo.GetString(key, string.Empty);
            _writes.Add(() => _zdo.RemoveString(key));
            _undo.Add(() => { if (old.Length > 0) _zdo.Set(key, old); else _zdo.RemoveString(key); });
            _checks.Add(() => _zdo.GetString(key, string.Empty).Length == 0);
        }
        internal void Bytes(int key, byte[] value)
        {
            byte[] old = (_zdo.GetByteArray(key, null) ?? Array.Empty<byte>()).ToArray();
            byte[] prepared = value.ToArray();
            _writes.Add(() => _zdo.Set(key, prepared));
            _undo.Add(() => _zdo.Set(key, old));
            _checks.Add(() => (_zdo.GetByteArray(key, null) ?? Array.Empty<byte>()).SequenceEqual(prepared));
        }
        internal void Item(ItemDrop.ItemData item, int index = -1)
        {
            string prefix = index < 0 ? "" : index + "_";
            int identityKey = (prefix + "item").GetStableHashCode();
            Int(identityKey, item == null ? 0 : item.m_dropPrefab.name.GetStableHashCode());
            ClearLegacy(identityKey);
            Int((prefix + "variant").GetStableHashCode(), item?.m_variant ?? 0);
            Int((prefix + "quality").GetStableHashCode(), item?.m_quality ?? 1);
            var native = new ZPackage();
            if (item != null)
            {
                native.Write((byte)global::Version.c_ItemDataVersion);
                item.Save(native);
            }
            Bytes((prefix + "itemData").GetStableHashCode(), item == null ? Array.Empty<byte>() : native.GetArray());
            int customKey = (index < 0 ? "RunicDisplayStands_itemdata" : "RunicDisplayStands_itemdata_" + index).GetStableHashCode();
            Bytes(customKey, item == null ? Array.Empty<byte>() : ItemDataSerializer.Serialize(item));
        }
        internal bool Commit()
        {
            if (!_view.IsValid() || !_view.IsOwner() || !ReferenceEquals(_view.GetZDO(), _zdo)) return false;
            try
            {
                foreach (var write in _writes) write();
                if (!_checks.All(check => check())) throw new InvalidOperationException("Stand write verification failed.");
                return true;
            }
            catch
            {
                // Do not force ownership or overwrite another peer's newer state.
                if (_view.IsValid() && _view.IsOwner() && ReferenceEquals(_view.GetZDO(), _zdo))
                    for (int i = _undo.Count - 1; i >= 0; i--) _undo[i]();
                throw;
            }
        }
    }
}
