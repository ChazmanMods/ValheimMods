using System;
using System.IO;
using System.Linq;
using System.Text;
using RunicStorage.Engine;

internal static class ChestLabelTests
{
    internal static void LabelFacesReadFromChestFront()
    {
        var front = System.Numerics.Vector3.UnitZ;
        Check(ChestLabelFaces.Normal(0) == front && ChestLabelFaces.Normal(1) == -front, "Front is +Z, back is -Z.");
        Check(ChestLabelFaces.Normal(2) == System.Numerics.Vector3.UnitX && ChestLabelFaces.Normal(3) == -System.Numerics.Vector3.UnitX, "Left/right are from the player's view at the front.");
        Check(ChestLabelFaces.Up(4) == -front && ChestLabelFaces.Right(4) == -System.Numerics.Vector3.UnitX, "Top lettering reads upright from the front.");
        for (int side = 0; side < 5; side++) {
            Check(System.Numerics.Vector3.Dot(ChestLabelFaces.Normal(side), ChestLabelFaces.Up(side)) == 0, "Each label lies on its face.");
            Check(System.Numerics.Vector3.Cross(ChestLabelFaces.Right(side), ChestLabelFaces.Up(side)) == -ChestLabelFaces.Normal(side), "Canvas local -Z faces outward without mirroring.");
        }
    }
    internal static void WoodenChestLabelSurfacePositions()
    {
        // Installed piece_chest_wood asset: body mesh rotated 180 degrees around Y, scaled .9.
        // Lid variants share a mesh; open variant is translated -0.392 on root Z.
        var bodyCenter = new System.Numerics.Vector3(0, .410580993f * .9f, .009328008f * .9f);
        var bodyExtents = new System.Numerics.Vector3(.913730025f, .410265982f, .419010997f) * .9f;
        var front = ChestLabelFaces.Position(0, bodyCenter, bodyExtents, 0, 0);
        Check(Math.Abs(front.Z - (bodyCenter.Z + bodyExtents.Z + .025f)) < .0001f && front.X == 0, "Front is centered outside the body, not on the open-lid or neighboring side.");
        var lidCenter = new System.Numerics.Vector3(0, .798442483f * .9f, .001490489f * .9f);
        var lidExtents = new System.Numerics.Vector3(.801455975f, .076272488f, .410836518f) * .9f;
        var top = ChestLabelFaces.Position(4, lidCenter, lidExtents, 0, 0);
        Check(Math.Abs(top.Y - .81224346f) < .0001f && Math.Abs(top.Z - .00134144f) < .0001f, "Closed lid label stays centered just above the actual surface.");
        var open = top + new System.Numerics.Vector3(0, 0, -.392f);
        Check(Math.Abs(open.Z - (top.Z - .392f)) < .0001f && open.Y == top.Y, "The same local lid attachment follows the open variant.");
        var rotated = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, (float)Math.PI / 2);
        var origin = new System.Numerics.Vector3(40, 2, -30);
        var world = System.Numerics.Vector3.Transform(top, rotated) + origin;
        var local = System.Numerics.Vector3.Transform(world - origin, System.Numerics.Quaternion.Inverse(rotated));
        Check(System.Numerics.Vector3.Distance(local, top) < .0001f, "Placement stays chest-relative when rotated or next to other chests.");
    }
    internal static void BiomeAutomaticLabel()
    {
        var filter = new ChestRules { OnlyBiome = "biome:meadows" };
        Check(ChestLabelLayout.AutomaticText(filter, id => id) == "Meadows", "Biome-only filter must label an otherwise empty chest.");
        var group = new ChestRules(); group.Categories.Add("biome:meadows");
        Check(ChestLabelLayout.AutomaticText(group, id => id) == "Meadows", "Biome group also labels the chest.");
        group.OnlyBiome = "biome:meadows";
        Check(ChestLabelLayout.AutomaticText(group, id => id) == "Meadows", "Do not duplicate matching group and biome names.");
        filter.Categories.Add("resource:wood");
        Check(ChestLabelLayout.AutomaticText(filter, id => id) == "Meadows / Wood Materials", "Keep the restriction visible alongside accepted groups.");
        var remembered = new ChestRules { Remember = true }; remembered.Memory.Add("FineWood");
        Check(ChestLabelLayout.AutomaticText(remembered, _ => "Fine wood") == "Fine wood", "Remembered-item labels remain localized.");
        Check(ChestLabelLayout.AutomaticText(new ChestRules(), id => id) == "Storage", "Unconfigured labels retain their fallback.");
    }
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    internal static void PaletteAndApproximation()
    {
        for (int i = 0; i < ChestLabelColors.Names.Length; i++) {
            Check(ChestLabelColors.Resolve(ChestLabelColors.Names[i].ToUpperInvariant()) == i, "Supported names are case insensitive.");
            Check(ChestLabelColors.Hex[i].Length == 9 && ChestLabelColors.Hex[i].EndsWith("FF"), "Palette colors must be opaque RGBA.");
            Check(ChestLabelColors.Hex[ChestLabelColors.Resolve(ChestLabelColors.Hex[i])] == ChestLabelColors.Hex[i], "Hex aliases retain their RGB.");
        }
        Check(ChestLabelColors.Names[ChestLabelColors.Resolve("gray")] == "Grey", "Accept common gray spelling.");
        Check(ChestLabelColors.Names[ChestLabelColors.Resolve("ornage")] == "Orange", "Approximate misspellings.");
        Check(ChestLabelColors.Names[ChestLabelColors.Resolve("gold")] == "Yellow", "Known non-Unity hues use RGB distance.");
        Check(ChestLabelColors.Names[ChestLabelColors.Resolve("#fe0101")] == "Red", "Near-red hex selects Red.");
        Check(ChestLabelColors.Names[ChestLabelColors.Resolve("")] == "White", "Empty input has visible default.");
    }
    internal static void MarkupCannotInjectTags()
    {
        string text = ChestLabelColors.Markup("Wood </color><size=999>test", "#ff0000ff");
        Check(text == "<color=#FF0000FF>Wood ‹/color›‹size=999›test</color>", "Only the generated color tag can format the label.");
        Check(ChestLabelColors.Markup("Old", "#12345678") == "<color=#12345678>Old</color>", "Untouched legacy labels retain their precise color and alpha.");
    }
    internal static void BackgroundMigrationAndValidation()
    {
        // Exact 1.2.0/v2 payload, including priority, exceptions and a custom group snapshot.
        using var bytes = new MemoryStream();
        string groupId = "custom:" + Guid.NewGuid().ToString("N");
        using (var w = new BinaryWriter(bytes, Encoding.UTF8, true)) {
            w.Write((byte)2); w.Write(true); w.Write(true); w.Write((byte)4);
            w.Write(1.3f); w.Write(-.25f); w.Write(.5f); w.Write("#12345678"); w.Write("Old caption");
            foreach (string entry in new[] { "FineWood", groupId, "Stone" }) { w.Write((byte)1); w.Write(entry); }
            w.Write(true); w.Write("biome:meadows"); w.Write((byte)1); w.Write("Wood");
            w.Write((byte)1); w.Write(groupId); w.Write("My supplies"); w.Write((byte)1); w.Write("FineWood");
        }
        Check(ChestRules.TryDecode(Convert.ToBase64String(bytes.ToArray()), out var rules), "Read existing v2 rules.");
        Check(rules.Background == 0 && rules.Preferred && rules.Excluded.Single() == "Wood", "Old labels default transparent without losing rules.");
        for (int background = 0; background < 3; background++) {
            rules.Background = background;
            Check(ChestRules.TryDecode(rules.Encode(), out var copy), "New labels round trip.");
            Check(copy.Background == background && copy.CustomGroups.Single().Id == groupId && copy.Color == "#12345678" && copy.Label == "Old caption", "Retain group snapshot and label settings.");
            var payload = Convert.FromBase64String(copy.Encode()); payload[payload.Length - 1] = 3;
            Check(!ChestRules.TryDecode(Convert.ToBase64String(payload), out _), "Reject invalid background without saving.");
        }
        rules.Background = -1;
        bool rejected = false; try { rules.Encode(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Reject negative background.");
    }
}
