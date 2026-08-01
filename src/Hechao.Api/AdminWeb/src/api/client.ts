interface RequestOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
  rawBody?: BodyInit;
  csrf?: boolean;
  timeoutMs?: number;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly payload: unknown = null
  ) {
    super(message);
  }
}

let csrfToken: string | null = null;
let csrfPromise: Promise<string> | null = null;

function validationMessage(payload: unknown): string | null {
  if (!payload || typeof payload !== "object" || !("errors" in payload)) return null;
  const errors = (payload as { errors?: Record<string, string[]> }).errors;
  return errors ? Object.values(errors).flat().join(" ") : null;
}

function payloadMessage(payload: unknown): string | null {
  if (!payload || typeof payload !== "object") return null;
  const record = payload as Record<string, unknown>;
  for (const key of ["detail", "message", "title"]) {
    if (typeof record[key] === "string" && record[key]) return record[key] as string;
  }
  return null;
}

async function parsePayload(response: Response): Promise<unknown> {
  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("json")) {
    try {
      return await response.json();
    } catch {
      return null;
    }
  }
  try {
    return await response.text();
  } catch {
    return null;
  }
}

function requestSignal(signal: AbortSignal | null | undefined, timeoutMs: number): AbortSignal {
  const timeout = AbortSignal.timeout(timeoutMs);
  return signal ? AbortSignal.any([signal, timeout]) : timeout;
}

async function fetchResponse(path: string, options: RequestOptions = {}): Promise<Response> {
  const method = (options.method ?? "GET").toUpperCase();
  const headers = new Headers(options.headers);
  headers.set("Accept", options.headers && new Headers(options.headers).has("Accept")
    ? new Headers(options.headers).get("Accept")!
    : "application/json");

  if (options.rawBody !== undefined) {
    if (!headers.has("Content-Type")) headers.set("Content-Type", "application/octet-stream");
  } else if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  const unsafe = !["GET", "HEAD", "OPTIONS"].includes(method);
  if (unsafe && options.csrf !== false) {
    headers.set("X-CSRF-TOKEN", await ensureCsrfToken());
  }

  let response: Response;
  try {
    response = await fetch(path, {
      ...options,
      method,
      headers,
      credentials: "same-origin",
      body: options.rawBody !== undefined
        ? options.rawBody
        : options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: requestSignal(options.signal, options.timeoutMs ?? 10_000)
    });
  } catch (error) {
    if (error instanceof DOMException && ["AbortError", "TimeoutError"].includes(error.name)) {
      throw new ApiError(0, error.name === "TimeoutError" ? "请求超时，请稍后重试。" : "请求已取消。");
    }
    throw new ApiError(0, "无法连接管理服务，请检查网络后重试。");
  }

  const protectedSessionRequest =
    path.startsWith("/v1/admin/") || path === "/v1/admin-auth/csrf";
  if (protectedSessionRequest && (response.status === 401 || response.status === 403)) {
    window.dispatchEvent(new CustomEvent("hechao:admin-session-expired"));
  }
  return response;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await fetchResponse(path, options);
  if (response.status === 204) return undefined as T;
  const payload = await parsePayload(response);
  if (!response.ok) {
    const text = typeof payload === "string" ? payload : null;
    throw new ApiError(
      response.status,
      validationMessage(payload) ?? payloadMessage(payload) ?? text ?? "请求失败。",
      payload
    );
  }
  return payload as T;
}

export async function download(path: string, fileName: string): Promise<void> {
  const response = await fetchResponse(path, { headers: { Accept: "application/octet-stream" }, timeoutMs: 60_000 });
  if (!response.ok) {
    const payload = await parsePayload(response);
    throw new ApiError(response.status, payloadMessage(payload) ?? "下载失败。", payload);
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}

export function resetCsrfToken(): void {
  csrfToken = null;
  csrfPromise = null;
}

export async function ensureCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken;
  csrfPromise ??= api<{ requestToken: string }>("/v1/admin-auth/csrf", { csrf: false })
    .then(result => {
      csrfToken = result.requestToken;
      return csrfToken;
    })
    .finally(() => { csrfPromise = null; });
  return csrfPromise;
}
