import { afterEach, describe, expect, it, vi } from "vitest";
import { api, resetCsrfToken } from "@/api/client";

function jsonResponse(payload: unknown, status = 200): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

describe("admin API client", () => {
  afterEach(() => {
    resetCsrfToken();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("adds one cached CSRF token to unsafe requests", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ requestToken: "csrf-token" }))
      .mockResolvedValueOnce(jsonResponse({ saved: true }))
      .mockResolvedValueOnce(jsonResponse({ saved: true }));
    vi.stubGlobal("fetch", fetchMock);

    await api("/v1/admin/example", { method: "POST", body: { value: 1 } });
    await api("/v1/admin/example", { method: "PUT", body: { value: 2 } });

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[0][0]).toBe("/v1/admin-auth/csrf");
    const firstWrite = fetchMock.mock.calls[1][1] as RequestInit;
    const secondWrite = fetchMock.mock.calls[2][1] as RequestInit;
    expect(new Headers(firstWrite.headers).get("X-CSRF-TOKEN")).toBe("csrf-token");
    expect(new Headers(secondWrite.headers).get("X-CSRF-TOKEN")).toBe("csrf-token");
  });

  it("does not recursively expire the session while probing an unauthenticated session", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "unauthorized" }, 401)));
    const expired = vi.fn();
    window.addEventListener("hechao:admin-session-expired", expired);

    await expect(api("/v1/admin-auth/session", { csrf: false }))
      .rejects.toMatchObject({ status: 401 });
    expect(expired).not.toHaveBeenCalled();
  });

  it("announces expiration for protected admin data", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ detail: "expired" }, 403)));
    const expired = vi.fn();
    window.addEventListener("hechao:admin-session-expired", expired, { once: true });

    await expect(api("/v1/admin/catalog/servers"))
      .rejects.toMatchObject({ status: 403 });
    expect(expired).toHaveBeenCalledOnce();
  });
});
