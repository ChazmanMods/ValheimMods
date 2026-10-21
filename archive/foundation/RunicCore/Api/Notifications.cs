using System;
using System.Collections.Generic;

namespace Runic.Foundation.Core
{
    public enum NotificationSeverity
    {
        Information = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>An actionable notification request. Both reason and remedy are mandatory.</summary>
    public sealed class NotificationRequest
    {
        public NotificationRequest(
            string sourceModuleId,
            string code,
            NotificationSeverity severity,
            string reason,
            string remedy,
            string deduplicationKey = null,
            TimeSpan? minimumInterval = null)
        {
            SourceModuleId = RunicIdentifier.Require(sourceModuleId, nameof(sourceModuleId));
            Code = RunicIdentifier.Require(code, nameof(code));
            if (!Enum.IsDefined(typeof(NotificationSeverity), severity))
                throw new ArgumentOutOfRangeException(nameof(severity));
            Reason = RequireText(reason, nameof(reason));
            Remedy = RequireText(remedy, nameof(remedy));
            DeduplicationKey = NormalizeDeduplicationKey(deduplicationKey);
            if (minimumInterval.HasValue && minimumInterval.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(minimumInterval));
            MinimumInterval = minimumInterval;
        }

        public string SourceModuleId { get; }
        public string Code { get; }
        public NotificationSeverity Severity { get; }
        public string Reason { get; }
        public string Remedy { get; }
        public string DeduplicationKey { get; }
        public TimeSpan? MinimumInterval { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Actionable notification text is required.", parameterName);
            string trimmed = value.Trim();
            if (trimmed.Length > 2048)
                throw new ArgumentException("Notification text cannot exceed 2048 characters.", parameterName);
            return trimmed;
        }

        private static string NormalizeDeduplicationKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length > 256)
                throw new ArgumentException(
                    "Notification deduplication keys cannot exceed 256 characters.",
                    nameof(value));
            return trimmed;
        }
    }

    public sealed class PublishedNotification
    {
        internal PublishedNotification(NotificationRequest request, DateTimeOffset publishedAt)
        {
            Request = request;
            PublishedAt = publishedAt;
        }

        public NotificationRequest Request { get; }
        public DateTimeOffset PublishedAt { get; }
        public string SourceModuleId => Request.SourceModuleId;
        public string Code => Request.Code;
        public NotificationSeverity Severity => Request.Severity;
        public string Reason => Request.Reason;
        public string Remedy => Request.Remedy;
        public string DeduplicationKey => Request.DeduplicationKey;
    }

    public readonly struct NotificationPublishResult
    {
        internal NotificationPublishResult(
            bool published,
            PublishedNotification notification,
            TimeSpan retryAfter)
        {
            Published = published;
            Notification = notification;
            RetryAfter = retryAfter;
        }

        public bool Published { get; }
        public bool RateLimited => !Published;
        public PublishedNotification Notification { get; }
        public TimeSpan RetryAfter { get; }
    }

    public sealed class NotificationPublishedEventArgs : EventArgs
    {
        internal NotificationPublishedEventArgs(PublishedNotification notification)
        {
            Notification = notification;
        }

        public PublishedNotification Notification { get; }
    }

    /// <summary>
    /// Thread-safe notification bus with per-source/code/context throttling. Subscriber failures
    /// are isolated so an optional HUD or logger cannot break the publishing module.
    /// </summary>
    public sealed class NotificationBus
    {
        private readonly object _sync = new object();
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly int _maxTrackedKeys;
        private readonly Dictionary<NotificationKey, DateTimeOffset> _lastPublished =
            new Dictionary<NotificationKey, DateTimeOffset>();
        private TimeSpan _defaultMinimumInterval;

        public NotificationBus()
            : this(() => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10), 1024)
        {
        }

        public NotificationBus(
            Func<DateTimeOffset> utcNow,
            TimeSpan defaultMinimumInterval,
            int maxTrackedKeys = 1024)
        {
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (defaultMinimumInterval < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(defaultMinimumInterval));
            if (maxTrackedKeys < 1)
                throw new ArgumentOutOfRangeException(nameof(maxTrackedKeys));
            _defaultMinimumInterval = defaultMinimumInterval;
            _maxTrackedKeys = maxTrackedKeys;
        }

        public event EventHandler<NotificationPublishedEventArgs> Published;

        public TimeSpan DefaultMinimumInterval
        {
            get { lock (_sync) return _defaultMinimumInterval; }
            set
            {
                if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
                lock (_sync) _defaultMinimumInterval = value;
            }
        }

        public int TrackedRateLimitKeyCount
        {
            get { lock (_sync) return _lastPublished.Count; }
        }

        public NotificationPublishResult Publish(NotificationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            PublishedNotification notification;
            lock (_sync)
            {
                DateTimeOffset now = _utcNow();
                TimeSpan interval = _defaultMinimumInterval;
                if (request.MinimumInterval.HasValue && request.MinimumInterval.Value > interval)
                    interval = request.MinimumInterval.Value;
                NotificationKey key = new NotificationKey(
                    request.SourceModuleId,
                    request.Code,
                    request.DeduplicationKey);
                if (_lastPublished.TryGetValue(key, out DateTimeOffset previous))
                {
                    TimeSpan elapsed = now >= previous ? now - previous : TimeSpan.Zero;
                    if (elapsed < interval)
                    {
                        return new NotificationPublishResult(
                            false,
                            null,
                            interval - elapsed);
                    }
                }

                if (!_lastPublished.ContainsKey(key) && _lastPublished.Count >= _maxTrackedKeys)
                    RemoveOldestKeyLocked();
                _lastPublished[key] = now;
                notification = new PublishedNotification(request, now);
            }

            RaisePublished(notification);
            return new NotificationPublishResult(true, notification, TimeSpan.Zero);
        }

        public bool ResetRateLimit(
            string sourceModuleId,
            string code,
            string deduplicationKey = null)
        {
            RunicIdentifier.Require(sourceModuleId, nameof(sourceModuleId));
            RunicIdentifier.Require(code, nameof(code));
            NotificationKey key = new NotificationKey(
                sourceModuleId,
                code,
                string.IsNullOrWhiteSpace(deduplicationKey) ? string.Empty : deduplicationKey.Trim());
            lock (_sync) return _lastPublished.Remove(key);
        }

        public void ClearRateLimits()
        {
            lock (_sync) _lastPublished.Clear();
        }

        private void RemoveOldestKeyLocked()
        {
            bool found = false;
            NotificationKey oldestKey = default;
            DateTimeOffset oldestTime = default;
            foreach (KeyValuePair<NotificationKey, DateTimeOffset> pair in _lastPublished)
            {
                if (!found || pair.Value < oldestTime ||
                    pair.Value == oldestTime && pair.Key.CompareTo(oldestKey) < 0)
                {
                    found = true;
                    oldestKey = pair.Key;
                    oldestTime = pair.Value;
                }
            }
            if (found) _lastPublished.Remove(oldestKey);
        }

        private void RaisePublished(PublishedNotification notification)
        {
            EventHandler<NotificationPublishedEventArgs> handlers = Published;
            if (handlers == null) return;
            NotificationPublishedEventArgs arguments =
                new NotificationPublishedEventArgs(notification);
            foreach (EventHandler<NotificationPublishedEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, arguments); }
                catch (Exception) { }
            }
        }

        private readonly struct NotificationKey : IEquatable<NotificationKey>, IComparable<NotificationKey>
        {
            internal NotificationKey(string sourceModuleId, string code, string deduplicationKey)
            {
                SourceModuleId = sourceModuleId;
                Code = code;
                DeduplicationKey = deduplicationKey;
            }

            internal string SourceModuleId { get; }
            internal string Code { get; }
            internal string DeduplicationKey { get; }

            public bool Equals(NotificationKey other) =>
                string.Equals(SourceModuleId, other.SourceModuleId, StringComparison.Ordinal) &&
                string.Equals(Code, other.Code, StringComparison.Ordinal) &&
                string.Equals(DeduplicationKey, other.DeduplicationKey, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is NotificationKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(SourceModuleId);
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Code);
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(DeduplicationKey);
                    return hash;
                }
            }

            public int CompareTo(NotificationKey other)
            {
                int comparison = StringComparer.Ordinal.Compare(SourceModuleId, other.SourceModuleId);
                if (comparison != 0) return comparison;
                comparison = StringComparer.Ordinal.Compare(Code, other.Code);
                return comparison != 0
                    ? comparison
                    : StringComparer.Ordinal.Compare(DeduplicationKey, other.DeduplicationKey);
            }
        }
    }
}
