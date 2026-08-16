# 天域远征工业季客户端档案 1.0.10 发布记录

- 发布日期：2026-08-16
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- 加载器：NeoForge `21.1.228`
- Java：`21`
- 当前通道：`Test=100%`
- 清单 SHA-256：
  `1971A8BFB01C26594A8D17D64F8499F3D82F55EF1BEE38A528B77B27E3DF8EEC`

## 故障与根因

工业季 `1.0.9` 启动后约 `0.24` 秒即以退出码 `1` 结束，游戏没有创建
`latest.log` 或崩溃报告。档案的 NeoForge 版本 JSON 以
`cpw.mods.bootstraplauncher.BootstrapLauncher` 为入口，但签名清单遗漏了以下三个必需
库文件：

- `cpw.mods:bootstraplauncher:2.0.2`
- `cpw.mods:modlauncher:11.0.5`
- `cpw.mods:securejarhandler:3.0.8`

因此 Java 在 Minecraft 日志系统初始化前退出。内存分配和游戏模组不是本次故障原因。

## 修复内容

`1.0.10` 从已验签的 `1.0.9` 清单和本地内容缓存重建。旧版 `4453` 个逻辑文件的路径、
长度、SHA-256、下载 URL 和必需标记全部保持不变，只新增上述三个库文件：

| 路径 | 字节 | SHA-256 |
| --- | ---: | --- |
| `libraries/cpw/mods/bootstraplauncher/2.0.2/bootstraplauncher-2.0.2.jar` | 11,116 | `D3F29309140540570FD6C709509706AA8FB133C1C8AEF24491688F1A3F4E1D49` |
| `libraries/cpw/mods/modlauncher/11.0.5/modlauncher-11.0.5.jar` | 116,486 | `FD9B9FC7CF043D2264EA0113EAC6B258F0A71FECE5B00E831BFBDF9726C78A24` |
| `libraries/cpw/mods/securejarhandler/3.0.8/securejarhandler-3.0.8.jar` | 103,765 | `945C63D6DEAFC821616B0380C23867D9B8C2852438F8A6DF72732AB933FC587D` |

新清单包含 `4456` 个逻辑文件、`4251` 个去重对象和 `1,202,579,151` 字节。OSS 发布器
对 `4248` 个旧对象完成长度与摘要元数据复核并跳过，只上传 `3` 个新对象、
`231,367` 字节，没有覆盖既有对象。

## 验证与上线范围

- 生产信任包验签通过，签名键 ID 为 `release-2026-07-primary`。
- 完整发布闭合校验通过：逻辑文件 `4456`，去重对象 `4251`。
- 新增模块经档案自带 Java 21 和启动器安全短路径加载，模块解析退出码为 `0`。
- 管理机启动器已升级到 `0.15.8`，目录缓存和安装状态均识别 `1.0.10`。
- API 本机与公网健康、就绪端点均为 `200`；API 和 Publisher PID、`NRestarts=0` 均未变化。
- 数据库写入发布导入和 Test 通道更新审计；Gray 与 Production 保持未设置。
- 没有启动、停止或重启任何 Minecraft、Velocity、服控代理、API 或 Publisher 服务。

本次尚未把 `1.0.10` 推进 Gray 或 Production。管理员完成一次真实 Minecraft 启动和
进服验收后，再从客户端档案后台按 `Test -> Gray -> Production` 顺序推进。

## 备份与回滚

发布前数据库和 `1.0.9` 清单备份位于：

```text
/var/backups/hechao-launcher/profile-publications/pre-skyrealm-industrial-1.0.10-20260816T040255Z
```

旧清单和 OSS 对象均保持不可变。若 Test 验收失败，将 Test 通道指回
`77AE2688860D1B62D4BC58D9B66655D119E41742DD515B7191C5BD017933301B`；不覆盖或删除
`1.0.9`、`1.0.10` 清单及内容对象。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.10_TEST_RELEASE_2026-08-16.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.10_TEST_RELEASE_2026-08-16.json)。
