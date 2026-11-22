import crypto from 'node:crypto';
import express from 'express';
import helmet from 'helmet';
import argon2 from 'argon2';
import pg from 'pg';
import { rateLimit } from 'express-rate-limit';

const { Pool } = pg;

const PORT = Number(process.env.PORT || 3000);
const ACCESS_TTL_MINUTES = 15;
const SESSION_TTL_DAYS = 30;
const requestedCacheSeconds = Number(process.env.GITHUB_CACHE_SECONDS || 15);
const GITHUB_CACHE_SECONDS = Number.isFinite(requestedCacheSeconds)
  ? Math.max(5, Math.min(60, requestedCacheSeconds))
  : 15;
const MAX_BODY_BYTES = '16kb';
const USERNAME_PATTERN = /^[a-zA-Z0-9._-]{3,32}$/;
const ALLOWED_ACCOUNT_ROLES = new Set(['owner', 'admin', 'user']);
const DUMMY_PASSWORD_HASH = '$argon2id$v=19$m=65536,t=3,p=4$Lnc/CTfWajPvgY0FwiXNyQ$C6KvMqPqo/JtAnZfHQ8CU808sW+pfr1/gtnP5u2PEyU';

function requiredEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`Missing required environment variable: ${name}`);
  return value;
}

const config = {
  databaseUrl: requiredEnv('DATABASE_URL'),
  githubToken: requiredEnv('GITHUB_TOKEN'),
  githubOwner: requiredEnv('GITHUB_OWNER'),
  githubRepo: requiredEnv('GITHUB_REPO'),
  githubBranch: process.env.GITHUB_BRANCH?.trim() || 'main',
  githubAccountsPath: process.env.GITHUB_ACCOUNTS_PATH?.trim() || 'auth/accounts.json',
  databaseSsl: String(process.env.DATABASE_SSL || 'false').toLowerCase() === 'true'
};

const pool = new Pool({
  connectionString: config.databaseUrl,
  ssl: config.databaseSsl ? { rejectUnauthorized: false } : undefined,
  max: 5,
  idleTimeoutMillis: 30_000,
  connectionTimeoutMillis: 8_000
});

pool.on('error', (error) => {
  console.error('[auth] PostgreSQL pool error:', error.message);
});

let accountsCache = {
  accounts: null,
  etag: null,
  expiresAt: 0
};

const app = express();
app.disable('x-powered-by');
app.set('trust proxy', 1);
app.use(helmet());
app.use(express.json({ limit: MAX_BODY_BYTES, strict: true }));
app.use('/auth', (_req, res, next) => {
  res.setHeader('Cache-Control', 'no-store, max-age=0');
  res.setHeader('Pragma', 'no-cache');
  next();
});

function normalizeUsername(value) {
  return typeof value === 'string' ? value.trim().toLowerCase() : '';
}

function sha256(value) {
  return crypto.createHash('sha256').update(value, 'utf8').digest('hex');
}

function createOpaqueToken() {
  return crypto.randomBytes(32).toString('base64url');
}

function sendAuthError(res, status = 401) {
  return res.status(status).json({
    ok: false,
    error: status === 503
      ? 'Kimlik doğrulama servisi şu anda kullanılamıyor.'
      : 'Oturum doğrulanamadı. Yeniden giriş yapın.'
  });
}

function parseAccountsFile(raw) {
  let parsed;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error('GitHub accounts file is not valid JSON.');
  }

  if (!parsed || !Array.isArray(parsed.accounts)) {
    throw new Error('GitHub accounts file must contain an accounts array.');
  }

  const seenIds = new Set();
  const seenUsernames = new Set();
  const accounts = parsed.accounts.map((account) => {
    if (!account || typeof account !== 'object') {
      throw new Error('Invalid account record.');
    }

    const id = typeof account.id === 'string' ? account.id.trim() : '';
    const username = typeof account.username === 'string' ? account.username.trim() : '';
    const normalized = normalizeUsername(username);
    const passwordHash = typeof account.passwordHash === 'string' ? account.passwordHash : '';
    const displayName = typeof account.displayName === 'string' && account.displayName.trim()
      ? account.displayName.trim().slice(0, 64)
      : username;
    const sessionVersion = Number.isInteger(account.sessionVersion) && account.sessionVersion >= 1
      ? account.sessionVersion
      : 1;
    // Older account files without a role remain sign-in compatible as regular users.
    const role = typeof account.role === 'string' && account.role.trim()
      ? account.role.trim().toLowerCase()
      : 'user';

    if (!id || id.length > 80 || !USERNAME_PATTERN.test(username)) {
      throw new Error('Account id or username is invalid.');
    }
    if (!passwordHash.startsWith('$argon2id$') || passwordHash.length > 512) {
      throw new Error(`Account ${username} does not have an Argon2id hash.`);
    }
    if (!ALLOWED_ACCOUNT_ROLES.has(role)) {
      throw new Error(`Account ${username} has an unsupported role. Use owner, admin, or user.`);
    }
    if (seenIds.has(id) || seenUsernames.has(normalized)) {
      throw new Error('Duplicate account id or username.');
    }

    seenIds.add(id);
    seenUsernames.add(normalized);

    return {
      id,
      username,
      normalizedUsername: normalized,
      displayName,
      passwordHash,
      role,
      enabled: account.enabled === true,
      sessionVersion
    };
  });

  return accounts;
}

async function fetchAccountsFromGitHub() {
  const now = Date.now();
  if (accountsCache.accounts && accountsCache.expiresAt > now) {
    return accountsCache.accounts;
  }

  const path = config.githubAccountsPath.split('/').map(encodeURIComponent).join('/');
  const url = new URL(
    `https://api.github.com/repos/${encodeURIComponent(config.githubOwner)}/${encodeURIComponent(config.githubRepo)}/contents/${path}`
  );
  url.searchParams.set('ref', config.githubBranch);

  const headers = {
    Authorization: `Bearer ${config.githubToken}`,
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
    'User-Agent': 'YTMT-STUDIO-Auth/1.0'
  };
  if (accountsCache.etag) headers['If-None-Match'] = accountsCache.etag;

  const response = await fetch(url, {
    headers,
    signal: AbortSignal.timeout(8_000)
  });

  if (response.status === 304 && accountsCache.accounts) {
    accountsCache.expiresAt = now + GITHUB_CACHE_SECONDS * 1_000;
    return accountsCache.accounts;
  }

  if (!response.ok) {
    const details = await response.text().catch(() => '');
    console.error('[auth] GitHub accounts fetch failed:', response.status, details.slice(0, 300));
    throw new Error('GitHub account source is unavailable.');
  }

  const file = await response.json();
  if (!file || file.type !== 'file' || typeof file.content !== 'string' || file.encoding !== 'base64') {
    throw new Error('GitHub account source has an unsupported format.');
  }

  const raw = Buffer.from(file.content.replace(/\s/g, ''), 'base64').toString('utf8');
  const accounts = parseAccountsFile(raw);

  accountsCache = {
    accounts,
    etag: response.headers.get('etag'),
    expiresAt: now + GITHUB_CACHE_SECONDS * 1_000
  };

  return accounts;
}

async function logEvent(eventType, accountId = null) {
  try {
    await pool.query(
      'INSERT INTO auth_events (account_id, event_type) VALUES ($1, $2)',
      [accountId, eventType]
    );
  } catch (error) {
    console.error('[auth] Event log write failed:', error.message);
  }
}

async function ensureDatabase() {
  const sql = `
    CREATE TABLE IF NOT EXISTS auth_sessions (
      session_id UUID PRIMARY KEY,
      account_id TEXT NOT NULL,
      username TEXT NOT NULL,
      session_version INTEGER NOT NULL,
      refresh_token_hash CHAR(64) NOT NULL UNIQUE,
      access_token_hash CHAR(64) NOT NULL UNIQUE,
      access_expires_at TIMESTAMPTZ NOT NULL,
      expires_at TIMESTAMPTZ NOT NULL,
      revoked_at TIMESTAMPTZ NULL,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      last_used_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
    CREATE INDEX IF NOT EXISTS ix_auth_sessions_account_active
      ON auth_sessions (account_id, expires_at) WHERE revoked_at IS NULL;
    CREATE INDEX IF NOT EXISTS ix_auth_sessions_expiry ON auth_sessions (expires_at);
    CREATE TABLE IF NOT EXISTS auth_events (
      event_id BIGSERIAL PRIMARY KEY,
      account_id TEXT NULL,
      event_type TEXT NOT NULL,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
    CREATE INDEX IF NOT EXISTS ix_auth_events_created_at ON auth_events (created_at DESC);
  `;
  await pool.query(sql);
}

const loginIpLimiter = rateLimit({
  windowMs: 15 * 60 * 1_000,
  limit: 20,
  standardHeaders: 'draft-8',
  legacyHeaders: false,
  message: { ok: false, error: 'Çok fazla giriş denemesi. Bir süre sonra tekrar deneyin.' }
});

const refreshLimiter = rateLimit({
  windowMs: 15 * 60 * 1_000,
  limit: 40,
  standardHeaders: 'draft-8',
  legacyHeaders: false,
  message: { ok: false, error: 'Çok fazla oturum yenileme isteği. Daha sonra tekrar deneyin.' }
});

const loginUsernameLimiter = rateLimit({
  windowMs: 15 * 60 * 1_000,
  limit: 5,
  keyGenerator: (req) => `username:${sha256(normalizeUsername(req.body?.username))}`,
  standardHeaders: 'draft-8',
  legacyHeaders: false,
  skipSuccessfulRequests: true,
  message: { ok: false, error: 'Çok fazla giriş denemesi. Bir süre sonra tekrar deneyin.' }
});

app.get('/health', async (_req, res) => {
  try {
    await pool.query('SELECT 1');
    res.status(200).json({ ok: true, service: 'ytmt-studio-auth', database: 'connected' });
  } catch {
    res.status(503).json({ ok: false, service: 'ytmt-studio-auth', database: 'unavailable' });
  }
});

app.post('/auth/login', loginIpLimiter, loginUsernameLimiter, async (req, res) => {
  const username = normalizeUsername(req.body?.username);
  const password = typeof req.body?.password === 'string' ? req.body.password : '';
  const rememberMe = req.body?.rememberMe !== false;

  if (!USERNAME_PATTERN.test(username) || password.length < 1 || password.length > 256) {
    await logEvent('login_failed');
    return res.status(401).json({ ok: false, error: 'Kullanıcı adı veya şifre hatalı.' });
  }

  try {
    const accounts = await fetchAccountsFromGitHub();
    const account = accounts.find((item) => item.normalizedUsername === username);
    const hashToVerify = account?.passwordHash || DUMMY_PASSWORD_HASH;
    const passwordValid = await argon2.verify(hashToVerify, password).catch(() => false);

    if (!account || !account.enabled || !passwordValid) {
      await logEvent('login_failed', account?.id || null);
      return res.status(401).json({ ok: false, error: 'Kullanıcı adı veya şifre hatalı.' });
    }

    const accessToken = createOpaqueToken();
    const refreshToken = createOpaqueToken();
    const now = new Date();
    const sessionDurationMs = (rememberMe ? SESSION_TTL_DAYS : 0.5) * 24 * 60 * 60 * 1_000;
    const expiresAt = new Date(now.getTime() + sessionDurationMs);
    const accessExpiresAt = new Date(Math.min(
      now.getTime() + ACCESS_TTL_MINUTES * 60 * 1_000,
      expiresAt.getTime()
    ));
    const sessionId = crypto.randomUUID();

    await pool.query(
      `INSERT INTO auth_sessions
        (session_id, account_id, username, session_version, refresh_token_hash,
         access_token_hash, access_expires_at, expires_at)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
      [sessionId, account.id, account.username, account.sessionVersion,
       sha256(refreshToken), sha256(accessToken), accessExpiresAt, expiresAt]
    );

    await logEvent('login_succeeded', account.id);
    return res.status(200).json({
      ok: true,
      accessToken,
      refreshToken,
      accessExpiresAt: accessExpiresAt.toISOString(),
      expiresAt: expiresAt.toISOString(),
      user: { id: account.id, username: account.username, displayName: account.displayName, role: account.role }
    });
  } catch (error) {
    console.error('[auth] Login failed:', error.message);
    return res.status(503).json({
      ok: false,
      error: 'Kimlik doğrulama sunucusuna şu anda erişilemiyor.'
    });
  }
});

app.post('/auth/refresh', refreshLimiter, async (req, res) => {
  const refreshToken = typeof req.body?.refreshToken === 'string' ? req.body.refreshToken : '';
  if (refreshToken.length < 32 || refreshToken.length > 256) return sendAuthError(res);

  const client = await pool.connect();
  try {
    await client.query('BEGIN');
    const result = await client.query(
      `SELECT session_id, account_id, username, session_version, expires_at, revoked_at
       FROM auth_sessions WHERE refresh_token_hash = $1 FOR UPDATE`,
      [sha256(refreshToken)]
    );
    const session = result.rows[0];

    if (!session || session.revoked_at || new Date(session.expires_at).getTime() <= Date.now()) {
      await client.query('ROLLBACK');
      return sendAuthError(res);
    }

    const accounts = await fetchAccountsFromGitHub();
    const account = accounts.find((item) => item.id === session.account_id);
    if (!account || !account.enabled || account.sessionVersion !== session.session_version) {
      await client.query(
        'UPDATE auth_sessions SET revoked_at = NOW() WHERE session_id = $1',
        [session.session_id]
      );
      await client.query('COMMIT');
      await logEvent('session_revoked_account_changed', session.account_id);
      return sendAuthError(res);
    }

    const accessToken = createOpaqueToken();
    const expiresAt = new Date(session.expires_at);
    const accessExpiresAt = new Date(Math.min(
      Date.now() + ACCESS_TTL_MINUTES * 60 * 1_000,
      expiresAt.getTime()
    ));

    await client.query(
      `UPDATE auth_sessions
       SET access_token_hash = $1, access_expires_at = $2, last_used_at = NOW()
       WHERE session_id = $3`,
      [sha256(accessToken), accessExpiresAt, session.session_id]
    );
    await client.query('COMMIT');

    return res.status(200).json({
      ok: true,
      accessToken,
      refreshToken,
      accessExpiresAt: accessExpiresAt.toISOString(),
      expiresAt: expiresAt.toISOString(),
      user: { id: account.id, username: account.username, displayName: account.displayName, role: account.role }
    });
  } catch (error) {
    await client.query('ROLLBACK').catch(() => {});
    console.error('[auth] Refresh failed:', error.message);
    return res.status(503).json({ ok: false, error: 'Oturum şu anda yenilenemiyor.' });
  } finally {
    client.release();
  }
});

async function findAuthorizedSession(req) {
  const header = req.headers.authorization || '';
  const match = /^Bearer ([A-Za-z0-9_-]{40,128})$/.exec(header);
  if (!match) return null;

  const result = await pool.query(
    `SELECT session_id, account_id, username, session_version, expires_at, revoked_at,
            access_expires_at
     FROM auth_sessions WHERE access_token_hash = $1`,
    [sha256(match[1])]
  );
  const session = result.rows[0];
  if (!session || session.revoked_at ||
      new Date(session.expires_at).getTime() <= Date.now() ||
      new Date(session.access_expires_at).getTime() <= Date.now()) {
    return null;
  }

  const accounts = await fetchAccountsFromGitHub();
  const account = accounts.find((item) => item.id === session.account_id);
  if (!account || !account.enabled || account.sessionVersion !== session.session_version) {
    await pool.query(
      'UPDATE auth_sessions SET revoked_at = NOW() WHERE session_id = $1',
      [session.session_id]
    );
    await logEvent('session_revoked_account_changed', session.account_id);
    return null;
  }

  await pool.query('UPDATE auth_sessions SET last_used_at = NOW() WHERE session_id = $1', [session.session_id]);
  return { session, account };
}

app.get('/auth/me', async (req, res) => {
  try {
    const authorized = await findAuthorizedSession(req);
    if (!authorized) return sendAuthError(res);
    return res.status(200).json({
      ok: true,
      user: {
        id: authorized.account.id,
        username: authorized.account.username,
        displayName: authorized.account.displayName,
        role: authorized.account.role
      },
      expiresAt: new Date(authorized.session.expires_at).toISOString()
    });
  } catch (error) {
    console.error('[auth] Session validation failed:', error.message);
    return res.status(503).json({ ok: false, error: 'Oturum şu anda doğrulanamıyor.' });
  }
});

app.get('/auth/validate', async (req, res) => {
  try {
    const authorized = await findAuthorizedSession(req);
    if (!authorized) return sendAuthError(res);
    return res.status(200).json({ ok: true, valid: true });
  } catch (error) {
    console.error('[auth] Validate failed:', error.message);
    return res.status(503).json({ ok: false, error: 'Oturum şu anda doğrulanamıyor.' });
  }
});

app.post('/auth/logout', async (req, res) => {
  const refreshToken = typeof req.body?.refreshToken === 'string' ? req.body.refreshToken : '';
  if (refreshToken.length >= 32 && refreshToken.length <= 256) {
    const result = await pool.query(
      `UPDATE auth_sessions SET revoked_at = NOW()
       WHERE refresh_token_hash = $1 AND revoked_at IS NULL
       RETURNING account_id`,
      [sha256(refreshToken)]
    );
    if (result.rows[0]) await logEvent('logout', result.rows[0].account_id);
  }
  return res.status(200).json({ ok: true });
});

app.use((req, res) => {
  // Bilinçli olarak kayıt/signup endpoint'i yoktur.
  if (req.path.startsWith('/auth/register') || req.path.startsWith('/auth/signup')) {
    return res.status(404).json({ ok: false, error: 'Bu özellik mevcut değil.' });
  }
  return res.status(404).json({ ok: false, error: 'Endpoint bulunamadı.' });
});

app.use((error, _req, res, _next) => {
  if (error?.type === 'entity.too.large') {
    return res.status(413).json({ ok: false, error: 'İstek gövdesi çok büyük.' });
  }
  console.error('[auth] Unhandled request error:', error?.message || 'unknown');
  return res.status(500).json({ ok: false, error: 'Beklenmeyen sunucu hatası.' });
});

async function cleanupOldRecords() {
  try {
    await pool.query("DELETE FROM auth_sessions WHERE expires_at < NOW() - INTERVAL '30 days' OR revoked_at < NOW() - INTERVAL '30 days'");
    await pool.query("DELETE FROM auth_events WHERE created_at < NOW() - INTERVAL '180 days'");
  } catch (error) {
    console.error('[auth] Old-record cleanup failed:', error.message);
  }
}

async function start() {
  await ensureDatabase();
  await cleanupOldRecords();
  const cleanupTimer = setInterval(cleanupOldRecords, 6 * 60 * 60 * 1_000);
  cleanupTimer.unref();

  const server = app.listen(PORT, '0.0.0.0', () => {
    console.log(`[auth] YTMT-STUDIO Authentication API listening on port ${PORT}`);
  });

  const shutdown = async (signal) => {
    console.log(`[auth] ${signal} received; shutting down.`);
    clearInterval(cleanupTimer);
    server.close(async () => {
      await pool.end();
      process.exit(0);
    });
    setTimeout(() => process.exit(1), 10_000).unref();
  };

  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));
}

start().catch((error) => {
  console.error('[auth] Startup failed:', error.message);
  process.exit(1);
});