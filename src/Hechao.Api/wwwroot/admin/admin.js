"use strict";

const iconRoot = "/admin/assets/icons/";
const state = {
    session: null,
    csrfToken: null,
    servers: [],
    users: [],
    profiles: [],
    diagnostics: [],
    auditEntries: [],
    auditBeforeId: null,
    activeView: "servers",
    serverFilter: "visible",
    serverSearch: "",
    userSearch: "",
    editingServer: null,
    selectedAccessPreview: null,
    editingAccessServer: null,
    selectedUserSecurity: null,
    pendingSecurityAction: null,
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
        "servers-section", "users-section", "profiles-section",
        "diagnostics-section", "audit-section",
        "server-total-count", "server-online-count", "server-maintenance-count",
        "server-archived-count", "server-search", "create-server-button",
        "server-table-body", "server-empty-state", "profile-count",
        "profile-table-body", "diagnostic-count", "diagnostic-table-body",
        "diagnostic-empty-state", "audit-list", "audit-empty-state",
        "load-more-audit-button", "server-drawer", "server-form",
        "drawer-kicker", "drawer-title", "close-drawer-button",
        "cancel-server-button", "save-server-button", "form-error",
        "server-id", "server-display-name", "server-short-name",
        "server-icon-glyph", "server-status", "server-max-players",
        "server-minecraft-version", "server-loader", "server-minimum-tier",
        "server-sort-order", "server-client-profile", "server-velocity-target",
        "server-is-visible", "server-visible-field", "server-revision-label",
        "server-announcement", "server-opens-at", "server-closes-at",
        "user-count", "user-search-form", "user-search", "user-search-button",
        "user-table-body", "user-empty-state", "access-preview-panel",
        "access-preview-title", "access-preview-subtitle", "access-preview-body",
        "close-access-preview-button", "access-rule-drawer", "access-rule-form",
        "access-rule-kicker", "access-rule-title", "close-access-rule-button",
        "cancel-access-rule-button", "save-access-rule-button",
        "delete-access-rule-button", "access-rule-error",
        "access-rule-decision", "access-rule-reason", "access-rule-expires-at",
        "user-security-drawer", "user-security-form", "user-security-kicker",
        "user-security-title", "close-user-security-button",
        "finish-user-security-button", "user-security-error",
        "user-security-account-name", "user-security-account-status",
        "user-security-account-meta", "user-security-account-action",
        "user-security-minecraft-name", "user-security-minecraft-status",
        "user-security-minecraft-uuid", "user-security-ban-meta",
        "user-security-ban-action", "user-security-session-count",
        "user-security-session-list", "user-security-session-empty",
        "revoke-all-user-sessions-button", "user-security-admin-session-count",
        "user-security-admin-ticket-count", "user-security-launch-grant-count",
        "security-action-dialog", "security-action-form", "security-action-icon",
        "security-action-title", "security-action-message",
        "security-action-error", "security-action-reason",
        "security-action-expiry-field", "security-action-expires-at",
        "cancel-security-action-button", "accept-security-action-button",
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
    elements["user-search-form"].addEventListener("submit", searchUsers);
    elements["close-access-preview-button"].addEventListener(
        "click",
        closeAccessPreview);
    elements["close-access-rule-button"].addEventListener(
        "click",
        closeAccessRuleDrawer);
    elements["cancel-access-rule-button"].addEventListener(
        "click",
        closeAccessRuleDrawer);
    elements["access-rule-form"].addEventListener("submit", saveAccessRule);
    elements["delete-access-rule-button"].addEventListener(
        "click",
        deleteAccessRule);
    elements["close-user-security-button"].addEventListener(
        "click",
        closeUserSecurity);
    elements["finish-user-security-button"].addEventListener(
        "click",
        closeUserSecurity);
    elements["user-security-account-action"].addEventListener(
        "click",
        () => {
            const security = state.selectedUserSecurity;
            if (security) {
                openSecurityAction(
                    security.user.isDisabled ? "account-enable" : "account-disable");
            }
        });
    elements["user-security-ban-action"].addEventListener(
        "click",
        () => {
            const security = state.selectedUserSecurity;
            if (security?.user.minecraftUuid) {
                openSecurityAction(
                    security.minecraftIdentityBan ? "minecraft-unban" : "minecraft-ban");
            }
        });
    elements["revoke-all-user-sessions-button"].addEventListener(
        "click",
        () => openSecurityAction("sessions-revoke-all"));
    elements["security-action-form"].addEventListener(
        "submit",
        submitSecurityAction);
    elements["cancel-security-action-button"].addEventListener(
        "click",
        closeSecurityAction);
    elements["user-security-drawer"].addEventListener("cancel", event => {
        event.preventDefault();
        closeUserSecurity();
    });
    elements["security-action-dialog"].addEventListener("cancel", event => {
        event.preventDefault();
        closeSecurityAction();
    });
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
        const [servers, profiles, diagnostics, users] = await Promise.all([
            api("/v1/admin/catalog/servers"),
            api("/v1/admin/catalog/client-profiles"),
            api("/v1/admin/diagnostics?limit=200"),
            api(userSearchPath())
        ]);
        state.servers = servers;
        state.profiles = profiles;
        state.diagnostics = diagnostics;
        state.users = users;
        renderServers();
        renderUsers();
        renderProfiles();
        renderDiagnostics();
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
    if (!["servers", "users", "profiles", "diagnostics", "audit"].includes(view)) {
        return;
    }
    state.activeView = view;
    document.querySelectorAll("[data-view]").forEach(button =>
        button.classList.toggle("active", button.dataset.view === view));
    const labels = {
        servers: "服务器目录",
        users: "玩家与权限",
        profiles: "客户端档案",
        diagnostics: "玩家诊断包",
        audit: "审计记录"
    };
    elements["breadcrumb-current"].textContent = labels[view];
    elements["view-title"].textContent = labels[view];
    elements["servers-section"].hidden = view !== "servers";
    elements["users-section"].hidden = view !== "users";
    elements["profiles-section"].hidden = view !== "profiles";
    elements["diagnostics-section"].hidden = view !== "diagnostics";
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
        state.servers.filter(
            server => server.isVisible && server.effectiveStatus === "Online").length;
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
    badge.textContent = server.isVisible
        ? effectiveStatusText(server)
        : "已归档";
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
    if (server.effectiveStatus === "Online") return "status-online";
    if (server.effectiveStatus === "Maintenance") return "status-maintenance";
    return "status-closed";
}

function effectiveStatusText(server) {
    if (server.status === "Online" && server.effectiveStatus === "Closed") {
        if (server.opensAt && new Date(server.opensAt) > new Date()) {
            return "等待开放";
        }
        if (server.closesAt && new Date(server.closesAt) <= new Date()) {
            return "计划已结束";
        }
    }
    return statusText(server.effectiveStatus);
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

function userSearchPath() {
    const params = new URLSearchParams({ limit: "50" });
    if (state.userSearch) params.set("query", state.userSearch);
    return `/v1/admin/users?${params}`;
}

async function searchUsers(event) {
    event.preventDefault();
    state.userSearch = elements["user-search"].value.trim();
    setBusy(elements["user-search-button"], true);
    try {
        state.users = await api(userSearchPath());
        renderUsers();
        closeAccessPreview();
        closeUserSecurity();
    } catch (error) {
        showToast(error.message, true);
    } finally {
        setBusy(elements["user-search-button"], false);
    }
}

function renderUsers() {
    elements["user-table-body"].replaceChildren();
    elements["user-count"].textContent = `${state.users.length} 个账号`;
    state.users.forEach(user => {
        const row = document.createElement("tr");

        const account = document.createElement("td");
        const accountCopy = document.createElement("div");
        accountCopy.className = "profile-name";
        const displayName = document.createElement("strong");
        displayName.textContent = user.displayName;
        const username = document.createElement("span");
        username.textContent = `@${user.username}`;
        accountCopy.append(displayName, username);
        account.append(accountCopy);

        const minecraft = document.createElement("td");
        const minecraftCopy = document.createElement("div");
        minecraftCopy.className = "meta-stack";
        const minecraftName = document.createElement("strong");
        minecraftName.textContent = user.minecraftName || "尚未绑定";
        const minecraftMeta = document.createElement("span");
        minecraftMeta.textContent = user.minecraftUuid
            ? user.minecraftUuid
            : "无 Minecraft 正版身份";
        minecraftCopy.append(minecraftName, minecraftMeta);
        minecraft.append(minecraftCopy);

        const status = document.createElement("td");
        const statusBadge = document.createElement("span");
        const restricted = user.isDisabled || user.isMinecraftIdentityBanned;
        statusBadge.className =
            `status-badge ${restricted ? "status-closed" : "status-online"}`;
        statusBadge.textContent = user.isDisabled
            ? "已停用"
            : user.isMinecraftIdentityBanned
                ? "UUID 已封禁"
                : "正常";
        status.append(statusBadge);

        const actions = document.createElement("td");
        actions.className = "actions-column";
        const actionGroup = document.createElement("div");
        actionGroup.className = "row-actions";
        actionGroup.append(iconButton(
            "eye",
            "预览最终权限",
            () => openAccessPreview(user.userId)));
        actionGroup.append(iconButton(
            "key-round",
            "管理账号安全",
            () => openUserSecurity(user.userId)));
        actions.append(actionGroup);

        row.append(
            account,
            minecraft,
            textCell(tierText(user.accessTier)),
            status,
            textCell(String(user.activeRuleCount)),
            actions
        );
        elements["user-table-body"].append(row);
    });
    elements["user-empty-state"].hidden = state.users.length !== 0;
    elements["user-table-body"].parentElement.hidden = state.users.length === 0;
}

async function openUserSecurity(userId) {
    try {
        const security = await api(
            `/v1/admin/users/${encodeURIComponent(userId)}/security`);
        state.selectedUserSecurity = security;
        renderUserSecurity();
        if (!elements["user-security-drawer"].open) {
            elements["user-security-drawer"].showModal();
        }
    } catch (error) {
        showToast(error.message, true);
    }
}

function closeUserSecurity() {
    state.selectedUserSecurity = null;
    state.pendingSecurityAction = null;
    if (elements["security-action-dialog"].open) {
        elements["security-action-dialog"].close();
    }
    if (elements["user-security-drawer"].open) {
        elements["user-security-drawer"].close();
    }
    elements["user-security-session-list"].replaceChildren();
    setInlineError(elements["user-security-error"], "");
}

function renderUserSecurity() {
    const security = state.selectedUserSecurity;
    if (!security) return;
    const user = security.user;
    const isSelf = state.session?.player?.userId === user.userId;
    elements["user-security-kicker"].textContent =
        `${tierText(user.accessTier)} · @${user.username}`;
    elements["user-security-title"].textContent = `${user.displayName} 的安全状态`;
    elements["user-security-account-name"].textContent = user.displayName;
    setStatusBadge(
        elements["user-security-account-status"],
        user.isDisabled ? "已停用" : "正常",
        user.isDisabled ? "status-closed" : "status-online");
    elements["user-security-account-meta"].textContent = [
        `账号 @${user.username}`,
        user.email || "未登记邮箱",
        `当前等级 ${tierText(user.accessTier)}`
    ].join(" · ");
    setButtonContent(
        elements["user-security-account-action"],
        "key-round",
        user.isDisabled ? "恢复账号" : "停用账号");
    elements["user-security-account-action"].className =
        `button ${user.isDisabled ? "button-secondary" : "button-danger"} ` +
        "security-full-button";
    elements["user-security-account-action"].disabled = isSelf;
    elements["user-security-account-action"].title = isSelf
        ? "不能停用当前管理员自身"
        : "";

    elements["user-security-minecraft-name"].textContent =
        user.minecraftName || "尚未绑定";
    elements["user-security-minecraft-uuid"].textContent =
        user.minecraftUuid || "无 Minecraft UUID";
    if (!user.minecraftUuid) {
        setStatusBadge(
            elements["user-security-minecraft-status"],
            "未绑定",
            "status-archived");
        elements["user-security-ban-meta"].textContent =
            "账号绑定正版身份后才能执行 UUID 封禁。";
        elements["user-security-ban-action"].disabled = true;
    } else if (security.minecraftIdentityBan) {
        const ban = security.minecraftIdentityBan;
        setStatusBadge(
            elements["user-security-minecraft-status"],
            "UUID 已封禁",
            "status-closed");
        elements["user-security-ban-meta"].textContent = [
            `原因：${ban.reason}`,
            ban.expiresAt
                ? `到期：${formatDateTime(ban.expiresAt)}`
                : "长期封禁",
            ban.createdByDisplayName
                ? `操作者：${ban.createdByDisplayName}`
                : null
        ].filter(Boolean).join(" · ");
        setButtonContent(
            elements["user-security-ban-action"],
            "rotate-ccw",
            "解除 UUID 封禁");
        elements["user-security-ban-action"].className =
            "button button-secondary security-full-button";
        elements["user-security-ban-action"].disabled = isSelf;
    } else {
        setStatusBadge(
            elements["user-security-minecraft-status"],
            "正常",
            "status-online");
        elements["user-security-ban-meta"].textContent =
            "该 UUID 当前没有生效中的封禁记录。";
        setButtonContent(
            elements["user-security-ban-action"],
            "shield-check",
            "封禁 UUID");
        elements["user-security-ban-action"].className =
            "button button-danger security-full-button";
        elements["user-security-ban-action"].disabled = isSelf;
    }
    elements["user-security-ban-action"].title = isSelf
        ? "不能封禁当前管理员自身"
        : "";

    elements["user-security-session-list"].replaceChildren();
    security.launcherSessions.forEach(session => {
        const item = document.createElement("div");
        item.className = "security-session-item";
        const copy = document.createElement("div");
        const title = document.createElement("strong");
        title.textContent = `设备会话 · ${session.sessionId.slice(-8)}`;
        const meta = document.createElement("span");
        meta.textContent = [
            session.sourceIp || "无来源地址",
            `最后活动 ${formatDateTime(session.lastSeenAt)}`,
            `到期 ${formatDateTime(session.refreshExpiresAt)}`
        ].join(" · ");
        copy.append(title, meta);
        const revoke = iconButton(
            "log-out",
            "撤销这个设备会话",
            () => openSecurityAction("session-revoke", session.sessionId));
        item.append(copy, revoke);
        elements["user-security-session-list"].append(item);
    });
    elements["user-security-session-count"].textContent =
        `${security.launcherSessions.length} 个活跃会话`;
    elements["user-security-session-empty"].hidden =
        security.launcherSessions.length !== 0;
    elements["revoke-all-user-sessions-button"].disabled = false;
    elements["user-security-admin-session-count"].textContent =
        String(security.activeAdminSessions);
    elements["user-security-admin-ticket-count"].textContent =
        String(security.pendingAdminTickets);
    elements["user-security-launch-grant-count"].textContent =
        String(security.pendingVelocityLaunchGrants);
    elements["user-security-forum-revocation-count"].textContent =
        String(security.pendingForumSessionRevocations);
    setInlineError(elements["user-security-error"], "");
}

function setStatusBadge(element, text, statusClassName) {
    element.className = `status-badge ${statusClassName}`;
    element.textContent = text;
}

function setButtonContent(button, icon, text) {
    const image = document.createElement("img");
    image.src = `${iconRoot}${icon}.svg`;
    image.alt = "";
    button.replaceChildren(image, document.createTextNode(text));
}

function openSecurityAction(kind, sessionId = null) {
    const security = state.selectedUserSecurity;
    if (!security) return;
    const user = security.user;
    const actions = {
        "account-disable": {
            icon: "key-round",
            title: "停用赫朝账号",
            message: `停用“${user.displayName}”后，新登录、启动器、后台会话和进服授权会立即失效，论坛已有 Cookie 会进入可靠撤销队列。`,
            accept: "确认停用",
            danger: true
        },
        "account-enable": {
            icon: "rotate-ccw",
            title: "恢复赫朝账号",
            message: `恢复“${user.displayName}”的赫朝账号。已有 UUID 封禁不会随账号恢复自动解除。`,
            accept: "确认恢复",
            danger: false
        },
        "sessions-revoke-all": {
            icon: "log-out",
            title: "撤销全部会话",
            message: `撤销“${user.displayName}”的全部启动器设备、后台会话、登录票据、待消费进服授权和论坛 Cookie。`,
            accept: "全部撤销",
            danger: true
        },
        "session-revoke": {
            icon: "log-out",
            title: "撤销设备会话",
            message: `撤销设备会话 ${sessionId?.slice(-8) || ""}，该设备需要重新登录。`,
            accept: "确认撤销",
            danger: true
        },
        "minecraft-ban": {
            icon: "shield-check",
            title: "封禁 Minecraft UUID",
            message: `封禁 ${user.minecraftName || user.minecraftUuid} 后，正版绑定、客户端下载和 Velocity 进服都会被拒绝。`,
            accept: "确认封禁",
            danger: true,
            expiry: true
        },
        "minecraft-unban": {
            icon: "rotate-ccw",
            title: "解除 Minecraft UUID 封禁",
            message: `解除 ${user.minecraftName || user.minecraftUuid} 的 UUID 封禁；账号停用状态不会自动改变。`,
            accept: "确认解除",
            danger: false
        }
    };
    const action = actions[kind];
    if (!action) return;
    state.pendingSecurityAction = { kind, sessionId };
    elements["security-action-icon"].src = `${iconRoot}${action.icon}.svg`;
    elements["security-action-title"].textContent = action.title;
    elements["security-action-message"].textContent = action.message;
    elements["security-action-reason"].value = "";
    elements["security-action-expires-at"].value = "";
    elements["security-action-expiry-field"].hidden = !action.expiry;
    elements["accept-security-action-button"].textContent = action.accept;
    elements["accept-security-action-button"].className =
        `button ${action.danger ? "button-danger" : "button-primary"}`;
    setInlineError(elements["security-action-error"], "");
    elements["security-action-dialog"].showModal();
    elements["security-action-reason"].focus();
}

function closeSecurityAction() {
    state.pendingSecurityAction = null;
    setInlineError(elements["security-action-error"], "");
    if (elements["security-action-dialog"].open) {
        elements["security-action-dialog"].close();
    }
}

async function submitSecurityAction(event) {
    event.preventDefault();
    const pending = state.pendingSecurityAction;
    const security = state.selectedUserSecurity;
    if (!pending || !security ||
        !elements["security-action-form"].reportValidity()) {
        return;
    }

    const userId = encodeURIComponent(security.user.userId);
    const reason = elements["security-action-reason"].value.trim();
    let path;
    let method = "POST";
    let body = { reason };
    switch (pending.kind) {
        case "account-disable":
            path = `/v1/admin/users/${userId}/account/disable`;
            break;
        case "account-enable":
            path = `/v1/admin/users/${userId}/account/enable`;
            break;
        case "sessions-revoke-all":
            path = `/v1/admin/users/${userId}/sessions/revoke-all`;
            break;
        case "session-revoke":
            path = `/v1/admin/users/${userId}/sessions/` +
                `${encodeURIComponent(pending.sessionId)}/revoke`;
            break;
        case "minecraft-ban":
            path = `/v1/admin/users/${userId}/minecraft-ban`;
            method = "PUT";
            body = {
                reason,
                expiresAt: parseInputDateTime(
                    elements["security-action-expires-at"].value),
                expectedRevision: null
            };
            break;
        case "minecraft-unban":
            path = `/v1/admin/users/${userId}/minecraft-ban`;
            method = "DELETE";
            body = {
                reason,
                expectedRevision: security.minecraftIdentityBan?.revision
            };
            break;
        default:
            return;
    }

    setBusy(elements["accept-security-action-button"], true);
    setInlineError(elements["security-action-error"], "");
    try {
        const response = await api(path, { method, body });
        const updated = response.security;
        closeSecurityAction();
        if (updated) {
            state.selectedUserSecurity = updated;
            const index = state.users.findIndex(
                item => item.userId === updated.user.userId);
            if (index >= 0) state.users[index] = updated.user;
            renderUsers();
            renderUserSecurity();
            if (state.selectedAccessPreview?.user?.userId === updated.user.userId) {
                closeAccessPreview();
            }
        }
        showToast(securityActionSuccessText(pending.kind, response.revoked));
    } catch (error) {
        if (error.status === 409) {
            try {
                const refreshed = await api(
                    `/v1/admin/users/${userId}/security`);
                state.selectedUserSecurity = refreshed;
                renderUserSecurity();
            } catch {
                // Preserve the original action error.
            }
        }
        setInlineError(elements["security-action-error"], error.message);
    } finally {
        setBusy(elements["accept-security-action-button"], false);
    }
}

function securityActionSuccessText(kind, revoked) {
    const labels = {
        "account-disable": "账号已停用",
        "account-enable": "账号已恢复",
        "sessions-revoke-all": "全部会话已撤销",
        "session-revoke": "设备会话已撤销",
        "minecraft-ban": "Minecraft UUID 已封禁",
        "minecraft-unban": "Minecraft UUID 封禁已解除"
    };
    const total = revoked
        ? revoked.launcherSessions +
          revoked.adminSessions +
          revoked.adminTickets +
          revoked.velocityLaunchGrants
        : 0;
    const forumQueued = revoked?.forumSessionRevocations > 0;
    if (total > 0 && forumQueued) {
        return `${labels[kind]}，共失效 ${total} 项凭据，论坛撤销已排队`;
    }
    if (total > 0) {
        return `${labels[kind]}，共失效 ${total} 项凭据`;
    }
    return forumQueued
        ? `${labels[kind]}，论坛撤销已排队`
        : labels[kind];
}

async function openAccessPreview(userId) {
    try {
        const preview = await api(
            `/v1/admin/users/${encodeURIComponent(userId)}/access-preview`);
        state.selectedAccessPreview = preview;
        renderAccessPreview();
        elements["access-preview-panel"].hidden = false;
        elements["access-preview-panel"].scrollIntoView({
            behavior: "smooth",
            block: "start"
        });
    } catch (error) {
        showToast(error.message, true);
    }
}

function closeAccessPreview() {
    state.selectedAccessPreview = null;
    state.editingAccessServer = null;
    elements["access-preview-panel"].hidden = true;
    elements["access-preview-body"].replaceChildren();
}

function renderAccessPreview() {
    const preview = state.selectedAccessPreview;
    if (!preview) return;
    const user = preview.user;
    elements["access-preview-title"].textContent =
        `${user.displayName} 的有效权限`;
    elements["access-preview-subtitle"].textContent = [
        `@${user.username}`,
        user.minecraftName || "未绑定 Minecraft",
        tierText(user.accessTier),
        `${user.activeRuleCount} 条有效规则`
    ].join(" · ");
    elements["access-preview-body"].replaceChildren();

    preview.servers.forEach(server => {
        const row = document.createElement("tr");
        const identity = document.createElement("td");
        const identityCopy = document.createElement("div");
        identityCopy.className = "profile-name";
        const displayName = document.createElement("strong");
        displayName.textContent = server.serverDisplayName;
        const id = document.createElement("span");
        id.textContent = server.serverId;
        identityCopy.append(displayName, id);
        identity.append(identityCopy);

        const status = document.createElement("td");
        const statusBadge = document.createElement("span");
        statusBadge.className =
            `status-badge ${statusClass({
                isVisible: server.isVisible,
                effectiveStatus: server.effectiveStatus
            })}`;
        statusBadge.textContent = server.isVisible
            ? statusText(server.effectiveStatus)
            : "已归档";
        status.append(statusBadge);

        const rule = document.createElement("td");
        const ruleCopy = document.createElement("div");
        ruleCopy.className = "meta-stack";
        const ruleDecision = document.createElement("strong");
        ruleDecision.textContent = accessRuleText(server.rule);
        const ruleExpiry = document.createElement("span");
        ruleExpiry.textContent = server.rule?.expiresAt
            ? `到期 ${formatDateTime(server.rule.expiresAt)}`
            : server.rule ? "长期有效" : "按称号等级";
        ruleCopy.append(ruleDecision, ruleExpiry);
        rule.append(ruleCopy);

        const result = document.createElement("td");
        const resultText = document.createElement("span");
        resultText.className =
            `access-result ${server.allowed ? "allowed" : "denied"}`;
        resultText.textContent =
            `${server.allowed ? "允许" : "拒绝"} · ${accessReasonText(server.reason)}`;
        result.append(resultText);

        const actions = document.createElement("td");
        actions.className = "actions-column";
        const actionGroup = document.createElement("div");
        actionGroup.className = "row-actions";
        actionGroup.append(iconButton(
            server.rule ? "pencil" : "plus",
            server.rule ? "编辑单服规则" : "新增单服规则",
            () => openAccessRuleDrawer(server)));
        actions.append(actionGroup);

        row.append(
            identity,
            status,
            textCell(tierText(server.minimumTier)),
            rule,
            result,
            actions
        );
        elements["access-preview-body"].append(row);
    });
}

function accessRuleText(rule) {
    if (!rule) return "无单服规则";
    return rule.decision === "Allow" ? "单服允许" : "单服拒绝";
}

function accessReasonText(reason) {
    return {
        AllowedByTier: "称号等级满足",
        AllowedByRule: "单服规则允许",
        PlayerNotLinked: "未绑定正版身份",
        PlayerDisabled: "账号已停用",
        MinecraftIdentityBanned: "UUID 已封禁",
        ServerArchived: "服务器已归档",
        ServerUnavailable: "服务器未开放",
        DeniedByRule: "单服规则拒绝",
        InsufficientTier: "称号等级不足",
        PermissionDataStale: "称号数据待同步"
    }[reason] || reason;
}

function openAccessRuleDrawer(server) {
    const preview = state.selectedAccessPreview;
    if (!preview) return;
    state.editingAccessServer = server;
    elements["access-rule-kicker"].textContent =
        `${preview.user.displayName} · ${server.serverDisplayName}`;
    elements["access-rule-title"].textContent =
        server.rule ? "编辑单服权限规则" : "新增单服权限规则";
    elements["access-rule-decision"].value =
        server.rule?.decision || "Allow";
    elements["access-rule-reason"].value = server.rule?.reason || "";
    elements["access-rule-expires-at"].value =
        formatInputDateTime(server.rule?.expiresAt);
    elements["delete-access-rule-button"].hidden = !server.rule;
    setInlineError(elements["access-rule-error"], "");
    elements["access-rule-drawer"].showModal();
    elements["access-rule-decision"].focus();
}

function closeAccessRuleDrawer() {
    elements["access-rule-drawer"].close();
    state.editingAccessServer = null;
}

async function saveAccessRule(event) {
    event.preventDefault();
    const preview = state.selectedAccessPreview;
    const server = state.editingAccessServer;
    if (!preview || !server) return;
    setBusy(elements["save-access-rule-button"], true);
    setInlineError(elements["access-rule-error"], "");
    try {
        await api(
            `/v1/admin/users/${encodeURIComponent(preview.user.userId)}` +
            `/access-rules/${encodeURIComponent(server.serverId)}`,
            {
                method: "PUT",
                body: {
                    decision: elements["access-rule-decision"].value,
                    reason: elements["access-rule-reason"].value.trim(),
                    expiresAt: parseInputDateTime(
                        elements["access-rule-expires-at"].value),
                    expectedRevision: server.rule?.revision ?? null
                }
            });
        const userId = preview.user.userId;
        closeAccessRuleDrawer();
        showToast("单服权限规则已保存");
        await Promise.all([openAccessPreview(userId), reloadUsers()]);
    } catch (error) {
        if (error.status === 409) {
            await openAccessPreview(preview.user.userId);
        }
        setInlineError(elements["access-rule-error"], error.message);
    } finally {
        setBusy(elements["save-access-rule-button"], false);
    }
}

async function deleteAccessRule() {
    const preview = state.selectedAccessPreview;
    const server = state.editingAccessServer;
    if (!preview || !server?.rule) return;
    setBusy(elements["delete-access-rule-button"], true);
    setInlineError(elements["access-rule-error"], "");
    try {
        await api(
            `/v1/admin/users/${encodeURIComponent(preview.user.userId)}` +
            `/access-rules/${encodeURIComponent(server.serverId)}`,
            {
                method: "DELETE",
                body: { expectedRevision: server.rule.revision }
            });
        const userId = preview.user.userId;
        closeAccessRuleDrawer();
        showToast("单服权限规则已清除");
        await Promise.all([openAccessPreview(userId), reloadUsers()]);
    } catch (error) {
        setInlineError(elements["access-rule-error"], error.message);
    } finally {
        setBusy(elements["delete-access-rule-button"], false);
    }
}

async function reloadUsers() {
    state.users = await api(userSearchPath());
    renderUsers();
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

function renderDiagnostics() {
    elements["diagnostic-table-body"].replaceChildren();
    elements["diagnostic-count"].textContent =
        `${state.diagnostics.length} 个诊断包`;
    state.diagnostics.forEach(upload => {
        const row = document.createElement("tr");
        const identity = document.createElement("td");
        const copy = document.createElement("div");
        copy.className = "profile-name";
        const id = document.createElement("strong");
        id.textContent = upload.uploadId.slice(0, 8);
        const hash = document.createElement("span");
        hash.textContent = upload.sha256;
        hash.title = upload.sha256;
        copy.append(id, hash);
        identity.append(copy);

        const player = document.createElement("td");
        const playerCopy = document.createElement("div");
        playerCopy.className = "meta-stack";
        const displayName = document.createElement("strong");
        displayName.textContent = upload.accountDisplayName;
        const userId = document.createElement("span");
        userId.textContent = upload.userId.slice(0, 8);
        playerCopy.append(displayName, userId);
        player.append(playerCopy);

        const profile = document.createElement("td");
        const profileCopy = document.createElement("div");
        profileCopy.className = "meta-stack";
        const profileId = document.createElement("strong");
        profileId.textContent = upload.profileId;
        const launcherVersion = document.createElement("span");
        launcherVersion.textContent = `启动器 v${upload.launcherVersion}`;
        profileCopy.append(profileId, launcherVersion);
        profile.append(profileCopy);

        const actions = document.createElement("td");
        actions.className = "actions-column";
        const actionGroup = document.createElement("div");
        actionGroup.className = "row-actions";
        actionGroup.append(iconButton(
            "download",
            "下载诊断包并写入审计",
            () => downloadDiagnostic(upload)));
        actions.append(actionGroup);

        row.append(
            identity,
            player,
            profile,
            textCell(formatBytes(upload.size)),
            textCell(formatDateTime(upload.uploadedAt)),
            textCell(formatDateTime(upload.expiresAt)),
            actions
        );
        elements["diagnostic-table-body"].append(row);
    });
    elements["diagnostic-empty-state"].hidden = state.diagnostics.length !== 0;
    elements["diagnostic-table-body"].parentElement.hidden =
        state.diagnostics.length === 0;
}

function downloadDiagnostic(upload) {
    const anchor = document.createElement("a");
    anchor.href =
        `/v1/admin/diagnostics/${encodeURIComponent(upload.uploadId)}/download`;
    anchor.download = `Hechao-Diagnostic-${upload.uploadId}.zip`;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    showToast("正在下载诊断包，操作已写入审计记录");
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
    elements["server-announcement"].value = "";
    elements["server-opens-at"].value = "";
    elements["server-closes-at"].value = "";
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
    elements["server-announcement"].value = server.announcement || "";
    elements["server-opens-at"].value = formatInputDateTime(server.opensAt);
    elements["server-closes-at"].value = formatInputDateTime(server.closesAt);
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
        sortOrder: Number(elements["server-sort-order"].value),
        announcement: elements["server-announcement"].value.trim(),
        opensAt: parseInputDateTime(elements["server-opens-at"].value),
        closesAt: parseInputDateTime(elements["server-closes-at"].value)
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
    if (action.includes("security.minecraft_ban")) return "shield-check";
    if (action.includes("security.account")) return "key-round";
    if (action.includes("access.server_rule")) return "users";
    if (action.includes("created")) return "plus";
    if (action.includes("archived")) return "archive";
    if (action.includes("restored")) return "rotate-ccw";
    if (action.includes("mfa")) return "shield-check";
    if (action.includes("session")) return "key-round";
    if (action.includes("diagnostic")) return "activity";
    return "pencil";
}

function auditActionText(action) {
    const labels = {
        "catalog.server.created": "新增服务器",
        "catalog.server.updated": "编辑服务器",
        "catalog.server.archived": "归档服务器",
        "catalog.server.restored": "恢复服务器",
        "access.server_rule.created": "新增单服权限规则",
        "access.server_rule.updated": "编辑单服权限规则",
        "access.server_rule.deleted": "清除单服权限规则",
        "security.account.disabled": "停用赫朝账号",
        "security.account.enabled": "恢复赫朝账号",
        "security.sessions.revoked_all": "撤销全部账号会话",
        "security.session.revoked": "撤销设备会话",
        "security.minecraft_ban.created": "封禁 Minecraft UUID",
        "security.minecraft_ban.updated": "更新 Minecraft UUID 封禁",
        "security.minecraft_ban.revoked": "解除 Minecraft UUID 封禁",
        "admin.login_ticket.created": "创建后台登录票据",
        "admin.web_session.created": "登录管理后台",
        "admin.web_session.revoked": "退出管理后台",
        "admin.mfa.enrollment.started": "开始设置双重验证",
        "admin.mfa.enabled": "启用双重验证",
        "admin.mfa.verified": "完成双重验证",
        "admin.mfa.recovery_code_used": "使用恢复码",
        "diagnostic.upload.authorized": "授权诊断上传",
        "diagnostic.upload.completed": "诊断包上传完成",
        "diagnostic.upload.failed": "诊断包上传失败",
        "diagnostic.upload.expired": "诊断包到期删除",
        "diagnostic.admin.downloaded": "管理员下载诊断包"
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

function formatInputDateTime(value) {
    if (!value) return "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
    return local.toISOString().slice(0, 16);
}

function parseInputDateTime(value) {
    if (!value) return null;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
}
