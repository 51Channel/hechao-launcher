# 赫朝启动器主页快捷设置候选

- 候选日期：2026-08-09
- 基线正式版本：`0.15.1`
- 目标版本：`0.15.2`
- 状态：已纳入 `0.15.2` 发布候选，尚未切换生产通道
- 来源：用户对快捷设置视觉与错误游戏路径的反馈、本次源码和 WPF 实时验证

## 变更

1. 快捷设置从三个同权重灰色小框改为两行分组控制栏。标题和当前档案位于上行，Java、
   运行内存和当前 `.minecraft` 位于下行；分组只使用间距、细分隔线和 IconPark 图标。
2. 内存选择器复用项目现有 `MemoryComboBoxStyle`，去掉原生系统下拉区域造成的灰色割裂，
   保留键盘操作、焦点样式和双向设置绑定。
3. 游戏目录文本从全局 `ClientDirectory` 改为
   `SelectedProfileGameDirectoryDisplayText`；有选中档案时来源为
   `SelectedProfileGameDirectory` 并显示真实
   `GameData\instances\<profile-id>\.minecraft`；长路径继续省略并提供完整 Tooltip 与
   UI Automation HelpText。没有选中服务器时明确显示“选择服务器后显示”，不再回退展示
   全局 `GameData`。
4. 新增 `OpenSelectedProfileGameDirectoryCommand`，主页文件夹按钮打开并在需要时创建当前
   档案 `.minecraft`。原 `OpenClientDirectoryCommand` 继续服务设置页和下载页的全局
   游戏数据目录。
5. 主页移除 `ChooseClientDirectoryButton_OnClick` 入口。更改全局数据根目录仍只在设置页
   提供，并继续受游戏运行状态和长任务锁保护。

## 布局边界

- 外层卡片保持 `6` 像素圆角，内部不再嵌套设置卡片。
- `1500 x 860` 时完整显示实际 `.minecraft` 路径；`1060 x 640` 时路径按字符省略，
  `.minecraft` 语义标签、完整 Tooltip 和打开按钮均保持可见。
- Java 与文件夹按钮使用 IconPark 图标和明确 Tooltip；内存值在最小窗口完整显示为
  `6 GB`，不再压缩为省略文本。

## 验证

- `MainWindow.xaml` XML 解析和 XAML 契约测试 `27/27` 通过。
- 启动器测试 `223/223`、完整解决方案测试 `708/708` 通过。
- `dotnet build Hechao.Launcher.sln -c Release --no-restore` 成功，`0` 警告、`0` 错误。
- 本次两个 C# 文件通过定向 `dotnet format --verify-no-changes`。
- Impeccable layout detector 返回空结果，`git diff --check` 通过。
- 独立 WPF 渲染器使用固定测试档案完成 `1500 x 860` 与 `1060 x 640` 截图；两种尺寸
  均无重叠、裁切或横向溢出。截图位于忽略 Git 的 `artifacts/validation/`，测试档案数据
  不代表生产服务器状态。

## 发布边界

- 功能提交本身未修改版本号、安装包、更新通道或 OSS 对象；后续正式发布统一由
  `0.15.2` 发布记录和结构化证据追踪。
- 不操作 API、Velocity、Minecraft 服务端、Publisher、服控代理或 VPS。
- 完整构建、测试、格式和视觉验收通过后提交并推送 `main`；正式发布另行执行。
