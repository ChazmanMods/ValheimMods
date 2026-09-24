using System.Collections.Generic;
using System.Linq;

namespace RunicCharacterVault
{
    internal interface ICharacterProfileCatalog
    {
        bool HasProfile(string accountId);
    }

    internal enum CharacterAdmission
    {
        ExistingProfile,
        NewEnrollment,
        RejectUnregisteredProfile,
        RejectAdditionalCharacter,
        RejectConcurrentEnrollment
    }

    internal static class CharacterAdmissionPolicy
    {
        internal static CharacterAdmission Decide(bool hasStoredProfile, bool createdThisSession,
            bool allowMultipleCharacters, bool accountHasProfile, bool enrollmentAvailable,
            bool allowExistingCharacters = false)
        {
            if (hasStoredProfile)
            {
                return CharacterAdmission.ExistingProfile;
            }
            if (!createdThisSession && !allowExistingCharacters)
            {
                return CharacterAdmission.RejectUnregisteredProfile;
            }
            if (!allowMultipleCharacters && accountHasProfile)
            {
                return CharacterAdmission.RejectAdditionalCharacter;
            }
            return enrollmentAvailable ? CharacterAdmission.NewEnrollment
                : CharacterAdmission.RejectConcurrentEnrollment;
        }
    }

    internal static class CharacterAdmissionMessages
    {
        internal static string ForRejection(CharacterAdmission admission,
            IReadOnlyList<string> existingProfileNames)
        {
            if (admission == CharacterAdmission.RejectAdditionalCharacter &&
                existingProfileNames != null && existingProfileNames.Count > 0)
            {
                string names = string.Join(", ", existingProfileNames
                    .Select(name => name.ToUpperInvariant()));
                return existingProfileNames.Count == 1
                    ? global::Runic.Localization.RunicText.Format("text_cf24dc035c05", names)
                    : global::Runic.Localization.RunicText.Format("text_b99199e4c3cc", names);
            }
            if (admission != CharacterAdmission.RejectUnregisteredProfile)
            {
                return CharacterRejectionMessages.AdditionalCharacterDenied;
            }

            if (existingProfileNames == null || existingProfileNames.Count == 0)
            {
                return global::Runic.Localization.RunicText.Get("text_25ddcadc5976");
            }

            string message = existingProfileNames.Count == 1
                ? "Create a new character or use the previously used one: "
                : "Create a new character or use one of the previously used ones: ";
            return message + string.Join(", ", existingProfileNames
                .Select(name => name.ToUpperInvariant())) + ".";
        }
    }

    internal sealed class CharacterAdmissionEvaluator
    {
        private readonly ICharacterProfileCatalog _profiles;

        internal CharacterAdmissionEvaluator(ICharacterProfileCatalog profiles)
        {
            _profiles = profiles;
        }

        internal CharacterAdmission Decide(bool hasStoredProfile, string accountId,
            bool createdThisSession, bool allowMultipleCharacters, bool enrollmentAvailable,
            bool allowExistingCharacters = false, bool isServerAdmin = false)
        {
            allowMultipleCharacters |= isServerAdmin;
            bool accountHasProfile = !hasStoredProfile &&
                !allowMultipleCharacters && _profiles.HasProfile(accountId);
            return CharacterAdmissionPolicy.Decide(hasStoredProfile, createdThisSession,
                allowMultipleCharacters, accountHasProfile, enrollmentAvailable, allowExistingCharacters);
        }
    }

    internal sealed class ServerProfileSessionState
    {
        internal bool CanSave => Verified && Admitted && Permitted;
        internal bool PermissionChecked { get; private set; }
        internal bool Verified { get; set; }
        internal bool Admitted { get; set; }
        internal bool Permitted { get; private set; }

        internal void RecordPermission(bool permitted)
        {
            PermissionChecked = true;
            Permitted = permitted;
        }
    }

    internal static class SaveAcknowledgementPolicy
    {
        internal static bool CanAcknowledge(ServerProfileSessionState session)
        {
            return session != null && session.CanSave;
        }
    }
}
