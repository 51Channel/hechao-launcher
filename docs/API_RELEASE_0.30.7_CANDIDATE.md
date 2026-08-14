# API 0.30.7 发布候选

- 候选版本：`0.30.7`
- 功能源码提交：`a5696c5`
- 数据库迁移：无，保持 `028`
- 直接回滚目标：`0.30.6-20260814T133415Z`

## 变更范围

- 已登录玩家目录对所有可见 `activity` 玩家活动保留记录，不再因最低称号不足而把活动、
  排期和客户端下载入口一起隐藏；
- 目录服务器新增向后兼容字段 `canJoin`。有效单服 `Deny` 优先拒绝，`Allow` 优先允许，
  其余情况按账号等级与 `minimumTier` 比较；
- 永久服务器继续沿用原有等级与单服例外过滤，无权账号不会扩大可见范围；
- 可见活动的 Production 客户端档案允许所有已登录账号提前读取签名清单和内容对象；
  隐藏活动、基础设施记录、已封禁身份和没有可解析发布的档案仍保持拒绝；
- Velocity 一次性授权和进服校验不变，等级不足或单服拒绝仍在服务端最终拒绝。

## 候选验证

- API `.NET` `326/326`；
- Launcher `.NET` `229/229`；
- 完整解决方案 `.NET` `748/748`；
- 本次 C# 文件格式校验、XAML XML 解析和 `git diff --check` 通过；
- 全仓库格式检查仍会命中未修改的既有
  `tests/Hechao.Launcher.Tests/MinecraftRunningStateStoreTests.cs` 空格问题，本候选未混入
  该无关格式修复；
- 本候选没有执行数据库写入、OSS 覆盖、服控命令或 Minecraft/Velocity 操作。

## 发布与验收边界

正式发布只允许重启 `hechao-launcher-api.service`。切换前确认整合包、服控和数据库
任务为空，创建 PostgreSQL custom-format 备份及 API、环境、systemd、Nginx 备份；
安装器就绪检查失败时自动恢复 `0.30.6-20260814T133415Z`。

部署后使用生产测试账号验证：`Member` 能看到最低 `Participant` 的活动且
`canJoin=false`，能读取其签名清单；`Participant` 或有效单服 `Allow` 返回
`canJoin=true`，有效 `Deny` 返回 `false`；无权永久服仍保持隐藏。随后确认一次性进服
授权仍拒绝等级不足账号，并核对健康、就绪、回环监听、迁移 `28/28`、旧官网、中转 API、
任务队列和新增 warning/error。不得重启 Publisher、Nginx、Velocity、服控代理或任何
Minecraft 服务端。
