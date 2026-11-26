using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade.core.ui.Panels;

internal static partial class Program
{
    private static void SchematicPlacementRegression(Fixture _)
    {
        const BindingFlags nonPublicStatic = BindingFlags.Static | BindingFlags.NonPublic;
        var resolveBlock = typeof(SpawnMenu).GetMethod("TryResolveLegacyBlock", nonPublicStatic) ??
            throw new MissingMethodException("SpawnMenu.TryResolveLegacyBlock");
        var colors = new[]
        {
            "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
            "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
        };
        for (int color = 0; color < colors.Length; color++)
        {
            object?[] args = [171, color, null, null];
            Check((bool)resolveBlock.Invoke(null, args)!, $"Legacy carpet metadata {color} resolves");
            Equal(colors[color] + "_carpet", (string)args[2]!, $"Legacy carpet metadata {color} color");
        }

        var resolveDoor = typeof(SpawnMenu).GetMethod("TryResolveLegacyDoor", nonPublicStatic) ??
            throw new MissingMethodException("SpawnMenu.TryResolveLegacyDoor");
        string[] facings = ["east", "south", "west", "north"];
        var doorNames = new Dictionary<int, string>
        {
            [64] = "oak_door", [71] = "iron_door", [193] = "spruce_door", [194] = "birch_door",
            [195] = "jungle_door", [196] = "acacia_door", [197] = "dark_oak_door"
        };
        foreach (var (id, name) in doorNames)
        foreach (int facingData in Enumerable.Range(0, 4))
        foreach (int hingeBit in Enumerable.Range(0, 2))
        {
            byte lowerData = (byte)(facingData | 0x4);
            byte upperData = (byte)(0x8 | hingeBit);
            byte[] blocks = [(byte)id, (byte)id];
            byte[] metadata = [lowerData, upperData];
            var lower = ResolveDoor(resolveDoor, id, lowerData, blocks, metadata, 0);
            var upper = ResolveDoor(resolveDoor, id, upperData, blocks, metadata, 1);
            string hinge = hingeBit == 0 ? "left" : "right";
            Equal(name, lower.Name, $"Legacy door {id} maps to the correct wood");
            Check(lower.Hint.Contains($"facing={facings[facingData]}") && lower.Hint.Contains("half=lower") &&
                  lower.Hint.Contains($"hinge={hinge}") && lower.Hint.Contains("open=true"),
                $"Legacy door {id} lower facing/open/hinge metadata {facingData}/{hingeBit}");
            Check(upper.Hint.Contains($"facing={facings[facingData]}") && upper.Hint.Contains("half=upper") &&
                  upper.Hint.Contains($"hinge={hinge}") && upper.Hint.Contains("open=true"),
                $"Legacy door {id} upper uses matching lower state and hinge metadata {facingData}/{hingeBit}");
        }

        var parseLegacy = typeof(SpawnMenu).Assembly.GetType("MineImatorSimplyRemade.core.world.WorldSpecialBlocks", true)!
            .GetMethod("Parse", nonPublicStatic) ?? throw new MissingMethodException("WorldSpecialBlocks.Parse");
        var parsedDoor = parseLegacy.Invoke(null, ["legacy:64:0"])!;
        var properties = (Dictionary<string, string>)parsedDoor.GetType().GetProperty("Properties")!.GetValue(parsedDoor)!;
        Equal("east", properties["facing"], "Legacy world-import door metadata matches schematic orientation");

        var calculatePlacement = typeof(SpawnMenu).GetMethod("CalculateSchematicPlacementPosition", nonPublicStatic) ??
            throw new MissingMethodException("SpawnMenu.CalculateSchematicPlacementPosition");
        void Placement(vec3 position, vec3 normal, vec3 size, vec3 expected, string label)
        {
            var actual = (vec3)calculatePlacement.Invoke(null, [position, normal, size])!;
            Near(expected, actual, label);
        }

        vec3 evenSize = new(4, 2, 3);
        Placement(new vec3(3.5f, 2.25f, 4.2f), new vec3(1, 0, 0), evenSize,
            new vec3(5.5f, 2, 4), "Positive X face anchors the negative scenery edge");
        Placement(new vec3(2.5f, 2.25f, 4.2f), new vec3(-1, 0, 0), evenSize,
            new vec3(0.5f, 2, 4), "Negative X face anchors the positive scenery edge");
        Placement(new vec3(3.2f, 3, 4.2f), new vec3(0, 1, 0), evenSize,
            new vec3(2.5f, 3, 4), "Positive Y face preserves horizontal even-size alignment");
        Placement(new vec3(3.2f, 2, 4.2f), new vec3(0, -1, 0), evenSize,
            new vec3(2.5f, 0, 4), "Negative Y face anchors the scenery top");
        Placement(new vec3(3.2f, 2.25f, 4.5f), new vec3(0, 0, 1), evenSize,
            new vec3(2.5f, 2, 6), "Positive Z face anchors the negative scenery edge");
        Placement(new vec3(3.2f, 2.25f, 3.5f), new vec3(0, 0, -1), evenSize,
            new vec3(2.5f, 2, 2), "Negative Z face anchors the positive scenery edge");
        Placement(new vec3(3.2f, 2, 4.2f), new vec3(0, -1, 0), new vec3(3, 3, 3),
            new vec3(3, -1, 4), "Odd-size scenery stays centered on the block grid");

        var calculateFromBounds = typeof(SpawnMenu).GetMethod("CalculateSchematicPlacementPositionFromBounds", nonPublicStatic) ??
            throw new MissingMethodException("SpawnMenu.CalculateSchematicPlacementPositionFromBounds");
        var customPlacement = (vec3)calculateFromBounds.Invoke(null,
            [new vec3(3.2f, 2.25f, 4.5f), new vec3(0, 0, 1), new vec3(5, -2, -3), new vec3(7, 4, 1)])!;
        Near(new vec3(-3.5f, 2, 7.5f), customPlacement,
            "Custom meshcache placement preserves authored bounds while anchoring the hit face");
    }

    private static (string Name, string Hint) ResolveDoor(MethodInfo method, int id, byte blockData,
        byte[] blocks, byte[] metadata, int index)
    {
        object?[] args = [id, blockData, blocks, metadata, index, 1, 2, 1, null, null, false];
        Check((bool)method.Invoke(null, args)!, $"Legacy door {id} half resolves");
        return ((string)args[8]!, (string)args[9]!);
    }
}