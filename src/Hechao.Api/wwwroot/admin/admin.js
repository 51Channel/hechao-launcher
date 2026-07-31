"use strict";

const iconRoot = "/admin/assets/icons/";
const state = {
    session: null,
    csrfToken: null,
    servers: [],
    users: [],
    profiles: [],
    telemetry: null,
    telemetryHours: 24,
    runtime: null,
    control: null,
    selectedControlServerId: null,
    pendingControlAction: null,
    controlPollTimer: null,
    serverPollTimer: null,
    alerts: null,
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
    selectedProfileDetail: null,
    pendingProfileRelease: null,
    pendingProfileChannelRollback: null,
    pendingProfileChannelAssignment: null,
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
        "telemetry-section", "runtime-section", "control-section", "alerts-section",
        "diagnostics-section", "audit-section",
        "server-total-count", "server-online-count", "server-maintenance-count",
        "server-archived-count", "server-search", "create-server-button",
        "server-table-body", "server-empty-state", "profile-count",
        "profile-table-body", "profile-empty-state",
        "telemetry-event-count", "telemetry-user-count",
        "telemetry-download-attempts", "telemetry-download-failure-rate",
        "telemetry-download-bytes", "telemetry-download-succeeded",
        "telemetry-download-failed", "telemetry-download-canceled",
        "telemetry-launch-failure-rate", "telemetry-launch-attempts",
        "telemetry-launch-succeeded", "telemetry-launch-failed",
        "telemetry-launcher-version-body", "telemetry-launcher-version-empty",
        "telemetry-profile-version-body", "telemetry-profile-version-empty",
        "telemetry-failure-body", "telemetry-failure-empty",
        "telemetry-period-label",
        "runtime-generated-at", "runtime-fresh-count", "runtime-online-count",
        "runtime-player-count", "runtime-issue-count", "runtime-table-body",
        "runtime-empty-state", "runtime-issue-body", "runtime-issue-empty",
        "control-generated-at", "control-target-count", "control-agent-count",
        "control-online-count", "control-operation-count",
        "control-target-label", "control-target-list", "control-empty-state",
        "control-detail", "control-selected-id", "control-selected-name",
        "control-selected-meta", "control-start-button", "control-stop-button",
        "control-restart-button", "control-conflict-notice",
        "control-info-status", "control-info-agent", "control-info-port",
        "control-info-process", "control-info-memory", "control-info-memory-limit",
        "control-settings-form", "control-max-players",
        "control-view-distance", "control-simulation-distance",
        "control-difficulty", "control-initial-memory", "control-maximum-memory",
        "control-memory-limit-hint", "control-whitelist",
        "control-save-settings-button", "control-command-prefixes",
        "control-console-time", "control-console-output",
        "control-command-form", "control-command-input",
        "control-send-command-button", "control-history-body",
        "control-history-empty", "control-action-dialog",
        "control-action-form", "control-action-title",
        "control-action-message", "control-action-warning",
        "control-action-error", "control-action-reason",
        "control-action-confirmation", "control-action-confirmation-hint",
        "cancel-control-action-button", "accept-control-action-button",
        "alert-generated-at", "alert-active-count", "alert-critical-count",
        "alert-warning-count", "alert-unacknowledged-count",
        "alert-table-body", "alert-empty-state",
        "diagnostic-count", "diagnostic-table-body",
        "diagnostic-empty-state", "audit-list", "audit-empty-state",
        "load-more-audit-button", "server-drawer", "server-form",
        "drawer-kicker", "drawer-title", "close-drawer-button",
        "cancel-server-button", "save-server-button", "form-error",
        "server-id", "server-display-name", "server-short-name",
        "server-icon-glyph", "server-status", "server-max-players",
        "server-minecraft-version", "server-loader", "server-minimum-tier",
        "server-role", "server-monitoring-enabled", "server-sort-order",
        "server-client-profile", "server-velocity-target",
        "server-allows-protocol-translation",
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
        "user-security-tier-name", "user-security-tier-status",
        "user-security-tier-meta", "user-security-target-tier",
        "user-security-tier-reason", "user-security-tier-action",
        "user-security-minecraft-name", "user-security-minecraft-status",
        "user-security-minecraft-uuid", "user-security-ban-meta",
        "user-security-ban-action", "user-security-session-count",
        "user-security-session-list", "user-security-session-empty",
        "revoke-all-user-sessions-button", "user-security-admin-session-count",
        "user-security-admin-ticket-count", "user-security-launch-grant-count",
        "user-security-forum-revocation-count",
        "security-action-dialog", "security-action-form", "security-action-icon",
        "security-action-title", "security-action-message",
        "security-action-error", "security-action-reason",
        "security-action-expiry-field", "security-action-expires-at",
        "cancel-security-action-button", "accept-security-action-button",
        "confirm-dialog", "confirm-icon", "confirm-title", "confirm-message",
        "cancel-confirm-button", "accept-confirm-button", "recovery-dialog",
        "create-profile-button", "profile-create-dialog", "profile-create-form",
        "profile-create-error", "profile-create-id", "profile-create-name",
        "cancel-profile-create-button", "save-profile-create-button",
        "profile-drawer", "profile-drawer-title", "close-profile-drawer-button",
        "finish-profile-drawer-button", "profile-drawer-error",
        "profile-manager-id", "profile-manager-revision", "profile-manager-name",
        "profile-manager-active", "save-profile-metadata-button",
        "profile-manifest-file", "import-profile-release-button",
        "profile-channel-list", "profile-release-count", "profile-release-list",
        "profile-release-empty", "profile-pause-dialog", "profile-pause-form",
        "profile-pause-icon", "profile-pause-title", "profile-pause-message",
        "profile-pause-error", "profile-pause-reason-field",
        "profile-pause-reason", "cancel-profile-pause-button",
        "accept-profile-pause-button",
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
    document.querySelectorAll("[data-control-action]").forEach(button => {
        button.addEventListener("click", () =>
            openControlAction(button.dataset.controlAction));
    });
    document.querySelectorAll("[data-control-command]").forEach(button => {
        button.addEventListener("click", () => {
            elements["control-command-input"].value =
                button.dataset.controlCommand;
            openControlAction("ConsoleCommand");
        });
    });
    elements["control-settings-form"].addEventListener(
        "submit",
        event => {
            event.preventDefault();
            openControlAction("ApplySettings");
        });
    ["control-initial-memory", "control-maximum-memory"].forEach(id => {
        elements[id].addEventListener("input", () => {
            elements["control-initial-memory"].setCustomValidity("");
            elements["control-maximum-memory"].setCustomValidity("");
        });
    });
    elements["control-command-form"].addEventListener(
        "submit",
        event => {
            event.preventDefault();
            openControlAction("ConsoleCommand");
        });
    elements["control-action-form"].addEventListener(
        "submit",
        submitControlAction);
    elements["cancel-control-action-button"].addEventListener(
        "click",
        () => elements["control-action-dialog"].close());
    document.querySelectorAll("[data-telemetry-hours]").forEach(button => {
        button.addEventListener("click", async () => {
            const hours = Number(button.dataset.telemetryHours);
            if (![24, 168, 720].includes(hours) ||
                hours === state.telemetryHours) {
                return;
            }
            state.telemetryHours = hours;
            document.querySelectorAll("[data-telemetry-hours]").forEach(item =>
                item.classList.toggle("active", item === button));
            await reloadTelemetry();
        });
    });
    elements["create-server-button"].addEventListener("click", openCreateServer);
    elements["create-profile-button"].addEventListener("click", openCreateProfile);
    elements["cancel-profile-create-button"].addEventListener(
        "click",
        closeCreateProfile);
    elements["profile-create-form"].addEventListener("submit", createProfile);
    elements["close-profile-drawer-button"].addEventListener(
        "click",
        closeProfileDrawer);
    elements["finish-profile-drawer-button"].addEventListener(
        "click",
        closeProfileDrawer);
    elements["save-profile-metadata-button"].addEventListener(
        "click",
        saveProfileMetadata);
    elements["import-profile-release-button"].addEventListener(
        "click",
        importProfileRelease);
    elements["cancel-profile-pause-button"].addEventListener(
        "click",
        closeProfilePauseDialog);
    elements["profile-pause-form"].addEventListener(
        "submit",
        submitProfilePause);
    elements["profile-create-dialog"].addEventListener("cancel", event => {
        event.preventDefault();
        closeCreateProfile();
    });
    elements["profile-drawer"].addEventListener("cancel", event => {
        event.preventDefault();
        closeProfileDrawer();
    });
    elements["profile-pause-dialog"].addEventListener("cancel", event => {
        event.preventDefault();
        closeProfilePauseDialog();
    });
    elements["close-drawer-button"].addEventListener("click", closeServerDrawer);
    elements["cancel-server-button"].addEventListener("click", closeServerDrawer);
    elements["server-form"].addEventListener("submit", saveServer);
    elements["server-role"].addEventListener("change", syncServerRoleFields);
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
    elements["user-security-tier-action"].addEventListener(
        "click",
        submitUserTierChange);
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
    elements["cancel-confirm-button"].addEventListener("click", closeConfirmation);
    elements["accept-confirm-button"].addEventListener("click", applyConfirmation);
    elements["confirm-dialog"].addEventListener("cancel", event => {
        event.preventDefault();
        closeConfirmation();
    });
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
    const headers = new Headers({
        "Accept": options.accept || "application/json"
    });
    if (options.rawBody !== undefined) {
        headers.set(
            "Content-Type",
            options.contentType || "application/octet-stream");
    } else if (options.body !== undefined) {
        headers.set("Content-Type", options.contentType || "application/json");
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
        body: options.rawBody !== undefined
            ? options.rawBody
            : options.body === undefined
                ? undefined
                : JSON.stringify(options.body)
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
        const [
            servers,
            profiles,
            diagnostics,
            users,
            telemetry,
            runtime,
            control,
            alerts
        ] =
            await Promise.all([
            api("/v1/admin/catalog/servers"),
            api("/v1/admin/catalog/client-profiles"),
            api("/v1/admin/diagnostics?limit=200"),
            api(userSearchPath()),
            api(`/v1/admin/telemetry/summary?hours=${state.telemetryHours}`),
            api("/v1/admin/server-runtime/summary"),
            api("/v1/admin/server-control/overview"),
            api("/v1/admin/operational-alerts")
            ]);
        state.servers = servers;
        state.profiles = profiles;
        state.diagnostics = diagnostics;
        state.users = users;
        state.telemetry = telemetry;
        state.runtime = runtime;
        state.control = control;
        state.alerts = alerts;
        renderServers();
        renderUsers();
        renderProfiles();
        renderTelemetry();
        renderRuntime();
        renderControl();
        renderAlerts();
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
    if (![
        "servers",
        "users",
        "profiles",
        "telemetry",
        "runtime",
        "control",
        "alerts",
        "diagnostics",
        "audit"
    ].includes(view)) {
        return;
    }
    state.activeView = view;
    document.querySelectorAll("[data-view]").forEach(button =>
        button.classList.toggle("active", button.dataset.view === view));
    const labels = {
        servers: "服务器目录",
        users: "玩家与权限",
        profiles: "客户端档案",
        telemetry: "运行数据",
        runtime: "服务状态",
        control: "服控面板",
        alerts: "告警中心",
        diagnostics: "玩家诊断包",
        audit: "审计记录"
    };
    elements["breadcrumb-current"].textContent = labels[view];
    elements["view-title"].textContent = labels[view];
    elements["servers-section"].hidden = view !== "servers";
    elements["users-section"].hidden = view !== "users";
    elements["profiles-section"].hidden = view !== "profiles";
    elements["telemetry-section"].hidden = view !== "telemetry";
    elements["runtime-section"].hidden = view !== "runtime";
    elements["control-section"].hidden = view !== "control";
    elements["alerts-section"].hidden = view !== "alerts";
    elements["diagnostics-section"].hidden = view !== "diagnostics";
    elements["audit-section"].hidden = view !== "audit";
    if (view === "audit" && state.auditEntries.length === 0) {
        loadAudit(true);
    }
    scheduleServerPolling(view === "servers");
    scheduleControlPolling(view === "control");
}

async function reloadTelemetry() {
    setBusy(elements["refresh-button"], true);
    try {
        state.telemetry = await api(
            `/v1/admin/telemetry/summary?hours=${state.telemetryHours}`);
        renderTelemetry();
        elements["last-refreshed"].textContent =
            `更新于 ${new Date().toLocaleTimeString("zh-CN", {
                hour: "2-digit",
                minute: "2-digit"
            })}`;
    } catch (error) {
        showToast(error.message, true);
    } finally {
        setBusy(elements["refresh-button"], false);
    }
}

function renderTelemetry() {
    const summary = state.telemetry;
    if (!summary) return;

    elements["telemetry-event-count"].textContent =
        summary.eventCount.toLocaleString("zh-CN");
    elements["telemetry-user-count"].textContent =
        summary.uniqueUsers.toLocaleString("zh-CN");
    elements["telemetry-download-attempts"].textContent =
        summary.downloads.attempts.toLocaleString("zh-CN");
    elements["telemetry-download-failure-rate"].textContent =
        formatPercentage(summary.downloads.failureRate);
    elements["telemetry-download-bytes"].textContent =
        formatBytes(summary.downloads.bytes);
    elements["telemetry-download-succeeded"].textContent =
        summary.downloads.succeeded.toLocaleString("zh-CN");
    elements["telemetry-download-failed"].textContent =
        summary.downloads.failed.toLocaleString("zh-CN");
    elements["telemetry-download-canceled"].textContent =
        summary.downloads.canceled.toLocaleString("zh-CN");
    elements["telemetry-launch-failure-rate"].textContent =
        formatPercentage(summary.launches.failureRate);
    elements["telemetry-launch-attempts"].textContent =
        summary.launches.attempts.toLocaleString("zh-CN");
    elements["telemetry-launch-succeeded"].textContent =
        summary.launches.succeeded.toLocaleString("zh-CN");
    elements["telemetry-launch-failed"].textContent =
        summary.launches.failed.toLocaleString("zh-CN");
    elements["telemetry-period-label"].textContent =
        `${formatDateTime(summary.from)} 至 ${formatDateTime(summary.to)}`;

    const launcherBody = elements["telemetry-launcher-version-body"];
    launcherBody.replaceChildren();
    summary.launcherVersions.forEach(version => {
        const percentage = summary.uniqueUsers === 0
            ? 0
            : version.users * 100 / summary.uniqueUsers;
        const row = document.createElement("tr");
        row.append(
            textCell(`v${version.launcherVersion}`),
            textCell(version.users.toLocaleString("zh-CN")),
            textCell(formatPercentage(percentage)));
        launcherBody.append(row);
    });
    toggleTelemetryTable(
        launcherBody,
        elements["telemetry-launcher-version-empty"],
        summary.launcherVersions.length > 0);

    const profileBody = elements["telemetry-profile-version-body"];
    profileBody.replaceChildren();
    summary.profileVersions.forEach(version => {
        const profile = state.profiles.find(item => item.id === version.profileId);
        const row = document.createElement("tr");
        row.append(
            identityTextCell(
                profile?.displayName || version.profileId,
                version.profileId),
            textCell(`v${version.profileVersion}`),
            textCell(
                `${version.users.toLocaleString("zh-CN")} / ` +
                version.events.toLocaleString("zh-CN")));
        profileBody.append(row);
    });
    toggleTelemetryTable(
        profileBody,
        elements["telemetry-profile-version-empty"],
        summary.profileVersions.length > 0);

    const failureBody = elements["telemetry-failure-body"];
    failureBody.replaceChildren();
    summary.failures.forEach(failure => {
        const row = document.createElement("tr");
        row.append(
            textCell(telemetryEventText(failure.type)),
            textCell(telemetryFailureText(failure.failureCode)),
            textCell(failure.count.toLocaleString("zh-CN")));
        failureBody.append(row);
    });
    toggleTelemetryTable(
        failureBody,
        elements["telemetry-failure-empty"],
        summary.failures.length > 0);
}

function toggleTelemetryTable(body, emptyState, hasData) {
    body.closest("table").hidden = !hasData;
    emptyState.hidden = hasData;
}

function formatPercentage(value) {
    const number = Number(value);
    return `${Number.isFinite(number) ? number.toFixed(number >= 10 ? 1 : 2) : "0"}%`;
}

function telemetryEventText(type) {
    const labels = {
        LauncherStarted: "启动器启动",
        Install: "安装客户端",
        Repair: "修复客户端",
        Rollback: "回滚版本",
        Launch: "启动 Minecraft",
        GameExit: "Minecraft 退出"
    };
    return labels[type] || type;
}

function telemetryFailureText(code) {
    const labels = {
        UserCanceled: "玩家取消",
        AuthenticationRequired: "赫朝登录失效",
        ProfileUnavailable: "客户端档案未发布",
        ApiUnavailable: "API 服务不可用",
        SignatureInvalid: "发布签名无效",
        IntegrityFailed: "文件校验失败",
        InsufficientDiskSpace: "磁盘空间不足",
        InstallBusy: "客户端正被占用",
        RuntimePreparationFailed: "Java 准备失败",
        NetworkUnavailable: "网络不可用",
        IoFailure: "本地文件读写失败",
        RollbackUnavailable: "回滚版本不可用",
        MinecraftIdentityRequired: "未绑定正版身份",
        MicrosoftReauthenticationRequired: "Microsoft 凭据过期",
        MicrosoftNotConfigured: "Microsoft 登录未配置",
        MicrosoftCanceled: "取消 Microsoft 登录",
        MicrosoftAccountMismatch: "Microsoft 账号不匹配",
        MicrosoftSignInFailed: "Microsoft 登录失败",
        MinecraftOwnership: "Minecraft 所有权验证失败",
        MinecraftSessionExpired: "游戏会话过期",
        LaunchAuthorizationFailed: "进服授权失败",
        GameAlreadyRunning: "游戏已在运行",
        InvalidProfile: "客户端启动信息无效",
        InvalidJavaSelection: "Java 版本不兼容",
        ProcessCreationFailed: "游戏进程创建失败",
        GameExitedNonZero: "Minecraft 异常退出",
        Unexpected: "未预期错误"
    };
    return labels[code] || code;
}

function renderRuntime() {
    const summary = state.runtime;
    if (!summary) return;

    const targets = summary.targets || [];
    const freshTargets = targets.filter(target =>
        target.hasHeartbeat && target.isFresh);
    const onlineTargets = freshTargets.filter(target => target.online);
    const issueTargets = targets.filter(target =>
        !target.hasHeartbeat ||
        !target.isFresh ||
        (target.issues || []).length > 0);
    const players = onlineTargets.reduce(
        (total, target) => total + target.onlinePlayers,
        0);

    elements["runtime-generated-at"].textContent =
        `生成于 ${formatDateTime(summary.generatedAt)}`;
    elements["runtime-fresh-count"].textContent =
        `${freshTargets.length} / ${targets.length}`;
    elements["runtime-online-count"].textContent =
        onlineTargets.length.toLocaleString("zh-CN");
    elements["runtime-player-count"].textContent =
        players.toLocaleString("zh-CN");
    elements["runtime-issue-count"].textContent =
        issueTargets.length.toLocaleString("zh-CN");

    const body = elements["runtime-table-body"];
    body.replaceChildren();
    targets.forEach(target => {
        const row = document.createElement("tr");
        row.append(
            runtimeTargetCell(target),
            runtimeStatusCell(target),
            runtimeTickCell(target),
            runtimeProcessCell(target),
            runtimeDiskCell(target),
            textCell(formatRuntimeUptime(target.processStartedAt)),
            identityTextCell(
                target.receivedAt ? formatRelativeTime(target.receivedAt) : "从未",
                target.receivedAt ? formatDateTime(target.receivedAt) : "没有心跳"),
            runtimeIssueCell(target));
        body.append(row);
    });
    elements["runtime-empty-state"].hidden = targets.length !== 0;
    body.parentElement.hidden = false;

    const issueBody = elements["runtime-issue-body"];
    issueBody.replaceChildren();
    (summary.issues || []).forEach(issue => {
        const row = document.createElement("tr");
        row.append(
            textCell(runtimeIssueText(issue.issue)),
            textCell(issue.samples.toLocaleString("zh-CN")),
            textCell(issue.targets.toLocaleString("zh-CN")));
        issueBody.append(row);
    });
    toggleTelemetryTable(
        issueBody,
        elements["runtime-issue-empty"],
        (summary.issues || []).length > 0);
}

function runtimeTargetCell(target) {
    const names = (target.servers || []).map(server => server.displayName);
    return identityTextCell(
        target.velocityTarget,
        names.length > 0 ? names.join(" / ") : "未绑定目录项");
}

function runtimeStatusCell(target) {
    const cell = document.createElement("td");
    const stack = document.createElement("div");
    stack.className = "runtime-status-stack";
    const badge = document.createElement("span");
    let status = "未上报";
    let badgeClass = "status-archived";
    if (target.hasHeartbeat && !target.isFresh) {
        status = "心跳过期";
        badgeClass = "status-maintenance";
    } else if (target.isFresh && target.online) {
        status = "在线";
        badgeClass = "status-online";
    } else if (target.isFresh) {
        status = "离线";
        badgeClass = "status-closed";
    }
    badge.className = `status-badge ${badgeClass}`;
    badge.textContent = status;
    const players = document.createElement("span");
    players.textContent = target.isFresh
        ? `${target.onlinePlayers} / ${target.maxPlayers} 人`
        : "人数不可用";
    stack.append(badge, players);
    cell.append(stack);
    return cell;
}

function runtimeTickCell(target) {
    const cell = document.createElement("td");
    const stack = document.createElement("div");
    stack.className = "meta-stack";
    const tps = document.createElement("strong");
    tps.textContent = target.tps1m === null || target.tps1m === undefined
        ? "等待指标代理"
        : `TPS ${Number(target.tps1m).toFixed(2)}`;
    const mspt = document.createElement("span");
    mspt.textContent =
        target.msptAverage === null || target.msptAverage === undefined
            ? "MSPT —"
            : `MSPT ${Number(target.msptAverage).toFixed(1)} ms`;
    if (target.msptAverage >= 50 || target.tps1m < 18) {
        stack.classList.add("runtime-warning");
    }
    stack.append(tps, mspt);
    cell.append(stack);
    return cell;
}

function runtimeProcessCell(target) {
    const cell = document.createElement("td");
    const stack = document.createElement("div");
    stack.className = "meta-stack";
    const memory = document.createElement("strong");
    memory.textContent = target.processWorkingSetBytes === null ||
        target.processWorkingSetBytes === undefined
        ? "进程不可用"
        : formatBytes(target.processWorkingSetBytes);
    const cpu = document.createElement("span");
    cpu.textContent = target.processCpuPercent === null ||
        target.processCpuPercent === undefined
        ? "CPU —"
        : `CPU ${Number(target.processCpuPercent).toFixed(1)}%`;
    stack.append(memory, cpu);
    cell.append(stack);
    return cell;
}

function runtimeDiskCell(target) {
    const cell = document.createElement("td");
    const stack = document.createElement("div");
    stack.className = "meta-stack";
    const free = document.createElement("strong");
    const hasDisk = Number.isFinite(target.diskFreeBytes) &&
        Number.isFinite(target.diskTotalBytes) &&
        target.diskTotalBytes > 0;
    free.textContent = hasDisk
        ? formatBytes(target.diskFreeBytes)
        : "磁盘不可用";
    const ratio = document.createElement("span");
    ratio.textContent = hasDisk
        ? `${(target.diskFreeBytes * 100 / target.diskTotalBytes).toFixed(1)}% 可用`
        : "余量 —";
    if (hasDisk && target.diskFreeBytes / target.diskTotalBytes < 0.1) {
        stack.classList.add("runtime-warning");
    }
    stack.append(free, ratio);
    cell.append(stack);
    return cell;
}

function runtimeIssueCell(target) {
    const cell = document.createElement("td");
    const issues = [];
    if (!target.hasHeartbeat) {
        issues.push("尚无心跳");
    } else if (!target.isFresh) {
        issues.push("心跳已过期");
    }
    (target.issues || []).forEach(issue =>
        issues.push(runtimeIssueText(issue)));
    const text = document.createElement("span");
    text.className = issues.length > 0
        ? "runtime-issues"
        : "runtime-ok";
    text.textContent = issues.length > 0
        ? [...new Set(issues)].join("、")
        : "正常";
    cell.append(text);
    return cell;
}

function runtimeIssueText(issue) {
    return {
        StatusTimeout: "状态查询超时",
        StatusUnavailable: "状态查询不可用",
        ProcessProbeNotConfigured: "未配置本机进程探针",
        ProcessNotRunning: "监听进程未运行",
        ProcessAccessDenied: "进程访问被拒绝",
        ProcessProbeFailed: "进程探针失败",
        DiskProbeFailed: "磁盘探针失败",
        MetricsNotConfigured: "未配置性能指标",
        MetricsFileMissing: "性能指标文件缺失",
        MetricsFileStale: "性能指标已过期",
        MetricsFileInvalid: "性能指标无效"
    }[issue] || issue;
}

function formatRuntimeUptime(startedAt) {
    if (!startedAt) return "—";
    const milliseconds = Date.now() - new Date(startedAt).getTime();
    if (!Number.isFinite(milliseconds) || milliseconds < 0) return "—";
    const totalMinutes = Math.floor(milliseconds / 60000);
    const days = Math.floor(totalMinutes / 1440);
    const hours = Math.floor((totalMinutes % 1440) / 60);
    const minutes = totalMinutes % 60;
    if (days > 0) return `${days} 天 ${hours} 小时`;
    if (hours > 0) return `${hours} 小时 ${minutes} 分`;
    return `${minutes} 分钟`;
}

function renderControl() {
    const overview = state.control;
    if (!overview) return;

    const targets = overview.targets || [];
    const connectedTargets = targets.filter(target => target.agentConnected);
    const activeOperations = targets.filter(target => target.activeOperation);
    const connectedAgents = new Set(
        connectedTargets.map(target => target.agentId));
    elements["control-generated-at"].textContent =
        `生成于 ${formatDateTime(overview.generatedAt)}`;
    elements["control-target-count"].textContent =
        targets.length.toLocaleString("zh-CN");
    elements["control-agent-count"].textContent =
        connectedAgents.size.toLocaleString("zh-CN");
    elements["control-online-count"].textContent =
        targets.filter(target => target.online).length.toLocaleString("zh-CN");
    elements["control-operation-count"].textContent =
        activeOperations.length.toLocaleString("zh-CN");
    elements["control-target-label"].textContent = `${targets.length} 个目标`;
    elements["control-empty-state"].hidden = targets.length !== 0;

    if (!targets.some(target =>
        target.serverId === state.selectedControlServerId)) {
        state.selectedControlServerId = targets[0]?.serverId || null;
    }

    const list = elements["control-target-list"];
    list.replaceChildren();
    targets.forEach(target => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "control-target-item";
        button.classList.toggle(
            "active",
            target.serverId === state.selectedControlServerId);
        const title = document.createElement("strong");
        title.textContent = target.displayName;
        const meta = document.createElement("span");
        meta.textContent = [
            target.online ? "运行中" : "已停止",
            target.agentConnected ? target.agentId : "代理离线",
            Number.isFinite(target.settings?.maximumMemoryMiB)
                ? `Xmx ${formatMemoryMiB(target.settings.maximumMemoryMiB)}`
                : "内存未上报"
        ].join(" · ");
        const marker = document.createElement("i");
        marker.className = [
            "control-target-marker",
            !target.agentConnected
                ? "offline"
                : target.online
                    ? "online"
                    : "stopped"
        ].join(" ");
        button.append(marker, title, meta);
        button.addEventListener("click", () => {
            state.selectedControlServerId = target.serverId;
            renderControl();
        });
        list.append(button);
    });

    const selected = getSelectedControlTarget();
    elements["control-detail"].hidden = !selected;
    if (!selected) {
        return;
    }

    elements["control-selected-id"].textContent = selected.serverId;
    elements["control-selected-name"].textContent = selected.displayName;
    elements["control-selected-meta"].textContent = [
        selected.online
            ? `运行中 · PID ${selected.processId || "—"} · 端口 ${selected.port}`
            : `已停止 · 端口 ${selected.port}`,
        selected.agentConnected
            ? `代理 ${selected.agentId} 在线`
            : `代理 ${selected.agentId} 离线`,
        `最后上报 ${formatRelativeTime(selected.lastSeenAt)}`
    ].join("　");

    const busy = Boolean(selected.activeOperation);
    elements["control-start-button"].disabled =
        !selected.agentConnected || busy ||
        (selected.online && !hasOnlineControlConflict(selected));
    elements["control-stop-button"].disabled =
        !selected.agentConnected || busy || !selected.online;
    elements["control-restart-button"].disabled =
        !selected.agentConnected || busy;

    const conflicts = getControlConflicts(selected);
    const notice = elements["control-conflict-notice"];
    notice.hidden = !selected.conflictGroup;
    notice.textContent = selected.conflictGroup
        ? `冲突组 ${selected.conflictGroup}：启动本服前会先正常关闭 ` +
          (conflicts.length > 0
              ? conflicts.map(target => target.displayName).join("、")
              : "同组中正在运行的其他服务器") +
          "，确认端口释放后才继续。"
        : "";

    const settings = selected.settings;
    const hasMemorySettings =
        Number.isFinite(settings?.initialMemoryMiB) &&
        Number.isFinite(settings?.maximumMemoryMiB) &&
        Number.isFinite(settings?.maximumAllowedMemoryMiB);
    elements["control-info-status"].textContent =
        selected.online ? "运行中" : "已停止";
    elements["control-info-agent"].textContent = selected.agentConnected
        ? `${selected.agentId} · 在线`
        : `${selected.agentId} · 离线`;
    elements["control-info-port"].textContent = String(selected.port);
    elements["control-info-process"].textContent = selected.processId
        ? String(selected.processId)
        : "—";
    elements["control-info-memory"].textContent = hasMemorySettings
        ? `Xms ${formatMemoryMiB(settings.initialMemoryMiB)} · Xmx ${formatMemoryMiB(settings.maximumMemoryMiB)}`
        : "未上报";
    elements["control-info-memory-limit"].textContent = hasMemorySettings
        ? formatMemoryMiB(settings.maximumAllowedMemoryMiB)
        : "—";
    const settingsEditing =
        elements["control-settings-form"].contains(document.activeElement);
    if (!settingsEditing) {
        elements["control-max-players"].value = settings?.maxPlayers ?? "";
        elements["control-view-distance"].value =
            settings?.viewDistance ?? "";
        elements["control-simulation-distance"].value =
            settings?.simulationDistance ?? "";
        elements["control-difficulty"].value =
            settings?.difficulty || "normal";
        elements["control-initial-memory"].value = hasMemorySettings
            ? formatMemoryGiBInput(settings.initialMemoryMiB)
            : "";
        elements["control-maximum-memory"].value = hasMemorySettings
            ? formatMemoryGiBInput(settings.maximumMemoryMiB)
            : "";
        const maximumMemoryGiB = hasMemorySettings
            ? formatMemoryGiBInput(settings.maximumAllowedMemoryMiB)
            : "64";
        elements["control-initial-memory"].max = maximumMemoryGiB;
        elements["control-maximum-memory"].max = maximumMemoryGiB;
        elements["control-whitelist"].checked =
            Boolean(settings?.whiteList);
    }
    elements["control-memory-limit-hint"].textContent = hasMemorySettings
        ? `单服最大可设 ${formatMemoryMiB(settings.maximumAllowedMemoryMiB)}；运行中的服务不会自动重启。`
        : "代理尚未上报可管理的 JVM 内存参数。";
    elements["control-settings-form"]
        .querySelectorAll("input, select, button")
        .forEach(control => {
            control.disabled =
                !selected.agentConnected || busy || !settings || !hasMemorySettings;
        });
    elements["control-save-settings-button"].disabled =
        !selected.agentConnected || busy || !settings || !hasMemorySettings;

    const prefixes = selected.allowedCommandPrefixes || [];
    elements["control-command-prefixes"].textContent =
        prefixes.length > 0
            ? `允许命令：${prefixes.join("、")}`
            : "本机未开放控制台命令";
    elements["control-console-time"].textContent =
        selected.consoleCapturedAt
            ? `日志 ${formatRelativeTime(selected.consoleCapturedAt)}`
            : "暂无日志";
    elements["control-console-output"].textContent =
        selected.consoleTail || "服务器尚未产生可读取的控制台日志。";
    elements["control-command-input"].disabled =
        !selected.agentConnected || busy || !selected.online;
    elements["control-send-command-button"].disabled =
        !selected.agentConnected || busy || !selected.online;
    document.querySelectorAll("[data-control-command]").forEach(button => {
        const prefix = button.dataset.controlCommand.split(/\s+/, 1)[0];
        button.disabled =
            !selected.agentConnected ||
            busy ||
            !selected.online ||
            !prefixes.includes(prefix);
    });

    renderControlHistory(selected.serverId);
}

function getSelectedControlTarget() {
    return (state.control?.targets || []).find(target =>
        target.serverId === state.selectedControlServerId) || null;
}

function getControlConflicts(target) {
    if (!target.conflictGroup) return [];
    return (state.control?.targets || []).filter(candidate =>
        candidate.serverId !== target.serverId &&
        candidate.conflictGroup === target.conflictGroup &&
        candidate.online);
}

function hasOnlineControlConflict(target) {
    return getControlConflicts(target).length > 0;
}

function renderControlHistory(serverId) {
    const operations = (state.control?.recentOperations || [])
        .filter(operation => operation.serverId === serverId)
        .slice(0, 20);
    const body = elements["control-history-body"];
    body.replaceChildren();
    operations.forEach(operation => {
        const row = document.createElement("tr");
        const automaticallyStopping =
            operation.automaticallyStoppingServerIds || [];
        const result = [
            operation.resultMessage || "等待代理执行",
            automaticallyStopping.length > 0
                ? `自动关闭：${automaticallyStopping.join("、")}`
                : ""
        ].filter(Boolean).join("；");
        row.append(
            textCell(formatDateTime(operation.requestedAt)),
            textCell(controlActionText(operation.action)),
            textCell(controlStatusText(operation.status)),
            textCell(result));
        body.append(row);
    });
    elements["control-history-empty"].hidden = operations.length !== 0;
    body.parentElement.hidden = false;
}

function controlActionText(action) {
    return {
        Start: "启动",
        Stop: "停止",
        Restart: "重启",
        ConsoleCommand: "控制台命令",
        ApplySettings: "快捷设置"
    }[action] || action;
}

function controlStatusText(status) {
    return {
        Pending: "等待代理",
        Running: "执行中",
        Succeeded: "已完成",
        Failed: "失败",
        Cancelled: "已取消"
    }[status] || status;
}

function openControlAction(action) {
    const target = getSelectedControlTarget();
    if (!target || !target.agentConnected || target.activeOperation) {
        showToast("该服务器当前不能执行控制动作。", true);
        return;
    }

    const pending = {
        action,
        serverId: target.serverId,
        consoleCommand: null,
        settings: null
    };
    if (action === "ConsoleCommand") {
        const command = elements["control-command-input"].value.trim();
        if (!command) {
            showToast("请先输入 Minecraft 控制台命令。", true);
            elements["control-command-input"].focus();
            return;
        }
        pending.consoleCommand = command;
    } else if (action === "ApplySettings") {
        const initialMemoryInput = elements["control-initial-memory"];
        const maximumMemoryInput = elements["control-maximum-memory"];
        initialMemoryInput.setCustomValidity("");
        maximumMemoryInput.setCustomValidity("");
        const initialMemoryMiB = memoryGiBInputToMiB(initialMemoryInput.value);
        const maximumMemoryMiB = memoryGiBInputToMiB(maximumMemoryInput.value);
        const maximumAllowedMemoryMiB = target.settings?.maximumAllowedMemoryMiB;
        if (initialMemoryMiB > maximumMemoryMiB) {
            maximumMemoryInput.setCustomValidity("最大内存不能小于初始内存。");
        } else if (Number.isFinite(maximumAllowedMemoryMiB) &&
                   maximumMemoryMiB > maximumAllowedMemoryMiB) {
            maximumMemoryInput.setCustomValidity(
                `最大内存不能超过 ${formatMemoryMiB(maximumAllowedMemoryMiB)}。`);
        }
        if (!elements["control-settings-form"].reportValidity()) {
            return;
        }
        pending.settings = {
            maxPlayers: Number(elements["control-max-players"].value),
            viewDistance: Number(elements["control-view-distance"].value),
            simulationDistance:
                Number(elements["control-simulation-distance"].value),
            difficulty: elements["control-difficulty"].value,
            whiteList: elements["control-whitelist"].checked,
            initialMemoryMiB,
            maximumMemoryMiB,
            maximumAllowedMemoryMiB
        };
    }

    state.pendingControlAction = pending;
    const labels = {
        Start: ["启动服务器", `启动 ${target.displayName}`],
        Stop: ["停止服务器", `保存世界后正常停止 ${target.displayName}`],
        Restart: ["重启服务器", `保存世界、停止并重新启动 ${target.displayName}`],
        ConsoleCommand: [
            "发送 Minecraft 命令",
            `向 ${target.displayName} 发送：${pending.consoleCommand}`
        ],
        ApplySettings: [
            "保存快捷设置",
            `更新 ${target.displayName} 的受管 server.properties 与 JVM 启动内存；运行中的服务不会自动重启`
        ]
    };
    elements["control-action-title"].textContent = labels[action][0];
    elements["control-action-message"].textContent = labels[action][1];
    const conflicts =
        action === "Start" || action === "Restart"
            ? getControlConflicts(target)
            : [];
    elements["control-action-warning"].hidden = conflicts.length === 0;
    elements["control-action-warning"].textContent = conflicts.length > 0
        ? `将先自动保存并关闭：${conflicts.map(item => item.displayName).join("、")}。` +
          "任何一个停止失败都会取消本次启动。"
        : "";
    elements["control-action-reason"].value = "";
    elements["control-action-confirmation"].value = "";
    elements["control-action-confirmation-hint"].textContent =
        `请输入：${target.serverId}`;
    setInlineError(elements["control-action-error"], "");
    elements["control-action-dialog"].showModal();
    elements["control-action-reason"].focus();
}

async function submitControlAction(event) {
    event.preventDefault();
    const pending = state.pendingControlAction;
    if (!pending) {
        elements["control-action-dialog"].close();
        return;
    }

    const submit = elements["accept-control-action-button"];
    setBusy(submit, true);
    setInlineError(elements["control-action-error"], "");
    try {
        const result = await api(
            `/v1/admin/server-control/targets/${encodeURIComponent(pending.serverId)}/operations`,
            {
                method: "POST",
                body: {
                    action: pending.action,
                    confirmation:
                        elements["control-action-confirmation"].value.trim(),
                    reason: elements["control-action-reason"].value.trim(),
                    consoleCommand: pending.consoleCommand,
                    settings: pending.settings
                }
            });
        elements["control-action-dialog"].close();
        state.pendingControlAction = null;
        const stopped = result.automaticallyStoppingServerIds || [];
        showToast(
            stopped.length > 0
                ? `操作已排队，将先关闭 ${stopped.join("、")}`
                : "服务器控制操作已安全排队");
        await reloadControl();
    } catch (error) {
        setInlineError(elements["control-action-error"], error.message);
    } finally {
        setBusy(submit, false);
    }
}

async function reloadControl() {
    try {
        state.control = await api("/v1/admin/server-control/overview");
        renderControl();
    } catch (error) {
        if (error.status === 401 || error.status === 403) {
            location.reload();
            return;
        }
        showToast(error.message, true);
    }
}

async function reloadServers() {
    try {
        state.servers = await api("/v1/admin/catalog/servers");
        renderServers();
    } catch (error) {
        if (error.status === 401 || error.status === 403) {
            location.reload();
            return;
        }
        console.warn("服务器目录自动刷新失败", error);
    }
}

function scheduleServerPolling(enabled) {
    if (state.serverPollTimer) {
        window.clearInterval(state.serverPollTimer);
        state.serverPollTimer = null;
    }
    if (enabled) {
        reloadServers();
        state.serverPollTimer = window.setInterval(() => {
            if (!document.hidden && !elements["server-drawer"].open) {
                reloadServers();
            }
        }, 5000);
    }
}

function scheduleControlPolling(enabled) {
    if (state.controlPollTimer) {
        window.clearInterval(state.controlPollTimer);
        state.controlPollTimer = null;
    }
    if (enabled) {
        state.controlPollTimer = window.setInterval(() => {
            const editing =
                elements["control-settings-form"].contains(
                    document.activeElement) ||
                elements["control-command-form"].contains(
                    document.activeElement);
            if (!elements["control-action-dialog"].open && !editing) {
                reloadControl();
            }
        }, 3000);
    }
}

function formatRelativeTime(value) {
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

function renderAlerts() {
    const summary = state.alerts;
    if (!summary) return;

    elements["alert-generated-at"].textContent =
        `评估于 ${formatDateTime(summary.generatedAt)}`;
    elements["alert-active-count"].textContent =
        summary.activeCount.toLocaleString("zh-CN");
    elements["alert-critical-count"].textContent =
        summary.criticalCount.toLocaleString("zh-CN");
    elements["alert-warning-count"].textContent =
        summary.warningCount.toLocaleString("zh-CN");
    elements["alert-unacknowledged-count"].textContent =
        summary.unacknowledgedCount.toLocaleString("zh-CN");

    const alerts = summary.alerts || [];
    const body = elements["alert-table-body"];
    body.replaceChildren();
    alerts.forEach(alert => {
        const row = document.createElement("tr");
        if (alert.status === "Resolved") {
            row.classList.add("alert-resolved");
        }
        row.append(
            alertSeverityCell(alert),
            identityTextCell(alert.title, alert.summary),
            identityTextCell(
                operationalAlertSourceText(alert.source),
                alert.code),
            alertStatusCell(alert),
            identityTextCell(
                formatRelativeTime(alert.openedAt),
                formatDateTime(alert.openedAt)),
            identityTextCell(
                formatRelativeTime(alert.lastSeenAt),
                `${formatDateTime(alert.lastSeenAt)} · ${alert.observationCount} 次`),
            alertActionCell(alert));
        body.append(row);
    });
    elements["alert-empty-state"].hidden = alerts.length !== 0;
}

function alertSeverityCell(alert) {
    const cell = document.createElement("td");
    const badge = document.createElement("span");
    badge.className =
        `alert-severity alert-severity-${alert.severity.toLowerCase()}`;
    badge.textContent = {
        Critical: "严重",
        Warning: "警告",
        Info: "提示"
    }[alert.severity] || alert.severity;
    cell.append(badge);
    return cell;
}

function alertStatusCell(alert) {
    const cell = document.createElement("td");
    const badge = document.createElement("span");
    if (alert.status === "Resolved") {
        badge.className = "status-badge status-archived";
        badge.textContent = "已恢复";
    } else if (alert.acknowledgedAt) {
        badge.className = "status-badge status-maintenance";
        badge.textContent = "已确认";
    } else {
        badge.className = "status-badge status-online";
        badge.textContent = "活动";
    }
    cell.append(badge);
    return cell;
}

function alertActionCell(alert) {
    const cell = document.createElement("td");
    cell.className = "alert-action";
    if (alert.status === "Active" && !alert.acknowledgedAt) {
        const button = actionButton(
            "check",
            "确认",
            () => acknowledgeAlert(alert, button));
        cell.append(button);
    } else {
        const text = document.createElement("span");
        text.className = "count-label";
        text.textContent = alert.status === "Resolved"
            ? `恢复于 ${formatDateTime(alert.resolvedAt)}`
            : `确认于 ${formatDateTime(alert.acknowledgedAt)}`;
        cell.append(text);
    }
    return cell;
}

async function acknowledgeAlert(alert, button) {
    setBusy(button, true);
    try {
        await api(
            `/v1/admin/operational-alerts/${encodeURIComponent(alert.fingerprint)}/acknowledge`,
            { method: "POST" });
        state.alerts = await api("/v1/admin/operational-alerts");
        renderAlerts();
        showToast("告警已确认；异常恢复前仍会保持活动状态");
    } catch (error) {
        showToast(error.message, true);
    } finally {
        setBusy(button, false);
    }
}

function operationalAlertSourceText(source) {
    return {
        Api: "启动器 API",
        Authentication: "账号认证",
        Distribution: "内容分发",
        Server: "游戏服务",
        Certificate: "HTTPS 证书",
        Infrastructure: "基础设施"
    }[source] || source;
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
            server => server.role === "Player" &&
                server.isVisible &&
                server.effectiveStatus === "Online").length;
    elements["server-maintenance-count"].textContent =
        state.servers.filter(server =>
            server.role === "Player" &&
            server.isVisible &&
            server.status === "Maintenance").length;
    elements["server-archived-count"].textContent =
        state.servers.filter(server =>
            server.role === "Player" && !server.isVisible).length;
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
    badge.textContent = server.role === "Infrastructure"
        ? "内部节点"
        : server.isVisible
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
    target.textContent = `Velocity: ${server.velocityTarget}` +
        (server.allowsProtocolTranslation ? " · 协议转换" : "") +
        (server.monitoringEnabled ? " · 已监控" : " · 未监控");
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

function identityTextCell(primary, secondary) {
    const cell = document.createElement("td");
    const stack = document.createElement("div");
    stack.className = "meta-stack";
    const strong = document.createElement("strong");
    strong.textContent = primary;
    const span = document.createElement("span");
    span.textContent = secondary;
    stack.append(strong, span);
    cell.append(stack);
    return cell;
}

function serverActionsCell(server) {
    const cell = document.createElement("td");
    cell.className = "actions-column";
    const actions = document.createElement("div");
    actions.className = "row-actions";
    actions.append(
        iconButton("pencil", "编辑服务器", () => openEditServer(server))
    );
    if (server.role === "Player") {
        actions.append(iconButton(
            server.isVisible ? "archive" : "rotate-ccw",
            server.isVisible ? "归档服务器" : "恢复服务器",
            () => confirmVisibilityChange(server)
        ));
    }
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
    if (server.role === "Infrastructure") return "status-archived";
    if (!server.isVisible) return "status-archived";
    if (server.effectiveStatus === "Online") return "status-online";
    if (server.effectiveStatus === "Maintenance") return "status-maintenance";
    return "status-closed";
}

function effectiveStatusText(server) {
    if (server.status === "Online" && server.effectiveStatus === "Closed") {
        if (server.hasControlTarget && !server.controlTargetFresh) {
            return "服控失联";
        }
        if (server.hasControlTarget && server.controlReportedOnline === false) {
            return "服务已停止";
        }
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

    const pendingTierChange = security.pendingLuckPermsTierChange;
    elements["user-security-tier-name"].textContent =
        `${tierText(user.accessTier)} · ${user.luckPermsPrimaryGroup}`;
    setStatusBadge(
        elements["user-security-tier-status"],
        pendingTierChange
            ? pendingTierChange.status === "Claimed"
                ? "正在执行"
                : "等待执行"
            : "已同步",
        pendingTierChange ? "status-maintenance" : "status-online");
    elements["user-security-tier-meta"].textContent = pendingTierChange
        ? [
            `目标 ${tierText(pendingTierChange.targetAccessTier)}`,
            `主组 ${pendingTierChange.targetPrimaryGroup}`,
            `尝试 ${pendingTierChange.attemptCount} 次`,
            `申请于 ${formatDateTime(pendingTierChange.requestedAt)}`
        ].join(" · ")
        : [
            `主组 ${user.luckPermsPrimaryGroup}`,
            user.luckPermsSyncedAt
                ? `同步于 ${formatDateTime(user.luckPermsSyncedAt)}`
                : "尚无同步时间"
        ].join(" · ");
    elements["user-security-target-tier"].value =
        pendingTierChange?.targetAccessTier || user.accessTier;
    elements["user-security-tier-reason"].value =
        pendingTierChange?.reason || "";
    const tierChangeDisabled =
        isSelf || !user.minecraftUuid || Boolean(pendingTierChange);
    elements["user-security-target-tier"].disabled = tierChangeDisabled;
    elements["user-security-tier-reason"].disabled = tierChangeDisabled;
    elements["user-security-tier-action"].disabled = tierChangeDisabled;
    setButtonContent(
        elements["user-security-tier-action"],
        pendingTierChange ? "clock" : "chevron-right",
        pendingTierChange ? "等待大厅代理处理" : "提交等级变更");
    elements["user-security-tier-action"].title = isSelf
        ? "不能修改当前管理员自身的全局等级"
        : !user.minecraftUuid
            ? "玩家需要先绑定 Minecraft 正版身份"
            : pendingTierChange
                ? "已有等级变更正在处理"
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

async function submitUserTierChange() {
    const security = state.selectedUserSecurity;
    if (!security) return;
    const reason = elements["user-security-tier-reason"].value.trim();
    if (reason.length < 4 || reason.length > 500) {
        setInlineError(
            elements["user-security-error"],
            "等级变更原因必须为 4 到 500 个字符。");
        elements["user-security-tier-reason"].focus();
        return;
    }

    setBusy(elements["user-security-tier-action"], true);
    setInlineError(elements["user-security-error"], "");
    try {
        await api(
            `/v1/admin/users/${encodeURIComponent(
                security.user.userId)}/access-tier`,
            {
                method: "PUT",
                body: {
                    targetTier: elements["user-security-target-tier"].value,
                    expectedPrimaryGroup:
                        security.user.luckPermsPrimaryGroup,
                    reason
                }
            });
        state.selectedUserSecurity = await api(
            `/v1/admin/users/${encodeURIComponent(
                security.user.userId)}/security`);
        state.users = await api(userSearchPath());
        renderUsers();
        renderUserSecurity();
        if (state.selectedAccessPreview?.user?.userId ===
            security.user.userId) {
            closeAccessPreview();
        }
        showToast("全局等级变更已提交，等待大厅代理处理");
    } catch (error) {
        setInlineError(elements["user-security-error"], error.message);
    } finally {
        setBusy(elements["user-security-tier-action"], false);
    }
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
        id.textContent = `${profile.id} · r${profile.revision}`;
        copy.append(name, id);
        identity.append(copy);

        const production = profile.channels?.find(
            channel => channel.channel === "Production");
        const productionCell = document.createElement("td");
        const productionCopy = document.createElement("div");
        productionCopy.className = "meta-stack";
        const productionVersion = document.createElement("strong");
        productionVersion.textContent = production?.version
            ? `v${production.version}`
            : "尚未发布";
        const productionMeta = document.createElement("span");
        productionMeta.textContent = production?.manifestSha256
            ? `${formatBytes(profile.downloadBytes)} · ${shortHash(production.manifestSha256)}`
            : "正式通道未分配";
        productionCopy.append(productionVersion, productionMeta);
        productionCell.append(productionCopy);

        const channelsCell = document.createElement("td");
        const channelSummary = document.createElement("div");
        channelSummary.className = "profile-channel-summary";
        ["Test", "Gray", "Production"].forEach(channelName => {
            const channel = profile.channels?.find(
                item => item.channel === channelName);
            const pill = document.createElement("span");
            pill.className = `channel-pill ${channel?.manifestSha256 ? "assigned" : ""}`;
            pill.textContent = channelSummaryText(channelName, channel);
            channelSummary.append(pill);
        });
        channelsCell.append(channelSummary);

        const status = document.createElement("td");
        const badge = document.createElement("span");
        badge.className = `status-badge ${profile.isActive ? "status-online" : "status-archived"}`;
        badge.textContent = profile.isActive ? "启用" : "停用";
        status.append(badge);

        const actions = document.createElement("td");
        actions.className = "actions-column";
        const actionList = document.createElement("div");
        actionList.className = "row-actions";
        actionList.append(
            iconButton("pencil", "管理客户端档案", () => openProfileDrawer(profile.id)));
        actions.append(actionList);

        row.append(
            identity,
            productionCell,
            channelsCell,
            textCell(String(profile.releaseCount)),
            status,
            actions
        );
        elements["profile-table-body"].append(row);
    });
    elements["profile-empty-state"].hidden = state.profiles.length !== 0;
    elements["profile-table-body"].parentElement.hidden = false;
}

function channelSummaryText(channelName, channel) {
    const label = channelText(channelName);
    if (!channel?.manifestSha256) return `${label} —`;
    if (channelName === "Production") return `${label} v${channel.version}`;
    return `${label} ${channel.rolloutPercentage}%`;
}

function channelText(channel) {
    return {
        Test: "测试",
        Gray: "灰度",
        Production: "正式"
    }[channel] || channel;
}

function openCreateProfile() {
    elements["profile-create-form"].reset();
    setInlineError(elements["profile-create-error"], "");
    elements["profile-create-dialog"].showModal();
    elements["profile-create-id"].focus();
}

function closeCreateProfile() {
    if (elements["profile-create-dialog"].open) {
        elements["profile-create-dialog"].close();
    }
    setInlineError(elements["profile-create-error"], "");
}

async function createProfile(event) {
    event.preventDefault();
    setBusy(elements["save-profile-create-button"], true);
    setInlineError(elements["profile-create-error"], "");
    try {
        const detail = await api("/v1/admin/catalog/client-profiles", {
            method: "POST",
            body: {
                id: elements["profile-create-id"].value.trim(),
                displayName: elements["profile-create-name"].value.trim()
            }
        });
        closeCreateProfile();
        showToast("客户端档案已创建");
        await reloadProfiles();
        await showProfileDetail(detail);
    } catch (error) {
        setInlineError(elements["profile-create-error"], error.message);
    } finally {
        setBusy(elements["save-profile-create-button"], false);
    }
}

async function openProfileDrawer(profileId) {
    setInlineError(elements["profile-drawer-error"], "");
    try {
        const detail = await api(
            `/v1/admin/catalog/client-profiles/${encodeURIComponent(profileId)}`);
        await showProfileDetail(detail);
    } catch (error) {
        showToast(error.message, true);
    }
}

async function showProfileDetail(detail) {
    state.selectedProfileDetail = detail;
    renderProfileManager();
    if (!elements["profile-drawer"].open) {
        elements["profile-drawer"].showModal();
    }
}

function closeProfileDrawer() {
    if (elements["profile-drawer"].open) {
        elements["profile-drawer"].close();
    }
    state.selectedProfileDetail = null;
    elements["profile-manifest-file"].value = "";
    setInlineError(elements["profile-drawer-error"], "");
}

function renderProfileManager() {
    const detail = state.selectedProfileDetail;
    if (!detail) return;
    const profile = detail.profile;
    elements["profile-drawer-title"].textContent = profile.displayName;
    elements["profile-manager-id"].textContent = profile.id;
    elements["profile-manager-revision"].textContent = `修订 ${profile.revision}`;
    elements["profile-manager-name"].value = profile.displayName;
    elements["profile-manager-active"].checked = profile.isActive;
    elements["profile-release-count"].textContent =
        `${detail.releases.length} 个版本`;
    renderProfileChannels(detail);
    renderProfileReleases(detail);
}

function renderProfileChannels(detail) {
    const container = elements["profile-channel-list"];
    container.replaceChildren();
    ["Test", "Gray", "Production"].forEach(channelName => {
        const channel = detail.profile.channels.find(
            item => item.channel === channelName);
        if (!channel) return;

        const card = document.createElement("article");
        card.className = "profile-channel-card";
        const heading = document.createElement("div");
        heading.className = "profile-channel-heading";
        const copy = document.createElement("div");
        const title = document.createElement("strong");
        title.textContent = `${channelText(channelName)}通道`;
        const description = document.createElement("span");
        description.textContent = channelDescription(channelName);
        copy.append(title, description);
        const badge = document.createElement("span");
        badge.className = `status-badge ${channel.manifestSha256 ? "status-online" : "status-archived"}`;
        badge.textContent = channel.version ? `v${channel.version}` : "未分配";
        heading.append(copy, badge);

        const controls = document.createElement("div");
        controls.className = "profile-channel-controls";
        const releaseField = document.createElement("label");
        releaseField.textContent = "发布版本";
        const releaseSelect = document.createElement("select");
        const emptyOption = document.createElement("option");
        emptyOption.value = "";
        emptyOption.textContent = "不分配";
        releaseSelect.append(emptyOption);
        detail.releases.filter(release => !release.isPaused).forEach(release => {
            const option = document.createElement("option");
            option.value = release.manifestSha256;
            option.textContent =
                `v${release.version} · ${shortHash(release.manifestSha256)}`;
            releaseSelect.append(option);
        });
        releaseSelect.value = channel.manifestSha256 || "";
        releaseField.append(releaseSelect);

        const percentageField = document.createElement("label");
        percentageField.textContent = "覆盖比例";
        const percentageInput = document.createElement("input");
        percentageInput.type = "number";
        percentageInput.min = "0";
        percentageInput.max = "100";
        percentageInput.step = "1";
        percentageInput.value = channelName === "Production"
            ? "100"
            : String(channel.rolloutPercentage);
        percentageInput.disabled = channelName === "Production";
        percentageField.append(percentageInput);
        controls.append(releaseField, percentageField);

        const actions = document.createElement("div");
        actions.className = "profile-channel-actions";
        const revision = document.createElement("span");
        revision.textContent = `通道修订 r${channel.revision}`;
        const buttons = document.createElement("div");
        const rollbackButton = actionButton(
            "rotate-ccw",
            "回滚上一版本",
            () => confirmProfileChannelRollback(channel));
        rollbackButton.disabled = !channel.manifestSha256;
        const saveButton = actionButton(
            "save",
            "保存通道",
            () => saveProfileChannel(
                channel,
                releaseSelect.value || null,
                Number(percentageInput.value),
                saveButton));
        buttons.append(rollbackButton, saveButton);
        actions.append(revision, buttons);
        card.append(heading, controls, actions);
        container.append(card);
    });
}

function channelDescription(channelName) {
    return {
        Test: "仅管理员账号按稳定桶命中，用于首轮验证。",
        Gray: "所有已登录玩家按稳定桶命中，逐步扩大覆盖。",
        Production: "未命中测试和灰度时使用的正式版本。"
    }[channelName] || "";
}

function actionButton(icon, label, handler, className = "button-secondary") {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `button ${className}`;
    const image = document.createElement("img");
    image.src = `${iconRoot}${icon}.svg`;
    image.alt = "";
    const text = document.createElement("span");
    text.textContent = label;
    button.append(image, text);
    button.addEventListener("click", handler);
    return button;
}

function renderProfileReleases(detail) {
    const container = elements["profile-release-list"];
    container.replaceChildren();
    detail.releases.forEach(release => {
        const card = document.createElement("article");
        card.className = `profile-release-card ${release.isPaused ? "paused" : ""}`;
        const heading = document.createElement("div");
        heading.className = "profile-release-heading";
        const identity = document.createElement("div");
        const version = document.createElement("strong");
        version.textContent = `v${release.version}`;
        const published = document.createElement("span");
        published.textContent =
            `${formatDateTime(release.publishedAt)} · r${release.revision}`;
        identity.append(version, published);
        const badge = document.createElement("span");
        badge.className = `status-badge ${release.isPaused ? "status-maintenance" : "status-online"}`;
        badge.textContent = release.isPaused ? "已暂停" : "可发布";
        heading.append(identity, badge);

        const facts = document.createElement("dl");
        facts.className = "profile-release-facts";
        appendReleaseFact(
            facts,
            "运行环境",
            `${release.minecraftVersion} · ${release.loader}` +
            (release.loaderVersion ? ` ${release.loaderVersion}` : ""));
        appendReleaseFact(facts, "Java", release.javaVersion);
        appendReleaseFact(
            facts,
            "资源",
            `${formatBytes(release.downloadBytes)} · ${release.fileCount} 个文件`);
        appendReleaseFact(
            facts,
            "导入人",
            release.createdByDisplayName || "系统迁移");

        const hash = document.createElement("code");
        hash.className = "profile-release-hash";
        hash.textContent = release.manifestSha256;
        hash.title = release.manifestSha256;

        const footer = document.createElement("div");
        footer.className = "profile-release-actions";
        const channelButtons = document.createElement("div");
        if (!release.isPaused) {
            ["Test", "Gray", "Production"].forEach(channelName => {
                const assignButton = document.createElement("button");
                assignButton.type = "button";
                assignButton.className = "button button-secondary";
                assignButton.textContent =
                    channelName === "Production"
                        ? "设为正式"
                        : `发布到${channelText(channelName)}`;
                assignButton.addEventListener(
                    "click",
                    () => requestProfileChannelAssignment(release, channelName));
                channelButtons.append(assignButton);
            });
        }
        const pauseButton = actionButton(
            release.isPaused ? "rotate-ccw" : "archive",
            release.isPaused ? "恢复版本" : "暂停版本",
            () => openProfilePauseDialog(release),
            release.isPaused ? "button-secondary" : "button-danger");
        footer.append(channelButtons, pauseButton);
        if (release.pauseReason) {
            const reason = document.createElement("p");
            reason.className = "profile-release-pause-reason";
            reason.textContent = `暂停原因：${release.pauseReason}`;
            card.append(heading, facts, hash, reason, footer);
        } else {
            card.append(heading, facts, hash, footer);
        }
        container.append(card);
    });
    elements["profile-release-empty"].hidden = detail.releases.length !== 0;
}

function appendReleaseFact(container, label, value) {
    const item = document.createElement("div");
    const term = document.createElement("dt");
    term.textContent = label;
    const description = document.createElement("dd");
    description.textContent = value || "—";
    item.append(term, description);
    container.append(item);
}

async function saveProfileMetadata() {
    const detail = state.selectedProfileDetail;
    if (!detail) return;
    setBusy(elements["save-profile-metadata-button"], true);
    setInlineError(elements["profile-drawer-error"], "");
    try {
        const updated = await api(
            `/v1/admin/catalog/client-profiles/${encodeURIComponent(detail.profile.id)}`,
            {
                method: "PUT",
                body: {
                    displayName: elements["profile-manager-name"].value.trim(),
                    isActive: elements["profile-manager-active"].checked,
                    expectedRevision: detail.profile.revision
                }
            });
        await applyProfileDetail(updated);
        showToast("客户端档案信息已保存");
    } catch (error) {
        await handleProfileMutationError(error);
    } finally {
        setBusy(elements["save-profile-metadata-button"], false);
    }
}

async function importProfileRelease() {
    const detail = state.selectedProfileDetail;
    const file = elements["profile-manifest-file"].files[0];
    if (!detail || !file) {
        setInlineError(elements["profile-drawer-error"], "请选择已签名 JSON 清单。");
        return;
    }

    setBusy(elements["import-profile-release-button"], true);
    setInlineError(elements["profile-drawer-error"], "");
    try {
        const updated = await api(
            `/v1/admin/catalog/client-profiles/${encodeURIComponent(detail.profile.id)}/releases`,
            {
                method: "POST",
                rawBody: await file.arrayBuffer(),
                contentType: "application/vnd.hechao.signed-manifest+json"
            });
        elements["profile-manifest-file"].value = "";
        await applyProfileDetail(updated);
        showToast("签名版本已验证并导入");
    } catch (error) {
        await handleProfileMutationError(error);
    } finally {
        setBusy(elements["import-profile-release-button"], false);
    }
}

async function saveProfileChannel(
    channel,
    manifestSha256,
    rolloutPercentage,
    button) {
    const detail = state.selectedProfileDetail;
    if (!detail) return;
    setBusy(button, true);
    setInlineError(elements["profile-drawer-error"], "");
    try {
        const updated = await updateProfileChannel(
            detail.profile.id,
            channel,
            manifestSha256,
            rolloutPercentage);
        await applyProfileDetail(updated);
        showToast(`${channelText(channel.channel)}通道已更新`);
    } catch (error) {
        await handleProfileMutationError(error);
    } finally {
        setBusy(button, false);
    }
}

function updateProfileChannel(
    profileId,
    channel,
    manifestSha256,
    rolloutPercentage) {
    return api(
        `/v1/admin/catalog/client-profiles/${encodeURIComponent(profileId)}` +
        `/channels/${encodeURIComponent(channel.channel)}`,
        {
            method: "PUT",
            body: {
                manifestSha256,
                rolloutPercentage,
                expectedRevision: channel.revision
            }
        });
}

function requestProfileChannelAssignment(release, channelName) {
    const detail = state.selectedProfileDetail;
    if (!detail) return;
    const channel = detail.profile.channels.find(
        item => item.channel === channelName);
    if (!channel) return;
    if (channelName !== "Production") {
        assignProfileChannelRelease(release, channel);
        return;
    }

    state.pendingVisibilityChange = null;
    state.pendingProfileChannelRollback = null;
    state.pendingProfileChannelAssignment = { release, channel };
    elements["confirm-icon"].src = `${iconRoot}package.svg`;
    elements["confirm-title"].textContent = "切换正式版本";
    elements["confirm-message"].textContent =
        `正式通道将切换到 v${release.version}，所有未命中测试和灰度的玩家都会使用该版本。`;
    elements["accept-confirm-button"].textContent = "确认设为正式";
    elements["accept-confirm-button"].className = "button button-primary";
    elements["confirm-dialog"].showModal();
}

async function assignProfileChannelRelease(release, channel) {
    const detail = state.selectedProfileDetail;
    if (!detail) return;
    const rolloutPercentage = channel.channel === "Production"
        ? 100
        : channel.rolloutPercentage > 0
            ? channel.rolloutPercentage
            : channel.channel === "Test" ? 100 : 10;
    setInlineError(elements["profile-drawer-error"], "");
    try {
        const updated = await updateProfileChannel(
            detail.profile.id,
            channel,
            release.manifestSha256,
            rolloutPercentage);
        await applyProfileDetail(updated);
        showToast(`v${release.version} 已发布到${channelText(channel.channel)}通道`);
    } catch (error) {
        await handleProfileMutationError(error);
    }
}

function confirmProfileChannelRollback(channel) {
    state.pendingVisibilityChange = null;
    state.pendingProfileChannelAssignment = null;
    state.pendingProfileChannelRollback = channel;
    elements["confirm-icon"].src = `${iconRoot}rotate-ccw.svg`;
    elements["confirm-title"].textContent = `回滚${channelText(channel.channel)}通道`;
    elements["confirm-message"].textContent =
        `${channelText(channel.channel)}通道将回到当前版本之前最近的可用版本。`;
    elements["accept-confirm-button"].textContent = "确认回滚";
    elements["accept-confirm-button"].className = "button button-danger";
    elements["confirm-dialog"].showModal();
}

async function applyProfileChannelRollback() {
    const channel = state.pendingProfileChannelRollback;
    const detail = state.selectedProfileDetail;
    if (!channel || !detail) return;
    setBusy(elements["accept-confirm-button"], true);
    try {
        const updated = await api(
            `/v1/admin/catalog/client-profiles/${encodeURIComponent(detail.profile.id)}` +
            `/channels/${encodeURIComponent(channel.channel)}/rollback`,
            {
                method: "POST",
                body: { expectedRevision: channel.revision }
            });
        closeConfirmation();
        await applyProfileDetail(updated);
        showToast(`${channelText(channel.channel)}通道已回滚`);
    } catch (error) {
        closeConfirmation();
        await handleProfileMutationError(error);
    } finally {
        setBusy(elements["accept-confirm-button"], false);
    }
}

function openProfilePauseDialog(release) {
    const pausing = !release.isPaused;
    state.pendingProfileRelease = { release, pausing };
    elements["profile-pause-icon"].src =
        `${iconRoot}${pausing ? "archive" : "rotate-ccw"}.svg`;
    elements["profile-pause-title"].textContent =
        pausing ? "暂停问题版本" : "恢复已暂停版本";
    elements["profile-pause-message"].textContent = pausing
        ? `v${release.version} 将被禁止继续分发，引用它的通道会自动回滚。`
        : `v${release.version} 将恢复为可发布状态，但不会自动重新分配到任何通道。`;
    elements["profile-pause-reason-field"].hidden = !pausing;
    elements["profile-pause-reason"].required = pausing;
    elements["profile-pause-reason"].value = "";
    elements["accept-profile-pause-button"].textContent =
        pausing ? "确认暂停并回滚" : "确认恢复版本";
    elements["accept-profile-pause-button"].className =
        `button ${pausing ? "button-danger" : "button-primary"}`;
    setInlineError(elements["profile-pause-error"], "");
    elements["profile-pause-dialog"].showModal();
    if (pausing) elements["profile-pause-reason"].focus();
}

function closeProfilePauseDialog() {
    if (elements["profile-pause-dialog"].open) {
        elements["profile-pause-dialog"].close();
    }
    state.pendingProfileRelease = null;
    setInlineError(elements["profile-pause-error"], "");
}

async function submitProfilePause(event) {
    event.preventDefault();
    const pending = state.pendingProfileRelease;
    const detail = state.selectedProfileDetail;
    if (!pending || !detail) return;
    setBusy(elements["accept-profile-pause-button"], true);
    setInlineError(elements["profile-pause-error"], "");
    try {
        const updated = await api(
            `/v1/admin/catalog/client-profiles/${encodeURIComponent(detail.profile.id)}` +
            `/releases/${encodeURIComponent(pending.release.manifestSha256)}/pause`,
            {
                method: "PUT",
                body: {
                    isPaused: pending.pausing,
                    reason: pending.pausing
                        ? elements["profile-pause-reason"].value.trim()
                        : "",
                    expectedRevision: pending.release.revision
                }
            });
        const version = pending.release.version;
        const pausing = pending.pausing;
        closeProfilePauseDialog();
        await applyProfileDetail(updated);
        showToast(pausing ? `v${version} 已暂停并完成通道回滚` : `v${version} 已恢复`);
    } catch (error) {
        setInlineError(elements["profile-pause-error"], error.message);
        if (error.status === 409) {
            await refreshSelectedProfile();
        }
    } finally {
        setBusy(elements["accept-profile-pause-button"], false);
    }
}

async function applyProfileDetail(detail) {
    state.selectedProfileDetail = detail;
    renderProfileManager();
    await reloadProfiles();
}

async function reloadProfiles() {
    state.profiles = await api("/v1/admin/catalog/client-profiles");
    renderProfiles();
    populateProfileOptions();
}

async function refreshSelectedProfile() {
    const profileId = state.selectedProfileDetail?.profile.id;
    if (!profileId) return;
    state.selectedProfileDetail = await api(
        `/v1/admin/catalog/client-profiles/${encodeURIComponent(profileId)}`);
    renderProfileManager();
    await reloadProfiles();
}

async function handleProfileMutationError(error) {
    setInlineError(elements["profile-drawer-error"], error.message);
    if (error.status === 409) {
        try {
            await refreshSelectedProfile();
        } catch (refreshError) {
            setInlineError(elements["profile-drawer-error"], refreshError.message);
        }
    }
}

function shortHash(value) {
    if (!value) return "—";
    return value.length > 12
        ? `${value.slice(0, 8)}…${value.slice(-4)}`
        : value;
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
    elements["server-role"].value = "Player";
    elements["server-role"].disabled = false;
    elements["server-monitoring-enabled"].checked = true;
    elements["server-sort-order"].value = "100";
    elements["server-announcement"].value = "";
    elements["server-allows-protocol-translation"].checked = false;
    elements["server-opens-at"].value = "";
    elements["server-closes-at"].value = "";
    elements["server-is-visible"].checked = true;
    elements["server-visible-field"].hidden = false;
    syncServerRoleFields();
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
    elements["server-role"].value = server.role;
    elements["server-role"].disabled =
        server.role === "Infrastructure";
    elements["server-monitoring-enabled"].checked = server.monitoringEnabled;
    elements["server-sort-order"].value = server.sortOrder;
    elements["server-client-profile"].value = server.clientProfileId;
    elements["server-velocity-target"].value = server.velocityTarget;
    elements["server-allows-protocol-translation"].checked =
        server.allowsProtocolTranslation;
    elements["server-announcement"].value = server.announcement || "";
    elements["server-opens-at"].value = formatInputDateTime(server.opensAt);
    elements["server-closes-at"].value = formatInputDateTime(server.closesAt);
    elements["server-visible-field"].hidden = true;
    syncServerRoleFields();
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
        role: elements["server-role"].value,
        monitoringEnabled: elements["server-monitoring-enabled"].checked,
        clientProfileId: elements["server-client-profile"].value,
        velocityTarget: elements["server-velocity-target"].value.trim(),
        allowsProtocolTranslation:
            elements["server-allows-protocol-translation"].checked,
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
    if (server.role === "Infrastructure") {
        showToast("内部基础设施服务器不能恢复到玩家目录", true);
        return;
    }

    state.pendingVisibilityChange = server;
    state.pendingProfileChannelRollback = null;
    state.pendingProfileChannelAssignment = null;
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

function syncServerRoleFields() {
    const infrastructure =
        elements["server-role"].value === "Infrastructure";
    elements["server-allows-protocol-translation"].disabled = infrastructure;
    elements["server-is-visible"].disabled = infrastructure;
    if (infrastructure) {
        elements["server-allows-protocol-translation"].checked = false;
        elements["server-is-visible"].checked = false;
    }
}

function closeConfirmation() {
    if (elements["confirm-dialog"].open) {
        elements["confirm-dialog"].close();
    }
    state.pendingVisibilityChange = null;
    state.pendingProfileChannelRollback = null;
    state.pendingProfileChannelAssignment = null;
}

function applyConfirmation() {
    if (state.pendingProfileChannelRollback) {
        return applyProfileChannelRollback();
    }
    if (state.pendingProfileChannelAssignment) {
        return applyProfileChannelAssignment();
    }
    return applyVisibilityChange();
}

async function applyProfileChannelAssignment() {
    const pending = state.pendingProfileChannelAssignment;
    if (!pending) return;
    setBusy(elements["accept-confirm-button"], true);
    try {
        await assignProfileChannelRelease(pending.release, pending.channel);
        closeConfirmation();
    } finally {
        setBusy(elements["accept-confirm-button"], false);
    }
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
        closeConfirmation();
        showToast(server.isVisible ? "服务器已归档" : "服务器已恢复");
        await loadConsoleData();
    } catch (error) {
        closeConfirmation();
        showToast(error.message, true);
        await loadConsoleData();
    } finally {
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
    if (action.includes("client_profile_release")) return "package";
    if (action.includes("client_profile_channel")) return "refresh-cw";
    if (action.includes("client_profile")) return "package";
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
        "catalog.client_profile.created": "创建客户端档案",
        "catalog.client_profile.updated": "编辑客户端档案",
        "catalog.client_profile.enabled": "启用客户端档案",
        "catalog.client_profile.disabled": "停用客户端档案",
        "catalog.client_profile_release.imported": "导入签名客户端版本",
        "catalog.client_profile_release.hydrated": "补全迁移版本元数据",
        "catalog.client_profile_release.paused": "暂停客户端版本",
        "catalog.client_profile_release.resumed": "恢复客户端版本",
        "catalog.client_profile_channel.updated": "更新客户端发布通道",
        "catalog.client_profile_channel.rolled_back": "回滚客户端发布通道",
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

function formatMemoryGiBInput(memoryMiB) {
    if (!Number.isFinite(memoryMiB) || memoryMiB <= 0) return "";
    return Number((memoryMiB / 1024).toFixed(2)).toString();
}

function formatMemoryMiB(memoryMiB) {
    const value = formatMemoryGiBInput(memoryMiB);
    return value ? `${value} GiB` : "—";
}

function memoryGiBInputToMiB(value) {
    const gibibytes = Number(value);
    return Number.isFinite(gibibytes)
        ? Math.round(gibibytes * 1024)
        : Number.NaN;
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
