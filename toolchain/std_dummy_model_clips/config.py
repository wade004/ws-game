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
#: 防止姿势函数被改出越界动作。肘/膝是解剖硬限；其余按"假人全部姿势的包络再留余量"拍——肩/髋的后伸下限比人体范围宽，
#: 因为受击后飞 / 倒地姿势用"腿绕髋整体后摆"表达躺平（骨盆没有俯仰自由度），见 README 已知限制。
SOURCE_ANGLE_LIMITS = {
    "m_sf": (-130.0, 185.0), "o_sf": (-130.0, 185.0),     # 肩前屈（躯干系，向前上举为正；击飞姿势双臂后甩到 -125）
    "m_sa": (-30.0, 70.0), "o_sa": (-45.0, 70.0),         # 肩外展
    "m_el": (0.0, 150.0), "o_el": (0.0, 150.0),           # 肘屈：不允许反向过伸
    "m_hf": (-95.0, 100.0), "o_hf": (-95.0, 100.0),       # 髋前屈
    "m_ha": (-30.0, 30.0), "o_ha": (-30.0, 30.0),         # 髋外展
    "m_kn": (0.0, 150.0), "o_kn": (0.0, 150.0),           # 膝屈：不允许反向过伸
    "t_pitch": (-95.0, 95.0), "t_roll": (-30.0, 30.0), "t_yaw": (-60.0, 60.0),   # 躯干
    "h_pitch": (-60.0, 60.0), "h_yaw": (-60.0, 60.0),     # 头
    "wp": (0.0, 230.0), "wy": (-90.0, 90.0), "wz": (-60.0, 60.0),  # 武器相对躯干
}

#: 规格里实际写出的骨骼局部旋转量（四元数转轴角后的旋转角，度）上限：对每根旋转骨骼的每个关键帧检查。
#: 肘/膝是单轴铰链，旋转角恒等于屈曲角，故上限同 SOURCE_ANGLE_LIMITS；其它骨骼取包络加余量。
BONE_ROT_LIMITS_DEG = {
    "hips": 5.0,
    "spine": 100.0,
    "head": 60.0,
    "upper_arm_r": 195.0, "upper_arm_l": 195.0,
    "forearm_r": 150.0, "forearm_l": 150.0,
    "thigh_r": 105.0, "thigh_l": 105.0,
    "shin_r": 150.0, "shin_l": 150.0,
    "foot_r": 150.0, "foot_l": 150.0,
    "socket.main_hand": 200.0,
}

#: 引擎侧事件别名：04 §5 的命中标记叫 ``hit``，而 model 型角色外壳（ModelCharacterRig）识别的命中帧事件名是 ``hit_frame``
#: （ADR-0017）。数据行只写 04 的名字（与 sprite 版同源）；烘进 .anim 的事件在每条 ``hit`` 旁边同时刻再放一条别名事件，
#: 让 model 型单位的命中帧回调（HitFrameReached）真的在 04 规定的时刻触发。规格里的 ``anim_events`` = 事件 + 别名事件。
ENGINE_EVENT_ALIASES = {"hit": "hit_frame"}

#: 数据行事件与 sprite 版同源，不在这里声明；这里只声明 model 型对帧数/时长的核对容差。
TIME_TOLERANCE_MS = 1e-3
FK_TOLERANCE_BH = 1e-4   # 正向运动学还原出的关节位置与 sprite 版关节位置的容差（身高倍数）


# --------------------------------------------------------------------------
# 体量组（04 §6.1 / §7、05 §9"标准骨骼三组（对应三体量）"）
# --------------------------------------------------------------------------

#: 轻/重两组：继承中体量主集（``extends``，04 §7），只覆盖"体量会让站姿变形"的键——待机与徒手/战斗姿态的走、跑、冲刺；
#: 其余键（受击、攻击、各武器族移动、变体……）沿用主集，所以数据行很小、资产增量只有这几份剪辑。
#: 步幅轴（短/中/长）**不出资产**：02 §7 的播放速率 = 实际速度 ÷ (剪辑每循环位移 ÷ 时长)，步幅由运行期 ``stride_scale`` 匹配，
#: 与体量正交；因此本组剪辑的时长、帧数、每步位移与中体量完全一致（只改站姿），步幅核对沿用同一条判据。
MASS_GROUPS = ("light", "heavy")
#: 体量组覆盖的键（均在主集里存在；``move.sprint.combat`` 在组内是 ``move.sprint`` 的别名，同主集口径）。
MASS_KEYS = ("idle", "idle.combat", "move.walk", "move.run", "move.sprint", "move.walk.combat", "move.run.combat",
             "move.sprint.combat")
#: 站姿偏移（度，自拟 experimental）：躯干前倾/后仰、头反向补偿保持视线、肩/髋外展（正=向外，左右同号即对称）。
#: 重：前倾、肩髋外展（宽站姿、手臂离身）；轻：微后仰、肩髋内收（窄站姿、手臂贴身）。只改静态站姿，不改摆动幅度，
#: 所以接触姿势脚间距（= 每步位移）不变，步幅核对原样通过。
MASS_PROFILES = {
    "light": {"t_pitch": -2.0, "h_pitch": 1.5, "sa": -3.0, "ha": -1.5},
    "heavy": {"t_pitch": 6.0, "h_pitch": -4.0, "sa": 8.0, "ha": 5.0},
}


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
