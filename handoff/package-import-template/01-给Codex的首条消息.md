# 给 Codex 的首条消息

把尖括号字段替换为真实信息。未知项写“待核验”，不要保留尖括号并继续打包。

```text
这是赫朝 Minecraft 双端整合包制作任务。请先完整读取本目录 AGENTS.md、
00-从这里开始.md、02-标准上传包格式.md、03-客户端制作规范.md、
04-服务端制作规范.md、05-导入与企划流程.md，以及 reference/platform-docs 下的
PACKAGE_IMPORT_OPERATIONS.md、ACTIVITY_PLAN_OPERATIONS.md、
ACTIVITY_CHANNEL_DEVELOPMENT_STANDARD.md 和 HECHAO_NEW_SERVER_BASELINE.md。

活动名称：<真实显示名>
活动源码：<绝对路径或 Git 地址>
客户端源：<绝对路径>
服务端源：<绝对路径>
任务类型：<新活动 / 兼容更新 / 不兼容升级>
Minecraft：<版本或待核验>
加载器：<Vanilla / Paper / NeoForge / Fabric / Forge 或待核验>
加载器版本：<精确版本或待核验>
Java 主版本：<8 / 17 / 21 或待核验>
客户端 versionId：<versions 下的真实目录名或待核验>
地图策略：<包内新地图 / 部署时保留旧世界 / 无预置地图 / 待核验>
生产权限：<仅制作，不上传 / 允许 Test 上传 / 允许部署但保持停服 / 其他明确授权>

目标是生成赫朝规范化业务 ZIP。ZIP 根目录必须直接包含 hechao-pack.json、client、
server 和可选 shared，不再套外层目录。不要直接提交 CurseForge 或 MRPACK 的远程引用包。

请先只读盘点真实文件、Git 状态、版本元数据、JAR 来源、许可证、地图和秘密文件风险，
再列出已确认事实、阻断项和拟采用的 profileId。不要猜测版本，不要要求我提供密码、
令牌、forwarding.secret、Cookie 或私钥。

客户端必须形成完整可启动的隔离 .minecraft：包含 hechao-profile.json、匹配的版本
JSON/JAR、assets、libraries、必要 mods/config；不要包含 Java runtime、账号缓存、日志、
世界、截图、PCL 状态或启动器账号文件。

服务端必须形成完整可启动目录：包含 server.properties、eula.txt、user_jvm_args.txt、
受管 start.bat、服务端核心/加载器、mods/plugins/config 和必要地图。start.bat 必须有
单独一行 `if not defined HECHAO_MANAGED_START pause`，并使用 user_jvm_args.txt。
不要把 forwarding.secret 放进包；不要复制大厅、Survival 或旧活动服的完整目录。

两端共用 JAR 必须来自同一次构建且 SHA-256 一致。只在确实要求两端相同字节时使用
shared；其他内容显式放 client 或 server。

完成后必须运行：
1. tools/Test-HechaoPackageImportSource.ps1；
2. tools/New-HechaoPackageImportArchive.ps1；
3. 全新客户端启动验证；
4. 专用服务端无图形启动、端口和安全停服验证；
5. 共同 JAR 哈希、文件数、字节数、ZIP SHA-256 和许可证复核。

最终交付业务 ZIP、同名 .sha256、.report.json 和按 06-最终交付清单.md 填写的报告。
未获得明确生产授权时，到本地制品和测试结束，不连接或修改生产平台。
```

## 修复任务追加信息

```text
当前整合包版本：<版本>
最近正常版本：<版本或未知>
复现步骤：<步骤>
期望行为：<期望>
实际行为：<实际>
必须保持不变的玩法和数据：<内容>
```
