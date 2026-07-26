# DollNight 档案 1.0.0 发布记录

> 发布日期：`2026-07-27`
>
> 档案 ID：`dollnight-1.21.11`
>
> 生产状态：已发布并绑定 DollNight

## 发布物

- 版本：`1.0.0`
- Minecraft：`1.21.11`
- Fabric：`0.19.2`
- Java：`21`
- 逻辑文件：`4,902`
- 去重对象：`4,900`
- 逻辑大小：`874,147,856` 字节
- 清单大小：`2,110,885` 字节
- 清单 SHA-256：`6D0C73C2B8CD34621C5D44212047DC562AD05E8277B1F195BDAC0FDA5DA16575`
- 签名 Key ID：`release-2026-07-primary`
- 签名时间：`2026-07-26T18:11:26.9552229+00:00`
- 批准源树 SHA-256：`3DF97F82AC00DF45A4EC392C1896C9B3D97CF5FA5185FB9F5B3366A01AB63D53`

`tools/Prepare-DollNightProfile.ps1` 只接受摘要与批准基线完全一致的干净客户端，
并拒绝复用输出目录。档案使用独立 `.minecraft`，不会与大厅、生存服或其他活动服
共享可写模组和配置。

## 发布与验收

- 使用生产信任包验签并执行发布物闭合校验。
- 在全新隔离目录完整安装，逐文件重算 SHA-256。
- 成功构建 Fabric `0.19.2`、Java 21 和统一 Velocity 入口参数；没有启动 Minecraft。
- 全部 `4,900` 个对象已存在于私有 OSS，经远端元数据校验后跳过，未重复上传。
- 生产目录中的版本、总大小和清单摘要与本地发布物完全一致。
- DollNight 目录继续绑定该独立档案；Member 请求返回 `404`，临时
  Participant 权限可取得清单并完成对象下载。
- 验收用户和会话均已精确清理；发布期间 API 未重启，warning 及以上日志为 `0`。

## 恢复点

- 备份目录：`/var/backups/hechao-launcher/profile-publication/20260726T182024Z`
- 数据库备份 SHA-256：`183CA211FA431656FCD982305ACEA1C7859579D6D0BA9DB2511F947C37117334`
- 清单归档 SHA-256：`3A5380329322D61BAC16E8FBCB84C6036D90E44E5399A95DEA3B077AA613D884`

数据库校验和 `pg_restore --list` 均通过。回滚时同时恢复档案行、清单文件和
DollNight 的档案绑定，不删除内容寻址对象。
