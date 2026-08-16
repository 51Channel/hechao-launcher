# 天域远征工业季 TAB 信息面板改版

> 日期：`2026-08-16`
>
> 范围：仅 `activity-survival` 的 TAB 展示配置；未重启服务端，未修改世界、模组、
> 插件 JAR、权限或玩家数据。

## 原因

工业季使用 `TAB v5.0.7`。原 `plugins/TAB/config.yml` 基本保留插件默认模板，玩家
按下 Tab 时会同时看到英文欢迎动画、重复在线人数、员工人数、两处 Ping、JVM 内存和
占位网址。信息层级混乱，并把管理员指标暴露在玩家界面。

## 当前配置

可见 header/footer 收敛为服务器名称、网络标识、在线人数、单处延迟、24 小时时间、
金币和正式网站：

```yaml
header-footer:
  enabled: true
  header:
    - ""
    - "&6&l天域远征 &8· &e工业季"
    - "&8HECHAO NETWORK"
    - "&8&m                                      "
    - "&7在线 &f%online% &8· &7延迟 &b%ping%ms &8· &7时间 &f%time%"
    - ""
  footer:
    - ""
    - "&7金币 &6%hechao_balance%"
    - "&8公告与资料  &7hechao.world"
    - "&8&m                                      "
  disable-condition: '%world%=disabledworld'
```

数字 Ping 目标关闭，只保留玩家列表原生信号格；时间统一为 `HH:mm`：

```yaml
playerlist-objective:
  enabled: false
  value: "%ping%"
  fancy-value: "&7%ping%ms"
  disable-condition: '%world%=disabledworld'

placeholders:
  date-format: "yyyy-MM-dd"
  time-format: "HH:mm"
  time-offset: 0
  register-tab-expansion: false
```

`groups.yml` 删除 TAB 默认示例组，只保留 `_DEFAULT_`，继续由 LuckPerms 提供称号、
前后缀和玩家名称。`%hechao_balance%` 继续由 HechaoEconomy 提供。

## 生产执行与验收

- 服务端：`activity-survival`；目录
  `E:\HechaoActivitySlots\activity-survival`；
- 插件：`TAB v5.0.7`；
- 配置 SHA-256：
  `E4E2108AF7D963AE126AC2528C3AA219BB891DF6A9D98630EF82B381801BA404`；
- 分组 SHA-256：
  `27E0516C4DB5E24A82F89D5CF9DFAC57A4E9637C23A6B1023AC97284ECD241E0`；
- 回滚目录：
  `E:\manual-backups\activity-survival-tab-20260816T140855Z`；
- 通过受管控制台向已核对的工业季 Java PID 发送 `tab reload`；
- TAB 日志依次返回 `Disabled in 4ms`、`Enabled in 83ms` 和
  `Successfully reloaded`；没有 YAML、配置或插件异常；
- 重载前后 Java PID 保持 `5464`，`127.0.0.1:25600` 持续监听。

PID 和端口只属于本次验收快照，后续操作必须实时复核。

## 回滚

出现显示异常时，从上述回滚目录恢复 `config.yml` 和 `groups.yml`，再执行一次
`tab reload`。不需要重启 Minecraft 服务端。

## 重新部署边界

当前改动位于生产服务端目录。若以后重新部署包含旧 `plugins/TAB` 配置的服务端归档，
这些文件可能被覆盖。重新打包工业季服务端时应把本页的配置同步进归档，或在部署后按
本页哈希复核并重新应用；不能仅依赖本次热修改。
