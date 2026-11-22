const { ReactionRoleConfig } = require('../data/db.js');

function matchesEmoji(configured, emoji) {
    const value = String(configured || '').trim();
    const reactionId = emoji?.id || '';
    const reactionName = emoji?.name || '';
    const customId = value.match(/:(\d+)>$/)?.[1];
    if (customId && reactionId) return customId === reactionId;
    return value === reactionId || value === reactionName || value === emoji?.toString();
}

module.exports = {
    name: 'messageReactionAdd',
    async execute(reaction, user) {
        if (user?.bot) return;
        try {
            if (reaction.partial) reaction = await reaction.fetch();
            if (reaction.message?.partial) await reaction.message.fetch();
            const guild = reaction.message?.guild;
            if (!guild) return;

            const config = await ReactionRoleConfig.findOne({
                guildId: guild.id,
                messageId: reaction.message.id,
                enabled: true,
                status: 'active'
            }).lean();
            if (!config) return;

            const mapping = (config.mappings || []).find(item =>
                matchesEmoji(item.emoji, reaction.emoji));
            if (!mapping?.roleId) return;

            const member = guild.members.cache.get(user.id)
                || await guild.members.fetch(user.id).catch(() => null);
            if (!member || member.user.bot) return;

            const role = guild.roles.cache.get(mapping.roleId)
                || await guild.roles.fetch(mapping.roleId).catch(() => null);
            if (!role || !role.editable || member.roles.cache.has(role.id)) return;

            await member.roles.add(role, 'YTMT-BOT mesaj rol tepkisi');
        } catch (error) {
            console.warn('[MESAJ ROL EKLEME]', error.message);
        }
    }
};
