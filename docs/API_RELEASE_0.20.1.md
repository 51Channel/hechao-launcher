# API 0.20.1 发布记录

> 状态：已生产部署并完成公网与日志脱敏回归
>
> 当前生产：`0.20.1-20260727T145451Z`
>
> 源码提交：`f90a2de9eae0fb6044f0fdf7571708b91da50b10`

## 1. 变更

- 私有对象下载不再使用框架的通用 `RedirectResult`，改为无日志的受限 HTTPS
  `302` 结果。
- 新结果只接受绝对 HTTPS 地址，固定返回 `Cache-Control: no-store` 和空响应体，
  不把 OSS 短时签名 URL 交给 ASP.NET 请求结果日志。
- Nginx 新增 `hechao_privacy` 访问日志格式，只记录方法、无查询参数的规范化路径、
  协议、状态、字节数、Host、User-Agent、耗时和请求 ID；不记录查询字符串或
  Referer。
- 论坛、旧中转 API、启动器 API 和管理后台的全部五个 server block 已在自身层级
  覆盖默认访问日志，避免密码重置、OAuth 或签名下载参数进入日志。

## 2. 制品

| 制品 | 大小 | SHA-256 |
| --- | ---: | --- |
| `hechao-api-0.20.1-20260727T145451Z.tar.gz` | `45,575,206` | `035C71CFCAB3ACF2986AE9936833CAD004B4B9087F08385F0FFC9DA39C46F6FC` |
| `Hechao.Api` | `104,442,419` | `94BC3831A4749A545968E90BD1ABD638BE26BD23B058091E2A91AF417D09AB54` |

归档包含 `99` 个文件，不含 PDB、环境文件、内部令牌、SMTP 凭据或外部私钥。

## 3. 自动验证

- `.NET` 完整解决方案 `355/355` 通过，其中 API `170/170`。
- 新增 HTTPS 重定向、`Location`、`no-store`、空响应体及 HTTP/相对地址拒绝测试。
- Nginx 部署脚本通过 `bash -n`，部署时核对旧站点 `2` 个和启动器站点 `3` 个
  隐私日志覆盖。

## 4. 生产部署

切换前统一备份位于：

```text
/var/backups/hechao-unified-account/20260727T145731Z
```

- 备份清单 SHA-256：
  `A95A941E7EA3E9F8C1C42E5004C47CED4515A3ED86B856F22662179693B1935D`
- 数据库 dump SHA-256：
  `D148A14E6108B7D800557CF73FC05D8BC5D4F8F2F5B34B01AEB7E6AC358B41CB`
- `pg_restore --list` 可读取 `177` 个目录项。
- 当前链接指向
  `/opt/hechao-launcher-api/releases/0.20.1-20260727T145451Z`。
- 程序哈希与本地候选一致，安装目录 PDB 数为 `0`。
- 迁移最大值保持 `17`，服务器和客户端档案均为 `6`。
- systemd `NRestarts=0`，部署后无 warning/error。

Nginx 切换前配置备份位于：

```text
/var/backups/hechao-nginx-privacy/20260727T150915Z
```

配置先通过 `nginx -t`，随后仅执行平滑 reload。Nginx、API 和论坛服务保持
`active`，均未产生自动重启。

## 5. 真实回归

- 临时认证会话完成一次清单 `200` 和私有对象 `302`，随后被精确删除。
- 对象响应的 `Location` 为带签名的 HTTPS 地址，响应体为 `0` 字节。
- 该请求之后 API journal 新增 AccessKey ID 行 `0`、OSS 签名行 `0`。
- 使用合成密码重置参数请求 `/forum/reset` 返回 `200`；新 Nginx 日志只记录
  `/forum/reset`，合成 token 命中 `0`、敏感查询参数命中 `0`。
- `/healthz`、`/readyz`、管理后台、官网和旧中转 API 均返回 `200`。
- 私有 OSS 匿名对象继续返回 `403`，无效 Bearer 目录请求继续返回 `401`。

历史 journal 与 Nginx 轮转日志没有被删除。里面的旧短时签名 URL 和重置链接已经
失效，文件权限为 `640`；保留它们是为了不破坏既有审计链。

本次没有启动、停止或重启 Minecraft、Velocity 或任何游戏服。

机器可读证据见
[`evidence/API_0.20.1_LOG_PRIVACY_2026-07-27.json`](evidence/API_0.20.1_LOG_PRIVACY_2026-07-27.json)。

## 6. 回滚

API 直接回滚目标为 `0.20.0-20260727T011953Z`。Nginx 可从
`/var/backups/hechao-nginx-privacy/20260727T150915Z` 恢复站点文件，并删除新增的
日志格式和 snippet；任何回滚都必须先执行 `nginx -t`，通过后才允许 reload。
