# ws-game 1.6.0 文档项目验证记录

## 基线与范围

- 工作树：D:\workespace\ws-game-review-85f1f4f，HEAD 85f1f4fbaff7aa3f01292f1b4b469a6a48bcc570，VERSION 1.6.0。
- 原 D:\workespace\ws-game 只读作为发布产物来源；没有网络发布、registry 启动、Release、真实 dist 覆盖或产品逻辑修改。
- 新增文件均位于本目录；check 的 bin/_check_artifacts、dist/1.6.0 是独立工作树衍生输出。

## 命令与结果

|命令|结果|原始证据|
|---|---|---|
|check.ps1 -SkipUnity|失败：13 PASS、1 FAIL、6 SKIP；FAIL 为 python -m pytest toolchain/tests -q|check-skipunity.raw.log、check-skipunity.exit.txt|
|python -m pytest toolchain/tests/test_get_framework_path_boundary.py -q|2 failed/9 passed/1 skipped；默认 GBK 解码 UnicodeDecodeError + stdout None TypeError|pytest-targeted-default.raw.log|
|PYTHONUTF8=1 python -m pytest ...|2 failed/9 passed/1 skipped；真实原因为 PowerShell 5.1 找不到 Get-FileHash|pytest-targeted-utf8.raw.log|
|pwsh 7.6.5 get_framework.ps1 -FromLocalDist|通过；原始 1.6 zip/lock 六 DLL hash 全通过并落地审计目录|get-framework-pwsh-smoke.raw.log、exit|
|六个 dotnet test tests.csproj -c Release --no-build --logger trx|全通过，共 2436：Foundation 657、Numbers 106、Rules 404、Carriers 314、Gameplay 464、Presentation.Common 491|各 dotnet-tests.*.raw.log、各 dotnet-results-*/*.trx、各 exit|
|1.5 原始 zip hash 对照|六 DLL 实际 hash 与 ws-game-1.5.0.lock 全匹配|dist-1.5.0-zip-verify.txt/json|
|API consumer OutputType=Library|1.5 原始 zip 编译成功；1.6 原始 DLL 对旧两方法各报 CS1061|api-compat-1.5-zip-oldapi-build.log、api-compat-1.6-oldapi-build.log|
|反射 API|1.5 为 PendingChestLootSnapshot/RestorePendingChestLoot；1.6 为 PendingLootSnapshot/RestorePendingLoot|api-compat-1.5-zip-reflect.log、api-compat-1.6-original-dist-reflect.log|

## 只读发布产物核对

原始 1.6 zip SHA256：3D5513AD70436C20EB2721BD23352B8DD56E4D1A0185D6A7ABD3C17FB0541349；lock：8230619C4E977C4BAF80A22BE4B2779ACECC7B63A8D8629D74E41510A3625899；git_commit=85f1f4f。原始 1.5 zip SHA256：4C596D4A93C802C2FD065EB3ADC0F4ABC113B3B2687CC4F901FFA5B3C7E8C76B；lock：84CEE708BF99253C452666AACC17FBF8CC7E77C4FD471D30475DA6DC022BC1B0；git_commit=3224ca1；六 DLL 与 lock 全匹配。

1.5 目录 MANIFEST 与 lock 曾出现不同 commit，API 对照严格采用 zip 内 DLL，不采用可能被 SyncOnly 重写的 dist/1.5.0。1.6 原始 zip 的离线 smoke 证明 pwsh 工具链路径，不证明远端 Release、registry 或 Unity。

## 门禁输出解释

check-skipunity.raw.log 是外层门禁汇总；pytest 子进程细节因原门禁捕获方式丢失，已用 targeted 日志补齐。dotnet test --no-build 使用每项目独立 TRX，避免并行覆盖同一个文件；控制台计数与 TRX 均保留。原始门禁失败和 pwsh smoke 成功是两条不同证据，不能把后者回填为 check 全通过。

## 未执行项

本子任务没有执行 Unity 编译/EditMode/PlayMode、独立版构建、consumer_smoke、网络 Release 查询/发布或 registry 启停；整体表现审计已有 43 个回归通过和 1 个 slot_mesh 故障探针，详见 ../presentation/presentation-findings.md。这里的“未执行”仅限本子任务的全量 Unity 门禁，不能把局部表现结果扩大为全量游戏 Runtime 验收。

