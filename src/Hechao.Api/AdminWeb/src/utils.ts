import type { AccessTier, EffectiveAccessReason, ServerStatus } from "@/api/types";

export const iconUrl = (name: string) => `/admin/assets/icons/${name}.svg`;
export const brandMarkUrl = "/admin/assets/hechao-mark.png";

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleString("zh-CN", {
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit"
  });
}

export function formatRelativeTime(value: string | null | undefined): string {
  if (!value) return "—";
  const milliseconds = Date.now() - new Date(value).getTime();
  if (!Number.isFinite(milliseconds)) return "—";
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  if (seconds < 60) return `${seconds} 秒前`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} 分钟前`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} 小时前`;
  return `${Math.floor(hours / 24)} 天前`;
}

export function formatBytes(bytes: number | null | undefined): string {
  if (!Number.isFinite(bytes) || !bytes || bytes <= 0) return "—";
  const units = ["B", "KiB", "MiB", "GiB", "TiB"];
  let value = bytes;
  let index = 0;
  while (value >= 1024 && index < units.length - 1) { value /= 1024; index += 1; }
  return `${value >= 10 || index === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[index]}`;
}

export function formatPercentage(value: number): string {
  return `${(value * 100).toFixed(value < 0.1 ? 1 : 0)}%`;
}

export function tierText(tier: AccessTier): string {
  return { Member: "成员", Participant: "活动成员", Collaborator: "协作者", Administrator: "管理员" }[tier];
}

export function tierRank(tier: AccessTier): number {
  return { Member: 0, Participant: 1, Collaborator: 2, Administrator: 3 }[tier];
}

export function statusText(status: ServerStatus): string {
  return { Online: "开放", Maintenance: "维护", Closed: "关闭" }[status];
}

export function accessReasonText(reason: EffectiveAccessReason): string {
  return {
    AllowedByTier: "称号等级满足", AllowedByRule: "单服规则允许", PlayerNotLinked: "未绑定正版身份",
    PlayerDisabled: "账号已停用", MinecraftIdentityBanned: "UUID 已封禁", ServerArchived: "服务器已归档",
    ServerUnavailable: "服务器未开放", DeniedByRule: "单服规则拒绝", InsufficientTier: "称号等级不足",
    PermissionDataStale: "称号数据待同步"
  }[reason];
}

export function shortHash(value: string | null | undefined): string {
  if (!value) return "—";
  return value.length > 12 ? `${value.slice(0, 8)}…${value.slice(-4)}` : value;
}

export function toLocalDateTimeInput(value: string | null | undefined): string {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

export function fromLocalDateTimeInput(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}
