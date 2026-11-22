const { EmbedBuilder } = require('discord.js');
const { Hgbb, GuildConfig } = require('../data/db.js');

const LEGACY_WELCOME_CHANNEL_ID = '1513121681785225438';

module.exports = {
    name: 'guildMemberAdd',
    async execute(member) {
        if (member.user?.bot) return;
        try {
            let welcomeSettings = null;
            let guildConfig = null;
            try {
                [welcomeSettings, guildConfig] = await Promise.all([
                    Hgbb.findOne({ guildId: member.guild.id }).lean(),
                    GuildConfig.findOne({ guildId: member.guild.id }).lean()
                ]);
            } catch (databaseError) {
                console.warn('[GUILD MEMBER ADD] MongoDB ayarları okunamadı; eski karşılama kanalı kullanılacak:', databaseError.message);
            }

            // Multi-role configuration takes priority. Keep autoRoleId as a
            // backward-compatible fallback for existing guild documents.
            const configuredRoleIds = Array.isArray(guildConfig?.autoRoleIds)
                ? guildConfig.autoRoleIds.filter(Boolean)
                : [];
            const autoRoleIds = [...new Set(
                configuredRoleIds.length > 0
                    ? configuredRoleIds
                    : (guildConfig?.autoRoleId ? [guildConfig.autoRoleId] : [])
            )];

            for (const roleId of autoRoleIds) {
                const autoRole = member.guild.roles.cache.get(roleId)
                    || await member.guild.roles.fetch(roleId).catch(() => null);
                if (!autoRole) {
                    console.warn('[OTO-ROL] Yapılandırılan rol bulunamadı:', member.guild.id, roleId);
                    continue;
                }
                if (!autoRole.editable) {
                    console.warn('[OTO-ROL] Bot rol hiyerarşisi bu role izin vermiyor:', member.guild.id, roleId);
                    continue;
                }
                if (!member.roles.cache.has(autoRole.id)) {
                    await member.roles.add(autoRole, 'YTMT-BOT çoklu otomatik rol sistemi')
                        .catch(error => console.warn('[OTO-ROL] Rol verilemedi:', roleId, error.message));
                }
            }

            // Eski kayıt sistemiyle uyumluluk: Kayıtsız rolü hâlâ kullanılabilir.
            const kayitsizRolu = member.guild.roles.cache.find(role => role.name === 'Kayıtsız');
            if (kayitsizRolu && kayitsizRolu.editable && !member.roles.cache.has(kayitsizRolu.id)) {
                await member.roles.add(kayitsizRolu, 'Kayıt sistemi başlangıç rolü').catch(error => {
                    console.warn('[KAYITSIZ ROL] Rol verilemedi:', error.message);
                });
            }

            if (guildConfig?.welcomeEnabled === false) return;

            const channelId = guildConfig?.welcomeChannelId
                || welcomeSettings?.channelId
                || LEGACY_WELCOME_CHANNEL_ID;
            const channel = member.guild.channels.cache.get(channelId)
                || await member.guild.channels.fetch(channelId).catch(() => null);
            if (!channel || !channel.isTextBased()) return;

            const defaultMessage = 'Hoş geldin {user}! Seninle birlikte sunucumuz {memberCount} kişi oldu.';
            const welcomeTemplate = guildConfig?.welcomeMessage || defaultMessage;
            const welcomeContent = welcomeTemplate
                .replaceAll('{user}', member.toString())
                .replaceAll('{username}', member.user.username)
                .replaceAll('{server}', member.guild.name)
                .replaceAll('{memberCount}', String(member.guild.memberCount));

            const embed = new EmbedBuilder()
                .setColor(0x22C55E)
                .setTitle('🎉 Aramıza yeni biri katıldı!')
                .setDescription(welcomeContent)
                .setThumbnail(member.user.displayAvatarURL({ size: 256 }))
                .setFooter({ text: member.guild.name + ' • Hoş geldin sistemi' })
                .setTimestamp();

            await channel.send({
                content: welcomeContent,
                embeds: [embed],
                allowedMentions: { users: [member.id] }
            });
        } catch (error) {
            console.error('[GUILD MEMBER ADD HATA]', member.guild.id, error);
        }
    }
};
