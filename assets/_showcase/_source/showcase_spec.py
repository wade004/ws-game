# -*- coding: utf-8 -*-
"""演示场景（ADR-0154）美术规格：角色设定提示词、方向视图、剪辑清单与逐剪辑的取帧规则。

出图只走本地 ComfyUI（Qwen Image 2.1 原生 RGBA 文生图与参考图编辑出角色三视图；Wan 2.2 I2V 14B 图生视频出动作片段）。
原始大图与视频帧只留本地（--raw-dir），入库的是 build_art.py 后处理后的序列帧图集。门禁不跑 gen_art.py / build_art.py。
"""

STYLE = (
    "Hand-painted 2D action RPG game character sprite, painterly digital illustration with clean dark outlines and cel shading, vibrant saturated colors, "
    "full body, standing in a ready stance, seen from a high three-quarter top-down game camera about 40 degrees above, the whole character centered with empty margin around, "
    "single character only, no ground, no cast shadow, no text."
)

# 视图：front = 面朝镜头（画面朝下），side = 面朝画面右，back = 背对镜头（画面朝上）。左侧由运行期水平翻转，斜向档位取侧向图（见 ADR-0154）。
VIEWS = ("front", "side", "back")
VIEW_SLOT = {"front": "front", "side": "side_r", "back": "back"}
VIEW_FACING = {
    "front": "facing the camera",
    "side": "facing right in side profile",
    "back": "facing away from the camera, seen from behind",
}
VIEW_FWD = {
    "front": "toward the camera",
    "side": "to the right",
    "back": "away from the camera, upward in the frame",
}

# 角色：desc 是外观设定；front_seed 文生图种子；side/back 用参考图编辑（种子见 edit_seeds）。
CHARACTERS = {
    "hero": {
        "desc": (
            "A young swordsman knight in blue-steel plate armor with gold trim, a short crimson scarf and a small crimson cape, brown leather boots and gauntlets, "
            "short brown hair, determined face, holding a bright steel arming sword with a faint cyan glow along the blade in the right hand, left hand open, "
        ),
        "front_seed": 13,
        "edit_seeds": {"side": 21, "back": 21},
        "canvas": 224,
        "height_px": 140,
    },
    "grunt": {
        "desc": (
            "A small scrappy goblin raider with olive-green skin, big pointed ears, a mischievous snarl, ragged brown leather vest and torn tan trousers, "
            "a dirty yellow headband, bare clawed feet, holding a rusty curved short dagger in the right hand, "
        ),
        "front_seed": 31,
        "edit_seeds": {"side": 41, "back": 41},
        "canvas": 160,
        "height_px": 100,
    },
    "brute": {
        "desc": (
            "A huge hulking orc brute with grey-green skin and tusks, a horned iron helmet, heavy rusty iron shoulder plates and a studded leather harness, "
            "dark purple loincloth, thick scarred arms, holding a massive spiked wooden club in both hands, "
        ),
        "front_seed": 51,
        "edit_seeds": {"side": 63, "back": 61},
        "edit_side_prompt": (
            "but now the whole body is turned 90 degrees to face the right edge of the image, a true right-side profile view showing only the character's left side of the body, "
            "the club held in front at the right, ready stance, full body centered."
        ),
        "canvas": 256,
        "height_px": 180,
    },
}

EDIT_BASE = (
    "Hand-painted 2D action RPG game character sprite. The exact same character as in the reference image (same armor and clothing, same colors, same face, same weapon, "
    "same art style, same camera angle and scale), "
)
EDIT_VIEW = {
    "side": "but now seen in right-side profile view, the character faces the right edge of the image, ready stance, full body centered.",
    "back": "but now seen from directly behind, the character faces away from the camera, ready stance, full body centered.",
}

WAN_BASE = "A 2D game character on a flat solid bright green background, the camera is completely static, the character stays in the same place in the frame, "

# 剪辑：key = 姿势键（见 data/_showcase），prompt 里 {fwd} 按视图替换；length = Wan 帧数（4n+1，16 fps）；start = stand | prev:<key>（取该片段末帧）。
CLIPS = {
    "hero": [
        dict(key="idle", length=33, prompt="the swordsman stands in a relaxed ready stance, breathing gently with a subtle idle bob, cape swaying slightly, sword held low."),
        dict(key="idle_combat", length=33, prompt="the swordsman is in a tense fighting stance with the sword held ready, knees bent, bouncing lightly on his feet, alert."),
        dict(key="run", length=33, prompt="the swordsman runs in place {fwd} with a smooth looping run cycle, legs pumping and arms swinging, sword held at his side, cape fluttering, treadmill style."),
        dict(key="sprint", length=25, prompt="the swordsman sprints in place {fwd} very fast, leaning forward, long strides, arms pumping, cape streaming behind him, treadmill style."),
        dict(key="combo1", length=33, prompt="the swordsman swings his sword in a fast horizontal slash {fwd} with a quick wind-up and a follow-through, then returns to ready stance."),
        dict(key="combo2", length=33, prompt="the swordsman performs a fast backhand slash {fwd} in the opposite direction of a normal swing, quick wind-up, strong follow-through, then returns to ready stance."),
        dict(key="combo3", length=33, prompt="the swordsman raises his sword high with both hands and brings it down in a powerful overhead vertical finishing strike {fwd}, heavy impact pose, then slowly recovers."),
        dict(key="slam", length=33, prompt="the swordsman jumps slightly and smashes his sword down {fwd} in a heavy ground slam with a big follow-through, then recovers to ready stance."),
        dict(key="charge", length=33, prompt="the swordsman crouches, pulls the sword back and gathers power, then spins in a whirlwind slash with the sword outstretched, then stops in ready stance."),
        dict(key="dodge", length=25, prompt="the swordsman does a quick evasive dash roll {fwd}, ducking low, then springs back up to ready stance."),
        dict(key="hit_light", length=17, prompt="the swordsman flinches from being hit, head and shoulders snapping back slightly, then recovers to ready stance."),
        dict(key="hit_heavy", length=17, prompt="the swordsman staggers backward hard from a heavy blow, arms flailing and body recoiling, then regains balance."),
        dict(key="knockback", length=25, prompt="the swordsman is knocked backward by a strong blow, sliding backward with his feet dragging and body bent, then struggles to stay on his feet."),
        dict(key="knockdown", length=33, prompt="the swordsman is struck and thrown to the ground, falling backward and landing flat on his back, then lying still on the ground."),
        dict(key="getup", length=25, start="prev:knockdown", prompt="the swordsman lying on the ground pushes himself up, gets back to his feet and returns to a ready stance."),
        dict(key="death", length=33, prompt="the swordsman is defeated, drops his sword, collapses to his knees and falls forward onto the ground, lying motionless."),
    ],
    "grunt": [
        dict(key="idle", length=33, prompt="the goblin stands hunched, breathing and fidgeting with its dagger, shifting its weight, menacing."),
        dict(key="run", length=33, prompt="the goblin scurries in place {fwd} with a quick scampering run cycle, arms swinging, treadmill style."),
        dict(key="hit_light", length=17, prompt="the goblin flinches from being hit, head snapping back, then recovers."),
        dict(key="hit_heavy", length=17, prompt="the goblin staggers backward hard from a heavy blow, arms flailing, then regains balance."),
        dict(key="knockback", length=25, prompt="the goblin is knocked backward by a strong blow, sliding backward with its feet dragging, then catches its balance."),
        dict(key="knockdown", length=33, prompt="(派生：取死亡片段的倒地过程，见 build_art.derive_grunt_floor_clips)"),
        dict(key="getup", length=25, start="prev:knockdown", prompt="(派生：死亡片段倒放)"),
        dict(key="death", length=33, prompt="the goblin is defeated, drops its dagger, collapses and falls onto the ground, lying motionless."),
    ],
    "brute": [
        dict(key="idle", length=33, prompt="the orc brute stands heavily, breathing deeply with a slow chest heave, resting the club on his shoulder, menacing."),
        dict(key="run", length=33, prompt="the orc brute stomps forward in place {fwd} with a heavy lumbering walk cycle, club swaying, treadmill style."),
        dict(key="swing", length=33, prompt="the orc brute lifts the massive club back with both hands and swings it down {fwd} in a slow, heavy, crushing smash, then recovers."),
        dict(key="hit_light", length=17, prompt="the orc brute grunts and barely flinches from being hit, shoulders jerking, then stands firm."),
        dict(key="hit_heavy", length=17, prompt="the orc brute staggers backward a step from a heavy blow, shoulders recoiling, then plants his feet and stands firm."),
        dict(key="knockdown", length=33, prompt="the orc brute is struck and thrown to the ground, falling backward and landing flat on his back with the club beside him, then lying still."),
        dict(key="getup", length=25, start="prev:knockdown", prompt="the orc brute lying on the ground pushes himself up with the club, gets back to his feet and returns to a menacing stance."),
        dict(key="death", length=33, prompt="the orc brute is defeated, drops the club, sways, falls to his knees and then crashes down onto the ground, lying motionless."),
    ],
}

SEEDS_BASE = 1000
