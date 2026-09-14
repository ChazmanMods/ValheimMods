using RunicDisplayStands;

internal static class TransferTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases()
    {
        yield return ("successful transfer commits exactly once", Success);
        yield return ("failed or partially applied inventory action rolls back", FailedAction);
        yield return ("changed ownership rejects commit and restores inventories", LostOwnership);
        yield return ("serialization exception restores inventories", PreparationFailure);
        yield return ("commit false restores inventories", CommitFalse);
        yield return ("commit exception restores inventories", CommitException);
        yield return ("snapshot rollback preserves original references and all item metadata", SnapshotMetadata);
        yield return ("partial insertion restores both inventories without losing or duplicating items", PartialInsertion);
        yield return ("conservation permits moves and stack splits but detects loss and duplication", Conservation);
        yield return ("conservation detects altered quality durability and custom data", MetadataConservation);
        yield return ("stand write uses native item data version and exact serialized item", NativePayload);
        yield return ("stand serialization failure makes zero writes", BatchPreparationFailure);
        yield return ("non-owner or destroyed stand makes zero writes", BatchOwnership);
        yield return ("stand write exceptions at every setter restore previous fields", BatchRollback);
        yield return ("failed readback rejects the persistent commit", BatchReadback);
        yield return ("clearing a stand clears native and custom item payloads", BatchClear);
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Transfer regression assertion failed."); }
    private static void Success()
    {
        int commits = 0, rollbacks = 0;
        Require(TransferBoundary.Complete(true, () => true, () => { commits++; return true; }, () => rollbacks++));
        Require(commits == 1 && rollbacks == 0);
    }
    private static void FailedAction() => Reject(false, () => throw new Exception("Must not validate"), () => throw new Exception("Must not commit"));
    private static void LostOwnership() => Reject(true, () => false, () => throw new Exception("Must not commit"));
    private static void PreparationFailure() => Reject(true, () => throw new InvalidOperationException(), () => true);
    private static void CommitFalse() => Reject(true, () => true, () => false);
    private static void CommitException() => Reject(true, () => true, () => throw new InvalidOperationException());
    private static void Reject(bool success, Func<bool> validate, Func<bool> commit)
    {
        var inventory = new Inventory();
        var item = new ItemDrop.ItemData(); inventory.GetAllItems().Add(item);
        var before = new InventorySnapshot(inventory);
        inventory.GetAllItems().Clear();
        int restored = 0;
        try { Require(!TransferBoundary.Complete(success, validate, commit, () => { before.Restore(); restored++; })); }
        catch (InvalidOperationException) { }
        Require(restored == 1 && ReferenceEquals(item, inventory.GetAllItems().Single()));
    }
    private static void SnapshotMetadata()
    {
        var inventory = new Inventory();
        var item = new ItemDrop.ItemData { m_stack = 4, m_gridPos = new Vector2i(2, 3), m_equipped = true };
        item.m_customData["enchant"] = "original";
        inventory.GetAllItems().Add(item);
        var snapshot = new InventorySnapshot(inventory);
        item.m_stack = 0; item.m_quality = 1; item.m_durability = 0; item.m_variant = 0;
        item.m_gridPos = new Vector2i(0, 0); item.m_equipped = false; item.m_customData["enchant"] = "changed";
        inventory.GetAllItems().Clear(); inventory.GetAllItems().Add(new ItemDrop.ItemData());
        snapshot.Restore();
        Require(ReferenceEquals(inventory.GetAllItems().Single(), item));
        Require(item.m_stack == 4 && item.m_quality == 3 && item.m_durability == 37 && item.m_variant == 2);
        Require(item.m_gridPos.x == 2 && item.m_gridPos.y == 3 && item.m_equipped && item.m_customData["enchant"] == "original");
    }
    private static void PartialInsertion()
    {
        var player = new Inventory(); var stand = new Inventory();
        var carried = new ItemDrop.ItemData { m_stack = 5 };
        var displayed = new ItemDrop.ItemData();
        player.GetAllItems().Add(carried); stand.GetAllItems().Add(displayed);
        var playerBefore = new InventorySnapshot(player); var standBefore = new InventorySnapshot(stand);
        string contents = InventorySnapshot.Contents(player.GetAllItems().Concat(stand.GetAllItems()));
        // A failed insertion merged one unit, then removed the original without returning success.
        carried.m_stack++; stand.GetAllItems().Clear();
        Require(!TransferBoundary.Complete(false, () => true, () => true,
            () => { playerBefore.Restore(); standBefore.Restore(); }));
        Require(carried.m_stack == 5 && ReferenceEquals(stand.GetAllItems().Single(), displayed));
        Require(contents == InventorySnapshot.Contents(player.GetAllItems().Concat(stand.GetAllItems())));
    }

    private static void Conservation()
    {
        var item = new ItemDrop.ItemData { m_stack = 8 };
        string before = InventorySnapshot.Contents(new[] { item });
        var a = item.Clone(); a.m_stack = 3; a.m_gridPos = new Vector2i(1, 3); a.m_equipped = true;
        var b = item.Clone(); b.m_stack = 5;
        Require(before == InventorySnapshot.Contents(new[] { a, b }));
        b.m_stack = 4; Require(before != InventorySnapshot.Contents(new[] { a, b }));
        b.m_stack = 6; Require(before != InventorySnapshot.Contents(new[] { a, b }));
    }
    private static void MetadataConservation()
    {
        var item = new ItemDrop.ItemData(); string before = InventorySnapshot.Contents(new[] { item });
        foreach (Action<ItemDrop.ItemData> mutate in new Action<ItemDrop.ItemData>[] {
            x => x.m_quality++, x => x.m_durability--, x => x.m_variant++, x => x.m_customData["test"] = "new" })
        { var copy = item.Clone(); mutate(copy); Require(before != InventorySnapshot.Contents(new[] { copy })); }
    }
    private static void NativePayload()
    {
        var view = new ZNetView(); var item = new ItemDrop.ItemData();
        item.m_customData["enchant"] = "kept";
        var batch = new StandWriteBatch(view); batch.Item(item, 2);
        Require(view.Data.Writes == 0 && batch.Commit());
        var expected = new ZPackage(); expected.Write((byte)Version.c_ItemDataVersion); item.Save(expected);
        Require(view.Data.GetByteArray("2_itemData".GetStableHashCode(), null).SequenceEqual(expected.GetArray()));
        Require(view.Data.GetInt("2_quality".GetStableHashCode(), 0) == 3);
    }
    private static void BatchPreparationFailure()
    {
        var view = new ZNetView(); var batch = new StandWriteBatch(view);
        try { batch.Item(new ItemDrop.ItemData { FailSave = true }); throw new Exception("Expected failure"); }
        catch (InvalidOperationException) { Require(view.Data.Writes == 0); }
    }
    private static void BatchOwnership()
    {
        foreach (bool valid in new[] { true, false })
        {
            var view = new ZNetView { Owner = !valid, Valid = valid }; var batch = new StandWriteBatch(view);
            batch.Item(new ItemDrop.ItemData()); Require(!batch.Commit() && view.Data.Writes == 0);
        }
    }
    private static void BatchRollback()
    {
        // The batch includes all seven armor slots and both native/custom metadata.
        for (int failAt = 1; failAt <= 42; failAt++)
        {
            var view = new ZNetView(); int[] keys = Enumerable.Range(0, 7).Select(i => ($"{i}_item").GetStableHashCode()).ToArray();
            foreach (int key in keys) { view.Data.Set(key, 123); view.Data.Set(key, "legacy"); }
            var batch = new StandWriteBatch(view);
            for (int i = 0; i < 7; i++) batch.Item(new ItemDrop.ItemData(), i);
            view.Data.ThrowOnWrite = view.Data.Writes + failAt;
            try { batch.Commit(); throw new Exception("Expected write failure"); }
            catch (InvalidOperationException) { }
            foreach (int key in keys) Require(view.Data.GetInt(key, 0) == 123 && view.Data.GetString(key, "") == "legacy");
        }
    }
    private static void BatchReadback()
    {
        var view = new ZNetView(); var batch = new StandWriteBatch(view); batch.Item(new ItemDrop.ItemData());
        view.Data.IgnoreWrites = true;
        try { batch.Commit(); throw new Exception("Expected failed readback"); } catch (InvalidOperationException) { }
    }
    private static void BatchClear()
    {
        var view = new ZNetView(); var fill = new StandWriteBatch(view); fill.Item(new ItemDrop.ItemData()); Require(fill.Commit());
        var clear = new StandWriteBatch(view); clear.Item(null!); Require(clear.Commit());
        Require(view.Data.GetInt("item".GetStableHashCode(), 1) == 0);
        Require(view.Data.GetByteArray("itemData".GetStableHashCode(), null).Length == 0);
        Require(view.Data.GetByteArray("RunicDisplayStands_itemdata".GetStableHashCode(), null).Length == 0);
    }
}
