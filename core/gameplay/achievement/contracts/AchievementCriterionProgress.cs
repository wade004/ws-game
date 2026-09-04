namespace Core.Gameplay.Achievement
{
    /// <summary>单条 criterion 当前的累计进度快照（供 <see cref="IAchievementHost.GetProgress"/>
    /// 与 UI 展示使用）。</summary>
    public readonly struct AchievementCriterionProgress
    {
        public int Current { get; }

        public int Target { get; }

        public bool Achieved => Current >= Target;

        public AchievementCriterionProgress(int current, int target)
        {
            Current = current;
            Target = target;
        }
    }
}
