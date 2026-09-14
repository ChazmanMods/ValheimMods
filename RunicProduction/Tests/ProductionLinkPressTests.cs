using System;
using System.Collections.Generic;
using RunicProduction.Core;
using RunicProduction.Integration;

namespace RunicProduction.Tests
{
    internal static class ProductionLinkPressTests
    {
        internal static IReadOnlyList<KeyValuePair<string, Action>> Cases() => new[]
        {
            Case("link press cannot repeat across frames while held", HeldAcrossFrames),
            Case("released quick click cannot repeat while down edge is stale", ReleasedWithStaleEdge),
            Case("fresh press after release completes station-to-chest sequence", StationToChest),
            Case("repeat hooks in one frame cannot rearm a consumed press", SameFrameHooks),
            Case("intentional new press on the same station can still cancel", IntentionalCancel),
            Case("link press reset clears capture and suppression", Reset),
            Case("changing gesture intent cannot reuse a captured physical press", ChangedIntent)
        };
        private static KeyValuePair<string, Action> Case(string name, Action action) => new KeyValuePair<string, Action>(name, action);
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Production press lifecycle invariant failed."); }
        private static readonly ProductionLinkMouseButton[] Buttons =
            { ProductionLinkMouseButton.Left, ProductionLinkMouseButton.Right, ProductionLinkMouseButton.Middle };
        private static void HeldAcrossFrames()
        {
            foreach (var button in Buttons)
            {
                ProductionLinkInput.Reset();
                Require(ProductionLinkInput.CanReadGesture(10));
                ProductionLinkInput.Consume(10, button);
                for (int frame = 11; frame < 100; frame++)
                {
                    ProductionLinkInput.BeginSample(frame, true, frame % 2 == 0);
                    Require(!ProductionLinkInput.CanReadGesture(frame));
                }
            }
            ProductionLinkInput.Reset();
        }
        private static void ReleasedWithStaleEdge()
        {
            foreach (var button in Buttons)
            {
                ProductionLinkInput.Reset();
                ProductionLinkInput.Consume(10, button);
                ProductionLinkInput.BeginSample(11, false, true);
                Require(!ProductionLinkInput.CanReadGesture(11));
                ProductionLinkInput.BeginSample(12, false, true);
                Require(!ProductionLinkInput.CanReadGesture(12));
                ProductionLinkInput.BeginSample(13, false, false);
                Require(ProductionLinkInput.CanReadGesture(13));
            }
            ProductionLinkInput.Reset();
        }
        private static void StationToChest()
        {
            foreach (var button in Buttons)
            {
                ProductionLinkInput.Reset();
                int accepted = 0;
                if (ProductionLinkInput.CanReadGesture(20)) { accepted++; ProductionLinkInput.Consume(20, button); }
                ProductionLinkInput.BeginSample(21, true, true);
                if (ProductionLinkInput.CanReadGesture(21)) accepted++;
                Require(accepted == 1);
                ProductionLinkInput.BeginSample(22, false, false);
                ProductionLinkInput.BeginSample(23, true, true);
                if (ProductionLinkInput.CanReadGesture(23)) { accepted++; ProductionLinkInput.Consume(23, button); }
                Require(accepted == 2);
                Require(!ProductionLinkInput.CanReadGesture(23));
            }
            ProductionLinkInput.Reset();
        }
        private static void SameFrameHooks()
        {
            ProductionLinkInput.Reset();
            ProductionLinkInput.Consume(50, ProductionLinkMouseButton.Left);
            ProductionLinkInput.BeginSample(50, false, false);
            Require(!ProductionLinkInput.CanReadGesture(50));
            Require(ProductionLinkInput.ShouldSuppress("Attack", 50));
            ProductionLinkInput.BeginSample(51, false, false);
            Require(!ProductionLinkInput.ShouldSuppress("Attack", 51));
            ProductionLinkInput.Reset();
        }
        private static void IntentionalCancel()
        {
            ProductionLinkInput.Reset();
            ProductionLinkInput.Consume(30, ProductionLinkMouseButton.Left);
            ProductionLinkInput.BeginSample(31, false, false);
            ProductionLinkInput.BeginSample(32, true, true);
            Require(ProductionLinkInput.CanReadGesture(32));
            ProductionLinkInput.Consume(32, ProductionLinkMouseButton.Left);
            Require(!ProductionLinkInput.CanReadGesture(33));
            ProductionLinkInput.Reset();
        }
        private static void Reset()
        {
            ProductionLinkInput.Consume(70, ProductionLinkMouseButton.Right);
            Require(ProductionLinkInput.ShouldSuppress("Block", 71));
            ProductionLinkInput.Reset();
            Require(ProductionLinkInput.CanReadGesture(70));
            Require(!ProductionLinkInput.ShouldSuppress("Block", 71));
        }
        private static void ChangedIntent()
        {
            ProductionLinkInput.Reset();
            ProductionLinkInput.Consume(80, ProductionLinkMouseButton.Left);
            // The latch precedes Alt/Ctrl/Shift/role/target parsing. None can rearm it.
            ProductionLinkInput.BeginSample(81, true, false);
            Require(!ProductionLinkInput.CanReadGesture(81));
            Require(ProductionLinkInput.ShouldSuppress("Attack", 81));
            Require(!ProductionLinkInput.ShouldSuppress("Block", 81));
            ProductionLinkInput.Reset();
        }
    }
}
