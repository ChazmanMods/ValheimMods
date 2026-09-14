using System;
using System.Threading;
using System.Threading.Tasks;

namespace RunicCharacterVault
{
    public interface ICharacterRestoreProvider
    {
        Task<CharacterRestoreResult> RestoreAsync(
            string platformPlayerId, string playerName, CancellationToken cancellationToken);
    }

    public sealed class CharacterRestoreResult
    {
        public CharacterRestoreStatus Status { get; }
        public byte[] Profile { get; }

        private CharacterRestoreResult(CharacterRestoreStatus status, byte[] profile)
        {
            Status = status;
            Profile = profile;
        }

        public static CharacterRestoreResult Restored(byte[] profile) =>
            new CharacterRestoreResult(CharacterRestoreStatus.Restored,
                profile ?? throw new ArgumentNullException(nameof(profile)));

        public static CharacterRestoreResult NotFound() =>
            new CharacterRestoreResult(CharacterRestoreStatus.NotFound, null);

        public static CharacterRestoreResult Failed() =>
            new CharacterRestoreResult(CharacterRestoreStatus.Failed, null);
    }

    public enum CharacterRestoreStatus { Restored, NotFound, Failed }

    public static class CharacterRestoreApi
    {
        private static readonly object Sync = new object();
        private static ICharacterRestoreProvider _provider;

        public static void Register(ICharacterRestoreProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (Sync)
            {
                if (_provider != null && !ReferenceEquals(_provider, provider))
                    throw new InvalidOperationException("A character restore provider is already registered.");
                _provider = provider;
            }
        }

        public static bool Unregister(ICharacterRestoreProvider provider)
        {
            lock (Sync)
            {
                if (!ReferenceEquals(_provider, provider)) return false;
                _provider = null;
                return true;
            }
        }

        internal static ICharacterRestoreProvider GetProvider()
        {
            lock (Sync) return _provider;
        }
    }
}
