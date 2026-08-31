using System;
using System.Collections.Generic;

namespace RunicAgriculture.Core
{
    public sealed class ReplantConfirmation<TPosition>
    {
        private readonly int _maximumPositions;
        private string _cropId;
        private IReadOnlyList<TPosition> _positions = Array.Empty<TPosition>();

        public ReplantConfirmation(int maximumPositions)
        {
            if (maximumPositions < 1) throw new ArgumentOutOfRangeException(nameof(maximumPositions));
            _maximumPositions = maximumPositions;
        }

        public bool IsPending => _cropId != null;
        public string CropId => _cropId;
        public IReadOnlyList<TPosition> Positions => _positions;

        public void Offer(string cropId, IEnumerable<TPosition> positions)
        {
            if (string.IsNullOrWhiteSpace(cropId))
                throw new ArgumentException("A crop identifier is required.", nameof(cropId));
            if (positions == null) throw new ArgumentNullException(nameof(positions));

            var copy = new List<TPosition>(_maximumPositions);
            foreach (TPosition position in positions)
            {
                if (copy.Count >= _maximumPositions) break;
                copy.Add(position);
            }
            if (copy.Count == 0)
            {
                Clear();
                return;
            }

            _cropId = cropId.Trim();
            _positions = copy.AsReadOnly();
        }

        public bool TryConfirm(string cropId, out IReadOnlyList<TPosition> positions, out string reasonCode)
        {
            positions = Array.Empty<TPosition>();
            if (!IsPending)
            {
                reasonCode = AgricultureReasonCodes.ReplantNotOffered;
                return false;
            }
            if (!string.Equals(_cropId, cropId, StringComparison.Ordinal))
            {
                reasonCode = AgricultureReasonCodes.ReplantCropMismatch;
                return false;
            }

            positions = _positions;
            reasonCode = AgricultureReasonCodes.Valid;
            Clear();
            return true;
        }

        public void Clear()
        {
            _cropId = null;
            _positions = Array.Empty<TPosition>();
        }
    }
}
