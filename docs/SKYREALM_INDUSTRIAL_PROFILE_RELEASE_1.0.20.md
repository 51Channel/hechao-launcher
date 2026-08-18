# 天域远征工业季客户端档案 1.0.20 Test 发布

- 发布日期：`2026-08-18`
- 档案：`skyrealm-industrial-neoforge-1.21.1`
- 不可变版本：`1.0.20`
- 正式标签：`profile-skyrealm-industrial-neoforge-1.21.1-v1.0.20`
- Test：`r13 / 1.0.20 / 100%`
- Gray：未分配
- Production：未分配

## 发布结果

- 签名清单大小：`2,025,517` 字节；
- 签名清单 SHA-256：
  `C343D06AD3B209723C9C91C6D53D0C70D61AFD2AE7EA896A7350E38321D7E825`；
- 逻辑文件 `4,457` 个、逻辑字节 `1,204,176,089`；
- 去重对象 `4,252` 个、对象字节 `1,204,167,579`；
- 与 `1.0.19` 共同的 `4,455` 个文件完全不变；
- 只删除 Screen `0.1.10` 并新增 Screen `0.2.0`，没有同路径内容覆盖。

OSS 发布器校验后跳过 `4,251` 个既有对象，只新增 `1` 个对象、`894,611` 字节。签名、
对象闭合和发布校验均通过，没有覆盖旧对象、旧清单或旧标签。

## API 导入与通道

生产 API 已验签导入不可变 `1.0.20`。存储清单为 `hechao-api:hechao-api / 0640`，大小和
摘要与本地一致；导入与 Test 通道更新审计均已生成。Test 为 `r13 / 100%`，Gray 和
Production 保持未分配。

## 回滚与待验收

客户端回滚只把 Test 指针恢复到 `1.0.19`，不删除 `1.0.20` 清单和对象。真人双账号完整
市场流程通过前，不得把 `1.0.20` 推进 Gray 或 Production。

结构化证据见
[`evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.20_TEST_RELEASE_2026-08-18.json`](evidence/SKYREALM_INDUSTRIAL_PROFILE_1.0.20_TEST_RELEASE_2026-08-18.json)。
