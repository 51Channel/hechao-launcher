# 天域远征工业季 HechaoEconomy 0.1.7 生产发布

日期：`2026-08-18`

## 故障

真人点击“回收目录”后，客户端显示“请求超时，请稍后重试”。生产日志确认请求已经到达
服务端，但 HechaoEconomy `0.1.6` 在渲染首个商品时抛出：

```text
java.lang.NoSuchMethodError: 'java.lang.String org.bukkit.Material.translationKey()'
```

生产 Arclight `1.0.2-SNAPSHOT+8086b06` 没有 Paper API 的该方法。此前生产商品表为空，
商品渲染循环不会执行，因此旧验收没有暴露这一二进制兼容缺口。

## 修复

- 移除商品图标对 `Material.translationKey()` 的调用；
- 不再覆盖商品原生显示名，由客户端根据物品描述 ID 和语言包本地化；
- 保留价格 Lore、`45 + 40` 服务端批次、客户端批次内分页和全部服务端授权；
- 新增 Arclight 兼容契约测试，禁止目录渲染重新引入该调用；
- Gradle JAR 固定 `preserveFileTimestamps=false` 与 `reproducibleFileOrder=true`。

功能提交：`74246b7`

## 构建

- Gradle `clean test build`：连续两次通过；
- 测试：`22/22`，失败 `0`，错误 `0`；
- JAR：`HechaoEconomy-0.1.7.jar`；
- 大小：`374,527` 字节；
- SHA-256：`56724A063878B1EBB6BBD907D41669B45D62CF1B9647C2B4681B7E5C17DA3760`；
- 两次清理构建的 JAR SHA-256 完全一致；
- `javap` 字节码检查中的 `translationKey` 引用为 `0`。

## 生产部署

生产目标为 `activity-survival`。新 JAR 先上传到 owl5 隔离暂存位置并回读长度与摘要；
运行目录没有热替换。

后台先执行受审计的在线人数查询，再使用结构化“停止”动作保存世界并正常关服。任务变为
`Ready` 且 `25600` 无监听后，旧 JAR 备份到：

```text
E:\manual-backups\activity-survival-economy-0.1.7-fix-20260818T1609
```

旧 JAR SHA-256 为
`13069366685FAB4BE15BE0F362F8B491727DA6284C8D14C3DCAB099F3C63E315`。离线替换后生产插件
目录只有一个 HechaoEconomy JAR，名称、长度和新摘要均与构建制品一致。随后只使用后台
结构化“启动”动作恢复工业季，未启停大厅或其他后端。

## 验收

`2026-08-18 16:12 CST` 复核：

- 计划任务为 `Running`；
- PID 为 `2836`，`25600` 只有一个监听者；
- Java 为 `E:\jdk\bin\java.exe`；
- 启动命令继续使用 Arclight JAR；
- HechaoEconomy `0.1.7` 加载和启用均恰好一次；
- `Done` 恰好一次；
- `/heco health` 的 API、Vault、命令权威和可交易均为 `true`，隔离交易为 `0`；
- 当前启动日志中的 `translationKey`、`NoSuchMethodError` 和 HechaoEconomy 警告/错误均为 `0`。

生产服务端回归已经完成。等待玩家重新进入并点击“回收目录”的最终目视验收；在取得该
证据前，不将真人客户端点击标记为通过。

## 回滚

如出现回归，必须使用后台结构化“停止”动作正常保存世界和关服，删除
`HechaoEconomy-0.1.7.jar`，从上述备份恢复 `HechaoEconomy-0.1.6.jar`，核对旧摘要后再
使用后台结构化“启动”。不得热替换 JAR，也不得从自由控制台发送生命周期命令。
