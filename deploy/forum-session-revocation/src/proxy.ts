import { NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE, verifySession } from "@/lib/session";

// 统一安全网关（Next 16 的 proxy 约定，原 middleware；默认 Node.js 运行时）。
// 保护 /admin 页面与 /api 接口：
//  1) 限流（所有 /api，登录更严，防爆破/灌库）
//  2) CSRF：变更请求校验 Origin 同源
//  3) 鉴权：会话 Cookie 校验（/api 缺则 401，/admin 缺则跳登录）
// 公开接口：POST /api/admin/login、论坛 API，以及固定的会话撤销内部端点。
// 内部端点仍受统一限流，并在路由内使用独立共享令牌鉴权。

const MUTATING = new Set(["POST", "PUT", "PATCH", "DELETE"]);
const LOGIN_PATH = "/api/admin/login";
const SESSION_REVOCATION_PATH = "/api/internal/hechao/session-revoke";

// 简易内存定窗限流。Node 单实例进程内有效、重启清零，属「尽力而为」的基础防滥用；
// 多实例/高要求场景应换 Redis 等共享存储。
const WINDOW_MS = 60_000;
const buckets = new Map<string, { count: number; reset: number }>();

function rateOk(key: string, limit: number): boolean {
  const now = Date.now();
  const b = buckets.get(key);
  if (!b || now > b.reset) {
    buckets.set(key, { count: 1, reset: now + WINDOW_MS });
    return true;
  }
  b.count += 1;
  return b.count <= limit;
}

function clientIp(req: NextRequest): string {
  const xff = req.headers.get("x-forwarded-for");
  if (xff) return xff.split(",")[0].trim();
  return req.headers.get("x-real-ip") ?? "unknown";
}

function deny(status: number, error: string) {
  return NextResponse.json({ error }, { status });
}

export async function proxy(req: NextRequest) {
  const { pathname } = req.nextUrl;
  const isApi = pathname.startsWith("/api");
  const isLogin = pathname === LOGIN_PATH;
  const isSessionRevocation = pathname === SESSION_REVOCATION_PATH;
  // 论坛接口：不走 CMS 管理员门（各路由自校验用户会话）；注册/登录额外严格限流。
  const isForum = pathname.startsWith("/api/forum");
  const isForumAuth = [
    "/api/forum/register",
    "/api/forum/login",
    "/api/forum/forgot",
    "/api/forum/send-code",
    "/api/forum/resend-verification",
  ].includes(pathname);

  // 1) 限流（仅登录/注册等认证端点严格防爆破；普通 API 放宽，避免后台密集请求误杀）
  if (isApi) {
    const ip = clientIp(req);
    const strict = isLogin || isForumAuth;
    // 后台已登录管理员操作密集，给更高额度；普通 API（论坛读/公开）240/分钟足够防滥用。
    const isAdminApi = !isLogin && pathname.startsWith("/api/admin");
    const limit = strict ? 10 : isAdminApi ? 600 : 240;
    const bucket = strict ? "auth" : isAdminApi ? "admin" : "api";
    if (!rateOk(`${ip}|${bucket}`, limit)) {
      return deny(429, "请求过于频繁，请稍后再试");
    }
  }

  // 2) CSRF：变更类请求要求 Origin 同源（含登录）
  if (isApi && MUTATING.has(req.method)) {
    const origin = req.headers.get("origin");
    if (origin) {
      let originHost: string;
      try {
        originHost = new URL(origin).host;
      } catch {
        return deny(403, "非法请求来源");
      }
      if (originHost !== req.headers.get("host")) {
        return deny(403, "跨站请求被拒绝");
      }
    }
    // Origin 缺失：浏览器跨站写必带 Origin；缺失者多为同源工具，交由会话校验把关。
  }

  // 3) 放行（已过限流 + 同源）：CMS 登录、论坛 API 和独立令牌内部端点。
  if (isLogin || isForum || isSessionRevocation) {
    return NextResponse.next();
  }

  // 4) 鉴权
  const authed = await verifySession(req.cookies.get(SESSION_COOKIE)?.value);

  if (isApi) {
    return authed ? NextResponse.next() : deny(401, "未授权，请先登录");
  }

  // /admin 页面：未登录跳转登录页并带回回跳目标
  if (!authed) {
    const url = req.nextUrl.clone();
    url.pathname = "/login";
    url.search = `?next=${encodeURIComponent(pathname)}`;
    return NextResponse.redirect(url);
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/admin/:path*", "/api/:path*"],
};
