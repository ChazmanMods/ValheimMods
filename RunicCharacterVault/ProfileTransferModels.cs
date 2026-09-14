namespace RunicCharacterVault
{
    internal sealed class VaultSession
    {
        internal VaultSession(string accountId, long characterId, string name, bool newCharacter)
        {
            AccountId = accountId;
            CharacterId = characterId;
            Name = name;
            NewCharacter = newCharacter;
        }

        internal string AccountId { get; }
        internal long CharacterId { get; }
        internal string Name { get; }
        internal bool NewCharacter { get; }
        internal ServerProfileSessionState State { get; } = new ServerProfileSessionState();
        internal bool Enrolling { get; set; }
    }

    internal sealed class PendingCommit
    {
        internal PendingCommit(ZRpc rpc, VaultSession session, string requestId, byte[] data)
        {
            Rpc = rpc;
            Session = session;
            RequestId = requestId;
            Data = data;
        }

        internal byte[] Data { get; }
        internal ZRpc Rpc { get; }
        internal string RequestId { get; }
        internal VaultSession Session { get; }
    }
}
