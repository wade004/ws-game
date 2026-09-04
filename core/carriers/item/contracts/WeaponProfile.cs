using Core.Foundation.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 武器类槽位装备的伤害/攻速定义（见 07 第 1.1 节 <c>weapon_profile</c>、第 6 节"武器决定
    /// 普通攻击动作与技能特效变体……具体分几层……属于表现层职责"）。<see
    /// cref="EquipmentHost.GetWeaponProfile"/> 的返回值类型，供普攻数值输入与表现层查询（本类型
    /// 只携带数值，不携带外形分类——外形分类经 07 第 6 节所述由 <c>display_ref</c> 关联查询，不属于
    /// 本模块职责）。
    /// </summary>
    public readonly struct WeaponProfile
    {
        public double DamageMin { get; }

        public double DamageMax { get; }

        public double Speed { get; }

        /// <summary>武器学派（见 07 第 1.1 节 <c>weapon_profile.weapon_school</c>）；数据未提供时为
        /// null（07 未把该子字段声明为必填，本模块防御性处理）。</summary>
        public Id? WeaponSchool { get; }

        public WeaponProfile(double damageMin, double damageMax, double speed, Id? weaponSchool)
        {
            DamageMin = damageMin;
            DamageMax = damageMax;
            Speed = speed;
            WeaponSchool = weaponSchool;
        }
    }
}
