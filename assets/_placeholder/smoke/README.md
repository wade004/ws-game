# ComfyUI 冒烟测试记录（阶段 0 / T0-10）

> 本目录是**阶段 0 冒烟测试记录**，用于验证本机 ComfyUI 服务可以用本地已有的 Qwen-Image 模型跑通一次出图流程。
> **不是正式占位资产**，不代表任何游戏的美术方向，图片内容为中性测试图（一枚简洁的圆形盾牌图标）。

## 基本信息

| 项目 | 内容 |
|---|---|
| 测试日期 | 2026-09-04 |
| ComfyUI 版本 | 0.33.3（`comfyui_version` 字段，来自 `/system_stats`） |
| 前端包版本 | comfyui-frontend-package 1.49.6 |
| 工作流模板包版本 | comfyui-workflow-templates 0.11.44 |
| Python | 3.13.12（`D:\ComfyUI\ComfyUI\ComfyUI\.venv`） |
| PyTorch | 2.10.0+cu130 |
| 显卡 | NVIDIA GeForce RTX 4090（显存约 24564 MB） |

## 启动方式

Comfy Desktop 桌面版（`C:\Program Files\Comfy Desktop\Comfy Desktop.exe`）启动后卡在启动流程（Electron 多进程已拉起，但 4 分钟内 `127.0.0.1:8188/system_stats` 始终无响应，`app.log` 也未见后续日志推进，疑似停在需要人工点击的界面），判定为方案 A 不可行，改用方案 B：直接用 ComfyUI 自带的虚拟环境启动后端进程。

实际使用的 Python 是仓库内的 `.venv`（而不是 `standalone-env\python.exe`——后者是一个不含 torch 的启动器 stub，直接调用会报 `ModuleNotFoundError: No module named 'torch'`）：

```
D:\ComfyUI\ComfyUI\ComfyUI\.venv\Scripts\python.exe ^
  D:\ComfyUI\ComfyUI\ComfyUI\main.py ^
  --listen 127.0.0.1 --port 8188 ^
  --extra-model-paths-config "C:\Users\1\AppData\Roaming\Comfy Desktop\shared_model_paths.yaml" ^
  --disable-auto-launch
```

`--extra-model-paths-config` 指向 Comfy Desktop 自动生成的 `shared_model_paths.yaml`，使后端直接复用桌面版的模型目录 `D:\ComfyUI\ComfyUI-Shared\models\`，未修改任何配置文件。

启动耗时：进程拉起后约 20 秒左右 HTTP 服务即就绪（`GET /system_stats` 返回 200）。

## 使用的模型文件

均为本机已有权重，未做任何下载：

| 用途 | 文件名 | 所在目录 | 大小 |
|---|---|---|---|
| Diffusion 模型（UNETLoader） | `qwen_image_2512_fp8_e4m3fn.safetensors` | `D:\ComfyUI\ComfyUI-Shared\models\diffusion_models\` | 约 19.5 GB |
| 文本编码器（CLIPLoader，type=qwen_image） | `qwen_2.5_vl_7b_fp8_scaled.safetensors` | `D:\ComfyUI\ComfyUI-Shared\models\text_encoders\` | 约 8.9 GB |
| VAE（VAELoader） | `qwen_image_vae.safetensors` | `D:\ComfyUI\ComfyUI-Shared\models\vae\` | 约 242 MB |

（同目录下另有 `Qwen-Image-2512-Lightning-4steps-V1.0-fp32.safetensors` 等 LoRA，本次冒烟未使用，走的是无 LoRA 的基础流程。）

## 工作流

未找到用户已保存的 Qwen-Image 工作流（`ComfyUI\user\default\workflows\` 为空）；ComfyUI 自带的官方模板 `image_qwen_Image_2512.json`（界面 UI 格式，内含 Subgraph 节点）虽然引用了同一批本地模型文件，但其节点被封装进 Subgraph，不便直接转换为 API 格式，因此按 ComfyUI 官方 Qwen-Image 文生图范式手写了一个扁平的 API 格式工作流，提交前已用 `/object_info` 逐个核对过下列 class_type 与参数取值均存在。

保存为 `smoke_workflow_api.json`，节点结构：

1. `UNETLoader` — `unet_name=qwen_image_2512_fp8_e4m3fn.safetensors`, `weight_dtype=default`
2. `CLIPLoader` — `clip_name=qwen_2.5_vl_7b_fp8_scaled.safetensors`, `type=qwen_image`
3. `VAELoader` — `vae_name=qwen_image_vae.safetensors`
4. `CLIPTextEncode`（正向） — 见下方提示词
5. `CLIPTextEncode`（负向） — 见下方提示词
6. `ModelSamplingAuraFlow` — `shift=3.1`
7. `EmptySD3LatentImage` — `width=1024, height=1024, batch_size=1`
8. `KSampler` — `seed=12345, steps=20, cfg=2.5, sampler_name=euler, scheduler=simple, denoise=1.0`
9. `VAEDecode`
10. `SaveImage` — `filename_prefix=ws_game_smoke`

## 提示词

- 正向：`a simple round shield icon, clean flat design, solid color background, centered composition, minimal, studio lighting`
- 负向：`text, watermark, blurry, low quality, extra objects`

## 出图参数与结果

| 项目 | 值 |
|---|---|
| 分辨率 | 1024 x 1024 |
| 步数 | 20 |
| CFG | 2.5 |
| 采样器 / 调度器 | euler / simple |
| 种子 | 12345 |
| prompt_id | `ed6087e3-1b8d-4d11-81fa-6585b22f2256` |
| 耗时 | 约 35 秒（`execution_start` 到 `execution_success`，含文本编码器与 Diffusion 模型加载、采样、VAE 解码全流程；模型此前未加载过，首次出图含冷启动开销） |
| 输出文件 | ComfyUI 端 `ws_game_smoke_00001_.png`，通过 `GET /view` 下载 |
| 本地落盘 | `smoke_2026-09-04.png`，1024x1024，约 843 KB |

## 重试记录

无重试，第一次提交即成功（`node_errors` 为空，`status_str: success`）。

## 服务状态（记录时）

- 进程：`python.exe`（父进程 PID 3900，`D:\ComfyUI\ComfyUI\ComfyUI\.venv\Scripts\python.exe`，为 venv 启动 stub；实际监听请求的子进程 PID 42444，`D:\ComfyUI\ComfyUI\standalone-env\python.exe`，加载全部模型后常驻内存约 29 GB）
- 监听：`http://127.0.0.1:8188`（`netstat`/`Get-NetTCPConnection` 确认 PID 42444 正在监听）
- 本次冒烟测试结束后**未关闭**该进程，服务保持运行供后续阶段使用。
- 桌面版 Comfy Desktop.exe 因卡在启动流程已被终止（未产生可用服务），与本次冒烟测试使用的后端进程无关。
