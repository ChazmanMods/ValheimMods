using System;
using System.Collections.Generic;
using RunicCrafting.Domain;

namespace RunicCrafting.Tests
{
    internal sealed class FakeMaterialSource : IMutableMaterialSource
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _quantities;

        internal FakeMaterialSource(
            string sourceId,
            MaterialSourceKind kind,
            float distanceSquared,
            IDictionary<string, int> quantities)
        {
            SourceId = sourceId;
            Kind = kind;
            DistanceSquared = distanceSquared;
            _quantities = new Dictionary<string, int>(quantities, StringComparer.Ordinal);
        }

        public string SourceId { get; }
        internal MaterialSourceKind Kind { get; }
        internal float DistanceSquared { get; }
        internal bool FailRestore { get; set; }
        internal bool FailNextTake { get; set; }

        public MaterialSourceSnapshot Snapshot()
        {
            lock (_gate)
                return new MaterialSourceSnapshot(
                    SourceId,
                    Kind,
                    DistanceSquared,
                    new Dictionary<string, int>(_quantities, StringComparer.Ordinal));
        }

        public bool TryTake(string resourceId, int quantity, out IMaterialRestoreToken restoreToken)
        {
            lock (_gate)
            {
                restoreToken = null;
                if (FailNextTake)
                {
                    FailNextTake = false;
                    return false;
                }
                _quantities.TryGetValue(resourceId, out int existing);
                if (quantity <= 0 || existing < quantity) return false;
                _quantities[resourceId] = existing - quantity;
                restoreToken = new Token(this, resourceId, quantity);
                return true;
            }
        }

        internal int Quantity(string resourceId)
        {
            lock (_gate) return _quantities.TryGetValue(resourceId, out int value) ? value : 0;
        }

        private void Restore(string resourceId, int quantity)
        {
            lock (_gate)
            {
                _quantities.TryGetValue(resourceId, out int existing);
                _quantities[resourceId] = checked(existing + quantity);
            }
        }

        private sealed class Token : IMaterialRestoreToken
        {
            private readonly FakeMaterialSource _source;
            private readonly string _resourceId;
            private readonly int _quantity;
            private bool _active = true;

            internal Token(FakeMaterialSource source, string resourceId, int quantity)
            {
                _source = source;
                _resourceId = resourceId;
                _quantity = quantity;
            }

            public bool Restore()
            {
                if (_source.FailRestore) return false;
                if (!_active) return true;
                _active = false;
                _source.Restore(_resourceId, _quantity);
                return true;
            }
        }
    }
}
