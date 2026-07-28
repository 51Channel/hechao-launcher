# 恐怖整蛊 Fabric 档案 1.0.0 发布记录

> `pvp-fabric-1.20.1`、服务器 ID `pvp` 与 Velocity 目标 `pvp` 是历史内部标识。
> 本档案绑定 `C:\mc\server` 的恐怖整蛊服，不绑定
> `E:\MinecraftServer` 的真正 PVP 服。见
> [`OWL9_DUAL_BACKEND_OPERATIONS.md`](OWL9_DUAL_BACKEND_OPERATIONS.md)。

> 档案 ID：`pvp-fabric-1.20.1`
> 版本：`1.0.0`
> 发布时间：`2026-07-25T20:12:10.4811149+00:00`
> 状态：生产清单、目录与 OSS 对象已激活

## 发布内容

- 源目录：`H:\MC\Minecraft 1.20.1 Fabric - 玩家客户端`
- 干净源：`artifacts/client-sources/pvp-fabric-1.20.1-1.0.0`
- Minecraft：`1.20.1`
- Fabric Loader：`0.16.14`
- Java：`17`
- 模组数：`14`
- 游戏服务器：`owl9.vipi9.top:19243`
- 语音端口：UDP `19267`

源目录清单 SHA-256 为 `5415D5C56D2BB83416EAAD1CD6FD5CDA755A9983162D6C64716DA785F2418D57`。发布过程只读取日常客户端，所有清理和构建均在独立干净源中完成。

## 清单与对象

- 清单：`artifacts/distributions/pvp-fabric-1.20.1-1.0.0/manifests/pvp-fabric-1.20.1.json`
- 清单 SHA-256：`A5BCBBA71C69E85F0ACE4000C1983F8C9C1C1D7F546AFA36C53AE39C895706E6`
- 签名密钥：`release-2026-07-primary`
- 逻辑文件：`3,749`
- 逻辑大小：`885,821,291` 字节
- 去重对象：`3,748`
- 去重大小：`862,792,438` 字节
- 新上传对象：`3,547`
- 新上传字节：`764,553,396`
- 已存在并校验跳过：`201`

跳过对象必须同时匹配远端 `Content-Length` 与 SHA-256 元数据；任何不匹配都应停止发布，不能覆盖现有内容寻址对象。

## 生产目录

- 服务器 ID：`pvp`
- 显示名：`恐怖整蛊`
- 最低等级：`Participant`
- Velocity 目标：`pvp`
- 最大人数：`20`
- 排序：`35`

心跳采集器已登记 `owl9.vipi9.top:19243`，生产观测为 Minecraft `1.20.1`、协议 `763`、`0/20`。

## 备份与回滚

- 数据库备份：`/var/backups/hechao-launcher/database/hechao-launcher-20260725T202241Z.dump`
- 发布前清单快照：`/var/backups/hechao-launcher/profile-publications/pre-pvp-fabric-1.0.0-20260725T202252Z`
- 清单快照归档 SHA-256：`CD99BB5059B58EA834B0BFF8D3A27D061C32439ED5E7D9E079ECA21DC4CBCF0F`

回滚只恢复上一份目录记录和签名清单，不删除已上传的内容寻址对象。恐怖整蛊档案的 Java 17、Fabric 版本和独立 `.minecraft` 不能与 1.21.11 档案混用。
