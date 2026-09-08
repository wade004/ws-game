using Core.Carriers.Gobj;

public static class ApiCompatProbe
{
    public static void UseOldPublicApi(GameObjectHost host)
    {
        _ = host.PendingChestLootSnapshot();
        host.RestorePendingChestLoot(null!);
    }
}
