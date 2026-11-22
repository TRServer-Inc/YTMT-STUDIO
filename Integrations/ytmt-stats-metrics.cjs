'use strict';

// YTMT-STUDIO integration helper for the YTMT BOT Express server.
// It exposes measurements only; it never returns tokens, environment variables,
// database connection strings, or other secrets.

const os = require('node:os');
const fs = require('node:fs');

const CGROUP_MEMORY_FILES = [
  { used: '/sys/fs/cgroup/memory.current', limit: '/sys/fs/cgroup/memory.max' },
  { used: '/sys/fs/cgroup/memory/memory.usage_in_bytes', limit: '/sys/fs/cgroup/memory/memory.limit_in_bytes' }
];

let previousHostCpu = readHostCpuTimes();
let previousProcessCpu = process.cpuUsage();
let previousProcessTime = process.hrtime.bigint();

function readHostCpuTimes() {
  const cores = os.cpus();
  if (!Array.isArray(cores) || cores.length === 0) return null;

  let total = 0;
  let idle = 0;
  for (const core of cores) {
    const times = core && core.times;
    if (!times) continue;
    total += Number(times.user || 0) + Number(times.nice || 0) + Number(times.sys || 0) +
      Number(times.irq || 0) + Number(times.idle || 0);
    idle += Number(times.idle || 0);
  }
  return total > 0 ? { total, idle } : null;
}

function readCgroupNumber(filePath) {
  try {
    const value = fs.readFileSync(filePath, 'utf8').trim();
    if (!value || value === 'max') return null;
    const parsed = Number(value);
    // Very large cgroup v1 values usually mean "unlimited".
    if (!Number.isFinite(parsed) || parsed < 0 || parsed > Number.MAX_SAFE_INTEGER) return null;
    return Math.floor(parsed);
  } catch {
    return null;
  }
}

function readContainerMemory() {
  for (const pair of CGROUP_MEMORY_FILES) {
    const used = readCgroupNumber(pair.used);
    const limit = readCgroupNumber(pair.limit);
    if (used !== null || limit !== null) {
      return { usedBytes: used, limitBytes: limit };
    }
  }
  return { usedBytes: null, limitBytes: null };
}

function finitePercent(value) {
  return Number.isFinite(value) ? Math.max(0, Math.min(100, Number(value.toFixed(2)))) : null;
}

function buildStatsPayload(client, options = {}) {
  const now = process.hrtime.bigint();
  const elapsedMicroseconds = Number(now - previousProcessTime) / 1000;
  const processCpuDelta = process.cpuUsage(previousProcessCpu);
  const processCpuMicroseconds = processCpuDelta.user + processCpuDelta.system;
  const cpuCount = typeof os.availableParallelism === 'function'
    ? Math.max(1, os.availableParallelism())
    : Math.max(1, os.cpus().length);

  const processCpuPercent = elapsedMicroseconds > 0
    ? finitePercent((processCpuMicroseconds / elapsedMicroseconds / cpuCount) * 100)
    : null;

  previousProcessCpu = process.cpuUsage();
  previousProcessTime = now;

  const currentHostCpu = readHostCpuTimes();
  let hostCpuPercent = null;
  if (currentHostCpu && previousHostCpu) {
    const totalDelta = currentHostCpu.total - previousHostCpu.total;
    const idleDelta = currentHostCpu.idle - previousHostCpu.idle;
    if (totalDelta > 0) hostCpuPercent = finitePercent((1 - idleDelta / totalDelta) * 100);
  }
  previousHostCpu = currentHostCpu;

  const memory = process.memoryUsage();
  const container = readContainerMemory();
  const containerMemoryPercent = container.usedBytes !== null && container.limitBytes > 0
    ? finitePercent((container.usedBytes / container.limitBytes) * 100)
    : null;

  const ping = Number(client?.ws?.ping);
  const commandCount = Number.isInteger(options.commandCount)
    ? options.commandCount
    : Number.isInteger(client?.commands?.size)
      ? client.commands.size
      : null;

  return {
    discordConnected: typeof client?.isReady === 'function' ? client.isReady() : null,
    uptimeSeconds: process.uptime(),
    websocketPingMs: Number.isFinite(ping) && ping >= 0 ? ping : null,

    rssBytes: memory.rss,
    heapUsedBytes: memory.heapUsed,
    heapTotalBytes: memory.heapTotal,
    externalBytes: memory.external,
    arrayBuffersBytes: memory.arrayBuffers,

    containerMemoryUsedBytes: container.usedBytes,
    containerMemoryLimitBytes: container.limitBytes,
    containerMemoryPercent,

    processCpuPercent,
    hostCpuPercent,
    loadAverage1m: Number.isFinite(os.loadavg?.()[0]) ? os.loadavg()[0] : null,
    cpuCoreCount: cpuCount,
    processId: process.pid,

    commandCount,
    nodeVersion: process.version,
    platform: process.platform,
    architecture: process.arch
  };
}

module.exports = { buildStatsPayload };