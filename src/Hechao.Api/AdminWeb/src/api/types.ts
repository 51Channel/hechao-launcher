export type ServerStatus = "Online" | "Maintenance" | "Closed";
export type ModLoaderKind = "Vanilla" | "Paper" | "NeoForge" | "Fabric" | "Forge";
export type AccessTier = "Member" | "Participant" | "Collaborator" | "Administrator";
export type AdminServerRole = "Player" | "Infrastructure";
export type ReleaseChannel = "Test" | "Gray" | "Production";
export type AccessDecision = "Allow" | "Deny";

export interface AuthenticatedPlayer {
  userId: string;
  minecraftUuid: string;
  minecraftName: string;
  luckPermsPrimaryGroup: string;
  accessTier: AccessTier;
  luckPermsSyncedAt: string | null;
}

export interface AdminSession {
  player: AuthenticatedPlayer;
  mfaConfigured: boolean;
  mfaVerified: boolean;
  expiresAt: string;
}

export interface AdminServer {
  id: string;
  displayName: string;
  shortName: string;
  iconGlyph: string;
  status: ServerStatus;
  maxPlayers: number;
  minecraftVersion: string;
  loader: ModLoaderKind;
  minimumTier: AccessTier;
  clientProfileId: string;
  velocityTarget: string;
  allowsProtocolTranslation: boolean;
  role: AdminServerRole;
  monitoringEnabled: boolean;
  sortOrder: number;
  isVisible: boolean;
  announcement: string;
  opensAt: string | null;
  closesAt: string | null;
  effectiveStatus: ServerStatus;
  revision: number;
  createdAt: string;
  updatedAt: string;
  hasControlTarget: boolean;
  controlTargetFresh: boolean;
  controlReportedOnline: boolean | null;
  controlLastSeenAt: string | null;
}

export interface ProfileChannel {
  channel: ReleaseChannel;
  manifestSha256: string | null;
  version: string | null;
  rolloutPercentage: number;
  revision: number;
  updatedAt: string;
}

export interface ClientProfile {
  id: string;
  displayName: string;
  version: string;
  downloadBytes: number;
  sha256: string;
  publishedAt: string;
  isActive: boolean;
  isArchived: boolean;
  archivedAt: string | null;
  archiveReason: string;
  serverReferenceCount: number;
  canDelete: boolean;
  updatedAt: string;
  revision: number;
  releaseCount: number;
  channels: ProfileChannel[];
}

export interface ProfileRelease {
  profileId: string;
  manifestSha256: string;
  version: string;
  downloadBytes: number;
  fileCount: number;
  minecraftVersion: string;
  javaVersion: string;
  loader: string;
  loaderVersion: string;
  publishedAt: string;
  isPaused: boolean;
  pauseReason: string;
  revision: number;
  createdAt: string;
  createdByDisplayName: string | null;
}

export interface ProfileDetail {
  profile: ClientProfile;
  releases: ProfileRelease[];
}

export interface AdminUser {
  userId: string;
  username: string;
  displayName: string;
  email: string | null;
  minecraftUuid: string | null;
  minecraftName: string | null;
  luckPermsPrimaryGroup: string;
  accessTier: AccessTier;
  luckPermsSyncedAt: string | null;
  isDisabled: boolean;
  isMinecraftIdentityBanned: boolean;
  activeRuleCount: number;
  createdAt: string;
}

export interface DeviceSession {
  sessionId: string;
  createdAt: string;
  lastSeenAt: string;
  refreshExpiresAt: string;
  sourceIp: string | null;
}

export interface TierChange {
  commandId: string;
  userId: string;
  minecraftUuid: string;
  expectedPrimaryGroup: string;
  targetPrimaryGroup: string;
  targetAccessTier: AccessTier;
  status: string;
  reason: string;
  requestedBy: string;
  requestedAt: string;
  claimedBy: string | null;
  claimedAt: string | null;
  claimExpiresAt: string | null;
  attemptCount: number;
  completedAt: string | null;
  observedPrimaryGroup: string | null;
  failureCode: string | null;
}

export interface MinecraftBan {
  minecraftUuid: string;
  reason: string;
  expiresAt: string | null;
  createdBy: string;
  createdByDisplayName: string | null;
  createdAt: string;
  revokedAt: string | null;
  revokedBy: string | null;
  revokedReason: string | null;
  updatedAt: string;
  revision: number;
}

export interface UserSecurity {
  user: AdminUser;
  launcherSessions: DeviceSession[];
  activeAdminSessions: number;
  pendingAdminTickets: number;
  pendingVelocityLaunchGrants: number;
  pendingForumSessionRevocations: number;
  pendingLuckPermsTierChange: TierChange | null;
  minecraftIdentityBan: MinecraftBan | null;
}

export interface AccessRule {
  userId: string;
  serverId: string;
  decision: AccessDecision;
  reason: string;
  expiresAt: string | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
}

export type EffectiveAccessReason =
  | "AllowedByTier" | "AllowedByRule" | "PlayerNotLinked" | "PlayerDisabled"
  | "MinecraftIdentityBanned" | "ServerArchived" | "ServerUnavailable"
  | "DeniedByRule" | "InsufficientTier" | "PermissionDataStale";

export interface AccessPreviewServer {
  serverId: string;
  serverDisplayName: string;
  configuredStatus: ServerStatus;
  effectiveStatus: ServerStatus;
  isVisible: boolean;
  minimumTier: AccessTier;
  allowed: boolean;
  reason: EffectiveAccessReason;
  rule: AccessRule | null;
}

export interface AccessPreview {
  user: AdminUser;
  servers: AccessPreviewServer[];
}

export interface TelemetryOperation {
  attempts: number;
  succeeded: number;
  failed: number;
  canceled: number;
  bytes: number;
  failureRate: number;
}

export interface TelemetrySummary {
  from: string;
  to: string;
  windowHours: number;
  eventCount: number;
  uniqueUsers: number;
  downloads: TelemetryOperation;
  launches: TelemetryOperation;
  launcherVersions: Array<{ launcherVersion: string; users: number }>;
  profileVersions: Array<{ profileId: string; profileVersion: string; users: number; events: number }>;
  failures: Array<{ type: string; failureCode: string; count: number }>;
}

export interface RuntimeBinding {
  serverId: string;
  displayName: string;
  isVisible: boolean;
}

export interface RuntimeTarget {
  velocityTarget: string;
  servers: RuntimeBinding[];
  hasHeartbeat: boolean;
  isFresh: boolean;
  online: boolean;
  onlinePlayers: number;
  maxPlayers: number;
  softwareVersion: string | null;
  protocolVersion: number | null;
  processWorkingSetBytes: number | null;
  processPrivateBytes: number | null;
  processCpuPercent: number | null;
  processStartedAt: string | null;
  diskFreeBytes: number | null;
  diskTotalBytes: number | null;
  tps1m: number | null;
  tps5m: number | null;
  tps15m: number | null;
  msptAverage: number | null;
  gcCollectionTimeMilliseconds: number | null;
  metricsCapturedAt: string | null;
  issues: string[];
  collectorInstance: string | null;
  capturedAt: string | null;
  receivedAt: string | null;
}

export interface RuntimeSummary {
  generatedAt: string;
  freshnessSeconds: number;
  targets: RuntimeTarget[];
  issues: Array<{ issue: string; samples: number; targets: number }>;
}

export type ControlAction = "Start" | "Stop" | "Restart" | "ConsoleCommand" | "ApplySettings" | "DeployPackage" | "DeleteServerFiles";
export type ControlOperationStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "Cancelled";

export interface QuickSettings {
  maxPlayers: number;
  viewDistance: number;
  simulationDistance: number;
  difficulty: string;
  whiteList: boolean;
  initialMemoryMiB: number | null;
  maximumMemoryMiB: number | null;
  maximumAllowedMemoryMiB: number | null;
}

export interface ServerMemoryGuidance {
  hostTotalMemoryMiB: number;
  recommendedMinimumMemoryMiB: number;
  recommendedMaximumMemoryMiB: number;
}

export interface ControlOperation {
  operationId: string;
  serverId: string;
  displayName: string;
  action: ControlAction;
  status: ControlOperationStatus;
  reason: string;
  requestedBy: string;
  requestedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  resultCode: string | null;
  resultMessage: string | null;
  automaticallyStoppingServerIds: string[];
}

export interface ControlTargetSummary {
  serverId: string;
  displayName: string;
  agentId: string;
  conflictGroup: string | null;
  port: number;
  agentConnected: boolean;
  lastSeenAt: string;
  online: boolean;
  processId: number | null;
  settings: QuickSettings | null;
  activeOperation: ControlOperation | null;
  packageDeploymentEnabled: boolean;
  serverDeletionEnabled: boolean;
  serverFilesPresent: boolean;
  deletionCleanupPending: boolean;
  packageDeploymentMemoryGuidance: ServerMemoryGuidance | null;
  deployedPackage: ServerPackageDeploymentIdentity | null;
}

export interface ServerPackageDeploymentIdentity {
  importId: string;
  profileId: string;
  version: string;
}

export interface ControlTarget extends ControlTargetSummary {
  allowedCommandPrefixes: string[];
  consoleTail: string;
  consoleCapturedAt: string | null;
}

export interface ControlOverview {
  generatedAt: string;
  agentFreshnessSeconds: number;
  targets: ControlTargetSummary[];
}

export interface ControlTargetDetail {
  generatedAt: string;
  agentFreshnessSeconds: number;
  target: ControlTarget;
  recentOperations: ControlOperation[];
}

export interface ControlQueueResult {
  operation: ControlOperation;
  automaticallyStoppingServerIds: string[];
}

export interface AlertRecord {
  fingerprint: string;
  code: string;
  source: string;
  severity: "Info" | "Warning" | "Critical";
  status: "Active" | "Resolved";
  title: string;
  summary: string;
  openedAt: string;
  lastSeenAt: string;
  lastTransitionAt: string;
  resolvedAt: string | null;
  observationCount: number;
  acknowledgedAt: string | null;
  acknowledgedBy: string | null;
  revision: number;
}

export interface AlertSummary {
  generatedAt: string;
  activeCount: number;
  criticalCount: number;
  warningCount: number;
  unacknowledgedCount: number;
  alerts: AlertRecord[];
}

export interface DiagnosticUpload {
  uploadId: string;
  userId: string;
  accountDisplayName: string;
  profileId: string;
  launcherVersion: string;
  size: number;
  sha256: string;
  uploadedAt: string;
  expiresAt: string;
}

export interface AuditEntry {
  id: number;
  actorUserId: string | null;
  actorDisplayName: string | null;
  action: string;
  targetType: string;
  targetId: string;
  sourceIp: string | null;
  beforeData: unknown | null;
  afterData: unknown | null;
  createdAt: string;
}

export type PackageImportStatus =
  | "Uploading" | "Uploaded" | "Analyzing" | "AwaitingReview"
  | "QueuedForPublishing" | "PublishingClient" | "QueuedForDeployment"
  | "DeployingServer" | "Finalizing" | "Completed" | "Failed" | "Cancelled";

export type PackageImportIssueSeverity = "Information" | "Warning" | "Blocking";

export interface PackageImportIssue {
  code: string;
  severity: PackageImportIssueSeverity;
  message: string;
  path: string | null;
}

export interface PackageImportMetadata {
  suggestedProfileId: string;
  displayName: string;
  version: string;
  minecraftVersion: string;
  javaMajorVersion: number;
  loader: string;
  loaderVersion: string;
  maximumPlayers: number | null;
  serverLaunchPath: string | null;
}

export interface PackageImportPart {
  sha256: string;
  archiveBytes: number;
  expandedBytes: number;
  fileCount: number;
}

export interface PackageImportFileSample {
  path: string;
  side: string;
  size: number;
  sha256: string;
}

export interface PackageImportAnalysis {
  layout: string;
  metadata: PackageImportMetadata;
  client: PackageImportPart | null;
  server: PackageImportPart | null;
  clientFileCount: number;
  serverFileCount: number;
  sharedFileCount: number;
  fileSamples: PackageImportFileSample[];
  issues: PackageImportIssue[];
}

export interface PackageImportDeploymentPlan {
  profileId: string;
  profileDisplayName: string;
  version: string;
  targetServerId: string;
  preserveWorldData: boolean;
  syncServerCatalog: boolean;
  serverDisplayName: string;
  minimumTier: AccessTier;
  maximumMemoryMiB: number;
  deployServer: boolean;
}

export type PackagePublisherProgressPhase =
  | "WaitingForWorkingSpace" | "DownloadingArchive"
  | "ExtractingArchive" | "BuildingDistribution"
  | "PublishingObjects" | "Finalizing";

export interface PackagePublisherProgress {
  phase: PackagePublisherProgressPhase;
  completedObjects: number;
  totalObjects: number;
  processedBytes: number;
  totalBytes: number;
  sampledAt: string;
}

export interface PackageImportRecord {
  importId: string;
  fileName: string;
  expectedUploadBytes: number;
  uploadedBytes: number;
  sourceSha256: string | null;
  status: PackageImportStatus;
  analysis: PackageImportAnalysis | null;
  plan: PackageImportDeploymentPlan | null;
  manifestSha256: string | null;
  deploymentOperationId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  createdBy: string;
  createdByDisplayName: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  revision: number;
  publisherProgress?: PackagePublisherProgress | null;
}

export interface PackageImportListResponse {
  imports: PackageImportRecord[];
  publisherAgentConnected: boolean;
  publisherAgentLastSeenAt: string | null;
}

export interface PackageUploadAppendResponse {
  importId: string;
  uploadedBytes: number;
  expectedUploadBytes: number;
  complete: boolean;
}

export type ActivityPlanStatus = "Draft" | "Published" | "Archived";
export type UnmanagedActivityScheduleIssue =
  | "MissingPlanStatus"
  | "MissingOpensAt"
  | "MissingClosesAt"
  | "MissingPackageBinding";

export interface ActivityPackage {
  importId: string;
  profileId: string;
  profileDisplayName: string;
  version: string;
  manifestSha256: string;
  minecraftVersion: string;
  loader: ModLoaderKind;
  loaderVersion: string;
  maximumPlayers: number;
  maximumMemoryMiB: number;
  preserveWorldData: boolean;
  productionReady: boolean;
  profileArchived: boolean;
  completedAt: string;
}

export interface ActivityPlan {
  id: string;
  title: string;
  announcement: string;
  opensAt: string;
  closesAt: string;
  maximumPlayers: number;
  minimumTier: AccessTier;
  packageImportId: string;
  profileId: string;
  profileDisplayName: string;
  version: string;
  minecraftVersion: string;
  loader: ModLoaderKind;
  status: ActivityPlanStatus;
  effectiveStatus: ServerStatus;
  productionReady: boolean;
  deploymentMatches: boolean;
  revision: number;
  createdAt: string;
  updatedAt: string;
}

export interface ActivitySlot {
  configured: boolean;
  agentConnected: boolean;
  online: boolean;
  serverFilesPresent: boolean;
  deployedPackage: ServerPackageDeploymentIdentity | null;
  activeOperation: ControlOperation | null;
  memoryGuidance: ServerMemoryGuidance | null;
}

export interface UnmanagedActivitySchedule {
  id: string;
  title: string;
  announcement: string;
  opensAt: string | null;
  closesAt: string | null;
  packageImportId: string | null;
  clientProfileId: string;
  isVisible: boolean;
  configuredStatus: ServerStatus;
  revision: number;
  updatedAt: string;
  issues: UnmanagedActivityScheduleIssue[];
}

export interface ActivityPlanOverview {
  generatedAt: string;
  plans: ActivityPlan[];
  packages: ActivityPackage[];
  slot: ActivitySlot;
  unmanagedSchedules: UnmanagedActivitySchedule[];
}
