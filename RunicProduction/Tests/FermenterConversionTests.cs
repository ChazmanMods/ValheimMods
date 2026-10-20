using System;
using System.Collections.Generic;
using System.Linq;
using RunicProduction.Core;
using RunicProduction.Integration;

namespace RunicProduction.Tests
{
    internal static class FermenterConversionTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() => new[]
        {
            new KeyValuePair<string, Action>("fermenter accepts declared non-vanilla batches without clamping", DeclaredBatches),
            new KeyValuePair<string, Action>("fermenter rejects invalid batch sizes with recipe details", InvalidBatches),
            new KeyValuePair<string, Action>("fermenter still rejects ambiguous or unregistered recipes", InvalidRecipes),
            new KeyValuePair<string, Action>("fermenter stock authorization changes when batch size changes", SignatureChanges),
            new KeyValuePair<string, Action>("bad recipes do not disable unrelated fermenter recipes", IsolatedFailures),
            new KeyValuePair<string, Action>("multiple bases can share an output without inventing a stock ingredient", SharedOutputs),
            new KeyValuePair<string, Action>("fermenter uses supported live timing in stock authorization", CustomTiming)
        };

        private static FermenterConversionDescriptor Recipe(int amount, string input = "MeadBase", string output = "Mead") =>
            new FermenterConversionDescriptor(input, output, amount);

        private static void Check(bool value, string message = "Fermenter regression failed")
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void DeclaredBatches()
        {
            foreach (int amount in new[] { 1, 2, 3, 4, 6, 10, 100, StockDomainValidation.MaximumItemAmount })
            {
                Check(FermenterCompatibility.TryValidateConversions(new[] { Recipe(amount), Recipe(6, "OtherBase", "OtherMead") },
                    _ => true, out var conversions, out string failure), failure);
                var descriptor = new FermenterDescriptor("fermenter", conversions);
                Check(descriptor.TryFromInput("MeadBase", out var conversion) && conversion.OutputAmount == amount);
                Check(descriptor.TryFromOutput("Mead", out var reverse) && ReferenceEquals(reverse, conversion));
                var output = new StockOutputDefinition(conversion.OutputPrefabId, conversion.OutputAmount, 1, 0, 0, string.Empty);
                Check(output.Amount == amount);
            }
        }

        private static void InvalidBatches()
        {
            foreach (int amount in new[] { int.MinValue, -1, 0, StockDomainValidation.MaximumItemAmount + 1, int.MaxValue })
            {
                Check(!FermenterCompatibility.TryValidateConversions(new[] { Recipe(amount) }, _ => true, out _, out string failure));
                Check(failure.Contains("MeadBase") && failure.Contains("Mead") && failure.Contains(amount.ToString()), failure);
            }
        }

        private static void InvalidRecipes()
        {
            foreach (var recipes in new[]
            {
                Array.Empty<FermenterConversionDescriptor>(),
                new[] { Recipe(4), Recipe(10, "MeadBase", "OtherMead") },
                new[] { Recipe(4, "") },
                new FermenterConversionDescriptor[] { null },
                Enumerable.Range(0, 257).Select(i => Recipe(4, "Base" + i, "Mead" + i)).ToArray()
            })
                Check(!FermenterCompatibility.TryValidateConversions(recipes, _ => true, out _, out _));
            Check(!FermenterCompatibility.TryValidateConversions(null, _ => true, out _, out _));
            foreach (string missing in new[] { "MeadBase", "Mead" })
            {
                Check(!FermenterCompatibility.TryValidateConversions(new[] { Recipe(4) }, id => id != missing, out _, out string failure));
                Check(failure.Contains(missing) && failure.Contains(missing == "MeadBase" ? "unregistered-input" : "unregistered-output"), failure);
            }
            Check(!FermenterCompatibility.TryValidateConversions(new[] { Recipe(6), Recipe(6, "MeadBase", "OtherMead") },
                _ => true, out _, out string duplicate));
            Check(duplicate.Contains("MeadBase") && duplicate.Contains("duplicate-input"), duplicate);
        }

        private static void IsolatedFailures()
        {
            var source = new[] { Recipe(6), Recipe(0, "BadBase", "BadMead"),
                Recipe(4, "MissingBase", "MissingMead"), Recipe(4, "Duplicate", "A"), Recipe(10, "Duplicate", "B") };
            Check(FermenterCompatibility.TryValidateConversions(source, id => id != "MissingBase", out var valid, out string skipped));
            Check(valid.Count == 1 && valid[0].InputPrefabId == "MeadBase");
            Check(skipped.Contains("BadBase") && skipped.Contains("unregistered-input") && skipped.Contains("duplicate-input"));
        }

        private static void SharedOutputs()
        {
            Check(FermenterCompatibility.TryValidateConversions(new[] { Recipe(6), Recipe(6) },
                _ => true, out var identical, out _) && identical.Count == 1);
            Check(FermenterCompatibility.TryValidateConversions(new[] { Recipe(6), Recipe(4, "OtherBase") },
                _ => true, out var valid, out _));
            var station = new FermenterDescriptor("fermenter", valid);
            Check(station.TryFromInput("MeadBase", out var a) && a.OutputAmount == 6);
            Check(station.TryFromInput("OtherBase", out var b) && b.OutputAmount == 4);
            Check(!station.TryFromOutput("Mead", out _));
            Check(FermenterCompatibility.TryValidateConversions(new[] { Recipe(6), Recipe(4, "Mead", "OtherMead") },
                _ => true, out var chain, out _) && chain.Count == 2);
        }

        private static void CustomTiming()
        {
            foreach (float duration in new[] { .1f, 60f, 2400f, 7200f }) Check(FermenterCompatibility.ValidDuration(duration));
            foreach (float duration in new[] { 0f, -1f, float.NaN, float.PositiveInfinity }) Check(!FermenterCompatibility.ValidDuration(duration));
            var recipe = Recipe(6);
            var ordinary = new FermenterDescriptor("fermenter", new[] { recipe });
            var fast = new FermenterDescriptor("fermenter", new[] { recipe }, 60f);
            Check(!FermenterProducerSignature.Create(ordinary, recipe).SequenceEqual(FermenterProducerSignature.Create(fast, recipe)));
        }

        private static void SignatureChanges()
        {
            var four = Recipe(4);
            var ten = Recipe(10);
            var station = new FermenterDescriptor("fermenter", new[] { four });
            Check(!FermenterProducerSignature.Create(station, four).SequenceEqual(
                FermenterProducerSignature.Create(station, ten)));
        }
    }
}
