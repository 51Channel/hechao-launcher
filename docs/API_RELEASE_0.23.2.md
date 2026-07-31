# API 0.23.2 正式发布

- 发布 ID：`0.23.2-20260731T050744Z`
- 正式标签：`api-v0.23.2`
- 制品源码提交：`57fad5e8a7cf39f7de143d4d743dcee559e15c7e`
- 当前状态：已于 2026-07-31 部署生产，随后由 `0.24.0` 正常替代

## 变更

- 限定服控快捷设置中白名单复选框的固有尺寸，避免通用表单样式把复选框拉伸为整行大块。
- 保持原有 CSRF、MFA、服控权限和设置协议不变。

## 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.23.2-20260731T050744Z.tar.gz` | `45,641,384` | `D4E1AF0A8E02820C04D52F199581D45D5436887D0C4B9C5730736F5B6D0E2DD5` |
| `Hechao.Api` | `104,595,507` | `6C3CB5B93086EB3CA96428AEDF409C74FAD96527E0547916FE07162AF57B0AE6` |

归档不包含 PDB、环境文件或凭据。

## 生产验收

- `hechao-launcher-api.service` 为 `active/running`，`NRestarts=0`。
- 公网 `/healthz` 与 `/readyz` 均返回 `200`，版本为 `0.23.2`。
- 已登录后台实测复选框为 `16 x 16 px`、同行垂直居中，浏览器控制台没有错误。
- 部署只重启 API，没有启动、停止或重启 Minecraft 服务。

## 回滚

该版本由 [`install-release.sh`](../deploy/linux/install-release.sh) 原子安装；就绪检查失败时会恢复原 `current` 符号链接。版本目录仍保留在：

`/opt/hechao-launcher-api/releases/0.23.2-20260731T050744Z`
