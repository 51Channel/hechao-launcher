# 天域远征工业季客户端档案 1.0.22

- 记录日期：`2026-08-19`
- 档案：`skyrealm-industrial-neoforge-1.21.1`
- 不可变版本：`1.0.22`
- 计划标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.22`
- 目标通道：Test-only

## 修正原因

上一份不可变档案 `1.0.21` 的文件内容与客户端一致，但导入元数据误写为 NeoForge
`21.11.42`；实际客户端启动参数和本地构建版本为 `21.1.228`。`1.0.21` 保留作为不可变
审计记录，不覆盖、不删除、不复用其版本号。

`1.0.22` 使用同一份客户端文件重新生成签名清单，只修正 loader 元数据，并由发布器重新
验签和闭合校验。客户端文件没有同路径内容变更，仅继续替换 Screen
`0.2.0` 为 `0.2.1`。

## 本地制品

- 清单：`artifacts/skyrealm-industrial-1.0.22/distribution/manifests/skyrealm-industrial-neoforge-1.21.1.json`；
- 清单大小：`2,025,521` 字节；
- 清单 SHA-256：`6841C556CDDAF6E69B546DEA2C5969A481C1672B66DBC6BACAA60D15EE78D5B8`；
- 逻辑文件：`4,457`；逻辑字节：`1,204,189,699`；
- 去重对象：`4,252`；对象字节：`1,204,181,189`；
- Minecraft：`1.21.1`；NeoForge：`21.1.228`；Java：`21`；
- 签名密钥 ID：`release-2026-07-primary`。

清单已通过离线签名验证和全部对象闭合校验。OSS 发布器 HeadObject 校验结果为
`4,252` 个既有对象、`0` 个上传对象、`0` 个上传字节，没有覆盖旧对象。

## 后台发布状态

`1.0.22` 已由 `51Channel / owner` 管理员会话导入，并且只发布到 Test。2026-08-19
14:27 CST 的后台和 PostgreSQL 回读结果为：

- Test：`1.0.22 / 100% / r15`，清单 SHA-256 为
  `6841C556CDDAF6E69B546DEA2C5969A481C1672B66DBC6BACAA60D15EE78D5B8`；
- Gray：未分配，`0% / r1`；
- Production：未分配，`r1`。

后台版本详情显示 NeoForge `21.1.228`、Java `21`、`4,457` 个文件。审计 `9349` 记录
不可变版本导入，审计 `9350` 记录 Test 从 `1.0.21 / r14` 切换到 `1.0.22 / r15`。
生产 API `0.35.0` 健康与就绪均为 `200`，数据库为 `ready`，`NRestarts=0`，发布窗口
warning 以上日志为 `0`。

远端不可变清单位于受控清单目录，大小与摘要均和本地一致，权限为
`hechao-api:hechao-api / 0640`。匿名访问清单和 Screen 对象都返回 `401`；玩家 Test
会话的实际下载与页面目视验收仍待在启动器内完成。

回滚目标为 Test 恢复到 `1.0.20`，不删除 `1.0.21`、`1.0.22` 清单或其 OSS 对象。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.22_TEST_RELEASE_2026-08-19.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.22_TEST_RELEASE_2026-08-19.json)。
