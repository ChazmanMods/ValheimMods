using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RunicCrafting.Domain
{
    // UI answers only. A fixed, short window also bounds the lifetime of watched game objects.
    internal sealed class PreviewAnswerCache<TKey, TValue> where TValue : class
    {
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        private readonly object _gate = new object();
        private readonly Dictionary<TKey, TValue> _answers = new Dictionary<TKey, TValue>();
        private readonly HashSet<object> _watched = new HashSet<object>(new ReferenceComparer());
        private readonly int _maximumAnswers, _maximumWatched;
        private double _started, _expires;
        private bool _active, _cacheable;
        private long _epoch;
        internal long Epoch { get { lock (_gate) return _epoch; } }
        internal int Count { get { lock (_gate) return _answers.Count; } }
        internal int WatchedCount { get { lock (_gate) return _watched.Count; } }
        internal const double WindowSeconds = 0.25;

        internal PreviewAnswerCache(int maximumAnswers = 512, int maximumWatched = 2048)
        {
            if (maximumAnswers < 1 || maximumWatched < 1) throw new ArgumentOutOfRangeException();
            _maximumAnswers = maximumAnswers; _maximumWatched = maximumWatched;
        }

        internal bool BeginWindow(double now)
        {
            lock (_gate)
            {
                if (_active && now >= _started && now < _expires) return false;
                Reset();
                _active = _cacheable = true;
                _started = now; _expires = now + WindowSeconds;
                return true;
            }
        }

        internal void Expire(double now)
        {
            lock (_gate)
                if (_active && (now < _started || now >= _expires)) Reset();
        }

        internal void Watch(object dependency)
        {
            if (ReferenceEquals(dependency, null)) return;
            lock (_gate)
            {
                if (!_active || !_cacheable || _watched.Contains(dependency)) return;
                if (_watched.Count >= _maximumWatched)
                {
                    Invalidate();
                    _cacheable = false;
                    return;
                }
                _watched.Add(dependency);
            }
        }

        internal void Changed(object dependency)
        {
            if (ReferenceEquals(dependency, null)) return;
            lock (_gate)
                if (_watched.Contains(dependency)) Invalidate();
        }

        internal bool TryGet(TKey key, double now, out TValue answer)
        {
            lock (_gate)
            {
                answer = null;
                return _active && _cacheable && now >= _started && now < _expires &&
                    _answers.TryGetValue(key, out answer);
            }
        }

        internal void Store(TKey key, TValue answer, double now, long expectedEpoch)
        {
            lock (_gate)
            {
                if (!_active || !_cacheable || now < _started || now >= _expires ||
                    expectedEpoch != _epoch || answer == null) return;
                if (_answers.Count >= _maximumAnswers && !_answers.ContainsKey(key)) return;
                _answers[key] = answer;
            }
        }

        internal void Invalidate()
        {
            lock (_gate) { _answers.Clear(); _epoch++; }
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _answers.Clear(); _watched.Clear(); _active = _cacheable = false; _epoch++;
            }
        }

        internal static bool TrySignature(IEnumerable<MaterialRequirement> requirements, out string signature)
        {
            signature = null;
            if (requirements == null) return false;
            try
            {
                var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int count = 0;
                foreach (MaterialRequirement requirement in requirements)
                {
                    if (requirement == null || ++count > ExactMaterialPlanner.MaximumRequirements ||
                        requirement.ResourceId.Length > 256) return false;
                    totals.TryGetValue(requirement.ResourceId, out int previous);
                    totals[requirement.ResourceId] = checked(previous + requirement.Quantity);
                }
                if (count == 0) return false;
                var builder = new StringBuilder();
                foreach (var pair in totals)
                {
                    builder.Append(pair.Key.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                        .Append(pair.Key).Append(':').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
                    if (builder.Length > 16384) return false;
                }
                signature = builder.ToString();
                return true;
            }
            catch (OverflowException) { return false; }
        }
    }
}
