# ServerControlAgent 0.4.0 发布候选

## 功能范围

- 新增显式配置 `serverDeletionEnabled`，默认关闭；
- 新增结构化 `DeleteServerFiles` 命令，不接受任意路径；
- 删除前两次确认受管 Java 进程已停止；
- 拒绝磁盘根目录、根重解析点、代理状态目录和包含其他受管目标的父目录；
- 同卷原子移出运行路径后再递归清理，清理不跟随文件系统重解析点；
- 文件占用导致清理未完成时通过心跳报告并持续重试；
- 删除命令和目录已不存在状态保持幂等，外置备份不受影响。
- 删除开放目标的运行目录缺失后，后续代理升级仍可完成；目录重新出现时恢复完整文件校验。

## 生产能力基线

- owl5 开放：`dollnight`、`activity`、`fanstreet`、`yugong`；
- owl9 开放：当前映射到恐怖整蛊服务端的 `pvp` 目标；
- 禁止：`lobby`、`survival1`、`survival2`、`pvp-purpur`。

owl9 的 `pvp` 是既有历史 ID，不代表长期 PVP 目录；真实 PVP 目标仍是 `pvp-purpur`。

## 候选验证

- ServerControlAgent `51/51`；
- API `278/278`；
- 完整解决方案 `666/666`；
- Vue/Vitest/Playwright 和 Impeccable 检查通过；
- 生产配置库存测试验证五个一次性目标是唯一开放项。

## 部署与回滚

必须在 API 0.28.0 就绪后升级代理。每台 VPS 先备份 EXE 与真实配置，再只重启服控代理
计划任务；不得启停 Minecraft 或 Velocity。心跳版本、七/二个目标和删除能力位不符合预期
时立即恢复旧 EXE 与配置。候选验收只读检查按钮与状态，禁止用真实服务端执行首次删除。
