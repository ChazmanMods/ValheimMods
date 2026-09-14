using RunicDisplayStands;
using Type = ItemDrop.ItemData.ItemType;

internal static class LoadoutTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases()
    {
        yield return ("nine named roles fit in two five-column rows", Grid);
        yield return ("full inventory swaps protected armor utility weapon and shield but leaves display hands", FullSwap);
        yield return ("failed equip restores both inventories and equipment flags", EquipFailure);
        yield return ("partial incoming insertion with no space restores both inventories", NoSpace);
        yield return ("each armor equip frees temporary ordinary space", SequentialEquip);
        yield return ("all weapon and shield presence combinations obey bilateral-only swaps", CombatPresence);
        yield return ("equipped then sheathed then carried weapons are preferred", SelectionPriority);
        yield return ("utility presence and equipped-state combinations always keep an available utility worn", UtilityPresence);
        yield return ("cape presence retains worn capes and equips stand capes without taking carried spares", CapePresence);
        yield return ("empty protected utility role is usable with full ordinary inventory", EmptyProtectedUtility);
        yield return ("native save mapping round-trips all nine distinct roles including three weapons", NativeRoundTrip);
        yield return ("legacy attachments map by category rather than physical index", LegacyMapping);
        yield return ("duplicate armor or insufficient native attachments reject without discarding items", InvalidMapping);
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Loadout regression assertion failed."); }
    private static readonly Type[] Types = { Type.Helmet, Type.Chest, Type.Legs, Type.Shoulder, Type.Utility,
        Type.OneHandedWeapon, Type.OneHandedWeapon, Type.Shield, Type.OneHandedWeapon };
    private static Inventory Stand() => new() { Width = ArmorStandSlots.Width, Height = ArmorStandSlots.Height };
    private static ItemDrop.ItemData Item(string name, Type type, int x, int y, bool equipped = false)
    {
        var item = new ItemDrop.ItemData { m_gridPos = new Vector2i(x, y), m_equipped = equipped };
        item.m_shared.m_itemType = type;
        item.m_dropPrefab.name = name;
        item.m_customData["enchantment"] = "preserve-me";
        return item;
    }
    private static ItemDrop.ItemData RoleItem(int role, string name = "stand")
    {
        var p = ArmorStandSlots.Position(role);
        return Item(name + role, Types[role], p.x, p.y);
    }
    private static void Unequip(ItemDrop.ItemData item) => item.m_equipped = false;
    private static bool Equip(ItemDrop.ItemData item) { item.m_equipped = true; return true; }
    private static ItemDrop.ItemData[] Select(Inventory player, Inventory stand) =>
        ArmorStandSlots.SelectPlayerItems(player, player.GetEquippedItems(), stand);
    private static void Grid()
    {
        Require(ArmorStandSlots.Labels.SequenceEqual(new[] { "Helmet", "Chest", "Legs", "Cape", "Utility", "Right hand", "Left hand", "Shield", "Weapon" }));
        var used = new HashSet<(int, int)>();
        for (int role = 0; role < ArmorStandSlots.Count; role++)
        {
            var item = RoleItem(role); var p = item.m_gridPos;
            Require(p.x >= 0 && p.x < 5 && p.y >= 0 && p.y < 2 && used.Add((p.x, p.y)));
            Require(ArmorStandSlots.Slot(item) == role && ArmorStandSlots.Accepts(role, item));
        }
        Require(!ArmorStandSlots.Accepts(9, RoleItem(0)));
        Require(!ArmorStandSlots.Accepts(ArmorStandSlots.Weapon, RoleItem(ArmorStandSlots.Shield)));
        Require(ArmorStandSlots.Accepts(ArmorStandSlots.LeftHand, RoleItem(ArmorStandSlots.Shield)));
    }
    private static void FullSwap()
    {
        var player = new Inventory { ProtectedRow = true };
        var stand = Stand();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) player.GetAllItems().Add(Item("carried", Type.Material, x, y));
        for (int role = 0; role <= ArmorStandSlots.Utility; role++)
            player.GetAllItems().Add(Item("old" + role, Types[role], role, 4, true));
        player.GetItemAt(0, 0)!.m_shared.m_itemType = Type.OneHandedWeapon;
        player.GetItemAt(1, 0)!.m_shared.m_itemType = Type.Shield;
        player.GetItemAt(0, 0)!.m_equipped = player.GetItemAt(1, 0)!.m_equipped = true;
        var incoming = Enumerable.Range(0, ArmorStandSlots.Count).Select(r => RoleItem(r)).ToArray();
        stand.GetAllItems().AddRange(incoming);
        var handRight = incoming[ArmorStandSlots.RightHand]; var handLeft = incoming[ArmorStandSlots.LeftHand];
        var outgoing = Select(player, stand);
        Require(outgoing[ArmorStandSlots.RightHand] == null && outgoing[ArmorStandSlots.LeftHand] == null);
        string before = InventorySnapshot.Contents(player.GetAllItems().Concat(stand.GetAllItems()));
        LoadoutSwap.Apply(player, stand, outgoing, Unequip, Equip);
        Require(before == InventorySnapshot.Contents(player.GetAllItems().Concat(stand.GetAllItems())));
        for (int role = 0; role < incoming.Length; role++)
        {
            if (ArmorStandSlots.IsDisplayHand(role))
                Require(ReferenceEquals(ArmorStandSlots.GetItem(stand, role), incoming[role]) && !incoming[role].m_equipped);
            else Require(incoming[role].m_equipped && player.GetAllItems().Contains(incoming[role]));
        }
        Require(stand.GetAllItems().Count == 9 && stand.GetAllItems().All(i => !i.m_equipped));
        Require(incoming.Take(5).All(i => i.m_gridPos.y == 4) && player.GetAllItems().Count == 37);
    }
    private static void EquipFailure() => Failure(false);
    private static void NoSpace() => Failure(true);
    private static void Failure(bool noSpace)
    {
        var player = new Inventory { Width = 1, Height = 2, ProtectedRow = true };
        var stand = Stand();
        var old = Item("old", Type.Helmet, 0, 1, true);
        player.GetAllItems().Add(old); player.GetAllItems().Add(Item("filler", Type.Material, 0, 0));
        var incoming = new[] { RoleItem(0), RoleItem(1) }; stand.GetAllItems().AddRange(incoming);
        var p = new InventorySnapshot(player); var s = new InventorySnapshot(stand);
        bool applied = false; int commits = 0;
        try { LoadoutSwap.Apply(player, stand, Select(player, stand), Unequip, i => noSpace && Equip(i)); applied = true; }
        catch (InvalidOperationException) { }
        Require(!TransferBoundary.Complete(applied, () => true, () => { commits++; return true; }, () => { p.Restore(); s.Restore(); }));
        Require(commits == 0 && old.m_equipped && player.GetAllItems().Contains(old));
        Require(stand.GetAllItems().SequenceEqual(incoming) && incoming.All(i => !i.m_equipped));
    }
    private static void SequentialEquip()
    {
        var player = new Inventory { Width = 2, Height = 2, ProtectedRow = true };
        var stand = Stand(); player.GetAllItems().Add(Item("filler", Type.Material, 1, 0));
        var incoming = new[] { RoleItem(0), RoleItem(1) }; stand.GetAllItems().AddRange(incoming);
        LoadoutSwap.Apply(player, stand, Select(player, stand), Unequip,
            i => { i.m_gridPos = new Vector2i(i.m_shared.m_itemType == Type.Helmet ? 0 : 1, 1); return Equip(i); });
        Require(incoming.All(i => i.m_equipped && i.m_gridPos.y == 1) && stand.GetAllItems().Count == 0);
    }
    private static void CombatPresence()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            var player = new Inventory(); var stand = Stand();
            foreach (var (role, bit) in new[] { (ArmorStandSlots.Weapon, 0), (ArmorStandSlots.Shield, 2) })
            {
                if ((mask & (1 << bit)) != 0) player.GetAllItems().Add(Item("old" + role, Types[role], bit, 0, true));
                if ((mask & (2 << bit)) != 0) stand.GetAllItems().Add(RoleItem(role));
            }
            var display = RoleItem(ArmorStandSlots.RightHand); stand.GetAllItems().Add(display);
            var originals = player.GetAllItems().ToArray();
            var incoming = stand.GetAllItems().ToArray();
            LoadoutSwap.Apply(player, stand, Select(player, stand), Unequip, Equip);
            foreach (var (role, bit) in new[] { (ArmorStandSlots.Weapon, 0), (ArmorStandSlots.Shield, 2) })
            {
                bool both = (mask & (3 << bit)) == (3 << bit);
                var old = originals.FirstOrDefault(i => i.m_shared.m_itemType == Types[role]);
                var next = incoming.FirstOrDefault(i => ArmorStandSlots.Slot(i) == role && !ReferenceEquals(i, display));
                if (old != null) Require(player.GetAllItems().Contains(old) != both);
                if ((mask & (2 << bit)) != 0)
                {
                    // Transferred positions may change; the incoming role is also encoded in its fixture name.
                    next = incoming.Single(i => i.m_dropPrefab.name == "stand" + role);
                    Require(player.GetAllItems().Contains(next) == both && next.m_equipped == both);
                }
            }
            Require(ReferenceEquals(ArmorStandSlots.GetItem(stand, ArmorStandSlots.RightHand), display) && !display.m_equipped);
        }
    }
    private static void SelectionPriority()
    {
        var player = new Inventory(); var stand = Stand(); stand.GetAllItems().Add(RoleItem(ArmorStandSlots.Weapon));
        var first = Item("first", Type.OneHandedWeapon, 0, 0);
        var active = Item("active", Type.Bow, 7, 2, true);
        player.GetAllItems().AddRange(new[] { first, active });
        Require(ReferenceEquals(Select(player, stand)[ArmorStandSlots.Weapon], active));
        active.m_equipped = false;
        Require(ReferenceEquals(ArmorStandSlots.SelectPlayerItems(player, new[] { active }, stand)[ArmorStandSlots.Weapon], active));
        Require(ReferenceEquals(Select(player, stand)[ArmorStandSlots.Weapon], first));
    }
    private static void UtilityPresence()
    {
        for (int mask = 0; mask < 8; mask++)
        {
            var player = new Inventory(); var stand = Stand();
            var old = Item("utility", Type.Utility, 4, 4, (mask & 4) != 0);
            var next = RoleItem(ArmorStandSlots.Utility);
            bool hasPlayer = (mask & 1) != 0, hasStand = (mask & 2) != 0;
            if (hasPlayer) player.GetAllItems().Add(old);
            if (hasStand) stand.GetAllItems().Add(next);
            LoadoutSwap.Apply(player, stand, Select(player, stand), Unequip, Equip);
            if (hasStand) Require(next.m_equipped && player.GetAllItems().Contains(next));
            if (hasPlayer && !hasStand) Require(old.m_equipped && player.GetAllItems().Contains(old) && stand.GetAllItems().Count == 0);
            if (hasPlayer && hasStand) Require(!player.GetAllItems().Contains(old) && stand.GetAllItems().Single().m_dropPrefab.name == "utility");
        }
    }
    private static void CapePresence()
    {
        for (int mask = 0; mask < 4; mask++)
        {
            var player = new Inventory { ProtectedRow = true }; var stand = Stand();
            // No ordinary space is available; incoming capes must use the protected role.
            for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++)
                player.GetAllItems().Add(Item("filler", Type.Material, x, y));
            var spare = player.GetItemAt(0, 0)!;
            spare.m_shared.m_itemType = Type.Shoulder;
            var old = Item("wornCape", Type.Shoulder, 3, 4, true);
            var next = RoleItem(ArmorStandSlots.Cape);
            bool hasPlayer = (mask & 1) != 0, hasStand = (mask & 2) != 0;
            if (hasPlayer) player.GetAllItems().Add(old);
            if (hasStand) stand.GetAllItems().Add(next);
            string before = InventorySnapshot.Contents(player.GetAllItems().Concat(stand.GetAllItems()));
            int unequips = 0;
            LoadoutSwap.Apply(player, stand, Select(player, stand), item => { unequips++; Unequip(item); }, Equip,
                new Dictionary<int, Vector2i> { [ArmorStandSlots.Cape] = new(3, 4) });
            Require(before == InventorySnapshot.Contents(player.GetAllItems().Concat(stand.GetAllItems())));
            Require(player.GetAllItems().Contains(spare) && !spare.m_equipped);
            Require(unequips == (hasPlayer && hasStand ? 1 : 0));
            if (hasStand) Require(next.m_equipped && ReferenceEquals(player.GetItemAt(3, 4), next));
            if (hasPlayer && !hasStand) Require(old.m_equipped && ReferenceEquals(player.GetItemAt(3, 4), old));
            if (hasPlayer && hasStand) Require(ArmorStandSlots.GetItem(stand, ArmorStandSlots.Cape)!.m_dropPrefab.name == "wornCape");
            else Require(stand.GetAllItems().Count == 0);
        }
    }
    private static void EmptyProtectedUtility()
    {
        var player = new Inventory { ProtectedRow = true }; var stand = Stand();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++) player.GetAllItems().Add(Item("filler", Type.Material, x, y));
        var utility = RoleItem(ArmorStandSlots.Utility); stand.GetAllItems().Add(utility);
        LoadoutSwap.Apply(player, stand, Select(player, stand), Unequip, Equip,
            new Dictionary<int, Vector2i> { [ArmorStandSlots.Utility] = new(4, 4) });
        Require(utility.m_equipped && ReferenceEquals(player.GetItemAt(4, 4), utility) && stand.GetAllItems().Count == 0);
    }
    // Native layout observed in the installed prefab: two copies of chest, legs,
    // helmet, left-back, right-back, cape, utility. Roles may not use grid indices as native indices.
    private static readonly int[] NativeRoles = { 1, 2, 0, 7, 8, 3, 4, 1, 2, 0, 7, 8, 3, 4 };
    private static bool Matches(int index, int role) => NativeRoles[index] ==
        (role == ArmorStandSlots.RightHand ? ArmorStandSlots.Weapon : role == ArmorStandSlots.LeftHand ? ArmorStandSlots.Shield : role);
    private static void NativeRoundTrip()
    {
        var items = Enumerable.Range(0, ArmorStandSlots.Count).Select(r => RoleItem(r)).ToArray();
        var mapped = ArmorStandSlots.MapNative(items, 14, Matches);
        Require(mapped.Count(i => i != null) == 9 && mapped.Distinct().Count(i => i != null) == 9);
        var reloaded = ArmorStandSlots.ArrangeSaved(mapped.Where(i => i != null)
            .Select(i => new KeyValuePair<int, ItemDrop.ItemData>(ArmorStandSlots.Slot(i), i.Clone())));
        for (int role = 0; role < items.Length; role++)
            Require(reloaded[role].m_dropPrefab.name == items[role].m_dropPrefab.name &&
                ArmorStandSlots.Slot(reloaded[role]) == role && reloaded[role].m_customData["enchantment"] == "preserve-me");
    }
    private static void LegacyMapping()
    {
        var items = new[] { RoleItem(2), RoleItem(8), RoleItem(0), RoleItem(7), RoleItem(4) };
        foreach (var item in items) item.m_gridPos = new Vector2i(12, 0);
        var mapped = ArmorStandSlots.ArrangeSaved(items.Select(i => new KeyValuePair<int, ItemDrop.ItemData>(-1, i)));
        Require(mapped[0] == items[2] && mapped[2] == items[0] && mapped[8] == items[1] && mapped[7] == items[3] && mapped[4] == items[4]);
    }
    private static void InvalidMapping()
    {
        var items = Enumerable.Range(0, ArmorStandSlots.Count).Select(r => RoleItem(r)).ToArray();
        try { ArmorStandSlots.MapNative(items, 7, Matches); throw new Exception("Expected capacity rejection"); } catch (InvalidOperationException) { }
        try { ArmorStandSlots.ArrangeSaved(new[] { new KeyValuePair<int, ItemDrop.ItemData>(-1, RoleItem(0)), new KeyValuePair<int, ItemDrop.ItemData>(-1, RoleItem(0)) }); throw new Exception("Expected duplicate rejection"); } catch (InvalidOperationException) { }
        try { ArmorStandSlots.MapNative(new[] { RoleItem(0), RoleItem(0) }, 14, Matches); throw new Exception("Expected duplicate role rejection"); } catch (InvalidOperationException) { }
        Require(items.All(i => i.m_stack == 1 && i.m_customData["enchantment"] == "preserve-me"));
    }
}
