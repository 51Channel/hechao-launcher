# Publisher Agent 1.2.1 正式发布

- 源码提交：`1c4d97cac0cd1ae3c4d91aa85e43c4499db0c679`
- 生产目录：`/opt/hechao-package-publisher/releases/1.2.1-20260806T153324Z`
- 生产二进制：74,638,875 字节
- SHA-256：`B54C39C4AA3DCF31FBD9D80E9A12323BA1B0A149F6E6961CF8B4B88567F3A736`

## 修复

- 内部 API 调用继续使用 `http://127.0.0.1:8090`；
- 签名清单中的对象地址改用独立的公网对象基址
  `https://launcher-api.hechao.world`；
- 避免 Linux 同机部署把回环地址写进玩家清单；
- 保留 1.2.0 的真实阶段、对象、字节和 ETA 进度上报。

生产服务 `hechao-package-publisher.service` 为 `active/running`、`NRestarts=0`。
任务 `777a31bf-acc9-4754-9f4b-a3a2e5be95f1` 已完成全部客户端对象校验和清单
最终化，后续服务端重试期间 `publisher_attempt_count` 保持 `4`，没有重复发布。
