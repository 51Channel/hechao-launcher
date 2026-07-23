"use strict";

const iconRoot = "/admin/assets/icons/";
const state = {
    session: null,
    csrfToken: null,
    servers: [],
    profiles: [],
    auditEntries: [],
    auditBeforeId: null,
    activeView: "servers",
    serverFilter: "visible",
    serverSearch: "",
    editingServer: null,
    pendingVisibilityChange: null,
    recoveryCodes: [],
    toastTimer: null
};

class ApiError extends Error {
    constructor(status, message, payload) {
        super(message);
        this.status = status;
        this.payload = payload;
    }
}

const elements = {};

document.addEventListener("DOMContentLoaded", () => {
    cacheElements();
    bindEvents();
    initialize().catch(error => {
        console.error(error);
        showSignIn("管理后台暂时不可用，请稍后重试。");
    });
});

function cacheElements() {
    [
        "loading-view", "sign-in-view", "sign-in-error", "close-tab-button",
        "mfa-view", "mfa-account-name", "mfa-verify-step", "mfa-enroll-step",
        "mfa-verify-form", "mfa-enroll-form", "mfa-code", "mfa-enroll-code",
        "mfa-error", "mfa-logout-button", "begin-enrollment-button",
        "enrollment-content", "mfa-qr-code", "mfa-secret-key",
        "copy-secret-button", "enrollment-expiry", "console-view",
        "account-avatar", "account-name", "account-group", "logout-button",
        "breadcrumb-current", "view-title", "last-refreshed", "refresh-button",
        "servers-section", "profiles-section", "audit-section",
        "server-total-count", "server-online-count", "server-maintenance-count",
        "server-archived-count", "server-search", "create-server-button",
        "server-table-body", "server-empty-state", "profile-count",
        "profile-table-body", "audit-list", "audit-empty-state",
        "load-more-audit-button", "server-drawer", "server-form",
        "drawer-kicker", "drawer-title", "close-drawer-button",
        "cancel-server-button", "save-server-button", "form-error",
        "server-id", "server-display-name", "server-short-name",
        "server-icon-glyph", "server-status", "server-max-players",
        "server-minecraft-version", "server-loader", "server-minimum-tier",
        "server-sort-order", "server-client-profile", "server-velocity-target",
        "server-is-visible", "server-visible-field", "server-revision-label",
        "confirm-dialog", "confirm-icon", "confirm-title", "confirm-message",
        "cancel-confirm-button", "accept-confirm-button", "recovery-dialog",
        "recovery-code-list", "copy-recovery-button", "download-recovery-button",
        "finish-recovery-button", "toast", "toast-icon", "toast-message"
    ].forEach(id => {
        elements[id] = document.getElementById(id);
    });
}

function bindEvents() {
    elements["close-tab-button"].addEventListener("click", () => window.close());
    elements["mfa-logout-button"].addEventListener("click", logout);
    elements["logout-button"].addEventListener("click", logout);
    elements["mfa-verify-form"].addEventListener("submit", verifyMfa);
    elements["mfa-enroll-form"].addEventListener("submit", completeEnrollment);
    elements["begin-enrollment-button"].addEventListener("click", beginEnrollment);
    elements["copy-secret-button"].addEventListener("click", () =>
        copyText(elements["mfa-secret-key"].value, "验证器密钥已复制"));
    elements["refresh-button"].addEventListener("click", refreshCurrentView);
    elements["server-search"].addEventListener("input", event => {
        state.serverSearch = event.target.value.trim().toLocaleLowerCase("zh-CN");
        renderServers();
    });
    document.querySelectorAll("[data-server-filter]").forEach(button => {
        button.addEventListener("click", () => {
            state.serverFilter = button.dataset.serverFilter;
            document.querySelectorAll("[data-server-filter]").forEach(item =>
                item.classList.toggle("active", item === button));
            renderServers();
        });
    });
    document.querySelectorAll("[data-view]").forEach(button => {
        button.addEventListener("click", () => switchView(button.dataset.view));
    });
    elements["create-server-button"].addEventListener("click", openCreateServer);
    elements["close-drawer-button"].addEventListener("click", closeServerDrawer);
    elements["cancel-server-button"].addEventListener("click", closeServerDrawer);
    elements["server-form"].addEventListener("submit", saveServer);
    elements["cancel-confirm-button"].addEventListener("click", () =>
        elements["confirm-dialog"].close());
    elements["accept-confirm-button"].addEventListener("click", applyVisibilityChange);
    elements["load-more-audit-button"].addEventListener("click", loadMoreAudit);
    elements["copy-recovery-button"].addEventListener("click", () =>
        copyText(state.recoveryCodes.join("\n"), "恢复码已复制"));
    elements["download-recovery-button"].addEventListener("click", downloadRecoveryCodes);
    elements["finish-recovery-button"].addEventListener("click", finishRecoverySetup);
    elements["recovery-dialog"].addEventListener("cancel", event =>
        event.preventDefault());
}

async function initialize() {
    const ticket = new URLSearchParams(window.location.hash.slice(1)).get("ticket");
    if (ticket) {
        history.replaceState(null, "", `${location.pathname}${location.search}`);
        try {
            await api("/v1/admin-auth/redeem", {
                method: "POST",
                body: { ticket },
                csrf: false
            });
        } catch (error) {
            showSignIn(error.message);
            return;
        }
    }

    try {
        state.session = await api("/v1/admin-auth/session", { csrf: false });
    } catch (error) {
        if (error.status === 401 || error.status === 403) {
            showSignIn();
            return;
        }

        throw error;
    }

    await ensureCsrfToken();
    if (!state.session.mfaVerified) {
        showMfa();
        return;
    }

    await enterConsole();
}

async function api(path, options = {}) {
    const method = options.method || "GET";
    const headers = new Headers({ "Accept": "application/json" });
    if (options.body !== undefined) {
        headers.set("Content-Type", "application/json");
    }

    const unsafe = !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());
    if (unsafe && options.csrf !== false) {
        await ensureCsrfToken();
        headers.set("X-CSRF-TOKEN", state.csrfToken);
    }

    const response = await fetch(path, {
        method,
        headers,
        credentials: "same-origin",
        body: options.body === undefined ? undefined : JSON.stringify(options.body)
    });
    if (response.status === 204) {
        return null;
    }

    const payload = await readJson(response);
    if (!response.ok) {
        const validationMessage = payload?.errors
            ? Object.values(payload.errors).flat().join(" ")
            : null;
        throw new ApiError(
            response.status,
            validationMessage || payload?.detail || payload?.message || payload?.title || "请求失败。",
            payload);
    }

    return payload;
}

async function readJson(response) {
    const type = response.headers.get("content-type") || "";
    if (!type.includes("json")) {
        return null;
    }

    try {
        return await response.json();
    } catch {
        return null;
    }
}

async function ensureCsrfToken() {
    if (state.csrfToken) {
        return;
    }

    const result = await api("/v1/admin-auth/csrf", { csrf: false });
    state.csrfToken = result.requestToken;
}

function showOnly(view) {
    ["loading-view", "sign-in-view", "mfa-view", "console-view"].forEach(id => {
        elements[id].hidden = id !== view;
    });
}

function showSignIn(message = "") {
    showOnly("sign-in-view");
    setInlineError(elements["sign-in-error"], message);
}

function showMfa() {
    showOnly("mfa-view");
    elements["mfa-account-name"].textContent = state.session.player.minecraftName;
    elements["mfa-verify-step"].hidden = !state.session.mfaConfigured;
    elements["mfa-enroll-step"].hidden = state.session.mfaConfigured;
    elements["enrollment-content"].hidden = true;
    elements["begin-enrollment-button"].hidden = state.session.mfaConfigured;
    setInlineError(elements["mfa-error"], "");
    window.setTimeout(() => {
        const target = state.session.mfaConfigured
            ? elements["mfa-code"]
            : elements["begin-enrollment-button"];
        target.focus();
    }, 0);
}

async function beginEnrollment() {
    setBusy(elements["begin-enrollment-button"], true);
    setInlineError(elements["mfa-error"], "");
    try {
        const enrollment = await api("/v1/admin-auth/mfa/enrollment", {
            method: "POST",
            body: {}
        });
        elements["mfa-qr-code"].src = enrollment.qrCodeDataUri;
        elements["mfa-secret-key"].value = enrollment.secretKey;
        elements["enrollment-expiry"].textContent =
            `设置于 ${formatDateTime(enrollment.expiresAt)} 前有效`;
        elements["enrollment-content"].hidden = false;
        elements["begin-enrollment-button"].hidden = true;
        elements["mfa-enroll-code"].focus();
    } catch (error) {
        setInlineError(elements["mfa-error"], error.message);
    } finally {
        setBusy(elements["begin-enrollment-button"], false);
    }
}

async function completeEnrollment(event) {
    event.preventDefault();
    const submit = event.submitter;
    setBusy(submit, true);
    setInlineError(elements["mfa-error"], "");
    try {
        const result = await api("/v1/admin-auth/mfa/enrollment/confirm", {
            method: "POST",
            body: { code: elements["mfa-enroll-code"].value }
        });
        state.recoveryCodes = result.recoveryCodes || [];
        renderRecoveryCodes();
        elements["recovery-dialog"].showModal();
    } catch (error) {
        setInlineError(elements["mfa-error"], error.message);
        elements["mfa-enroll-code"].select();
    } finally {
        setBusy(submit, false);
    }
}

async function verifyMfa(event) {
    event.preventDefault();
    const submit = event.submitter;
    setBusy(submit, true);
    setInlineError(elements["mfa-error"], "");
    try {
        const result = await api("/v1/admin-auth/mfa/verify", {
            method: "POST",
            body: { code: elements["mfa-code"].value }
        });
        if (result.recoveryCodeUsed) {
            showToast("恢复码已使用，请及时补充新的恢复方案");
        }
        elements["mfa-code"].value = "";
        state.session = await api("/v1/admin-auth/session", { csrf: false });
        await enterConsole();
    } catch (error) {
        setInlineError(elements["mfa-error"], error.message);
        elements["mfa-code"].select();
    } finally {
        setBusy(submit, false);
    }
}

function renderRecoveryCodes() {
    elements["recovery-code-list"].replaceChildren();
    state.recoveryCodes.forEach(code => {
        const item = document.createElement("code");
        item.textContent = code;
        elements["recovery-code-list"].append(item);
    });
}

async function finishRecoverySetup() {
    elements["recovery-dialog"].close();
    state.recoveryCodes = [];
    elements["recovery-code-list"].replaceChildren();
    elements["mfa-secret-key"].value = "";
    elements["mfa-qr-code"].removeAttribute("src");
    elements["mfa-enroll-code"].value = "";
    state.session = await api("/v1/admin-auth/session", { csrf: false });
    await enterConsole();
}

function downloadRecoveryCodes() {
    const content = [
        "赫朝管理控制台恢复码",
        `账号：${state.session.player.minecraftName}`,
        `生成时间：${new Date().toLocaleString("zh-CN")}`,
        "",
        ...state.recoveryCodes,
        "",
        "每枚恢复码只能使用一次，请离线安全保存。"
    ].join("\r\n");
    const blob = new Blob([content], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `hechao-admin-recovery-${new Date().toISOString().slice(0, 10)}.txt`;
    anchor.click();
    URL.revokeObjectURL(url);
}

async function enterConsole() {
    showOnly("console-view");
    const player = state.session.player;
    elements["account-name"].textContent = player.minecraftName;
    elements["account-group"].textContent = `${tierText(player.accessTier)} · ${player.luckPermsPrimaryGroup}`;
    elements["account-avatar"].textContent = player.minecraftName.slice(0, 1).toUpperCase();
    await loadConsoleData();
}

async function loadConsoleData() {
    setBusy(elements["refresh-button"], true);
    try {
        const [servers, profiles] = await Promise.all([
            api("/v1/admin/catalog/servers"),
            api("/v1/admin/catalog/client-profiles")
        ]);
        state.servers = servers;
        state.profiles = profiles;
        renderServers();
        renderProfiles();
        populateProfileOptions();
        if (state.activeView === "audit" && state.auditEntries.length === 0) {
            await loadAudit(true);
        }
        elements["last-refreshed"].textContent =
            `更新于 ${new Date().toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })}`;
    } catch (error) {
        if (error.status === 401 || error.status === 403) {
            location.reload();
            return;
        }
        showToast(error.message, true);
    } finally {
        setBusy(elements["refresh-button"], false);
    }
}

async function refreshCurrentView() {
    if (state.activeView === "audit") {
        await loadAudit(true);
        return;
    }
    await loadConsoleData();
}

function switchView(view) {
    if (!["servers", "profiles", "audit"].includes(view)) {
        return;
    }
    state.activeView = view;
    document.querySelectorAll("[data-view]").forEach(button =>
        button.classList.toggle("active", button.dataset.view === view));
    const labels = {
        servers: "服务器目录",
        profiles: "客户端档案",
        audit: "审计记录"
    };
    elements["breadcrumb-current"].textContent = labels[view];
    elements["view-title"].textContent = labels[view];
    elements["servers-section"].hidden = view !== "servers";
    elements["profiles-section"].hidden = view !== "profiles";
    elements["audit-section"].hidden = view !== "audit";
    if (view === "audit" && state.auditEntries.length === 0) {
        loadAudit(true);
    }
}

function renderServers() {
    const servers = state.servers
        .filter(server => {
            if (state.serverFilter === "visible" && !server.isVisible) return false;
            if (state.serverFilter === "archived" && server.isVisible) return false;
            if (!state.serverSearch) return true;
            return [
                server.id,
                server.displayName,
                server.velocityTarget,
                server.clientProfileId
            ].some(value => value.toLocaleLowerCase("zh-CN").includes(state.serverSearch));
        })
        .sort((left, right) => left.sortOrder - right.sortOrder || left.id.localeCompare(right.id));

    elements["server-total-count"].textContent = state.servers.length;
    elements["server-online-count"].textContent =
        state.servers.filter(server => server.isVisible && server.status === "Online").length;
    elements["server-maintenance-count"].textContent =
        state.servers.filter(server => server.isVisible && server.status === "Maintenance").length;
    elements["server-archived-count"].textContent =
        state.servers.filter(server => !server.isVisible).length;
    elements["server-table-body"].replaceChildren();

    servers.forEach(server => {
        const row = document.createElement("tr");
        row.append(
            serverIdentityCell(server),
            statusCell(server),
            runtimeCell(server),
            profileCell(server),
            textCell(tierText(server.minimumTier)),
            textCell(String(server.sortOrder)),
            serverActionsCell(server)
        );
        elements["server-table-body"].append(row);
    });
    elements["server-empty-state"].hidden = servers.length !== 0;
}

function serverIdentityCell(server) {
    const cell = document.createElement("td");
    const wrapper = document.createElement("div");
    wrapper.className = "server-cell";
    const glyph = document.createElement("div");
    glyph.className = "server-glyph";
    glyph.textContent = server.iconGlyph;
    const copy = document.createElement("div");
    const name = document.createElement("strong");
    name.textContent = server.displayName;
    const id = document.createElement("span");
    id.textContent = `${server.id} · r${server.revision}`;
    copy.append(name, id);
    wrapper.append(glyph, copy);
    cell.append(wrapper);
    return cell;
}

function statusCell(server) {
    const cell = document.createElement("td");
    const badge = document.createElement("span");
    badge.className = `status-badge ${statusClass(server)}`;
    badge.textContent = server.isVisible ? statusText(server.status) : "已归档";
    cell.append(badge);
    return cell;
}

function runtimeCell(server) {
    const cell = document.createElement("td");
    const stack = document.createElement("div");
    stack.className = "meta-stack";
    const loader = document.createElement("strong");
    loader.textContent = `${server.minecraftVersion} · ${server.loader}`;
    const target = document.createElement("span");
    target.textContent = `Velocity: ${server.velocityTarget}`;
    stack.append(loader, target);
    cell.append(stack);
    return cell;
}

function profileCell(server) {
    const cell = document.createElement("td");
    const profile = state.profiles.find(item => item.id === server.clientProfileId);
    const stack = document.createElement("div");
    stack.className = "meta-stack";
    const name = document.createElement("strong");
    name.textContent = profile?.displayName || server.clientProfileId;
    const version = document.createElement("span");
    version.textContent = profile ? `v${profile.version}` : "档案不可用";
    stack.append(name, version);
    cell.append(stack);
    return cell;
}

function textCell(value) {
    const cell = document.createElement("td");
    cell.textContent = value;
    return cell;
}

function serverActionsCell(server) {
    const cell = document.createElement("td");
    cell.className = "actions-column";
    const actions = document.createElement("div");
    actions.className = "row-actions";
    actions.append(
        iconButton("pencil", "编辑服务器", () => openEditServer(server)),
        iconButton(
            server.isVisible ? "archive" : "rotate-ccw",
            server.isVisible ? "归档服务器" : "恢复服务器",
            () => confirmVisibilityChange(server)
        )
    );
    cell.append(actions);
    return cell;
}

function iconButton(icon, title, handler) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "icon-button";
    button.title = title;
    const image = document.createElement("img");
    image.src = `${iconRoot}${icon}.svg`;
    image.alt = "";
    button.append(image);
    button.addEventListener("click", handler);
    return button;
}

function statusClass(server) {
    if (!server.isVisible) return "status-archived";
    if (server.status === "Online") return "status-online";
    if (server.status === "Maintenance") return "status-maintenance";
    return "status-closed";
}

function statusText(status) {
    return {
        Online: "在线开放",
        Maintenance: "维护中",
        Closed: "未开放"
    }[status] || status;
}

function tierText(tier) {
    return {
        Member: "成员",
        Participant: "活动成员",
        Collaborator: "协作者",
        Administrator: "管理员"
    }[tier] || tier;
}

function renderProfiles() {
    elements["profile-table-body"].replaceChildren();
    elements["profile-count"].textContent = `${state.profiles.length} 个档案`;
    state.profiles.forEach(profile => {
        const row = document.createElement("tr");
        const identity = document.createElement("td");
        const copy = document.createElement("div");
        copy.className = "profile-name";
        const name = document.createElement("strong");
        name.textContent = profile.displayName;
        const id = document.createElement("span");
        id.textContent = profile.id;
        copy.append(name, id);
        identity.append(copy);
        const status = document.createElement("td");
        const badge = document.createElement("span");
        badge.className = `status-badge ${profile.isActive ? "status-online" : "status-archived"}`;
        badge.textContent = profile.isActive ? "启用" : "停用";
        status.append(badge);
        const hash = document.createElement("td");
        const hashText = document.createElement("span");
        hashText.className = "hash-text";
        hashText.title = profile.sha256 || "尚未发布哈希";
        hashText.textContent = profile.sha256 || "—";
        hash.append(hashText);
        row.append(
            identity,
            textCell(`v${profile.version}`),
            textCell(formatBytes(profile.downloadBytes)),
            textCell(formatDateTime(profile.publishedAt)),
            status,
            hash
        );
        elements["profile-table-body"].append(row);
    });
}

function populateProfileOptions() {
    const selected = elements["server-client-profile"].value;
    elements["server-client-profile"].replaceChildren();
    state.profiles.filter(profile => profile.isActive).forEach(profile => {
        const option = document.createElement("option");
        option.value = profile.id;
        option.textContent = `${profile.displayName} · v${profile.version}`;
        elements["server-client-profile"].append(option);
    });
    if (selected && state.profiles.some(profile => profile.id === selected && profile.isActive)) {
        elements["server-client-profile"].value = selected;
    }
}

function openCreateServer() {
    state.editingServer = null;
    elements["server-form"].reset();
    elements["drawer-kicker"].textContent = "服务器目录";
    elements["drawer-title"].textContent = "新增服务器";
    elements["server-id"].disabled = false;
    elements["server-status"].value = "Online";
    elements["server-max-players"].value = "30";
    elements["server-minecraft-version"].value = "1.21.11";
    elements["server-loader"].value = "Paper";
    elements["server-minimum-tier"].value = "Member";
    elements["server-sort-order"].value = "100";
    elements["server-is-visible"].checked = true;
    elements["server-visible-field"].hidden = false;
    elements["server-revision-label"].textContent = "新记录";
    setInlineError(elements["form-error"], "");
    elements["server-drawer"].showModal();
    elements["server-id"].focus();
}

function openEditServer(server) {
    state.editingServer = server;
    elements["drawer-kicker"].textContent = server.id;
    elements["drawer-title"].textContent = "编辑服务器";
    elements["server-id"].value = server.id;
    elements["server-id"].disabled = true;
    elements["server-display-name"].value = server.displayName;
    elements["server-short-name"].value = server.shortName;
    elements["server-icon-glyph"].value = server.iconGlyph;
    elements["server-status"].value = server.status;
    elements["server-max-players"].value = server.maxPlayers;
    elements["server-minecraft-version"].value = server.minecraftVersion;
    elements["server-loader"].value = server.loader;
    elements["server-minimum-tier"].value = server.minimumTier;
    elements["server-sort-order"].value = server.sortOrder;
    elements["server-client-profile"].value = server.clientProfileId;
    elements["server-velocity-target"].value = server.velocityTarget;
    elements["server-visible-field"].hidden = true;
    elements["server-revision-label"].textContent = `修订号 r${server.revision}`;
    setInlineError(elements["form-error"], "");
    elements["server-drawer"].showModal();
    elements["server-display-name"].focus();
}

function closeServerDrawer() {
    elements["server-drawer"].close();
    state.editingServer = null;
}

async function saveServer(event) {
    event.preventDefault();
    const form = elements["server-form"];
    if (!form.reportValidity()) return;
    setBusy(elements["save-server-button"], true);
    setInlineError(elements["form-error"], "");
    const payload = {
        displayName: elements["server-display-name"].value.trim(),
        shortName: elements["server-short-name"].value.trim(),
        iconGlyph: elements["server-icon-glyph"].value.trim(),
        status: elements["server-status"].value,
        maxPlayers: Number(elements["server-max-players"].value),
        minecraftVersion: elements["server-minecraft-version"].value.trim(),
        loader: elements["server-loader"].value,
        minimumTier: elements["server-minimum-tier"].value,
        clientProfileId: elements["server-client-profile"].value,
        velocityTarget: elements["server-velocity-target"].value.trim(),
        sortOrder: Number(elements["server-sort-order"].value)
    };
    try {
        if (state.editingServer) {
            payload.expectedRevision = state.editingServer.revision;
            await api(`/v1/admin/catalog/servers/${encodeURIComponent(state.editingServer.id)}`, {
                method: "PUT",
                body: payload
            });
            showToast("服务器目录已更新");
        } else {
            payload.id = elements["server-id"].value.trim();
            payload.isVisible = elements["server-is-visible"].checked;
            await api("/v1/admin/catalog/servers", {
                method: "POST",
                body: payload
            });
            showToast("服务器已创建");
        }
        closeServerDrawer();
        await loadConsoleData();
    } catch (error) {
        if (error.status === 409 && error.payload?.current) {
            state.editingServer = error.payload.current;
            elements["server-revision-label"].textContent =
                `服务器已有新修订 r${error.payload.current.revision}`;
        }
        setInlineError(elements["form-error"], error.message);
    } finally {
        setBusy(elements["save-server-button"], false);
    }
}

function confirmVisibilityChange(server) {
    state.pendingVisibilityChange = server;
    const restoring = !server.isVisible;
    elements["confirm-icon"].src =
        `${iconRoot}${restoring ? "rotate-ccw" : "archive"}.svg`;
    elements["confirm-title"].textContent = restoring ? "恢复服务器" : "归档服务器";
    elements["confirm-message"].textContent = restoring
        ? `“${server.displayName}”将重新出现在符合权限的玩家目录中。`
        : `“${server.displayName}”将从玩家目录隐藏，但不会停止对应服务端进程。`;
    elements["accept-confirm-button"].textContent = restoring ? "确认恢复" : "确认归档";
    elements["accept-confirm-button"].className =
        `button ${restoring ? "button-primary" : "button-danger"}`;
    elements["confirm-dialog"].showModal();
}

async function applyVisibilityChange() {
    const server = state.pendingVisibilityChange;
    if (!server) return;
    setBusy(elements["accept-confirm-button"], true);
    try {
        await api(`/v1/admin/catalog/servers/${encodeURIComponent(server.id)}/visibility`, {
            method: "PUT",
            body: {
                isVisible: !server.isVisible,
                expectedRevision: server.revision
            }
        });
        elements["confirm-dialog"].close();
        showToast(server.isVisible ? "服务器已归档" : "服务器已恢复");
        await loadConsoleData();
    } catch (error) {
        elements["confirm-dialog"].close();
        showToast(error.message, true);
        await loadConsoleData();
    } finally {
        state.pendingVisibilityChange = null;
        setBusy(elements["accept-confirm-button"], false);
    }
}

async function loadAudit(reset) {
    if (reset) {
        state.auditBeforeId = null;
        state.auditEntries = [];
    }
    setBusy(elements["load-more-audit-button"], true);
    try {
        const query = state.auditBeforeId
            ? `?limit=50&beforeId=${state.auditBeforeId}`
            : "?limit=50";
        const entries = await api(`/v1/admin/audit-logs${query}`);
        state.auditEntries.push(...entries);
        state.auditBeforeId = entries.length ? entries[entries.length - 1].id : state.auditBeforeId;
        elements["load-more-audit-button"].disabled = entries.length < 50;
        renderAudit();
    } catch (error) {
        showToast(error.message, true);
    } finally {
        setBusy(elements["load-more-audit-button"], false);
    }
}

function loadMoreAudit() {
    return loadAudit(false);
}

function renderAudit() {
    elements["audit-list"].replaceChildren();
    state.auditEntries.forEach(entry => {
        const item = document.createElement("article");
        item.className = "audit-entry";
        const icon = document.createElement("div");
        icon.className = "audit-icon";
        const image = document.createElement("img");
        image.src = `${iconRoot}${auditIcon(entry.action)}.svg`;
        image.alt = "";
        icon.append(image);
        const main = auditMeta(
            auditActionText(entry.action),
            `${entry.targetType} · ${entry.targetId}`
        );
        main.className = "audit-main";
        const actor = auditMeta(
            entry.actorDisplayName || "系统",
            entry.sourceIp || "无来源地址"
        );
        const time = auditMeta(
            formatDateTime(entry.createdAt),
            `记录 #${entry.id}`
        );
        item.append(icon, main, actor, time);
        elements["audit-list"].append(item);
    });
    elements["audit-empty-state"].hidden = state.auditEntries.length !== 0;
    elements["audit-list"].hidden = state.auditEntries.length === 0;
}

function auditMeta(primary, secondary) {
    const wrapper = document.createElement("div");
    wrapper.className = "audit-meta";
    const strong = document.createElement("strong");
    strong.textContent = primary;
    const span = document.createElement("span");
    span.textContent = secondary;
    wrapper.append(strong, span);
    return wrapper;
}

function auditIcon(action) {
    if (action.includes("created")) return "plus";
    if (action.includes("archived")) return "archive";
    if (action.includes("restored")) return "rotate-ccw";
    if (action.includes("mfa")) return "shield-check";
    if (action.includes("session")) return "key-round";
    return "pencil";
}

function auditActionText(action) {
    const labels = {
        "catalog.server.created": "新增服务器",
        "catalog.server.updated": "编辑服务器",
        "catalog.server.archived": "归档服务器",
        "catalog.server.restored": "恢复服务器",
        "admin.login_ticket.created": "创建后台登录票据",
        "admin.web_session.created": "登录管理后台",
        "admin.web_session.revoked": "退出管理后台",
        "admin.mfa.enrollment.started": "开始设置双重验证",
        "admin.mfa.enabled": "启用双重验证",
        "admin.mfa.verified": "完成双重验证",
        "admin.mfa.recovery_code_used": "使用恢复码"
    };
    return labels[action] || action;
}

async function logout() {
    try {
        await api("/v1/admin-auth/logout", { method: "POST", body: {} });
    } catch {
        // The local cookie is cleared by the response whenever the session is still valid.
    }
    location.assign("/admin/");
}

function setInlineError(container, message) {
    container.hidden = !message;
    const span = container.querySelector("span");
    if (span) span.textContent = message || "";
}

function setBusy(button, busy) {
    if (!button) return;
    button.disabled = busy;
    button.setAttribute("aria-busy", busy ? "true" : "false");
}

function showToast(message, error = false) {
    window.clearTimeout(state.toastTimer);
    elements["toast-message"].textContent = message;
    elements["toast"].classList.toggle("error", error);
    elements["toast-icon"].src = `${iconRoot}${error ? "circle-alert" : "check"}.svg`;
    elements["toast"].hidden = false;
    state.toastTimer = window.setTimeout(() => {
        elements["toast"].hidden = true;
    }, 3600);
}

async function copyText(text, successMessage) {
    try {
        await navigator.clipboard.writeText(text);
        showToast(successMessage);
    } catch {
        showToast("无法访问剪贴板，请手动保存", true);
    }
}

function formatBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes <= 0) return "—";
    const units = ["B", "KiB", "MiB", "GiB"];
    let value = bytes;
    let index = 0;
    while (value >= 1024 && index < units.length - 1) {
        value /= 1024;
        index += 1;
    }
    return `${value >= 10 || index === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[index]}`;
}

function formatDateTime(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "—";
    return date.toLocaleString("zh-CN", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
    });
}
