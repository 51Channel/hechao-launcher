# API 0.24.0 正式发布

- 发布 ID：`0.24.0-20260731T062107Z`
- 正式标签：`api-v0.24.0`
- 制品源码提交：`088ca911abcceba741c45f3fef0296439a350d14`
- 生产切换时间：2026-07-31 14:31 CST

## 变更

- 服控目标列表与详情显示代理、端口、PID、`Xms`、`Xmx` 和单服内存硬上限。
- 快捷设置增加 `Xms`、`Xmx` GiB 输入；保存后不自动重启 Minecraft，下次启动生效。
- API 只接受 `512-65536 MiB`、`256 MiB` 步长且 `Xms <= Xmx <= 单服上限` 的参数。
- 代理再次执行相同边界检查，拒绝越权或不完整的内存设置。

## 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.24.0-20260731T062107Z.tar.gz` | `45,644,864` | `3C852B98AA7BC99DB3EF8CE9EB3BC500262A12CE29D55905D03D5CC16D1439B4` |
| `Hechao.Api` | `104,597,043` | `A90E7C61FECF811C183293403CD4B1816EFF6047DB5788A415BD6EFC1D34B66A` |

归档不包含 PDB、环境文件或凭据。

## 生产结果

- 当前目录：`/opt/hechao-launcher-api/releases/0.24.0-20260731T062107Z`
- 服务：`active/running`，`NRestarts=0`
- 公网和本机 `/healthz`、`/readyz`：`200`，版本 `0.24.0`，数据库 `ready`
- 两台代理心跳版本：`0.2.0`
- 服控目标：`9`；在线代理：`2`；运行目标：`5`
- 待处理操作：`0`；待处理命令：`0`
- 发布后错误级日志：`0`

运行中的 PID 在发布前后保持为：

- owl5：`2576`、`6008`、`6112`、`9428`
- owl9：`2912`

## 内存基线

| 服务端 | Xms | Xmx | 硬上限 |
| --- | ---: | ---: | ---: |
| `lobby` | 1 GiB | 2 GiB | 4 GiB |
| `survival1` | 0.5 GiB | 2 GiB | 6 GiB |
| `survival2` | 1 GiB | 2 GiB | 6 GiB |
| `dollnight` | 4 GiB | 11 GiB | 12 GiB |
| `activity` | 2 GiB | 6 GiB | 8 GiB |
| `fanstreet` | 2 GiB | 6 GiB | 8 GiB |
| `yugong` | 2 GiB | 6 GiB | 8 GiB |
| `pvp`（恐怖整蛊） | 2 GiB | 5 GiB | 6 GiB |
| `pvp-purpur`（真正 PVP） | 2 GiB | 4 GiB | 6 GiB |

## 验证

- 完整解决方案：`467/467`
- API：`215/215`
- 服控代理：`19/19`
- API 与代理零警告构建通过。
- JavaScript、JSON 和 PowerShell 语法检查通过。
- 九个生产内存文件均完成只读唯一参数核验。
- 生产静态资源、九个目标内存 JSON、输入边界和零错误日志均已复核。
- 2026-07-31 已在完成 MFA 的生产后台补做最终目视验收：九个目标均显示状态、代理和 `Xmx`，详情页均显示 `Xms`、`Xmx` 与单服上限，两个内存输入框尺寸一致且可编辑，页面无横向溢出。验收过程未保存设置，也未启停或重启 Minecraft。

详细证据见 [`evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_ACCEPTANCE_2026-07-31.json`](evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_ACCEPTANCE_2026-07-31.json)。
登录态目视补充证据见 [`evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_VISUAL_ACCEPTANCE_2026-07-31.json`](evidence/SERVER_CONTROL_MEMORY_MANAGEMENT_VISUAL_ACCEPTANCE_2026-07-31.json)。

## 回滚

[`install-release.sh`](../deploy/linux/install-release.sh) 在切换后等待 `/readyz`，失败会自动恢复旧链接。上一已知正常版本仍保留在：

`/opt/hechao-launcher-api/releases/0.23.2-20260731T050744Z`

本次没有修改环境文件、Nginx、数据库结构或任何 Minecraft 进程。
