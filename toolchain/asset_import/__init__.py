"""资产导入工具包（``toolchain/import_assets.py`` 的实现）。

职责边界见 ``architecture/11_工程规范与测试.md`` 第 2.1 节"资产导入工具"与
``architecture/落地计划/落地方案与分阶段计划.md`` 第 15 节：把出图产物（精灵/图标/
特效序列帧/音效）规范化落到 ``assets/<dataset>/`` 目录，并把对应的外形表/特效表/
音效表数据行合并写入 ``data/<dataset>/...``，供 ``toolchain/validate_data.py`` 校验。

子模块：

- ``common``：仓库根解析、id 规范、表信封读写与合并写入、通用小工具。
- ``atlas``：不依赖第三方打包库的简单行式图集打包。
- ``image_ops``：抠图（``colorkey``/``rembg``）、裁边（trim）等图像处理。
- ``directions``：方向档位命名与镜像规则（对应 09 第 3.2 节）。
- ``sprite_cmd`` / ``icon_cmd`` / ``vfx_cmd`` / ``sfx_cmd`` / ``check_cmd``：五个子命令的实现。
- ``cli``：``argparse`` 命令行入口，供 ``toolchain/import_assets.py`` 调用。
"""
