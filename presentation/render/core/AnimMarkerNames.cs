using System;

namespace Presentation.Render
{
    /// <summary>
    /// 动画标记名的标准化（手感设计/04 第 5 节，ADR-0148）。
    /// <list type="bullet">
    /// <item>同一剪辑里重复的标记（走/跑剪辑的两次 <c>footstep</c>）在帧索引表里登记为 <c>name#N</c>（首个不带后缀）——
    /// 帧索引表是"标记名 → 帧"的字典，重复名此前互相覆盖、只剩最后一个；播放器触发时用 <see cref="StripRepeat"/> 去掉后缀再发出。</item>
    /// <item>model 型的事件 id 是 <c>anim_event.&lt;name&gt;</c>，带参标记的冒号被换成点（<c>anim_event.fx.smoke</c>）；
    /// <see cref="FromModelEventId"/> 还原为 <c>fx:smoke</c>。</item>
    /// </list>
    /// </summary>
    public static class AnimMarkerNames
    {
        public const string Footstep = "footstep";
        public const string TrailStart = "trail_start";
        public const string TrailEnd = "trail_end";
        public const string Impact = "impact";
        public const string FxPrefix = "fx:";

        private const char RepeatSeparator = '#';
        private const string ModelEventPrefix = "anim_event.";

        /// <summary>第 <paramref name="occurrence"/>（从 0 起）个同名标记在帧索引表里的键：首个就是原名，之后是 <c>name#1</c>、<c>name#2</c>……</summary>
        public static string RepeatKey(string name, int occurrence) =>
            occurrence <= 0 ? name : name + RepeatSeparator + occurrence;

        /// <summary>去掉 <see cref="RepeatKey"/> 加的序号后缀；没有后缀原样返回。</summary>
        public static string StripRepeat(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var i = key.LastIndexOf(RepeatSeparator);
            if (i <= 0 || i == key.Length - 1)
            {
                return key;
            }
            for (var k = i + 1; k < key.Length; k++)
            {
                if (key[k] < '0' || key[k] > '9') return key;
            }
            return key.Substring(0, i);
        }

        /// <summary>model 型事件 id 文本 → 标准标记名；不是 <c>anim_event.</c> 前缀（或前缀后为空）返回 null。</summary>
        public static string? FromModelEventId(string eventId)
        {
            if (eventId == null || !eventId.StartsWith(ModelEventPrefix, StringComparison.Ordinal))
            {
                return null;
            }
            var name = eventId.Substring(ModelEventPrefix.Length);
            if (name.Length == 0)
            {
                return null;
            }
            if (name.StartsWith("fx.", StringComparison.Ordinal))
            {
                return FxPrefix + name.Substring(3);
            }
            return name;
        }

        /// <summary>标记是不是 <c>fx:&lt;id&gt;</c>，是则给出 <c>&lt;id&gt;</c>。</summary>
        public static bool TryGetFxId(string marker, out string id)
        {
            if (marker != null && marker.StartsWith(FxPrefix, StringComparison.Ordinal) && marker.Length > FxPrefix.Length)
            {
                id = marker.Substring(FxPrefix.Length);
                return true;
            }
            id = string.Empty;
            return false;
        }
    }
}
