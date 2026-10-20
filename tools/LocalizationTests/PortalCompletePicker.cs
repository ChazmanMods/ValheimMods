using System.Collections.Generic;
partial class PickerFixture
{
        private void CompletePicker(List<PickerCandidate> candidates, bool truncated)
        {
            PickerSession session = _mapPicker;
            if (session == null) return;
            session.Candidates.AddRange(candidates ?? new List<PickerCandidate>());
            if (session.Candidates.Count == 0)
            {
                CancelMapPicker(
                    global::Runic.Localization.RunicText.Get("text_5449d5e12c2e") +
                    session.NetworkId + "'.", true);
                return;
            }
            CaptureAndEnablePickerIcon(session);
            foreach (PickerCandidate candidate in session.Candidates)
            {
                candidate.Pin = session.Map.AddPin(
                    candidate.Position,
                    Minimap.PinType.Icon3,
                    candidate.Label,
                    false,
                    false);
                if (candidate.Pin == null)
                {
                    CancelMapPicker(global::Runic.Localization.RunicText.Get("text_96b2b222ad70"), true);
                    return;
                }
                PortalMapMarkerSprite.Apply(candidate.Pin);
                candidate.Pin.m_doubleSize = true;
                candidate.Pin.m_animate = true;
            }
            // The directory can finish after the player pans or zooms. Add pins without changing that view.
            Message(session.Player,
                session.Candidates.Count + global::Runic.Localization.RunicText.Get("text_3f57fc99c65e") +
                (session.Candidates.Count == 1 ? string.Empty : "s") +
                global::Runic.Localization.RunicText.Get("text_ad4abfd7ce03") +
                (truncated ? global::Runic.Localization.RunicText.Get("text_a899fb11b1be") : string.Empty));
        }

}
