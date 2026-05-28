using System;
using System.Collections.Generic;
using System.Linq;
using Automatics.AutomaticStorage;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Run("exact stacks are routed before category and empty slots", ExactStacksFirst);
        Run("category containers are routed before empty and unrelated containers", CategoryBeforeFallback);
        Run("empty containers are routed before unrelated mixed containers", EmptyBeforeOther);
        Run("reference limit keeps only nearest containers", ReferenceLimit);
        Run("multiple exact-stack targets preserve distance order", MultipleExactTargets);

        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"ok: {name}");
        }
        catch (Exception e)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL: {name}");
            Console.Error.WriteLine(e.Message);
        }
    }

    private static void ExactStacksFirst()
    {
        var source = Item("$item_wood", itemType: 1, stack: 20, maxStackSize: 50);
        var containers = new[]
        {
            Container(0, emptySlots: 4, Item("$item_wood", itemType: 1, stack: 45, maxStackSize: 50)),
            Container(1, emptySlots: 4, Item("$item_stone", itemType: 1, stack: 1, maxStackSize: 50)),
            Container(2, emptySlots: 4)
        };

        var routes = StoragePlanner.Plan(source, containers);

        AssertRoute(routes[0], 0, StorageRouteKind.ExactStack);
        AssertBefore(routes, StorageRouteKind.ExactStack, StorageRouteKind.CategoryAffinity);
        AssertBefore(routes, StorageRouteKind.ExactStack, StorageRouteKind.Empty);
    }

    private static void CategoryBeforeFallback()
    {
        var source = Item("$item_wood", itemType: 1, stack: 20, maxStackSize: 50);
        var containers = new[]
        {
            Container(0, emptySlots: 4, Item("$item_mead", itemType: 2, stack: 1, maxStackSize: 10)),
            Container(1, emptySlots: 4, Item("$item_stone", itemType: 1, stack: 1, maxStackSize: 50)),
            Container(2, emptySlots: 4)
        };

        var routes = StoragePlanner.Plan(source, containers);

        AssertRoute(routes[0], 1, StorageRouteKind.CategoryAffinity);
        AssertBefore(routes, StorageRouteKind.CategoryAffinity, StorageRouteKind.Empty);
        AssertBefore(routes, StorageRouteKind.CategoryAffinity, StorageRouteKind.Other);
    }

    private static void EmptyBeforeOther()
    {
        var source = Item("$item_wood", itemType: 1, stack: 20, maxStackSize: 50);
        var containers = new[]
        {
            Container(0, emptySlots: 4, Item("$item_mead", itemType: 2, stack: 1, maxStackSize: 10)),
            Container(1, emptySlots: 4)
        };

        var routes = StoragePlanner.Plan(source, containers);

        AssertRoute(routes[0], 1, StorageRouteKind.Empty);
        AssertBefore(routes, StorageRouteKind.Empty, StorageRouteKind.Other);
    }

    private static void ReferenceLimit()
    {
        var source = Item("$item_wood", itemType: 1, stack: 20, maxStackSize: 50);
        var containers = new[]
        {
            Container(0, emptySlots: 4),
            Container(1, emptySlots: 4, Item("$item_wood", itemType: 1, stack: 45, maxStackSize: 50)),
            Container(2, emptySlots: 4, Item("$item_wood", itemType: 1, stack: 45, maxStackSize: 50))
        };

        var routes = StoragePlanner.Plan(source, containers, referenceLimit: 1);

        Assert(routes.All(x => x.ContainerIndex == 0),
            "reference limit should prevent routes to containers outside the nearest limit");
    }

    private static void MultipleExactTargets()
    {
        var source = Item("$item_wood", itemType: 1, stack: 20, maxStackSize: 50);
        var containers = new[]
        {
            Container(0, emptySlots: 0, Item("$item_wood", itemType: 1, stack: 45, maxStackSize: 50)),
            Container(1, emptySlots: 0, Item("$item_wood", itemType: 1, stack: 40, maxStackSize: 50)),
            Container(2, emptySlots: 4)
        };

        var exactRoutes = StoragePlanner.Plan(source, containers)
            .Where(x => x.Kind == StorageRouteKind.ExactStack)
            .ToList();

        AssertEqual(2, exactRoutes.Count, "expected two exact-stack routes");
        AssertRoute(exactRoutes[0], 0, StorageRouteKind.ExactStack);
        AssertRoute(exactRoutes[1], 1, StorageRouteKind.ExactStack);
    }

    private static StorageItemSnapshot Item(string name, int itemType, int stack = 1,
        int maxStackSize = 50, int quality = 1, int worldLevel = 0)
    {
        return new StorageItemSnapshot(name, quality, worldLevel, itemType, stack, maxStackSize);
    }

    private static StorageContainerSnapshot Container(int index, int emptySlots,
        params StorageItemSnapshot[] items)
    {
        return new StorageContainerSnapshot(index, emptySlots,
            items ?? Array.Empty<StorageItemSnapshot>());
    }

    private static void AssertBefore(IReadOnlyList<StorageRoute> routes, StorageRouteKind first,
        StorageRouteKind second)
    {
        var firstIndex = FindFirst(routes, first);
        var secondIndex = FindFirst(routes, second);
        if (firstIndex >= 0 && secondIndex >= 0 && firstIndex > secondIndex)
            throw new Exception($"{first} should be routed before {second}");
    }

    private static int FindFirst(IEnumerable<StorageRoute> routes, StorageRouteKind kind)
    {
        var index = 0;
        foreach (var route in routes)
        {
            if (route.Kind == kind) return index;
            index++;
        }

        return -1;
    }

    private static void AssertRoute(StorageRoute route, int containerIndex, StorageRouteKind kind)
    {
        AssertEqual(containerIndex, route.ContainerIndex, "unexpected container index");
        AssertEqual(kind, route.Kind, "unexpected route kind");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}. Expected {expected}, got {actual}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
