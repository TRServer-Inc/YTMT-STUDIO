const { ChannelType, PermissionFlagsBits } = require('discord.js');

async function verifyGuildManager(req, guildId) {
    const match = /^Bearer ([A-Za-z0-9._~-]{20,4096})$/.exec(req.get('authorization') || '');
    if (!match) return { ok: false, status: 401 };
    try {
        const response = await fetch('https://discord.com/api/v10/users/@me/guilds', {
            headers: { Authorization: `Bearer ${match[1]}` },
            signal: AbortSignal.timeout(8000)
        });
        if (!response.ok) return { ok: false, status: response.status === 401 ? 401 : 503 };
        const guilds = await response.json();
        const guild = Array.isArray(guilds) ? guilds.find(item => item.id === guildId) : null;
        if (!guild) return { ok: false, status: 403 };
        const permissions = BigInt(guild.permissions || '0');
        const administrator = (permissions & (1n << 3n)) !== 0n;
        const manageGuild = (permissions & (1n << 5n)) !== 0n;
        const manageRoles = (permissions & (1n << 28n)) !== 0n;
        return { ok: Boolean(guild.owner || administrator || manageGuild || manageRoles), status: 403 };
    } catch {
        return { ok: false, status: 503 };
    }
}

const SNOWFLAKE = /^\d{17,20}$/;

// Read-only metadata for YTMT-STUDIO. No guild settings or member data are exposed here.
module.exports = function registerStudioRoutes(app, getDiscordClient) {
    app.get('/api/studio/guilds/:guildId/status', async (req, res) => {
        const { guildId } = req.params;
        if (!SNOWFLAKE.test(guildId)) {
            return res.status(400).json({ ok: false, error: 'Geçersiz sunucu kimliği.' });
        }

        const authorization = await verifyGuildManager(req, guildId);
        if (!authorization.ok) {
            return res.status(authorization.status).json({
                ok: false,
                error: authorization.status === 403
                    ? 'Bu Discord sunucusunu yönetme yetkin doğrulanamadı.'
                    : 'Discord oturumu doğrulanamadı.'
            });
        }

        const client = getDiscordClient();
        if (!client?.user) {
            return res.status(503).json({ ok: false, serviceAvailable: false, error: 'Bot henüz hazır değil.' });
        }

        try {
            const guild = await client.guilds.fetch(guildId).catch(() => null);
            if (!guild) {
                return res.status(404).json({ ok: true, installed: false, serviceAvailable: true });
            }

            return res.json({
                ok: true,
                serviceAvailable: true,
                installed: true,
                guild: {
                    id: guild.id,
                    name: guild.name,
                    memberCount: guild.memberCount || 0
                },
                bot: {
                    id: client.user.id,
                    username: client.user.username
                }
            });
        } catch (error) {
            console.error('[STUDIO STATUS]', guildId, error.message);
            return res.status(503).json({ ok: false, serviceAvailable: false, error: 'Sunucu durumu şu anda alınamıyor.' });
        }
    });

    app.get('/api/studio/guilds/:guildId/resources', async (req, res) => {
        const { guildId } = req.params;
        if (!SNOWFLAKE.test(guildId)) {
            return res.status(400).json({ ok: false, error: 'Geçersiz sunucu kimliği.' });
        }

        const authorization = await verifyGuildManager(req, guildId);
        if (!authorization.ok) {
            return res.status(authorization.status).json({
                ok: false,
                error: authorization.status === 403
                    ? 'Bu Discord sunucusunu yönetme yetkin doğrulanamadı.'
                    : 'Discord oturumu doğrulanamadı.'
            });
        }

        const client = getDiscordClient();
        if (!client?.user) {
            return res.status(503).json({ ok: false, error: 'Bot henüz hazır değil.' });
        }

        try {
            const guild = await client.guilds.fetch(guildId).catch(() => null);
            if (!guild) {
                return res.status(404).json({ ok: false, error: 'Bot bu sunucuda bulunmuyor.' });
            }

            const [roles, channels] = await Promise.all([
                guild.roles.fetch(),
                guild.channels.fetch()
            ]);
            const botMember = guild.members.me
                || await guild.members.fetch(client.user.id).catch(() => null);

            return res.json({
                ok: true,
                roles: [...roles.values()]
                    .filter(role => role.id !== guild.id && !role.managed)
                    .map(role => ({
                        id: role.id,
                        name: role.name,
                        position: role.position,
                        color: role.color,
                        managed: role.managed,
                        editable: Boolean(role.editable)
                    }))
                    .sort((a, b) => b.position - a.position),
                channels: [...channels.values()]
                    .filter(channel => channel && (
                        channel.type === ChannelType.GuildText ||
                        channel.type === ChannelType.GuildAnnouncement
                    ))
                    .filter(channel => {
                        const permissions = botMember ? channel.permissionsFor(botMember) : null;
                        return permissions?.has(PermissionFlagsBits.ViewChannel)
                            && permissions?.has(PermissionFlagsBits.SendMessages);
                    })
                    .map(channel => ({
                        id: channel.id,
                        name: channel.name,
                        type: channel.type
                    }))
                    .sort((a, b) => a.name.localeCompare(b.name, 'tr'))
            });
        } catch (error) {
            console.error('[STUDIO RESOURCES]', guildId, error.message);
            return res.status(503).json({ ok: false, error: 'Sunucu kaynakları şu anda alınamıyor.' });
        }
    });
};
