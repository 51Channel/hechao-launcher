# 天域远征工业季第三方屏幕 0.1.10 候选

- 候选日期：2026-08-18
- 组件：`HechaoEconomyScreen-NeoForge-1.21.1-0.1.10.jar`
- Minecraft：`1.21.1`
- NeoForge：`21.1.228`
- 网络协议：`2`，与服务端 Screen `0.1.3` 兼容
- JAR 大小：`841,439` 字节
- JAR SHA-256：
  `02FC9FE719A103AAC1FCC0270560D2DD19EC9C51B8E12BD8017E08E35A5A2468`

## 改版范围

`0.1.10` 使用中转站 `https://api.hechao.world/v1` 的 `gpt-image-2` 生成两项位图素材，
将导航、余额与出售结果、回收目录统一为暗色工业远征风格：

- 全屏背景使用铁板、铜管、黄铜构件和少量青绿色仪表，中心保持低细节，避免干扰文字、
  商品图标和按钮；
- 标题使用透明齿轮罗盘徽记；
- 面板、商品卡片、状态轨和按钮使用一致的金属边框、铆钉、黄铜及青绿色状态反馈；
- 背景按 `cover` 语义居中裁切，`16:9`、竖屏和超宽窗口均有确定的裁切结果；
- 极矮窗口的导航标题区增加间距，避免徽记、标题与首个按钮重叠。

余额、出售确认、商品分页、固定动作、短期会话、服务端授权、价格、网络数据包和协议版本
均未改变。该版本只替换客户端视觉资源和渲染样式，不修改服务端 JAR、API、经济数据或
85 项正式回收目录，也不要求重启 Minecraft、Velocity、API、代理或服控进程。

## Image2 素材

### 工业背景

完整提示词：

```text
Use case: ui-mockup
Asset type: full-screen bitmap backdrop for a Minecraft NeoForge economy menu; game-rendered Chinese text, item icons, and controls will be overlaid later.
Primary request: create a polished dark industrial expedition workshop backdrop, not a screenshot of a finished UI.
Scene/backdrop: layered dark iron plates, copper pipes, small brass fittings, restrained oxidized-teal glass indicators, subtle blueprint and expedition-map linework.
Style/medium: crisp hand-painted pixel-art texture compatible with Minecraft and Create-mod machinery; readable after downscaling; mostly hard edges and restrained texture.
Composition/framing: exact 16:9 landscape. Keep the central 65 percent quiet, flat, dark, and low-detail for interface readability. Put richer machinery only along the outer edges and corners. Add a thin brass structural rail near the upper edge. Balanced but not perfectly mirrored.
Lighting/mood: calm workshop instrument panel, modest warm highlights, no dramatic glow.
Color palette: charcoal iron, near-black, aged copper, pale brass, small teal and muted green accents.
Constraints: no words, no letters, no numbers, no logos, no watermark, no characters, no inventory items, no buttons, no fake text, no UI cards, no round bubbles, no bokeh, no fog, no photographic depth of field.
Avoid: photorealism, glossy 3D render, steampunk clutter covering the center, beige parchment dominance, orange-brown monochrome, purple-blue gradient, soft blurred edges.
```

- 原始生成文件：`1672 x 941`、`1,657,626` 字节；SHA-256
  `590A27381A70DBEF0845B29A02FD6EA8B2AFB105F6154B566EE88B337437C4B4`；
- 最终资源：`assets/hechao_economy_screen/textures/gui/industrial_backdrop.png`；
- 最终资源：`1024 x 576` RGB、`759,132` 字节；SHA-256
  `4E996311E2C3BCAA18364891FA99D2DAF8C9CF83530314F98631030E232085EE`。

### 远征徽记

完整提示词：

```text
Use case: stylized-concept
Asset type: small title emblem for a Minecraft NeoForge industrial economy interface.
Primary request: a front-facing circular expedition compass fused with one sturdy gear, with a simple upward needle and four cardinal spokes. No letters or symbols.
Style/medium: crisp low-resolution pixel art, Minecraft-compatible, hard square pixels, strong silhouette, limited detail that remains readable at 24 to 32 pixels.
Composition/framing: exactly one centered emblem, straight-on orthographic view, generous empty padding, no perspective tilt.
Color palette: aged copper outer gear, pale brass compass ring, very small oxidized-teal enamel accent, charcoal recesses.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local removal. The background must be uniform with no shadows, gradients, texture, reflections, floor plane, or lighting variation.
Constraints: keep the emblem fully separated from the background with crisp edges; do not use #00ff00 in the emblem; no cast shadow, no contact shadow, no reflection, no text, no letters, no numbers, no logo, no watermark, no extra objects.
Avoid: photorealism, smooth vector art, glossy 3D, thin fragile details, circular glow, soft edges.
```

- 原始绿幕文件：`1254 x 1254`、`1,153,057` 字节；SHA-256
  `E49BF27460B7E55C1387327B5F0BB85DAAF6639FF5BB54F339A5ED3EFB068B1A`；
- 本地去除绿幕后文件：`1254 x 1254`、`722,656` 字节；SHA-256
  `205DC63156D35FC7B2E82FFD3DF9170D1D76B3ADBA19C433D368CF97D2CFC184`；
- 最终资源：`assets/hechao_economy_screen/textures/gui/expedition_emblem.png`；
- 最终资源：`128 x 128` RGBA、四角透明、`20,089` 字节；SHA-256
  `716AED2EB082B1B8BC707042BC7AA84893B7B143AC8177DDF75B4F3B2B70D590`。

生成和后处理使用的中间文件不进入 Git；项目只保留最终游戏资源和上述可审计提示词、尺寸
及摘要。认证材料只在生成进程内注入，没有写入项目或发布文档。

## 离线验证

- Gradle `clean test build`：连续两次通过；
- 单元与资源合同测试：`32/32`，失败、错误和跳过均为 `0`；
- 两次清理后重建的 JAR 大小和 SHA-256 完全一致；
- JAR 内模组版本为 `0.1.10`，网络协议保持 `2`；
- `git diff --check` 通过；
- 背景和徽记已完成原图及最终资源目视检查；徽记四角透明。

候选计划随不可变客户端档案 `skyrealm-industrial-neoforge-1.21.1 / 1.0.19` 只发布到
`Test=100%`。若真人视觉验收出现回归，只把 Test 指针回退到 `1.0.18` 清单
`9BE857DAEAD9743D79C96F04917E4040B5796153A9BA5C91826E3B51809562EB`，不覆盖或删除新
对象与清单。
