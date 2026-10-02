"""model 型假人姿势集（骨骼剪辑）生成器的参数：标准人形骨骼定义、关节角限、资产路径。

键清单、时长、三相、事件、帧数规则、走/跑位移**全部来自 sprite 型假人集**的 ``std_dummy_poses.config``（单一来源，
本模块不持有任何第二份副本）；本模块只声明 model 型独有的东西：骨骼层级与比例、可视块体、关节角限、资产落点。

来源标注沿用 sprite 版约定：
- 04 §N：architecture/手感设计/04_姿势与动画契约.md 第 N 节。
- 05 §9：architecture/手感设计/05_手感档案与解析.md 第 9 节"试调起点"。
- 自拟：文档没有给数，按"先能用"拍的起点，标 experimental，改动只需改本文件并重新生成。
"""

from __future__ import annotations

from std_dummy_poses import config as SC

# --------------------------------------------------------------------------
# 命名与资源引用
# --------------------------------------------------------------------------

#: 框架级姿势集 id（display.anim_set.<name>），与 sprite 版 ``display.anim_set.std_dummy_biped`` 并列（ADR-0119：std_ 前缀）。
ANIM_SET_NAME = "std_dummy_biped_model"
ANIM_SET_ID = f"display.anim_set.{ANIM_SET_NAME}"
#: 标准人形骨骼占位体的模型资源名（resource id = model.<名字>；引擎侧资产 Resources/GameFoundation/models/<名字>.prefab）。
MODEL_NAME = "std_dummy_biped"
MODEL_REF = f"model.{MODEL_NAME}"
#: 动画剪辑资源引用类别（ADR-0038）：model 型走 ``anim.<名字>``，名字取 sprite 版同一个 stem（std_dummy_<键点号换下划线>），
#: 引擎侧 Animator 状态名 = 该名字（clipId 末段），剪辑资产 Resources/GameFoundation/anim_clips/<名字>.anim。
REF_CATEGORY = "anim"

#: 规格文件（资产根下，随框架占位资产入库）。
SPEC_FILE = "std_dummy_model_clips.json"
#: Unity 导入根里的落点（相对 adapters/unity）。
UNITY_RESOURCES = "Assets/Resources/GameFoundation"
PREFAB_REL = f"models/{MODEL_NAME}.prefab"
CONTROLLER_REL = f"models/{MODEL_NAME}.controller"
CLIP_DIR_REL = "anim_clips"

# --------------------------------------------------------------------------
# 比例与约定
# --------------------------------------------------------------------------

#: 身高对应的世界单位：与 sprite 版 64 像素 / 32 像素每单位 = 2 世界单位同量级，也与占位胶囊体（高 2）一致。
BODY_HEIGHT_UNITS = 2.0

#: 剪辑关键帧取在"sprite 版帧边界"上（含终点，故关键帧数 = 帧数 + 1），帧率共用 sprite 版标定帧率。
FPS = SC.FPS

#: 四元数分量保留小数位（规格文件体积与跨平台浮点末位差异的折中）。
ROT_DECIMALS = 6
#: 位置保留小数位（世界单位）。
POS_DECIMALS = 5

#: 骨骼局部坐标约定（写进规格 params，04 §6.1 "前向轴、根骨约定"）：Y 向上，Z 为人物前方，X 为主手侧（人物自己的右手）；
#: 全部旋转曲线是相对父骨骼的局部四元数；休息姿势（所有旋转为单位）= 双臂与双腿自然下垂、面向 +Z。
CONVENTION = {
    "up": "+Y",
    "forward": "+Z",
    "main_hand_side": "+X",
    "rest_pose": "arms_and_legs_hanging_down",
    "rotation": "local_quaternion_relative_to_parent",
    "weapon_blade_axis": "-Y of socket.main_hand",
    "root_bone": "hips (position curve = ground-solved pelvis position; prefab root stays at the feet origin)",
    "foot": "foot bone counter-rotates so the sole stays parallel to the hips frame",
}

# --------------------------------------------------------------------------
# 骨骼
# --------------------------------------------------------------------------

#: 预制体根（Animator 所在）名字；曲线路径相对它。
ROOT_NAME = MODEL_NAME

#: (骨骼名, 父骨骼名或 None=挂在根下, 休息局部位置，单位=身高倍数 (x,y,z))。顺序即层级创建顺序（父先于子）。
#: 数值取自 sprite 版骨架（std_dummy_poses.skeleton）的关节位置，同一份比例：
#: 髋部=骨盆中心；躯干绕骨盆中心旋转（脊骨骨点与髋同位）；头骨点=头块中心；肩 x=±0.145、y=+0.27（躯干系）；
#: 髋关节 x=±0.075；手骨点=腕再往前臂方向 0.025。
BONES: list[tuple[str, str | None, tuple[float, float, float]]] = [
    ("hips", None, (0.0, SC.REST_HIP_Y, 0.0)),
    ("spine", "hips", (0.0, 0.0, 0.0)),
    ("head", "spine", (0.0, 0.395, 0.0)),
    ("upper_arm_r", "spine", (0.145, 0.27, 0.0)),
    ("forearm_r", "upper_arm_r", (0.0, -SC.UPPER_ARM, 0.0)),
    ("hand_r", "forearm_r", (0.0, -(SC.FOREARM + 0.025), 0.0)),
    ("socket.main_hand", "hand_r", (0.0, 0.0, 0.0)),
    ("upper_arm_l", "spine", (-0.145, 0.27, 0.0)),
    ("forearm_l", "upper_arm_l", (0.0, -SC.UPPER_ARM, 0.0)),
    ("hand_l", "forearm_l", (0.0, -(SC.FOREARM + 0.025), 0.0)),
    ("socket.off_hand", "hand_l", (0.0, 0.0, 0.0)),
    ("thigh_r", "hips", (0.075, 0.0, 0.0)),
    ("shin_r", "thigh_r", (0.0, -SC.THIGH, 0.0)),
    ("foot_r", "shin_r", (0.0, -SC.SHIN, 0.0)),
    ("thigh_l", "hips", (-0.075, 0.0, 0.0)),
    ("shin_l", "thigh_l", (0.0, -SC.THIGH, 0.0)),
    ("foot_l", "shin_l", (0.0, -SC.SHIN, 0.0)),
]

#: 有旋转曲线的骨骼（其余骨骼——手骨、副手挂点——恒为休息姿势，不出曲线）。hips 另有位置曲线。
ROT_BONES = ["hips", "spine", "head", "upper_arm_r", "forearm_r", "upper_arm_l", "forearm_l",
             "thigh_r", "shin_r", "foot_r", "thigh_l", "shin_l", "foot_l", "socket.main_hand"]

#: 可视块体（占位人偶）：(挂在哪根骨骼, 块名, 中心(身高倍数,骨骼局部), 尺寸(身高倍数))。块体尺寸取自 sprite 版零件的横截面半宽，
#: 脚/骨盆与 sprite 版同厚，使"着地求解"（把最低点落在地面）在两种外形上一致。头块名固定 ``slot.head``（槽位网格替换的约定名）。
VISUALS: list[tuple[str, str, tuple[float, float, float], tuple[float, float, float]]] = [
    ("hips", "pelvis", (0.0, 0.0, 0.0), (0.18, 0.09, 0.13)),
    ("spine", "torso", (0.0, 0.17, 0.0), (0.245, 0.28, 0.14)),
    ("head", "slot.head", (0.0, 0.0, 0.0), (0.156, 0.17, 0.156)),
    ("head", "nose", (0.0, -0.008, 0.085), (0.036, 0.024, 0.032)),
    ("upper_arm_r", "mesh", (0.0, -SC.UPPER_ARM / 2, 0.0), (0.072, SC.UPPER_ARM, 0.072)),
    ("forearm_r", "mesh", (0.0, -SC.FOREARM / 2, 0.0), (0.06, SC.FOREARM, 0.06)),
    ("hand_r", "mesh", (0.0, 0.0, 0.0), (0.06, 0.05, 0.06)),
    ("upper_arm_l", "mesh", (0.0, -SC.UPPER_ARM / 2, 0.0), (0.072, SC.UPPER_ARM, 0.072)),
    ("forearm_l", "mesh", (0.0, -SC.FOREARM / 2, 0.0), (0.06, SC.FOREARM, 0.06)),
    ("hand_l", "mesh", (0.0, 0.0, 0.0), (0.06, 0.05, 0.06)),
    ("thigh_r", "mesh", (0.0, -SC.THIGH / 2, 0.0), (0.096, SC.THIGH, 0.104)),
    ("shin_r", "mesh", (0.0, -SC.SHIN / 2, 0.0), (0.072, SC.SHIN, 0.08)),
    ("foot_r", "mesh", (0.0, -0.02, 0.04), (0.072, 0.04, 0.15)),
    ("thigh_l", "mesh", (0.0, -SC.THIGH / 2, 0.0), (0.096, SC.THIGH, 0.104)),
    ("shin_l", "mesh", (0.0, -SC.SHIN / 2, 0.0), (0.072, SC.SHIN, 0.08)),
    ("foot_l", "mesh", (0.0, -0.02, 0.04), (0.072, 0.04, 0.15)),
]

# --------------------------------------------------------------------------
# 关节角限（度）
# --------------------------------------------------------------------------

#: 铰链关节（肘、膝）夹紧范围：取样时夹到 [0, 150]，不允许反向过伸（见 build.effective_pose）。
HINGE_CLAMP = {"m_el": (0.0, 150.0), "o_el": (0.0, 150.0), "m_kn": (0.0, 150.0), "o_kn": (0.0, 150.0)}

#: 取样后姿势参数（sprite 版 Pose 关节角，经铰链夹紧）的范围：(下限, 上限)。自检对每个关键帧时刻重新取样并检查，
#: 防止姿势函数被改出越界动作。M4-W5 起肩、髋、躯干收紧到人体范围（带运动幅度的风格化余量）：肩前屈 -65..185（人体后伸约 60、
#: 前屈至头顶约 180）、髋 -46..100（人体后伸约 30，奔跑冲刺的蹬地后伸加骨盆倾斜到 45）、躯干俯仰 -50..60。躺平不再靠
#: "腿绕髋整体后摆 + 躯干俯仰 ±88" 表达，改用整身俯仰 bp（见 sprite 版 poses.LYING_BACK），所以这些限不必为躺平留余量。
SOURCE_ANGLE_LIMITS = {
    "m_sf": (-65.0, 185.0), "o_sf": (-65.0, 185.0),       # 肩前屈（躯干系，向前上举为正）
    "m_sa": (-30.0, 70.0), "o_sa": (-45.0, 70.0),         # 肩外展
    "m_el": (0.0, 150.0), "o_el": (0.0, 150.0),           # 肘屈：不允许反向过伸
    "m_hf": (-46.0, 100.0), "o_hf": (-46.0, 100.0),       # 髋前屈
    "m_ha": (-30.0, 30.0), "o_ha": (-30.0, 30.0),         # 髋外展
    "m_kn": (0.0, 150.0), "o_kn": (0.0, 150.0),           # 膝屈：不允许反向过伸
    "t_pitch": (-50.0, 60.0), "t_roll": (-30.0, 30.0), "t_yaw": (-60.0, 60.0),   # 躯干
    "h_pitch": (-60.0, 60.0), "h_yaw": (-60.0, 60.0),     # 头
    "wp": (0.0, 230.0), "wy": (-90.0, 90.0), "wz": (-60.0, 60.0),  # 武器相对躯干
    "bp": (-400.0, 400.0),                                # 整身俯仰（击飞翻滚一整圈 + 余量；只有 BP_KEYS 里的键可非零，重受击/击退另限 [-BP_RECOIL_LIMIT_DEG, 0]）
}

#: 允许非零整身俯仰 bp 的键：击飞翻滚类（一整圈）与躺平类（仰卧 -88 / 俯卧 +88，击飞起手后仰）。其余键 bp 恒为 0。
BP_TUMBLE_KEYS = ("hit.launch.tumble", "hit.launch.land")
BP_LYING_KEYS = ("hit.launch", "hit.knockdown", "hit.getup", "death")
#: 重受击/击退（M4-W6）：肩、髋、躯干全在人体范围内，"往后一仰"的幅度由整身后仰 bp 表达（绕骨盆，度，只后仰不前翻）。
BP_RECOIL_KEYS = ("hit.heavy", "hit.knockback")
BP_RECOIL_LIMIT_DEG = 35.0
BP_KEYS = BP_TUMBLE_KEYS + BP_LYING_KEYS + BP_RECOIL_KEYS


#: 规格里实际写出的骨骼局部旋转量（四元数转轴角后的旋转角，度）上限：对每根旋转骨骼的每个关键帧检查。
#: 肘/膝是单轴铰链，旋转角恒等于屈曲角，故上限同 SOURCE_ANGLE_LIMITS；肩（上臂）、髋（大腿）、躯干、头按人体范围加风格化余量
#: （M4-W5 收紧：上臂 195 -> 175，大腿 105 -> 90，躯干 100 -> 55，头 60 -> 45）；其它骨骼取包络加余量。
BONE_ROT_LIMITS_DEG = {
    "hips": 180.0,       # 只有整身俯仰键（BONE_ROT_HIPS_PITCH_KEYS）允许髋大角度（整身 bp）；其它键 <= HIPS_ROT_LIMIT_DEG
    "spine": 55.0,
    "head": 45.0,
    "upper_arm_r": 175.0, "upper_arm_l": 175.0,
    "forearm_r": 150.0, "forearm_l": 150.0,
    "thigh_r": 90.0, "thigh_l": 90.0,
    "shin_r": 150.0, "shin_l": 150.0,
    "foot_r": 150.0, "foot_l": 150.0,
    "socket.main_hand": 200.0,
}

#: 髋旋转（绕竖轴的 yaw，度）在非整身俯仰键里的上限（原 5 度）；翻滚类键（一整圈）受 BONE_ROT_LIMITS_DEG["hips"]，躺平类键（放平到 ±88 度）受下面的躺平上限。
HIPS_ROT_LIMIT_DEG = 5.0
HIPS_LYING_LIMIT_DEG = 100.0
BONE_ROT_HIPS_TUMBLE_KEYS = BP_TUMBLE_KEYS
BONE_ROT_HIPS_PITCH_KEYS = BP_KEYS


def hips_rot_limit_deg(key: str) -> float:
    """某个键髋骨骼旋转角的上限：翻滚类键整圈、躺平类键放平到 ±88 度、其余键只绕竖轴转几度。"""
    if key in BP_TUMBLE_KEYS:
        return BONE_ROT_LIMITS_DEG["hips"]
    if key in BP_LYING_KEYS:
        return HIPS_LYING_LIMIT_DEG
    if key in BP_RECOIL_KEYS:
        return BP_RECOIL_LIMIT_DEG
    return HIPS_ROT_LIMIT_DEG


#: 引擎侧事件别名：04 §5 的命中标记叫 ``hit``，而 model 型角色外壳（ModelCharacterRig）识别的命中帧事件名是 ``hit_frame``
#: （ADR-0017）。数据行只写 04 的名字（与 sprite 版同源）；烘进 .anim 的事件在每条 ``hit`` 旁边同时刻再放一条别名事件，
#: 让 model 型单位的命中帧回调（HitFrameReached）真的在 04 规定的时刻触发。读条施法剪辑 ``cast`` 没有 ``hit`` 而是施放点 ``release``
#: （M4-W5 起 ``release`` 也烘出同刻 ``hit_frame``：手感场景的技能走 cast 剪辑，没有它 model 平面的命中对齐恒为缺失）；
#: 弓攻击的 ``release`` 与 ``hit`` 同刻且数据行已带 ``hit_frame``，按"同名同时刻已存在则不重复"去重。规格里的 ``anim_events`` = 事件 + 别名事件。
ENGINE_EVENT_ALIASES = {"hit": "hit_frame", "release": "hit_frame"}

#: 数据行事件与 sprite 版同源，不在这里声明；这里只声明 model 型对帧数/时长的核对容差。
TIME_TOLERANCE_MS = 1e-3
FK_TOLERANCE_BH = 1e-4   # 正向运动学还原出的关节位置与 sprite 版关节位置的容差（身高倍数）


# --------------------------------------------------------------------------
# 体量组（04 §6.1 / §7、05 §9"标准骨骼三组（对应三体量）"）
# --------------------------------------------------------------------------

#: 体量档（手感落地 M4-D 起声明在 sprite 版 ``std_dummy_poses.config.MASS_TIERS``，两版同源，本模块不持有副本）：
#: 非主档（轻/重……）各一组，覆盖**全部**键（含别名键），``extends`` 主集（中体量，04 §7）；数据行 ``_medium`` 为空覆盖行。
#: 步幅轴（短/中/长）**不出资产**：02 §7 的播放速率 = 实际速度 ÷ (剪辑每循环位移 ÷ 时长)，步幅由运行期 ``stride_scale`` 匹配，
#: 与体量正交；因此各档剪辑的时长、帧数、每步位移与主集完全一致（只改站姿与受击反应幅度）。
#: M3-D 时期体量组覆盖的 8 个键（均在主集里存在）：它们在两档里的剪辑字节必须与 M3-D 逐字节一致（只追加不改既有，测试固定其哈希）。
LEGACY_MASS_KEYS = ("idle", "idle.combat", "move.walk", "move.run", "move.sprint", "move.walk.combat", "move.run.combat",
                    "move.sprint.combat")


def mass_profiles() -> dict:
    """静态站姿偏移的四个量（度；M3-D 的口径，规格 params.mass_profiles_deg 仍按这四个量写出）。完整档表（含 react/air）见 SC.MASS_TIERS。"""
    return {m: {k: v[k] for k in ("t_pitch", "h_pitch", "sa", "ha")} for m, v in SC.MASS_TIERS.items()}


def mass_anim_set_id(mass: str) -> str:
    return f"{ANIM_SET_ID}_{mass}"


def clip_state_name(key: str, mass: str | None = None) -> str:
    """Animator 状态名 = 剪辑资产名 = sprite 版同一 stem（std_dummy_<键点号换下划线>）；体量组在 std_dummy_ 后插 <体量>_。"""
    return SC.STEM_PREFIX + (f"{mass}_" if mass else "") + key.replace(".", "_")


def clip_resource_ref(key: str, alias_of: str | None = None, mass: str | None = None) -> str:
    return f"{REF_CATEGORY}.{clip_state_name(alias_of or key, mass)}"


def bone_path(name: str) -> str:
    """骨骼相对预制体根的层级路径（Animator 曲线绑定路径）。"""
    parents = {n: p for n, p, _ in BONES}
    parts = [name]
    while parents[parts[-1]] is not None:
        parts.append(parents[parts[-1]])
    return "/".join(reversed(parts))


# --------------------------------------------------------------------------
# 动画控制器的确定性 id（编辑器生成脚本与自检共用同一公式）
# --------------------------------------------------------------------------

_FNV_OFFSET = 0xCBF29CE484222325
_FNV_PRIME = 0x100000001B3
_MASK64 = (1 << 64) - 1


def _fnv1a_signed64(text: str) -> int:
    h = _FNV_OFFSET
    for b in text.encode("utf-8"):
        h = ((h ^ b) * _FNV_PRIME) & _MASK64
    return h - (1 << 64) if h >= (1 << 63) else h


def controller_state_file_id(state: str) -> int:
    """动画控制器里状态子资产的 fileID：状态名的 FNV-1a 64（有符号），不随生成次数变化（Unity 自己分配的是随机数）。"""
    return _fnv1a_signed64(f"{MODEL_NAME}.controller/state/{state}")


def controller_relay_file_id(state: str) -> int:
    """状态上挂的 AnimStateFinishRelay 行为子资产的 fileID（同上公式，不同前缀）。"""
    return _fnv1a_signed64(f"{MODEL_NAME}.controller/relay/{state}")
