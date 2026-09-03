# 赫朝商业街建筑对决 1.0.0 标准整合包候选

> 晋级状态：本候选已于 `2026-09-03` 完成 Test-only 客户端发布和停止槽部署，正式记录见
> [`COMMERCIAL_STREET_PACKAGE_1.0.0.md`](COMMERCIAL_STREET_PACKAGE_1.0.0.md)。Gray、
> Production 和玩家开放仍未授权。

## 身份

- 档案 ID：`minigame-commercial-street-forge-1.12.2`
- 显示名：`赫朝商业街建筑对决`
- 版本：`1.0.0`
- Minecraft：`1.12.2`
- Forge：`14.23.5.2859`
- Java：`8`
- 计划动态槽：`minigame-commercial-street / Minigame`

用户提供的 `商业街建筑对决交接.zip` 是外层交接容器，内部再次嵌套客户端 ZIP 和服务端
ZIP。后台不会递归分析嵌套归档，因此原上传只识别成 `source / Unknown / 5 文件`。
本候选已重建为根级 `hechao-pack.json + client + server` 标准结构；可选 `shared` 为空，
因此构建器没有在最终 ZIP 中写入空目录。

## 最终制品

| 项目 | 值 |
| --- | --- |
| 文件 | `H:\\MCMOD\\artifacts\\package-import\\minigame-commercial-street-forge-1.12.2-1.0.0.zip` |
| 大小 | `465,456,939` 字节 |
| SHA-256 | `BA59F103599ADBEFFE9CB5EB706732936B2616579D1D7B04430CE3F8FC76BBD2` |
| 总文件 | `1,475` |
| 客户端 | `1,405` 文件 / `352,494,958` 字节 |
| 服务端 | `69` 文件 / `112,661,170` 字节 |
| 客户端/服务端共同 JAR | `10` 份，逐文件摘要一致 |

同目录包含 `.sha256` 和逐文件 `.report.json`。标准构建器使用固定时间戳生成归档，并在
落盘前逐条回读文件长度与 SHA-256。生产分析器复核为 `Canonical`，准确识别 Forge、
Java 8、客户端与服务端，问题数 `0`、阻断数 `0`。

## 净化与世界

- 客户端只保留 `.minecraft` 内容，删除 PCL 状态、PCL 版本目录和误带的服务端语音配置；
- 不携带 PCL/Node 启动器、独立 runtime、账号缓存、日志、截图或存档；
- 自定义版本 JSON 显式声明 `jre-legacy / 8`，并继承 Mojang 的安全日志配置；
- 保留 `options.txt`，因为五份活动资源包需要按既定顺序启用；
- 服务端删除内嵌 runtime 和手工 `START-SERVER.cmd`，改用受管 `start.bat` 与
  `user_jvm_args.txt`；
- 世界目录标准化为 `world`，保留地图区块与 `LayoutBuilt=1 / LayoutVersion=3`；
- 删除 `playerdata/stats/advancements`、旧支付审计，并清空钱包、图片槽、认领、交易、
  破坏追踪和事故记录；没有保留原交接包中的离线 UUID 身份状态。

## 隔离验收

- Java：签名有效的 Eclipse Temurin `8u502`；
- Forge 冷启动：`Done (1.885s)`；
- 状态协议：`1.12.2 / 340 / 0/24`；
- 玩法自检：`SELFTEST PASS`，覆盖 22 个店铺、Cocricot 收银台、LittleFrames 图片服务、
  付款账本、样板建筑和固定晴天；
- `list`、`save-all`、正常 `stop`、退出码 `0` 和端口释放通过；
- 冷启动日志 ERROR/Exception/OOM/崩溃匹配数为 `0`。

## 生产门禁

当前只能进入 Test 客户端分发和停止状态部署，不能向普通玩家开放：

- 生产 Velocity 使用正版登录与 modern forwarding，Forge `1.12.2` 当前没有已批准的
  兼容实现；
- 当前没有 Forge `1.12.2` 深度 TPS/MSPT/GC 指标组件；
- Simple Voice Chat 仍使用包内 UDP `24454`，尚未分配独立公网 UDP 映射；
- 第三方模组来源与许可证仍需按组件计划逐项归档；
- 正确客户端经启动器 fresh grant 进入、错误客户端拒绝、直接后端不可达、世界正式备份
  恢复，以及 `2/3/5/20` 人灰度尚未完成。

在上述门禁闭合前，目录必须保持隐藏且 `Closed`，服务端保持停止，Gray/Production 不得
分配。本记录不授权临时关闭正版验证、直连后端或回退大厅。
