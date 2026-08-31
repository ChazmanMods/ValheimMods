using System;
using System.Diagnostics;
using RunicAwareness.Core;
using RunicAwareness.Integration;

namespace RunicAwareness.Tests
{
    internal static class CoreBehaviorTests
    {
        internal static void Register()
        {
            TestRunner.Run("timer buckets round upward deterministically", TimerBucketsRoundUp);
            TestRunner.Run("timer formatter covers minutes and hours", TimerFormattingIsStable);
            TestRunner.Run("timer input is finite and hard bounded", TimerInputIsBounded);
            TestRunner.Run("sanitizer strips tags controls and bidi", SanitizerStripsUnsafeText);
            TestRunner.Run("sanitizer bounds adversarial whitespace input", SanitizerBoundsAdversarialInput);
            TestRunner.Run("panel cache reuses identical composition", PanelCacheReusesComposition);
            TestRunner.Run(
                "panel modes and default left-middle layout are exact",
                PanelModesAndLeftMiddleLayoutAreExact);
            TestRunner.Run(
                "legacy TopRight anchor migration is one-time and idempotent",
                LegacyAnchorMigrationIsOneTime);
            TestRunner.Run(
                "anchor migration preserves every custom value",
                AnchorMigrationPreservesCustomValues);
            TestRunner.Run("panel composition is line and character bounded", PanelCompositionIsBounded);
            TestRunner.Run("panel separators cannot cross character ceiling", PanelSeparatorAccountingIsExact);
            TestRunner.Run("item comparison is deterministic and bounded", ItemComparisonIsBounded);
            TestRunner.Run("unsupported values remain raw rather than interpreted", UnsupportedValuesStayRaw);
            TestRunner.Run("localization tokens and outputs are allocation bounded", LocalizationIsBounded);
            TestRunner.Run("detailed building disclosure requires avatar reach", DetailsRequireAvatarReach);
            TestRunner.Run("building details deny any hostile ward overlap", HostileWardOverlapDenies);
            TestRunner.Run("item type policy covers vanilla and raw modded values", ItemTypesAreTruthful);
        }

        private static void TimerBucketsRoundUp()
        {
            TestAssert.Equal(1, TimerFormatter.Bucket(0.01f, AwarenessTimerPrecision.WholeSecond));
            TestAssert.Equal(5, TimerFormatter.Bucket(0.01f, AwarenessTimerPrecision.FiveSeconds));
            TestAssert.Equal(10, TimerFormatter.Bucket(5.01f, AwarenessTimerPrecision.TenSeconds));
            TestAssert.Equal(60, TimerFormatter.Bucket(1f, AwarenessTimerPrecision.WholeMinute));
        }

        private static void TimerFormattingIsStable()
        {
            TestAssert.Equal("0:00", TimerFormatter.Format(0));
            TestAssert.Equal("1:05", TimerFormatter.Format(65));
            TestAssert.Equal("1:01:01", TimerFormatter.Format(3661));
        }

        private static void TimerInputIsBounded()
        {
            TestAssert.Equal(0, TimerFormatter.Bucket(float.NaN, AwarenessTimerPrecision.WholeSecond));
            TestAssert.Equal(0, TimerFormatter.Bucket(float.PositiveInfinity, AwarenessTimerPrecision.WholeSecond));
            TestAssert.Equal(0, TimerFormatter.Bucket(-1f, AwarenessTimerPrecision.WholeSecond));
            TestAssert.Equal(
                TimerFormatter.MaximumDisplaySeconds,
                TimerFormatter.Bucket(float.MaxValue, AwarenessTimerPrecision.WholeSecond));
        }

        private static void SanitizerStripsUnsafeText()
        {
            string actual = BoundedText.Sanitize(
                "<color=red>Wolf</color>\u202E\u2066\u0001\t Ready\r\nNext",
                80,
                2);
            TestAssert.Equal("Wolf Ready\nNext", actual);
            TestAssert.DoesNotContain("color", actual);
        }

        private static void SanitizerBoundsAdversarialInput()
        {
            string huge = new string('\u202E', 2_000_000);
            var watch = Stopwatch.StartNew();
            string actual = BoundedText.Sanitize(huge, 64, 2);
            watch.Stop();
            TestAssert.True(actual.Length <= 64);
            TestAssert.True(watch.Elapsed < TimeSpan.FromSeconds(1),
                "Sanitizer inspected an unbounded hostile input.");
        }

        private static void PanelCacheReusesComposition()
        {
            var cache = new PanelCache();
            TestAssert.True(cache.Set(AwarenessPanel.Food, "Bread: 1:00"));
            TestAssert.True(cache.Compose(false));
            string first = cache.ComposedText;
            TestAssert.False(cache.Set(AwarenessPanel.Food, "Bread: 1:00"));
            TestAssert.False(cache.Compose(false));
            TestAssert.True(ReferenceEquals(first, cache.ComposedText),
                "Identical state allocated a new composition string.");
        }

        private static void PanelModesAndLeftMiddleLayoutAreExact()
        {
            var cache = new PanelCache();
            cache.Set(AwarenessPanel.Food, "Bread");
            cache.Set(AwarenessPanel.ItemComparison, "Sword");
            cache.Compose(false);
            TestAssert.Contains("Food", cache.ComposedText);
            TestAssert.DoesNotContain("Item comparison", cache.ComposedText);
            cache.Compose(true);
            TestAssert.Contains("Item comparison", cache.ComposedText);
            TestAssert.DoesNotContain("Food", cache.ComposedText);

            UnityEngine.Rect safe = new UnityEngine.Rect(10f, 40f, 1000f, 900f);
            UnityEngine.Rect panel = AwarenessRuntime.AnchoredRect(
                safe,
                1080f,
                200f,
                100f,
                12f,
                AwarenessOverlayAnchor.MiddleLeft);
            TestAssert.Equal(22f, panel.x);
            TestAssert.Equal(540f, panel.y);

            UnityEngine.Rect topRight = AwarenessRuntime.AnchoredRect(
                safe, 1080f, 200f, 100f, 12f, AwarenessOverlayAnchor.TopRight);
            TestAssert.Equal(798f, topRight.x);
            TestAssert.Equal(152f, topRight.y);
            UnityEngine.Rect topLeft = AwarenessRuntime.AnchoredRect(
                safe, 1080f, 200f, 100f, 12f, AwarenessOverlayAnchor.TopLeft);
            TestAssert.Equal(22f, topLeft.x);
            TestAssert.Equal(152f, topLeft.y);
            UnityEngine.Rect bottomLeft = AwarenessRuntime.AnchoredRect(
                safe, 1080f, 200f, 100f, 12f, AwarenessOverlayAnchor.BottomLeft);
            TestAssert.Equal(22f, bottomLeft.x);
            TestAssert.Equal(928f, bottomLeft.y);
            UnityEngine.Rect bottomRight = AwarenessRuntime.AnchoredRect(
                safe, 1080f, 200f, 100f, 12f, AwarenessOverlayAnchor.BottomRight);
            TestAssert.Equal(798f, bottomRight.x);
            TestAssert.Equal(928f, bottomRight.y);

        }

        private static void LegacyAnchorMigrationIsOneTime()
        {
            AwarenessOverlayAnchor first = AwarenessConfig.MigrateLegacyAnchor(
                AwarenessOverlayAnchor.TopRight,
                false);
            TestAssert.Equal(AwarenessOverlayAnchor.MiddleLeft, first);
            TestAssert.Equal(first, AwarenessConfig.MigrateLegacyAnchor(first, true));
            TestAssert.Equal(
                AwarenessOverlayAnchor.TopRight,
                AwarenessConfig.MigrateLegacyAnchor(AwarenessOverlayAnchor.TopRight, true));
        }

        private static void AnchorMigrationPreservesCustomValues()
        {
            foreach (AwarenessOverlayAnchor custom in new[]
                     {
                         AwarenessOverlayAnchor.TopLeft,
                         AwarenessOverlayAnchor.BottomRight,
                         AwarenessOverlayAnchor.BottomLeft,
                         AwarenessOverlayAnchor.MiddleLeft
                     })
            {
                TestAssert.Equal(custom, AwarenessConfig.MigrateLegacyAnchor(custom, false));
                TestAssert.Equal(custom, AwarenessConfig.MigrateLegacyAnchor(custom, true));
            }
        }

        private static void PanelCompositionIsBounded()
        {
            var cache = new PanelCache();
            string oversized = new string('x', AwarenessConfig.HardMaximumPanelCharacters);
            for (int index = 0; index <= (int)AwarenessPanel.TamedAnimal; index++)
                cache.Set((AwarenessPanel)index, oversized);
            cache.Compose(false);
            TestAssert.True(cache.ComposedText.Length <= AwarenessConfig.HardMaximumPanelCharacters);
            TestAssert.True(cache.ComposedLines <= AwarenessConfig.HardMaximumPanelLines);
        }

        private static void PanelSeparatorAccountingIsExact()
        {
            var cache = new PanelCache();
            cache.Set(AwarenessPanel.Food, new string('x', 2034));
            cache.Set(AwarenessPanel.Effects, new string('y', 2034));
            cache.Compose(false);
            TestAssert.True(cache.ComposedText.Length <= AwarenessConfig.HardMaximumPanelCharacters,
                "A two-character panel separator was omitted from capacity accounting.");
        }

        private static void ItemComparisonIsBounded()
        {
            var selected = new ItemMetrics(
                "Iron Sword", "OneHandedWeapon", 3, 72f, 0f, 20f, -5f,
                0.8f, 92f, 100f, "Swords", 43f);
            var equipped = new ItemMetrics(
                "Bronze Sword", "OneHandedWeapon", 2, 50f, 0f, 18f, -5f,
                0.7f, 80f, 90f, "Swords", 43f);
            string first = ItemComparisonFormatter.Format(selected, equipped, false);
            string second = ItemComparisonFormatter.Format(selected, equipped, false);
            TestAssert.Equal(first, second);
            TestAssert.True(BoundedText.CountLines(first) <= ItemComparisonFormatter.MaximumLines);
            TestAssert.Contains("Damage (item): 72 (+22)", first);
            TestAssert.Contains("Skill: Swords 43", first);
        }

        private static void UnsupportedValuesStayRaw()
        {
            var modded = new ItemMetrics(
                "Modded Relic", "ForeignType42", 7, 12.5f, 3.5f, 0f, 2f,
                4.2f, 0f, 0f, "ForeignSkill", 9.5f);
            string text = ItemComparisonFormatter.Format(modded, null, false);
            TestAssert.Contains("Type: ForeignType42", text);
            TestAssert.Contains("Damage (item): 12.5", text);
            TestAssert.DoesNotContain("DPS", text);
            TestAssert.DoesNotContain("predicted", text);
        }

        private static void LocalizationIsBounded()
        {
            string huge = new string('X', 2_000_000);
            TestAssert.False(BoundedLocalization.TryGetTranslationKey(huge, out _));
            string label = BoundedLocalization.Label(huge);
            TestAssert.True(label.Length <= BoundedText.MaximumLabelCharacters);
            TestAssert.True(BoundedLocalization.TryGetTranslationKey(
                "$item_wood", out string key));
            TestAssert.Equal("item_wood", key);
            TestAssert.False(BoundedLocalization.TryGetTranslationKey("$KEY_Use", out _));
            TestAssert.False(BoundedLocalization.TryGetTranslationKey("$item_<size>", out _));
        }

        private static void DetailsRequireAvatarReach()
        {
            TestAssert.True(ContextDisclosurePolicy.AllowsDetailedDisclosure(16f, 5f));
            TestAssert.False(ContextDisclosurePolicy.AllowsDetailedDisclosure(36f, 5f));
            TestAssert.False(ContextDisclosurePolicy.AllowsDetailedDisclosure(121f, 100f));
            TestAssert.False(ContextDisclosurePolicy.AllowsDetailedDisclosure(float.NaN, 5f));
        }

        private static void ItemTypesAreTruthful()
        {
            foreach (string equipment in new[]
                     {
                         "Helmet", "Chest", "Legs", "Hands", "Shoulder", "Utility",
                         "Trinket", "Ammo", "OneHandedWeapon", "TwoHandedWeapon",
                         "TwoHandedWeaponLeft", "Attach_Atgeir", "Bow", "Shield", "Tool", "Torch"
                     })
                TestAssert.Equal(
                    ItemComparisonDisposition.CompareKnownEquipment,
                    ItemTypePolicy.Classify(equipment));
            TestAssert.Equal(
                ItemComparisonDisposition.HideKnownNonEquipment,
                ItemTypePolicy.Classify("Material"));
            TestAssert.Equal(
                ItemComparisonDisposition.ShowRawUnsupported,
                ItemTypePolicy.Classify("ForeignType42"));
        }

        private static void HostileWardOverlapDenies()
        {
            TestAssert.True(StrictWardDisclosure.IsHostileOverlap(true, true, false));
            TestAssert.False(StrictWardDisclosure.IsHostileOverlap(true, true, true));
            TestAssert.False(StrictWardDisclosure.IsHostileOverlap(false, true, false));
            TestAssert.False(StrictWardDisclosure.IsHostileOverlap(true, false, false));
        }
    }
}
