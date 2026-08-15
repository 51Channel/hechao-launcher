---
name: "赫朝启动器跨平台设计系统"
description: "面向 Windows 与仅支持 M4 的 macOS 客户端的紧凑服务器操作工作台"
colors:
  primary-macos: "#A8282C"
  primary-macos-hover: "#8E2024"
  primary-windows: "#B3261E"
  primary-windows-dark: "#861A14"
  canvas-macos: "#F5F6F3"
  canvas-windows: "#F1F2F0"
  surface: "#FFFFFF"
  rail-macos: "#FAFBF8"
  rail-windows: "#FDFDFC"
  ink-macos: "#262824"
  ink-windows: "#171716"
  ink-muted: "#6E726B"
  border-macos: "#D9DBD6"
  border-windows: "#E1E2DE"
  status-success: "#34865A"
  status-warning: "#D39128"
  steel-windows: "#62737D"
  busy-surface: "#20221F"
typography:
  display-macos:
    fontFamily: "得意黑, Smiley Sans, PingFang SC, sans-serif"
    fontSize: "25px"
    fontWeight: 600
    letterSpacing: "normal"
  title-macos:
    fontFamily: "得意黑, Smiley Sans, PingFang SC, sans-serif"
    fontSize: "17px"
    fontWeight: 600
    letterSpacing: "normal"
  body-macos:
    fontFamily: "-apple-system, BlinkMacSystemFont, PingFang SC, Helvetica Neue, sans-serif"
    letterSpacing: "normal"
  body-windows:
    fontFamily: "PingFang SC, Microsoft YaHei UI, Segoe UI, sans-serif"
    letterSpacing: "normal"
  label-macos:
    fontFamily: "-apple-system, BlinkMacSystemFont, PingFang SC, Helvetica Neue, sans-serif"
    fontSize: "12px"
    fontWeight: 600
    letterSpacing: "normal"
rounded:
  none: "0px"
  field: "4px"
  control: "5px"
  panel: "6px"
  card-max: "8px"
spacing:
  xxs: "4px"
  xs: "8px"
  sm: "12px"
  md: "14px"
  lg: "18px"
  xl: "24px"
  page: "28px"
components:
  button-primary-macos:
    backgroundColor: "{colors.primary-macos}"
    textColor: "{colors.surface}"
    rounded: "{rounded.control}"
    padding: "9px 12px"
    height: "44px"
  button-primary-windows:
    backgroundColor: "{colors.primary-windows}"
    textColor: "{colors.surface}"
    rounded: "{rounded.panel}"
    padding: "12px 24px"
    height: "54px"
  button-secondary-macos:
    backgroundColor: "transparent"
    textColor: "{colors.ink-macos}"
    rounded: "{rounded.control}"
    padding: "9px 12px"
  navigation-active-macos:
    backgroundColor: "#EEE4E3"
    textColor: "{colors.primary-macos-hover}"
    rounded: "{rounded.control}"
    padding: "11px 14px"
  server-row-selected-macos:
    backgroundColor: "#F1E7E6"
    textColor: "{colors.ink-macos}"
    rounded: "{rounded.none}"
    padding: "15px 18px"
  field-macos:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-macos}"
    rounded: "{rounded.field}"
    height: "38px"
  download-card-macos:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-macos}"
    rounded: "{rounded.panel}"
    padding: "18px"
  status-strip-info-macos:
    backgroundColor: "#EDF2EC"
    textColor: "{colors.ink-macos}"
    padding: "9px 18px"
  busy-bar-macos:
    backgroundColor: "{colors.busy-surface}"
    textColor: "{colors.surface}"
    padding: "12px 18px"
---

# 赫朝启动器跨平台设计系统

## Overview

**Creative North Star: "安静的服务器控制台"**

赫朝启动器采用紧凑的 Operate 模式：玩家进入应用后直接选择服务器、检查签名客户端与 ARM64 Java、处理下载状态并启动 Minecraft。品牌表达来自赫朝红、真实堡垒横幅、图标与精准标题，不依赖营销式大标题、装饰性卡片阵列或与任务无关的视觉噱头。

Windows WPF 与 macOS Avalonia 共享“导航、目录、当前任务”这条信息骨架，同时保留各自已实现的平台差异。Windows 使用既有 `HechaoTheme.xaml`、`UiFont` 与 IconPark；macOS 使用 Avalonia Compact Fluent、系统正文字体、Lucide 图标和内置得意黑标题字体。两端都以高频操作、真实状态和可恢复失败为界面中心。

**Key Characteristics:**

- 三段式桌面工作台，服务器选择和启动主流程保持同屏。
- 冷白工作面、石墨正文、1px 规则线与少量赫朝红强调。
- 得意黑仅在 macOS 的品牌、页面标题和服务器标题中出现。
- 状态同时使用文字与颜色，下载和启动任务持续可见。
- 真实目录、公告、活动、账号和品牌资产优先，不制造演示性指标。

## Colors

调色板以冷白和石墨中性色维持长时间操作的安静感，赫朝红只负责主操作、当前导航和需要注意的失败。Windows 与 macOS 的红色和中性色值已经分别落地，不应在没有跨端回归的情况下强行归一。

### Primary

- **macOS 赫朝红** (`primary-macos`): Avalonia 主按钮、进度和关键图标的主强调；悬停使用更深的 `primary-macos-hover`。
- **Windows 赫朝红** (`primary-windows`): WPF 主操作和品牌强调；按下态使用 `primary-windows-dark`。

### Secondary

- **Windows 冷钢蓝灰** (`steel-windows`): Windows 已有的次级信息角色，不替代红色主操作。
- **成功绿** (`status-success`): 正版绑定、服务器在线和完成状态。
- **警告金** (`status-warning`): 需要关注但尚未失败的状态。

### Neutral

- **平台画布** (`canvas-macos`, `canvas-windows`): 两端窗口的最底层工作面。
- **纯白表面** (`surface`): 输入、下载记录和需要明确承载的局部表面。
- **平台侧栏** (`rail-macos`, `rail-windows`): 左侧导航与连续目录栏。
- **平台正文** (`ink-macos`, `ink-windows`): 两端主要文字和图标。
- **弱化正文** (`ink-muted`): 辅助说明、版本、路径摘要和时间。
- **平台分隔线** (`border-macos`, `border-windows`): 1px 分栏、分段和控件边界。
- **持续任务深色面** (`busy-surface`): macOS 跨页面下载与安装任务区。

**The Red Is a Command Rule.** 赫朝红优先表示当前选择、主要动作、进度或错误，不把大面积普通容器涂红。

**The Platform Token Rule.** 跨平台结构可以共享，但落地颜色必须引用对应平台的实际令牌，禁止把 Windows 或 macOS 的十六进制值直接覆盖到另一端。

## Typography

**macOS Display Font:** 得意黑（内置 `SmileySans-Oblique.otf`，中文回退为 PingFang SC）

**macOS Body Font:** `-apple-system`, BlinkMacSystemFont, PingFang SC, Helvetica Neue

**Windows Body Font:** `UiFont`，即 PingFang SC、Microsoft YaHei UI、Segoe UI

**Character:** macOS 的得意黑为品牌和关键标题带来明确识别度，正文仍保持原生系统工具的清晰与克制；Windows 保持既有系统字体链。两端都不使用负字距，也不按视口宽度缩放字号。

### Hierarchy

- **macOS Display** (`display-macos`, 600): 页面标题与服务器横幅标题，固定为紧凑桌面尺度。
- **macOS Title** (`title-macos`, 600): 品牌名、目录标题和分区标题。
- **Body** (`body-macos` / `body-windows`): 服务器公告、账号、设置和任务说明；常规正文保持平台系统字体。
- **Label** (`label-macos`, 600): 12px 的字段名与分组名，使用弱化墨色而非全大写字距。
- **Metadata** (11-13px): 版本、在线人数、运行环境、文件和时间等次级信息。

**The Display Restraint Rule.** 得意黑只用于 macOS 的品牌与标题，不进入按钮正文、表单正文、路径和长说明。

## Layout

桌面端共享“全局导航、服务器或活动目录、当前工作区”三段模型。macOS 默认窗口为 1380 × 840，最小为 1180 × 680；左栏固定 218px，主页与活动页目录固定 338px，右侧内容占余量。顶部状态条跨越后两栏，持续任务条固定在窗口底部，滚动只发生在目录或当前页面内部。

Windows 默认窗口为 1500 × 860，最小为 1060 × 640；主页使用 140 / 225 / `*` 三列。左栏包含品牌与五个紧凑工作区，账户面板固定在栏底，只呈现身份、登录状态、访问身份与皮肤头像；登录、退出和跳转操作统一进入“账户”工作区。导航与账户之间使用弹性留白，账户卡、目录和快捷设置的底边统一保持 14px。

Windows 目录从顶部栏下方开始，与当前服务器主卡片共享首屏基线；它作为连续次级导航贯穿业务区，列表只在剩余高度内滚动。目录左侧以及目录与主视区之间都保持 14px 间隔，不因条目数量改变栏高，也不增加虚假的“添加服务器”命令。

Windows 右侧依次为当前服务器、公告与近期活动、快捷设置，并按 39 / 36 / 20 的弹性比例填充业务区。当前服务器横幅、详情和主操作属于一个连续主卡片：外距 14px、内距 20px，横幅约占剩余宽度的 47%，与详情间隔 20px；详情占满横幅高度，状态与分类留在详情区，不叠到横幅。主操作固定为 148 × 40，旁边的 40 × 40 工具菜单与横幅底边对齐，均不随宽屏任意拉伸。

Windows 顶部栏只承载启动器设置和窗口控制，不重复服务器面包屑或通知入口；通知中心从公告区进入。快捷设置只保留 Java、内存和当前档案目录三个浅灰字段组，按 188 / 168 / `*` 分配宽度并在面板内垂直居中，档案目录取得主要余量。

macOS 其他工作区使用 28-30px 页面边距和 48-52px 双栏间隔；按钮、字段和状态区保持稳定尺寸。最小窗口不得产生横向滚动、按钮裁切或文字重叠。长目录、下载记录和说明应在所属区域内滚动或省略，不能反向撑大固定栏。

**The Same-Screen Task Rule.** 服务器选择、当前客户端状态和主操作必须在桌面首屏同时可定位，不能拆成互不相干的装饰页面。

## Elevation & Depth

系统以平面和结构化层级为主，没有通用阴影令牌。深度来自画布、侧栏、纯白局部表面、深色持续任务区之间的色调变化，以及 1px 分隔线；堡垒横幅只服务于当前服务器识别。下载记录可以使用带细边框的独立卡片，但页面分区不应全部浮成卡片。

**The Flat-by-Default Rule.** 静止表面不靠阴影悬浮；只有明确的容器边界、状态色块和固定任务区建立层级。

## Shapes

形状语言接近桌面系统工具：服务器目录项使用直角连续行（0px），输入和小徽标使用 4px，常规按钮使用 5px，下载记录和 Windows 主按钮使用 6px。普通业务卡片圆角不得超过 8px；头像、图标容器和横幅保持紧凑，不使用药丸形容器包装普通文字命令。

连续侧栏和目录靠边界线组织，不包成独立浮卡。卡片内部不得再嵌套只为装饰而存在的卡片。

## Components

### Buttons

- **macOS Primary:** 赫朝红、白字、5px 圆角、最小 44px 高，图标与命令文本水平排列；悬停进入深红。
- **Windows Primary:** 既有 WPF 规格保持 54px 高、24 × 12px 内边距和 6px 圆角；悬停、按下、禁用继续使用 `HechaoTheme.xaml` 状态。
- **Secondary:** 透明或白色工作面、1px 中性边界、5px 圆角；悬停只改变浅中性色表面。
- **Danger:** 保持中性表面，用红色文字和柔和红边表达删除，不与主按钮争夺层级。
- **Icon Commands:** macOS 使用 Lucide，Windows 使用现有 IconPark；熟悉工具命令可以只显示图标，但必须保留工具提示和可访问名称。

### Navigation

- 左栏导航为紧凑纵向列表，当前项使用柔和红底、深红文字和半粗字重。
- macOS 导航内边距为 14 × 11px，18px Lucide 图标与文本间隔 11px；账号入口固定在栏底。
- 服务器和活动目录是连续次级导航，选中行使用淡红背景和分隔线，不增加伪造的“添加服务器”动作。
- Windows 账户面板只展示身份摘要；登录、注册、绑定、退出等操作集中在“账户”工作区，避免与导航入口重复。

### Cards / Containers

- 下载任务是白色、1px 边框、6px 圆角、18px 内边距的独立记录，包含状态、进度、当前文件和失败原因。
- 当前服务器横幅、公告、环境、档案和主操作属于一个连续工作区，不拆成并列卡片。
- 顶部状态条使用淡绿中性背景；错误态切换为淡红背景并保留具体文字。

### Inputs / Fields

- macOS 文本框和下拉框最小 38px 高、4px 圆角、白底与中性边界；Windows 继续复用既有主题控件。
- 密码、验证码、目录和内存控件必须有可见标签；只读路径通过专用目录按钮选择，不伪装成可编辑字段。
- 焦点由 Avalonia Compact Fluent 或 WPF `HechaoFocusVisualStyle` 提供，禁止移除键盘焦点可见性。

### Status and Progress

- 状态必须同时给出文字；在线、警告、关闭、失败不能只靠红绿颜色区分。
- 进度条固定为细轨道，使用赫朝红表示完成部分；跨页面忙碌时显示深色底栏、任务名、百分比和取消命令。
- 失败记录在对应任务内显示具体失败原因，并保留重试、修复或取消路径。

### Quick Settings and Tool Menus

- Windows 主页只直接显示 Java、内存和当前档案目录；自定义 Java、回滚、修复、删除客户端和更完整设置进入主按钮旁的工具菜单。
- 主页目录字段只显示并打开所选档案的 `instances\\<profile-id>\\.minecraft`，不能显示或修改全局 `GameData` 根目录；全局根目录只在设置页更改。
- Java 和内存继续写入现有设置，服务器长任务期间禁止切换目录项，避免界面状态与正在处理的档案分离。

### Content Integrity

- 服务器公告来自目录中的真实公告；空值显示明确空状态。近期活动来自真实排期；没有排期时显示空状态并提供活动页入口。
- 服务器图像使用仓库中的真实品牌资产；未提供逐服图片时复用品牌横幅，不伪造差异化图片。
- 主操作文案与可用性必须反映真实的安装、更新、登录、绑定或启动状态；修复、删除、目录、通知、活动和设置入口始终保持可达。

## Do's and Don'ts

### Do:

- **Do** 保持三段式工作台，让选择、客户端状态和主操作在同一桌面上下文中可见。
- **Do** 使用真实服务器目录、公告、活动、账号和当前档案；演示数据必须明确标注“演示”。
- **Do** 在 macOS 品牌与标题中使用内置得意黑，在正文和控件中使用平台系统字体。
- **Do** 复用平台现有图标库、焦点样式、主题控件和颜色令牌。
- **Do** 在默认与最小窗口检查长中文、路径、下载失败和持续任务状态。

### Don't:

- **Don't** 增加虚假的延迟、标签、收藏、玩家人数、管理员入口或服务器图片差异。
- **Don't** 把页面分区做成浮动卡片阵列，也不要在业务卡片里嵌套装饰卡片。
- **Don't** 用得意黑排正文、表单内容或长说明，也不要使用负字距或视口字号缩放。
- **Don't** 用圆角文字块代替已有的工具图标，或为普通命令制造药丸形按钮。
- **Don't** 用颜色作为状态的唯一信息，也不要隐藏下载、授权、修复和失败恢复路径。
