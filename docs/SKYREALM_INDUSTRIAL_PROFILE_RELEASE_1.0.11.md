# 天域远征工业季客户端档案 1.0.11 发布记录

- 发布日期：2026-08-16
- 档案 ID：`skyrealm-industrial-neoforge-1.21.1`
- Minecraft：`1.21.1`
- 加载器：NeoForge `21.1.228`
- Java：`21`
- 当前通道：`Test=100%`
- 清单 SHA-256：
  `58613946D3CB832C9E06B23CD28669DC562BAE62C40ACB7225F9DE4ED892091C`

## 故障与根因

`1.0.10` 已补齐 NeoForge 的三个启动库，Java 能进入 ModLauncher，但真实启动继续在
Mixin 应用阶段退出。Sable 内嵌 Veil `4.1.4`，Veil 的
`PerformanceAbstractTextureMixin` 使用 MixinExtras 的 `@Local`。NeoForge 只从内嵌
依赖发现 `mixinextras-neoforge-0.5.3.jar` 时，MixinExtras 初始化晚于 Veil Mixin 处理，
`@Local` 没有改写方法签名，最终触发 Mixin 注入签名错误。

这不是内存不足、Java 版本错误或世界数据导致的故障。

## 修复内容

`1.0.11` 保持 `1.0.10` 的 `4456` 个逻辑文件完全不变，只将同一份 MixinExtras 作为
顶层游戏库放入 `mods`，使其在 Veil Mixin 之前初始化：

| 路径 | 字节 | SHA-256 |
| --- | ---: | --- |
| `mods/mixinextras-neoforge-0.5.3.jar` | 725,927 | `9822E773BD9F42D36ED53EA3D67486207291747A7BFACC29BEEB92040721BC9B` |

新清单包含 `4457` 个逻辑文件、`4252` 个去重对象和 `1,203,305,078` 字节。OSS
发布器复核并跳过 `4251` 个既有对象，只上传上述 `1` 个新对象、`725,927` 字节，
没有覆盖旧对象。

## 验证与上线范围

- 生产信任包验签与完整发布物闭合校验通过。
- 后台导入不可变 `1.0.11`，Test 通道更新为 `100%`、修订 `r4`；Gray 与 Production
  继续未分配。
- 启动器 `0.15.8` 从生产 Test 通道取得 `1.0.11`，完成对象校验和原子安装；本地安装
  状态与清单 SHA-256 一致。
- 分发安装后的真实 Minecraft 进程完成 MixinExtras `0.5.3`、NeoForge、Veil、Create、
  Sable、声音和渲染资源初始化，持续运行后由管理员正常关闭，退出码为 `0`。
- API 与 Publisher 保持原 PID、`NRestarts=0` 和 `active/running`；内外网健康与就绪
  端点均为 `200`。发布期间没有启停或重启 Minecraft 服务端、Velocity、服控代理、
  API 或 Publisher。

客户端崩溃已修复。自动进服随后被独立的 Velocity 路由问题拒绝：连接被送到内部
`lobby`，该后端提示没有收到合法的 Velocity forwarding 数据。此问题不属于客户端
档案，必须单独核对一次性授权目标、默认路由和大厅 forwarding；不得通过关闭正版、
开放直连或重新向玩家开放大厅规避。

后续状态：该进服问题已于 `2026-08-16` 解决。实际根因是工业季 `start.bat` 直接运行
NeoForge `win_args.txt`，绕过了 Arclight 的 Velocity forwarding mixin；改为从
Arclight JAR 启动后，真实正版账号已通过 fresh grant 和统一 Velocity 入口稳定进服。
详见
[`SKYREALM_INDUSTRIAL_SERVER_ARCLIGHT_START_FIX_2026-08-16.md`](SKYREALM_INDUSTRIAL_SERVER_ARCLIGHT_START_FIX_2026-08-16.md)。

## 回滚

`1.0.9`、`1.0.10`、`1.0.11` 清单和 OSS 对象均保持不可变。若 Test 后续出现回归，
将 Test 通道回退到 `1.0.10` 清单：

```text
1971A8BFB01C26594A8D17D64F8499F3D82F55EF1BEE38A528B77B27E3DF8EEC
```

回滚不删除或覆盖 `1.0.11`，也不修改 Gray、Production 或服务端状态。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.11_TEST_RELEASE_2026-08-16.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.11_TEST_RELEASE_2026-08-16.json)。
