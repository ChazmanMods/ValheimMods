using System;
using System.Collections.Generic;
using RunicSafety.Api;

namespace RunicSafety.Services
{
    public sealed class ProtectedItemPolicy : IProtectedItemPolicy
    {
        private const int MaximumProviders = 16;
        private readonly object _sync = new object();
        private readonly List<ProviderEntry> _providers = new List<ProviderEntry>();
        private readonly ISafetyDiagnosticService _diagnostics;
        private readonly Func<bool> _rareConfirmationEnabled;
        private readonly Func<bool> _administratorBypassEnabled;
        private long _token;

        public ProtectedItemPolicy(
            ISafetyDiagnosticService diagnostics,
            Func<bool> rareConfirmationEnabled,
            Func<bool> administratorBypassEnabled)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _rareConfirmationEnabled = rareConfirmationEnabled ?? throw new ArgumentNullException(nameof(rareConfirmationEnabled));
            _administratorBypassEnabled = administratorBypassEnabled ?? throw new ArgumentNullException(nameof(administratorBypassEnabled));
        }

        public bool HasExternalProvider
        {
            get { lock (_sync) return _providers.Count != 0; }
        }

        public int ProviderCount
        {
            get { lock (_sync) return _providers.Count; }
        }

        public IDisposable RegisterProvider(IItemProtectionProvider provider, int priority = 0)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (!IsIdentifier(provider.ProviderId))
                throw new ArgumentException("ProviderId must be a lowercase dotted identifier.", nameof(provider));
            lock (_sync)
            {
                if (_providers.Count >= MaximumProviders)
                    throw new InvalidOperationException("The protected-item provider limit is 16.");
                foreach (ProviderEntry existing in _providers)
                    if (string.Equals(existing.Provider.ProviderId, provider.ProviderId, StringComparison.Ordinal))
                        throw new InvalidOperationException("Provider already registered: " + provider.ProviderId);
                long token = ++_token;
                _providers.Add(new ProviderEntry(provider, priority, token));
                _providers.Sort(CompareProviders);
                return new Registration(this, token);
            }
        }

        public ItemProtectionDecision Evaluate(ItemProtectionRequest request)
        {
            string correlation = request?.CorrelationId;
            if (string.IsNullOrWhiteSpace(correlation))
                correlation = _diagnostics.NewCorrelationId("protect");
            if (request?.Item == null || string.IsNullOrWhiteSpace(request.Item.StableItemId))
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.Deny, ProtectionReason.InvalidRequest));

            if (request.Administrator && _administratorBypassEnabled())
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.Allow, ProtectionReason.AdministratorBypass));

            if (request.Item.Equipped)
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.Deny, ProtectionReason.Equipped));
            if (request.Item.QuestItem)
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.Deny, ProtectionReason.QuestItem));
            if (request.Item.LockState == ItemLockState.Locked)
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.Deny, ProtectionReason.Locked));

            if (request.ExternalInventoryCapabilityAdvertised &&
                request.Item.LockState == ItemLockState.Unknown)
            {
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.Deny, ProtectionReason.ProviderUnavailable));
            }

            ProviderEntry[] providers;
            lock (_sync) providers = _providers.ToArray();

            ItemProtectionDecision strongest = null;
            foreach (ProviderEntry provider in providers)
            {
                ItemProtectionDecision decision;
                try { decision = provider.Provider.Evaluate(request); }
                catch (Exception)
                {
                    return Record(correlation, new ItemProtectionDecision(
                        ProtectionOutcome.Deny,
                        ProtectionReason.ProviderFailure,
                        provider.Provider.ProviderId));
                }
                if (decision == null)
                    return Record(correlation, new ItemProtectionDecision(
                        ProtectionOutcome.Deny,
                        ProtectionReason.ProviderFailure,
                        provider.Provider.ProviderId));
                if (decision.Outcome == ProtectionOutcome.Deny)
                    return Record(correlation, decision);
                if (decision.Outcome == ProtectionOutcome.RequireConfirmation && strongest == null)
                    strongest = decision;
            }

            if (strongest != null) return Record(correlation, strongest);
            if (request.Item.ConfiguredRare && _rareConfirmationEnabled())
                return Record(correlation, new ItemProtectionDecision(
                    ProtectionOutcome.RequireConfirmation,
                    ProtectionReason.ConfiguredRareItem));
            return Record(correlation, new ItemProtectionDecision(
                ProtectionOutcome.Allow, ProtectionReason.None));
        }

        private ItemProtectionDecision Record(string correlation, ItemProtectionDecision decision)
        {
            SafetyDiagnosticSeverity severity = decision.Outcome == ProtectionOutcome.Deny
                ? SafetyDiagnosticSeverity.Warning
                : SafetyDiagnosticSeverity.Information;
            _diagnostics.Record(
                correlation,
                "protected-item",
                decision.Outcome.ToString().ToLowerInvariant() + "-" +
                decision.Reason.ToString().ToLowerInvariant(),
                severity);
            return decision;
        }

        private void Unregister(long token)
        {
            lock (_sync) _providers.RemoveAll(entry => entry.Token == token);
        }

        private static int CompareProviders(ProviderEntry left, ProviderEntry right)
        {
            int priority = right.Priority.CompareTo(left.Priority);
            if (priority != 0) return priority;
            int id = StringComparer.Ordinal.Compare(left.Provider.ProviderId, right.Provider.ProviderId);
            return id != 0 ? id : left.Token.CompareTo(right.Token);
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 96) return false;
            bool segmentStart = true;
            foreach (char character in value)
            {
                if (character == '.')
                {
                    if (segmentStart) return false;
                    segmentStart = true;
                    continue;
                }
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9') && character != '-') return false;
                segmentStart = false;
            }
            return !segmentStart;
        }

        private readonly struct ProviderEntry
        {
            internal ProviderEntry(IItemProtectionProvider provider, int priority, long token)
            {
                Provider = provider;
                Priority = priority;
                Token = token;
            }
            internal IItemProtectionProvider Provider { get; }
            internal int Priority { get; }
            internal long Token { get; }
        }

        private sealed class Registration : IDisposable
        {
            private ProtectedItemPolicy _owner;
            private readonly long _token;
            internal Registration(ProtectedItemPolicy owner, long token)
            {
                _owner = owner;
                _token = token;
            }
            public void Dispose()
            {
                ProtectedItemPolicy owner = _owner;
                _owner = null;
                owner?.Unregister(_token);
            }
        }
    }
}
