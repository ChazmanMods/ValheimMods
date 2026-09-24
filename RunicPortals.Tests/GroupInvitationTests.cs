using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RunicPermissions.Contracts;
using RunicPermissions.Groups;
using RunicPortals.Core;
using RunicPortals.Integration;

namespace RunicPortals.Tests
{
    internal static partial class Program
    {
        private static void InvitationSnapshotIsPrivateAndExpires()
        {
            var owner = new StableIdentity("valheim.player", "1");
            var invited = new StableIdentity("valheim.player", "-2");
            var stranger = new StableIdentity("valheim.player", "3");
            Guid id = Guid.NewGuid();
            var created = GroupCatalog.Empty.Create(0, id, "Builders", owner);
            var result = created.Catalog.Invite(created.Catalog.Revision, id, created.Group.Revision,
                owner, invited, 100, 1000);
            True(result.Success);
            var choices = GroupInvitationSnapshot.Select(result.Catalog, invited, 101, _ => "Host");
            Equal(1, choices.Length);
            Equal("Builders", choices[0].Name);
            Equal("Host", choices[0].Inviter);
            Equal(0, GroupInvitationSnapshot.Select(result.Catalog, stranger, 101, _ => "Host").Length);
            Equal(0, GroupInvitationSnapshot.Select(result.Catalog, owner, 101, _ => "Host").Length);
            Equal(0, GroupInvitationSnapshot.Select(result.Catalog, invited, 1000, _ => "Host").Length);
            result.Group.TryGetInvitation(invited, out var invitation);
            True(GroupInvitationSnapshot.Matches(invitation, choices[0].Revision.ToString(), 101));
            False(GroupInvitationSnapshot.Matches(invitation, (choices[0].Revision + 1).ToString(), 101));
            False(GroupInvitationSnapshot.Matches(invitation, choices[0].Revision.ToString(), 1000));
        }

        private static void InvitationSnapshotCodecIsBounded()
        {
            var choices = Enumerable.Range(0, GroupInvitationSnapshot.MaximumChoices)
                .Select(index => new GroupInvitationChoice(Guid.NewGuid().ToString("N"), "Builders " + index,
                    "Player", index + 1, 9999)).ToArray();
            string encoded = GroupInvitationSnapshot.Encode(choices);
            True(encoded.Length < 24000);
            True(GroupInvitationSnapshot.TryDecode(encoded, out var decoded));
            Equal(choices.Length, decoded.Length);
            for (int i = 0; i < choices.Length; i++)
            { Equal(choices[i].Token, decoded[i].Token); Equal(choices[i].Expires, decoded[i].Expires); }
            False(GroupInvitationSnapshot.TryDecode(encoded + "!", out _));
            False(GroupInvitationSnapshot.TryDecode(new string('a', 24001), out _));
            False(GroupInvitationSnapshot.TryDecode("That Group query is unsupported.", out _));
            False(GroupInvitationSnapshot.TryDecode(GroupInvitationSnapshot.Encode(new[] { choices[0], choices[0] }), out _));
            True(GroupInvitationSnapshot.TryDecode(GroupInvitationSnapshot.Encode(Array.Empty<GroupInvitationChoice>()), out decoded));
            Equal(0, decoded.Length);
        }

        private static void InvitationResponsesPreserveLegacyCommands()
        {
            foreach (var request in new[]
            {
                new GroupFriendlyRequest(GroupFriendlyOperation.Invitations),
                new GroupFriendlyRequest(GroupFriendlyOperation.Accept, "Builders"),
                new GroupFriendlyRequest(GroupFriendlyOperation.Decline, "Builders"),
                new GroupFriendlyRequest(GroupFriendlyOperation.Accept, Guid.NewGuid().ToString("N"), "42"),
                new GroupFriendlyRequest(GroupFriendlyOperation.Decline, Guid.NewGuid().ToString("N"), "42")
            })
            {
                True(GroupFriendlyProtocol.TryDecodeRequest(GroupFriendlyProtocol.EncodeRequest(request), out var decoded, out _));
                Equal(request.Operation, decoded.Operation); Equal(request.Secondary, decoded.Secondary);
            }
            foreach (string revision in new[] { "-1", "0", "1.5", "abc", "9223372036854775808" })
            {
                bool rejected = false;
                try { new GroupFriendlyRequest(GroupFriendlyOperation.Decline, "Builders", revision); }
                catch (ArgumentException) { rejected = true; }
                True(rejected);
            }
        }

        private static void DeclinePersistsOnlyInviteesOwnInvitation()
        {
            string root = Path.Combine(Path.GetTempPath(), "runic-invite-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                const string scope = "valheim.0000000000000005";
                long now = DateTime.UtcNow.Ticks;
                var owner = new StableIdentity("valheim.player", "1");
                var invited = new StableIdentity("valheim.player", "-2");
                var other = new StableIdentity("valheim.player", "3");
                var stranger = new StableIdentity("valheim.player", "4");
                var store = new CompatibleGroupWorldStore(root);
                var processor = new GroupCommandProcessor(store, () => scope, () => now);
                Guid id = Guid.NewGuid();
                True(processor.Execute(owner, new GroupCommand(GroupCommandKind.Create, id, "Builders")).Success);
                foreach (var who in new[] { invited, other })
                    True(processor.Execute(owner, new GroupCommand(GroupCommandKind.Invite, id,
                        target: who, invitationExpiresUtcTicks: now + TimeSpan.FromHours(1).Ticks)).Success);
                False(processor.Execute(stranger, new GroupCommand(GroupCommandKind.Decline, id)).Success);
                var before = store.Read(scope).Catalog.Groups.Single();
                before.TryGetInvitation(invited, out var invitation);
                True(processor.Execute(invited, new GroupCommand(GroupCommandKind.Decline, id), invitation.IssuedRevision).Success);
                var saved = new CompatibleGroupWorldStore(root).Read(scope).Catalog.Groups.Single();
                False(saved.TryGetInvitation(invited, out _));
                True(saved.TryGetInvitation(other, out _));
                True(saved.TryGetMember(owner, out _));
                False(saved.TryGetMember(invited, out _));
                Equal(1, saved.Members.Count);
                False(processor.Execute(invited, new GroupCommand(GroupCommandKind.Accept, id), invitation.IssuedRevision).Success);
                now += TimeSpan.FromHours(2).Ticks;
                False(processor.Execute(other, new GroupCommand(GroupCommandKind.Decline, id)).Success);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void StalePopupCannotAcceptReplacementInvitation()
        {
            string root = Path.Combine(Path.GetTempPath(), "runic-invite-stale-" + Guid.NewGuid().ToString("N"));
            try
            {
                const string scope = "valheim.0000000000000006";
                var owner = new StableIdentity("valheim.player", "1");
                var invited = new StableIdentity("valheim.player", "-2");
                var store = new CompatibleGroupWorldStore(root);
                var processor = new GroupCommandProcessor(store, () => scope);
                Guid id = Guid.NewGuid();
                True(processor.Execute(owner, new GroupCommand(GroupCommandKind.Create, id, "Builders")).Success);
                var invite = new GroupCommand(GroupCommandKind.Invite, id, target: invited,
                    invitationExpiresUtcTicks: DateTime.UtcNow.AddHours(1).Ticks);
                True(processor.Execute(owner, invite).Success);
                store.Read(scope).Catalog.Groups.Single().TryGetInvitation(invited, out var original);
                True(processor.Execute(owner, new GroupCommand(GroupCommandKind.CancelInvitation, id, target: invited)).Success);
                True(processor.Execute(owner, invite).Success);
                False(processor.Execute(invited, new GroupCommand(GroupCommandKind.Accept, id), original.IssuedRevision).Success);
                False(processor.Execute(invited, new GroupCommand(GroupCommandKind.Decline, id), original.IssuedRevision).Success);
                store.Read(scope).Catalog.Groups.Single().TryGetInvitation(invited, out var replacement);
                True(replacement.IssuedRevision > original.IssuedRevision);
                True(processor.Execute(invited, new GroupCommand(GroupCommandKind.Accept, id), replacement.IssuedRevision).Success);
                True(new CompatibleGroupWorldStore(root).Read(scope).Catalog.Groups.Single().TryGetMember(invited, out _));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void NativeInvitationDialogContractsAndInputProtection()
        {
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            foreach (string field in new[] { "instance", "popupStack", "buttonLeftText", "buttonRightText", "buttonLeft" })
                True(typeof(UnifiedPopup).GetField(field, fields) != null, "Missing native popup field " + field);
            True(typeof(YesNoPopup).GetConstructor(new[] { typeof(string), typeof(string), typeof(PopupButtonCallback),
                typeof(PopupButtonCallback), typeof(bool), typeof(bool) }) != null);
            string popup = Runic.Tests.LocalizedSource.ReadAllText(Path.Combine(ProjectRoot(), "Integration", "NativeGroupInvitationPopup.cs"));
            Contains(popup, "\"Accept\"", "\"Decline\"", "ReferenceEquals(stack.Peek(), popup)", "CapturePrimaryPointer", "SetTakeInputDelay");
            string ui = Runic.Tests.LocalizedSource.ReadAllText(Path.Combine(ProjectRoot(), "Integration", "GroupInvitationUi.cs"));
            Contains(ui, "generation != _generation", "_responding", "_handled", "IsDead()", "Chat.instance.HasFocus()", "now >= _freshUntil");
            Contains(ui, "if (!_groups.SupportsInvitationUi) return;");
            string runtime = Runic.Tests.LocalizedSource.ReadAllText(Path.Combine(ProjectRoot(), "Integration", "PortalGroupRuntime.cs"));
            Contains(runtime, "ok-invites-v1", "server.m_uid != sender", "_supportsInvitationUi = false");
            Contains(runtime, "decline require RunicPortals 1.2.4 on the server");
            string guard = Runic.Tests.LocalizedSource.ReadAllText(Path.Combine(ProjectRoot(), "Integration", "PortalEditorInputGuard.cs"));
            Contains(guard, "NativeGroupInvitationPopup.IsOpen", "JoyHotbarUse", "_primaryReleaseFrame");
        }
    }
}
