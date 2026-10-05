# 3D 演示场景模型包来源（ADR-0158）

- 包名：Quaternius「RPG Character Pack」（Ultimate RPG Characters，低模骨骼角色，自带动画）
- 来源：https://quaternius.com/packs/rpgcharacters.html
- 许可：CC0 1.0 通用（公有领域，见 `quaternius_rpg/License.txt`，作者 @Quaternius）
- 取用日期：2026-10-05
- 只保留实际使用的文件（整包其余角色未入库）：
  - FBX：`Warrior.fbx`（演示英雄）、`Rogue.fbx`（杂兵）、`Cleric.fbx`（精英）、`Monk.fbx`（木桩）
  - 贴图：`Warrior_Texture.png`、`Warrior_Sword_Texture.png`、`Rogue_Texture.png`、`Rogue_Dagger_Texture.png`、
    `Cleric_Texture.png`、`Cleric_Staff_Texture.png`、`Monk_Texture.png`
  - `ShowcaseModelLit.shader`、`showcase3d_models.json` 是本仓库自写（规格由 `assets/_showcase/_source/build_data_3d.py` 生成，
    不手改）。
- 体积：约 13 MB（4 个 FBX + 7 张贴图）。
