using System;
using RunicPermissions.Groups;

namespace RunicPermissions.Tests
{
    internal static class GroupFriendlyProtocolTests
    {
        internal static void Register()
        {
            TestRunner.Run("friendly Group request protocol round trips bounded commands", RequestRoundTrip);
            TestRunner.Run("friendly Group request protocol rejects malformed bytes", RequestRejectsMalformed);
            TestRunner.Run("friendly Group response carries exact active UUID", ResponseRoundTrip);
            TestRunner.Run("friendly Group command shapes reject ambiguous input", ShapeValidation);
        }

        private static void RequestRoundTrip()
        {
            Guid id = Guid.ParseExact("51515151515151515151515151515151", "N");
            var values = new[]
            {
                new GroupFriendlyRequest(GroupFriendlyOperation.List),
                new GroupFriendlyRequest(GroupFriendlyOperation.Create, "Builders", proposedGroupId: id),
                new GroupFriendlyRequest(GroupFriendlyOperation.Select, "Builders"),
                new GroupFriendlyRequest(GroupFriendlyOperation.Invite, "Charles", number: 168),
                new GroupFriendlyRequest(GroupFriendlyOperation.SetRole, "Charles", "officer")
            };
            for (int index = 0; index < values.Length; index++)
            {
                byte[] exact = GroupFriendlyProtocol.EncodeRequest(values[index]);
                TestAssert.True(GroupFriendlyProtocol.TryDecodeRequest(
                    exact, out GroupFriendlyRequest decoded, out string reason));
                TestAssert.Equal("ok", reason);
                TestAssert.Equal(values[index].Operation, decoded.Operation);
                TestAssert.Equal(values[index].Primary, decoded.Primary);
                TestAssert.Equal(values[index].Secondary, decoded.Secondary);
                TestAssert.Equal(values[index].Number, decoded.Number);
                TestAssert.Equal(values[index].ProposedGroupId, decoded.ProposedGroupId);
            }
        }

        private static void RequestRejectsMalformed()
        {
            var valid = GroupFriendlyProtocol.EncodeRequest(
                new GroupFriendlyRequest(GroupFriendlyOperation.Select, "Builders"));
            var trailing = new byte[valid.Length + 1];
            Buffer.BlockCopy(valid, 0, trailing, 0, valid.Length);
            TestAssert.False(GroupFriendlyProtocol.TryDecodeRequest(
                trailing, out _, out _));
            TestAssert.False(GroupFriendlyProtocol.TryDecodeRequest(null, out _, out _));
            TestAssert.False(GroupFriendlyProtocol.TryDecodeRequest(
                new byte[GroupFriendlyProtocol.MaximumRequestBytes + 1], out _, out _));
        }

        private static void ResponseRoundTrip()
        {
            const string id = "61616161616161616161616161616161";
            var response = new GroupFriendlyResponse(
                "Active Group: Builders.",
                new ActiveGroupSelection(
                    ActiveGroupSelectionStatus.Available, id, "Builders"));
            byte[] exact = GroupFriendlyProtocol.EncodeResponse(response);
            TestAssert.True(GroupFriendlyProtocol.TryDecodeResponse(
                exact, out GroupFriendlyResponse decoded, out string reason));
            TestAssert.Equal("ok", reason);
            TestAssert.Equal(response.Text, decoded.Text);
            TestAssert.True(decoded.Active.IsAvailable);
            TestAssert.Equal(id, decoded.Active.GroupId);
            TestAssert.Equal("Builders", decoded.Active.DisplayName);
        }

        private static void ShapeValidation()
        {
            TestAssert.Throws<ArgumentException>(() =>
                new GroupFriendlyRequest(GroupFriendlyOperation.Invite, "Charles"));
            TestAssert.Throws<ArgumentException>(() =>
                new GroupFriendlyRequest(GroupFriendlyOperation.SetRole, "Charles", "owner"));
            TestAssert.Throws<ArgumentException>(() =>
                new GroupFriendlyRequest(GroupFriendlyOperation.Select, " Builders "));
            TestAssert.Throws<ArgumentException>(() =>
                new GroupFriendlyRequest(GroupFriendlyOperation.List, "extra"));
        }
    }
}
