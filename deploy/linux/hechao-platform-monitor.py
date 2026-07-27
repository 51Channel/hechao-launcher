#!/usr/bin/env python3
"""Independent Hechao platform checks and transition-only email delivery."""

from __future__ import annotations

import json
import os
import smtplib
import socket
import ssl
import sys
import tempfile
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from email.message import EmailMessage
from pathlib import Path
from typing import Any


MONITOR_VERSION = "0.1.2"
USER_AGENT = f"Hechao-Platform-Monitor/{MONITOR_VERSION}"

DEFAULT_CHECKS = (
    ("platform:api-health", "Api.Health", "Api",
     "启动器 API 健康检查失败",
     "https://launcher-api.hechao.world/healthz", (200,)),
    ("platform:api-readiness", "Api.Readiness", "Api",
     "启动器 API 就绪检查失败",
     "https://launcher-api.hechao.world/readyz", (200,)),
    ("platform:admin-web", "Infrastructure.AdminWeb", "Infrastructure",
     "管理后台入口异常",
     "https://admin.hechao.world/admin/", (200,)),
    ("platform:oss-endpoint", "Distribution.OssEndpoint", "Distribution",
     "OSS 下载入口异常",
     "https://download.hechao.world/", (403,)),
    ("platform:legacy-site", "Infrastructure.LegacySite", "Infrastructure",
     "赫朝官网入口异常",
     "https://hechao.world/", (200,)),
    ("platform:legacy-api", "Infrastructure.LegacyApi", "Infrastructure",
     "既有中转 API 入口异常",
     "https://api.hechao.world/", (200,)),
)

TLS_HOSTS = (
    "launcher-api.hechao.world",
    "admin.hechao.world",
    "download.hechao.world",
    "hechao.world",
    "api.hechao.world",
)

SEVERITY_RANK = {"Info": 0, "Warning": 1, "Critical": 2}


@dataclass(frozen=True)
class CheckResult:
    event: dict[str, Any]
    latency_ms: int | None = None


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso_timestamp(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def bounded(value: str, maximum: int) -> str:
    text = " ".join(value.split())
    return text[:maximum]


def make_event(
    fingerprint: str,
    code: str,
    source: str,
    severity: str,
    active: bool,
    title: str,
    summary: str,
    observed_at: datetime,
) -> dict[str, Any]:
    return {
        "fingerprint": fingerprint,
        "code": code,
        "source": source,
        "severity": severity,
        "active": active,
        "title": bounded(title, 120),
        "summary": bounded(summary, 500),
        "observedAt": iso_timestamp(observed_at),
    }


def check_http(
    fingerprint: str,
    code: str,
    source: str,
    title: str,
    url: str,
    expected_statuses: tuple[int, ...],
    observed_at: datetime,
    timeout_seconds: float,
) -> CheckResult:
    started = time.monotonic()
    status: int | None = None
    failure = ""
    try:
        request = urllib.request.Request(
            url,
            headers={"User-Agent": USER_AGENT},
        )
        with urllib.request.urlopen(
            request,
            timeout=timeout_seconds,
        ) as response:
            status = response.status
            response.read(4096)
    except urllib.error.HTTPError as error:
        status = error.code
        error.read(4096)
    except Exception as error:  # Network exceptions are mapped to a fixed class.
        failure = type(error).__name__
    elapsed_ms = max(0, round((time.monotonic() - started) * 1000))

    active = bool(failure) or status not in expected_statuses
    if failure:
        summary = f"{url} 请求失败，分类 {failure}。"
    else:
        summary = (
            f"{url} 返回 HTTP {status}，预期 "
            f"{'/'.join(str(item) for item in expected_statuses)}，"
            f"耗时 {elapsed_ms} ms。"
        )
    if not active:
        summary = f"{url} 检查正常，HTTP {status}，耗时 {elapsed_ms} ms。"

    return CheckResult(
        make_event(
            fingerprint,
            code,
            source,
            "Critical" if active else "Info",
            active,
            title,
            summary,
            observed_at,
        ),
        elapsed_ms if not failure else None,
    )


def certificate_event(
    host: str,
    observed_at: datetime,
    timeout_seconds: float,
) -> dict[str, Any]:
    fingerprint = f"certificate:{host}"
    try:
        context = ssl.create_default_context()
        with socket.create_connection(
            (host, 443),
            timeout=timeout_seconds,
        ) as connection:
            with context.wrap_socket(
                connection,
                server_hostname=host,
            ) as tls:
                certificate = tls.getpeercert()
        expires_at = datetime.strptime(
            certificate["notAfter"],
            "%b %d %H:%M:%S %Y %Z",
        ).replace(tzinfo=timezone.utc)
        remaining_days = int(
            (expires_at - observed_at).total_seconds() // 86400
        )
        if remaining_days <= 7:
            severity = "Critical"
            active = True
        elif remaining_days <= 30:
            severity = "Warning"
            active = True
        else:
            severity = "Info"
            active = False
        return make_event(
            fingerprint,
            "Certificate.Expiry",
            "Certificate",
            severity,
            active,
            f"{host} HTTPS 证书接近到期",
            (
                f"{host} 证书到期时间 {iso_timestamp(expires_at)}，"
                f"剩余 {remaining_days} 天。"
            ),
            observed_at,
        )
    except Exception as error:
        return make_event(
            fingerprint,
            "Certificate.Validation",
            "Certificate",
            "Critical",
            True,
            f"{host} HTTPS 证书验证失败",
            f"{host} TLS 检查失败，分类 {type(error).__name__}。",
            observed_at,
        )


def latency_event(
    latency_ms: int | None,
    observed_at: datetime,
) -> dict[str, Any]:
    active = latency_ms is not None and latency_ms >= 2000
    severity = (
        "Critical"
        if latency_ms is not None and latency_ms >= 5000
        else "Warning" if active else "Info"
    )
    return make_event(
        "platform:api-public-latency",
        "Api.PublicLatency",
        "Api",
        severity,
        active,
        "启动器 API 公网延迟升高",
        (
            f"公网 /healthz 耗时 {latency_ms} ms。"
            if latency_ms is not None
            else "公网 /healthz 延迟样本不可用。"
        ),
        observed_at,
    )


def backup_event(
    receipt_path: Path,
    failure_path: Path,
    observed_at: datetime,
    *,
    fingerprint: str = "backup:database-offsite",
    code: str = "Backup.DatabaseOffsite",
    subject: str = "异地数据库备份",
) -> dict[str, Any]:
    if failure_path.exists():
        try:
            failure = json.loads(failure_path.read_text(encoding="utf-8"))
            failed_at = bounded(str(failure.get("failedAt", "unknown")), 40)
            exit_code = int(failure.get("exitCode", -1))
            summary = (
                f"最近一次{subject}失败，时间 {failed_at}，"
                f"退出码 {exit_code}。"
            )
        except Exception as error:
            summary = (
                f"{subject}失败标记无法读取，分类 "
                f"{type(error).__name__}。"
            )
        return make_event(
            fingerprint,
            code,
            "Infrastructure",
            "Critical",
            True,
            f"{subject}失败",
            summary,
            observed_at,
        )

    if not receipt_path.exists():
        return make_event(
            fingerprint,
            code,
            "Infrastructure",
            "Warning",
            True,
            f"尚无成功的{subject}",
            f"未找到成功凭据 {receipt_path}。",
            observed_at,
        )

    try:
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        completed_text = str(receipt["completedAt"])
        completed_at = datetime.fromisoformat(
            completed_text.replace("Z", "+00:00")
        ).astimezone(timezone.utc)
        object_key = bounded(str(receipt["objectKey"]), 240)
        age_hours = (observed_at - completed_at).total_seconds() / 3600
        if age_hours < -1:
            raise ValueError("backup receipt timestamp is in the future")
    except Exception as error:
        return make_event(
            fingerprint,
            code,
            "Infrastructure",
            "Critical",
            True,
            f"{subject}凭据无效",
            f"{receipt_path} 无法验证，分类 {type(error).__name__}。",
            observed_at,
        )

    active = age_hours >= 30
    severity = "Critical" if age_hours >= 48 else "Warning" if active else "Info"
    return make_event(
        fingerprint,
        code,
        "Infrastructure",
        severity,
        active,
        f"{subject}已经过期",
        (
            f"最近成功备份 {object_key}，完成于 "
            f"{iso_timestamp(completed_at)}，距今 {age_hours:.1f} 小时。"
        ),
        observed_at,
    )


def post_event(
    api_base_url: str,
    token: str,
    event: dict[str, Any],
    timeout_seconds: float,
) -> bool:
    request = urllib.request.Request(
        f"{api_base_url.rstrip('/')}/v1/internal/operational-alerts/events",
        data=json.dumps(event, ensure_ascii=False).encode("utf-8"),
        method="POST",
        headers={
            "Accept": "application/json",
            "Content-Type": "application/json",
            "X-Hechao-Monitor-Token": token,
            "User-Agent": USER_AGENT,
        },
    )
    try:
        with urllib.request.urlopen(
            request,
            timeout=timeout_seconds,
        ) as response:
            response.read(4096)
            return response.status == 202
    except Exception:
        return False


def fetch_active_alerts(
    api_base_url: str,
    token: str,
    timeout_seconds: float,
) -> dict[str, dict[str, Any]] | None:
    request = urllib.request.Request(
        f"{api_base_url.rstrip('/')}/v1/internal/operational-alerts/active",
        headers={
            "Accept": "application/json",
            "X-Hechao-Monitor-Token": token,
            "User-Agent": USER_AGENT,
        },
    )
    try:
        with urllib.request.urlopen(
            request,
            timeout=timeout_seconds,
        ) as response:
            payload = json.load(response)
        return {
            item["fingerprint"]: {
                "fingerprint": item["fingerprint"],
                "code": item["code"],
                "source": item["source"],
                "severity": item["severity"],
                "title": item["title"],
                "summary": item["summary"],
            }
            for item in payload.get("alerts", [])
        }
    except Exception:
        return None


def read_state(path: Path) -> dict[str, dict[str, Any]]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        alerts = payload.get("alerts", {})
        return alerts if isinstance(alerts, dict) else {}
    except (OSError, ValueError):
        return {}


def write_state(path: Path, alerts: dict[str, dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "updatedAt": iso_timestamp(utc_now()),
        "alerts": alerts,
    }
    descriptor, temporary = tempfile.mkstemp(
        prefix=".state-",
        suffix=".json",
        dir=path.parent,
    )
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, 0o600)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def transitions(
    previous: dict[str, dict[str, Any]],
    current: dict[str, dict[str, Any]],
) -> list[tuple[str, dict[str, Any]]]:
    result: list[tuple[str, dict[str, Any]]] = []
    for fingerprint in sorted(current):
        alert = current[fingerprint]
        old = previous.get(fingerprint)
        if old is None:
            result.append(("触发", alert))
        elif old.get("severity") != alert.get("severity"):
            result.append(("级别变更", alert))
    for fingerprint in sorted(set(previous) - set(current)):
        result.append(("恢复", previous[fingerprint]))
    return result


def parse_env_file(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        value = value.strip()
        if (
            len(value) >= 2
            and value[0] == value[-1]
            and value[0] in ("'", '"')
        ):
            value = value[1:-1]
        values[key.strip()] = value
    return values


def send_email(
    changes: list[tuple[str, dict[str, Any]]],
    smtp_env_path: Path,
    configured_recipient: str,
) -> bool:
    if not changes:
        return True
    try:
        values = parse_env_file(smtp_env_path)
        host = values["SMTP_HOST"]
        port = int(values["SMTP_PORT"])
        username = values["SMTP_USER"]
        password = values["SMTP_PASS"]
        sender = values["SMTP_FROM"]
        recipient = configured_recipient or username

        critical = sum(
            1
            for action, alert in changes
            if action != "恢复" and alert.get("severity") == "Critical"
        )
        message = EmailMessage()
        message["Subject"] = (
            f"[赫朝告警] {len(changes)} 项状态变化"
            + (f"，{critical} 项严重" if critical else "")
        )
        message["From"] = sender
        message["To"] = recipient
        lines = [
            "赫朝平台巡检检测到以下状态变化：",
            "",
        ]
        for action, alert in changes:
            lines.extend(
                [
                    f"[{action}] [{alert.get('severity', 'Info')}] "
                    f"{alert.get('title', alert.get('fingerprint', '告警'))}",
                    f"来源：{alert.get('source', 'Infrastructure')} / "
                    f"{alert.get('code', 'Unknown')}",
                    f"摘要：{alert.get('summary', '')}",
                    f"指纹：{alert.get('fingerprint', '')}",
                    "",
                ]
            )
        lines.extend(
            [
                "管理入口：https://admin.hechao.world/admin/",
                "此邮件只在触发、级别变化或恢复时发送。",
            ]
        )
        message.set_content("\n".join(lines))

        if port == 465:
            with smtplib.SMTP_SSL(
                host,
                port,
                timeout=15,
                context=ssl.create_default_context(),
            ) as client:
                client.login(username, password)
                client.send_message(message)
        else:
            with smtplib.SMTP(host, port, timeout=15) as client:
                client.ehlo()
                client.starttls(context=ssl.create_default_context())
                client.ehlo()
                client.login(username, password)
                client.send_message(message)
        return True
    except Exception as error:
        log("email_failed", error=type(error).__name__)
        return False


def log(event: str, **fields: Any) -> None:
    payload = {
        "event": event,
        "at": iso_timestamp(utc_now()),
        **fields,
    }
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def run() -> int:
    observed_at = utc_now()
    timeout_seconds = float(
        os.environ.get("HECHAO_MONITOR_HTTP_TIMEOUT_SECONDS", "10")
    )
    api_base_url = os.environ.get(
        "HECHAO_MONITOR_API_BASE_URL",
        "http://127.0.0.1:8090",
    )
    token = os.environ.get("HECHAO_MONITOR_TOKEN", "")
    state_path = Path(
        os.environ.get(
            "HECHAO_MONITOR_STATE_PATH",
            "/var/lib/hechao-platform-monitor/state.json",
        )
    )
    smtp_env_path = Path(
        os.environ.get(
            "HECHAO_MONITOR_SMTP_ENV_FILE",
            "/home/ecs-user/hechao/.env",
        )
    )
    recipient = os.environ.get("HECHAO_MONITOR_ALERT_TO", "")
    backup_receipt_path = Path(
        os.environ.get(
            "HECHAO_MONITOR_BACKUP_RECEIPT_PATH",
            "/var/lib/hechao-offsite-backup/latest.json",
        )
    )
    backup_failure_path = Path(
        os.environ.get(
            "HECHAO_MONITOR_BACKUP_FAILURE_PATH",
            "/var/lib/hechao-offsite-backup/failure.json",
        )
    )
    platform_backup_receipt_path = Path(
        os.environ.get(
            "HECHAO_MONITOR_PLATFORM_BACKUP_RECEIPT_PATH",
            "/var/lib/hechao-offsite-platform-backup/latest.json",
        )
    )
    platform_backup_failure_path = Path(
        os.environ.get(
            "HECHAO_MONITOR_PLATFORM_BACKUP_FAILURE_PATH",
            "/var/lib/hechao-offsite-platform-backup/failure.json",
        )
    )
    if len(token) < 32:
        log("configuration_invalid", field="HECHAO_MONITOR_TOKEN")
        return 2

    synthetic_events: list[dict[str, Any]] = []
    api_health_latency: int | None = None
    for check in DEFAULT_CHECKS:
        result = check_http(
            *check,
            observed_at,
            timeout_seconds,
        )
        synthetic_events.append(result.event)
        if result.event["fingerprint"] == "platform:api-health":
            api_health_latency = result.latency_ms
    synthetic_events.append(latency_event(api_health_latency, observed_at))
    synthetic_events.extend(
        certificate_event(host, observed_at, timeout_seconds)
        for host in TLS_HOSTS
    )
    synthetic_events.append(
        backup_event(
            backup_receipt_path,
            backup_failure_path,
            observed_at,
        )
    )
    synthetic_events.append(
        backup_event(
            platform_backup_receipt_path,
            platform_backup_failure_path,
            observed_at,
            fingerprint="backup:platform-data-offsite",
            code="Backup.PlatformDataOffsite",
            subject="论坛与 Sub2API 异地备份",
        )
    )

    posted = sum(
        1
        for event in synthetic_events
        if post_event(api_base_url, token, event, timeout_seconds)
    )
    previous = read_state(state_path)
    active_from_api = fetch_active_alerts(
        api_base_url,
        token,
        timeout_seconds,
    )
    if active_from_api is None:
        current = dict(previous)
        snapshot_available = False
    else:
        current = active_from_api
        snapshot_available = True

    synthetic_fingerprints = {
        event["fingerprint"] for event in synthetic_events
    }
    for fingerprint in synthetic_fingerprints:
        current.pop(fingerprint, None)
    for event in synthetic_events:
        if event["active"]:
            current[event["fingerprint"]] = {
                key: event[key]
                for key in (
                    "fingerprint",
                    "code",
                    "source",
                    "severity",
                    "title",
                    "summary",
                )
            }

    changes = transitions(previous, current)
    delivered = send_email(changes, smtp_env_path, recipient)
    if delivered:
        write_state(state_path, current)
    log(
        "check_complete",
        version=MONITOR_VERSION,
        checks=len(synthetic_events),
        posted=posted,
        active=len(current),
        transitions=len(changes),
        snapshotAvailable=snapshot_available,
        emailDelivered=delivered,
    )
    return 0 if delivered else 1


def self_test() -> int:
    now = datetime(2026, 7, 27, tzinfo=timezone.utc)
    active = make_event(
        "platform:test",
        "Infrastructure.Test",
        "Infrastructure",
        "Warning",
        True,
        "测试告警",
        "测试摘要。",
        now,
    )
    old: dict[str, dict[str, Any]] = {}
    current = {"platform:test": active}
    assert transitions(old, current)[0][0] == "触发"
    escalated = dict(active)
    escalated["severity"] = "Critical"
    assert transitions(current, {"platform:test": escalated})[0][0] == "级别变更"
    assert transitions(current, {})[0][0] == "恢复"
    assert bounded("a  \n b", 10) == "a b"
    with tempfile.TemporaryDirectory() as temporary_directory:
        receipt_path = Path(temporary_directory) / "latest.json"
        failure_path = Path(temporary_directory) / "failure.json"
        receipt_path.write_text(
            json.dumps(
                {
                    "completedAt": iso_timestamp(now),
                    "objectKey": "backups/database/test.hcbackup",
                }
            ),
            encoding="utf-8",
        )
        assert not backup_event(
            receipt_path,
            failure_path,
            now,
        )["active"]
        failure_path.write_text(
            json.dumps(
                {
                    "failedAt": iso_timestamp(now),
                    "exitCode": 1,
                }
            ),
            encoding="utf-8",
        )
        assert backup_event(
            receipt_path,
            failure_path,
            now,
        )["severity"] == "Critical"
        failure_path.unlink()
        platform_event = backup_event(
            receipt_path,
            failure_path,
            now,
            fingerprint="backup:platform-data-offsite",
            code="Backup.PlatformDataOffsite",
            subject="论坛与 Sub2API 异地备份",
        )
        assert platform_event["fingerprint"] == "backup:platform-data-offsite"
        assert platform_event["code"] == "Backup.PlatformDataOffsite"
        assert not platform_event["active"]
    print("PASS: platform monitor self-test")
    return 0


if __name__ == "__main__":
    sys.exit(self_test() if "--self-test" in sys.argv else run())
