const { ReactionRoleConfig } = require('../data/db.js');

async function processPending(client) {
    // Recover work interrupted by a previous process restart.
    await ReactionRoleConfig.updateMany(
        { status: 'processing', updatedAt: { $lt: new Date(Date.now() - 5 * 60 * 1000) } },
        { $set: { status: 'pending' } }
    );

    const pending = await ReactionRoleConfig.find({
        enabled: true,
        status: 'pending',
        mode: 'new-message'
    }).limit(50).select('_id guildId channelId messageContent mappings');

    for (const item of pending) {
        // Claim atomically so two overlapping polling ticks cannot send duplicates.
        const config = await ReactionRoleConfig.findOneAndUpdate(
            { _id: item._id, status: 'pending' },
            { $set: { status: 'processing' } },
            { new: true }
        );
        if (!config) continue;

        try {
            const channel = await client.channels.fetch(config.channelId).catch(() => null);
            if (!channel?.isTextBased() || !channel.guild ||
                channel.guild.id !== config.guildId) {
                config.status = 'failed';
                await config.save();
                continue;
            }

            const message = await channel.send({
                content: String(config.messageContent || '').slice(0, 2000),
                allowedMentions: { parse: [] }
            });

            for (const mapping of config.mappings || []) {
                if (!mapping.emoji) continue;
                await message.react(mapping.emoji).catch(error => {
                    console.warn('[MESAJ ROL REAKSİYON]', mapping.emoji, error.message);
                });
            }

            config.messageId = message.id;
            config.status = 'active';
            await config.save();
            console.log('[MESAJ ROL] Yeni mesaj sistemi etkinleştirildi:', config.guildId, message.id);
        } catch (error) {
            config.status = 'failed';
            await config.save().catch(() => {});
            console.warn('[MESAJ ROL HAZIRLAMA]', config.guildId, error.message);
        }
    }
}

module.exports = {
    name: 'ready',
    once: true,
    async execute(client) {
        try {
            await processPending(client);
        } catch (error) {
            console.warn('[MESAJ ROL] Bekleyen yapılandırmalar okunamadı:', error.message);
        }

        const timer = setInterval(() => {
            processPending(client).catch(error => {
                console.warn('[MESAJ ROL] Bekleyen yapılandırmalar işlenemedi:', error.message);
            });
        }, 30_000);
        timer.unref?.();
    }
};
