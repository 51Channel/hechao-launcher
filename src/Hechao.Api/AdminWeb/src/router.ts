import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

const routes: RouteRecordRaw[] = [
  { path: "/", redirect: "/servers" },
  { path: "/servers", name: "servers", component: () => import("./views/ServersView.vue"), meta: { title: "服务器目录" } },
  { path: "/users", name: "users", component: () => import("./views/UsersView.vue"), meta: { title: "玩家与权限" } },
  { path: "/profiles", name: "profiles", component: () => import("./views/ProfilesView.vue"), meta: { title: "客户端档案" } },
  { path: "/package-imports", name: "package-imports", component: () => import("./views/PackageImportsView.vue"), meta: { title: "整合包导入" } },
  { path: "/activity-plans", name: "activity-plans", component: () => import("./views/ActivityPlansView.vue"), meta: { title: "活动企划" } },
  { path: "/telemetry", name: "telemetry", component: () => import("./views/TelemetryView.vue"), meta: { title: "运行数据" } },
  { path: "/economy", name: "economy", component: () => import("./views/EconomyView.vue"), meta: { title: "经济监控" } },
  { path: "/runtime", name: "runtime", component: () => import("./views/RuntimeView.vue"), meta: { title: "服务状态" } },
  { path: "/control", name: "control", component: () => import("./views/ControlView.vue"), meta: { title: "服控面板" } },
  { path: "/alerts", name: "alerts", component: () => import("./views/AlertsView.vue"), meta: { title: "告警中心" } },
  { path: "/diagnostics", name: "diagnostics", component: () => import("./views/DiagnosticsView.vue"), meta: { title: "玩家诊断包" } },
  { path: "/audit", name: "audit", component: () => import("./views/AuditView.vue"), meta: { title: "审计记录" } },
  { path: "/:pathMatch(.*)*", redirect: "/servers" }
];

export const router = createRouter({
  history: createWebHistory("/admin/"),
  routes,
  scrollBehavior: () => ({ top: 0 })
});
