# 赫朝启动器 0.11.10 发布记录

> 构建日期：`2026-07-27`
>
> 启动器源码提交：`efdde7662d097638d181d93af3f5e2ae695df8cf`
>
> Git 标签：`launcher-v0.11.10`
>
> 配套 API：`0.12.0-20260725T203001Z`，本次没有服务端变更
>
> 状态：私有 OSS 灰度候选已验证；替换 `0.11.9`

## 变化

- “Java 使用”改为与内存档位一致的全宽分段选择器。
- Java 模式、路径和内存档位按稳定行高重新排布。
- 修复 WPF `MinHeight` 大于父级固定高度时按钮底边被裁切的问题。
- 内存档位在列表视口中垂直居中，窄侧栏下四边边框保持完整。
- 保留 `0.11.9` 的安全游戏目录、每档案 Java 和 Fabric 类路径修复。

## 验收证据

- Debug 与 Release 完整解决方案测试均为 `218/218`。
- Debug 实机窗口确认 Java 与内存选择器等宽、完整且没有相互覆盖。
- 本机从 `0.11.9` 静默覆盖到 `0.11.10`，安装器退出码为 `0`。
- 安装前后 `settings.json` 与 DPAPI `session.dat` SHA-256 完全一致。
- 安装后的 EXE ProductVersion 为
  `0.11.10+efdde7662d097638d181d93af3f5e2ae695df8cf`。
- 本次没有修改 API、数据库、Velocity、客户端档案或 Minecraft 服务端。

## 制品

| 项目 | 值 |
| --- | --- |
| 启动器 EXE | `D:\Hechao Launcher\Hechao.Launcher.exe` |
| EXE 大小 | `68,679,985` 字节 |
| EXE SHA-256 | `A3B2DA5260DEFC694A8D1C15257FFC31E91C6ADDAC5D66489B0A5A38379BF7B7` |
| 安装包 | `artifacts/installer/Hechao-Launcher-Setup-0.11.10-win-x64.exe` |
| 安装包大小 | `61,819,393` 字节 |
| 安装包 SHA-256 | `4703FEF3113418BB13DBA86F097BE45D2C66BFD020774354117A0001FAA127AA` |
| Windows 签名 | EXE 与安装包均为 `NotSigned` |

## 私有灰度发布

安装包已写入不可变对象：

```text
releases/launcher/0.11.10/Hechao-Launcher-Setup-0.11.10-win-x64.exe
```

第二次发布确认远端对象匹配并跳过，没有覆盖或重复上传。匿名访问返回 `403`；
24 小时 OSS 原始节点签名下载返回 `200`，完整下载长度与 SHA-256 和本机制品一致。
签名链接只保存在当前管理员账户的 ACL 保护目录，没有写入 Git、文档或公告。

## 回滚

若灰度发现独立阻断，关闭启动器后使用 `0.11.9` 覆盖安装。程序覆盖不会删除赫朝
会话、设置、客户端、受管 Java、下载缓存或世界存档。回滚只恢复旧运行配置布局，
不会撤销 `0.11.9` 的安全路径修复。

## 边界

本次只修改 Windows 启动器运行配置侧栏、版本和文档。没有公开下载目录、永久链接、
API 部署、数据库迁移、Velocity 重启或 Minecraft 服务启停。
