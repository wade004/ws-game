namespace Core.Foundation.DisplayInfo
{
    /// <summary>外形类型：决定 <see cref="Core.Foundation.DisplayInfo.DisplayInfo"/> 携带
    /// <see cref="SpriteInfo"/> 还是 <see cref="ModelInfo"/>（见 04_数据与内容管线.md 第 7.1 节
    /// <c>display.map.kind</c> 字段：<c>sprite|model</c>）。</summary>
    public enum DisplayKind
    {
        Sprite,
        Model
    }

    /// <summary>逻辑对象类别（见 04 第 7.1 节 <c>display.map.category</c> 字段：
    /// <c>skill|aura|item|creature|gobj|projectile</c>，与 03_运行时骨架.md 第 9 节
    /// <c>DisplayInfoRegistry.lookupByCategory</c> 参数枚举一致）。</summary>
    public enum DisplayCategory
    {
        Skill,
        Aura,
        Item,
        Creature,
        Gobj,
        Projectile
    }

    /// <summary>阴影呈现模式（见 04 第 7.1 节 <c>display.map.shadow</c> 字段：
    /// <c>none|blob|projected</c>，默认 <c>blob</c>）。</summary>
    public enum ShadowMode
    {
        None,
        Blob,
        Projected
    }
}
