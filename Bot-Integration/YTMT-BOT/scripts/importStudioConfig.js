require('dotenv').config();

const fs = require('fs');
const path = require('path');
const mongoose = require('mongoose');
const { connectDatabase, isDatabaseReady } = require('../utils/database.js');
const { GuildConfig, Hgbb, ReactionRoleConfig } = require('../data/db.js');

const SNOWFLAKE = /^\d{17,20}$/;

function validateConfig(config) {
    if (!config || !SNOWFLAKE.test(String(config.guildId || ''))) {
        throw new Error('JSON geçerli bir guildId içermiyor.');
    }
    if (!Array.isArray(config.autoRoleIds) || !Array.isArray(config.reactionRoles)) {
        throw new Error('autoRoleIds ve reactionRoles alanları dizi olmalı.');
    }

    for (const roleId of config.autoRoleIds) {
        if (!SNOWFLAKE.test(String(roleId))) throw new Error('Geçersiz oto rol ID: ' + roleId);
    }

    for (const draft of config.reactionRoles) {
        if (!SNOWFLAKE.test(String(draft.channelId || ''))) {
            throw new Error('Mesaj rol taslağında geçersiz kanal ID.');
        }
        if (!Array.isArray(draft.mappings) || draft.mappings.length === 0) {
            throw new Error('Her mesaj rol taslağı en az bir emoji/rol eşleştirmesi içermeli.');
        }
        if (draft.mode === 'existing-message' && !SNOWFLAKE.test(String(draft.messageId || ''))) {
            throw new Error('Mevcut mesaj yönteminde geçerli messageId gerekli.');
        }
        if (draft.mode === 'new-message' && !String(draft.messageContent || '').trim()) {
            throw new Error('Yeni mesaj yönteminde messageContent gerekli.');
        }
        for (const mapping of draft.mappings) {
            if (!String(mapping.emoji || '').trim() || !SNOWFLAKE.test(String(mapping.roleId || ''))) {
                throw new Error('Geçersiz emoji/rol eşleştirmesi.');
            }
        }
    }
}

async function main() {
    const input = process.argv[2];
    if (!input) {
        console.error('Kullanım: node scripts/importStudioConfig.js <studio-export.json>');
        process.exitCode = 2;
        return;
    }

    const configPath = path.resolve(input);
    if (!fs.existsSync(configPath)) throw new Error('Dosya bulunamadı: ' + configPath);
    const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
    validateConfig(config);

    if (!process.env.MONGO_URI) throw new Error('MONGO_URI ortam değişkeni tanımlı değil.');
    const connected = await connectDatabase();
    if (!connected || !isDatabaseReady()) throw new Error('MongoDB bağlantısı kurulamadı.');

    const guildId = String(config.guildId);
    const autoRoleIds = [...new Set(config.autoRoleIds.map(String))];
    const welcomeChannelId = String(config.welcomeChannelId || '').trim() || null;

    await GuildConfig.findOneAndUpdate(
        { guildId },
        {
            $set: {
                autoRoleIds,
                autoRoleId: autoRoleIds[0] || null,
                welcomeEnabled: config.welcomeEnabled !== false,
                welcomeChannelId,
                welcomeMessage: String(config.welcomeMessage || 'Hoş geldin {user}!').slice(0, 2000),
                logChannelId: String(config.logChannelId || '').trim() || null,
                antiSpamEnabled: config.antiSpamEnabled === true,
                capsFilterEnabled: config.capsFilterEnabled !== false,
                profanityFilterEnabled: config.profanityFilterEnabled !== false,
                updatedBy: 'YTMT-STUDIO'
            }
        },
        { upsert: true, new: true, setDefaultsOnInsert: true }
    );

    if (welcomeChannelId) {
        await Hgbb.findOneAndUpdate(
            { guildId },
            { $set: { channelId: welcomeChannelId } },
            { upsert: true, new: true, setDefaultsOnInsert: true }
        );
    } else if (config.welcomeEnabled === false) {
        await Hgbb.deleteOne({ guildId });
    }

    await ReactionRoleConfig.deleteMany({ guildId });
    let imported = 0;
    for (const draft of config.reactionRoles) {
        const isNewMessage = draft.mode === 'new-message';
        await ReactionRoleConfig.create({
            guildId,
            channelId: String(draft.channelId),
            messageId: isNewMessage ? null : String(draft.messageId),
            mode: isNewMessage ? 'new-message' : 'existing-message',
            messageContent: String(draft.messageContent || '').slice(0, 2000),
            status: isNewMessage ? 'pending' : 'active',
            enabled: true,
            mappings: draft.mappings.map(item => ({
                emoji: String(item.emoji).trim().slice(0, 80),
                roleId: String(item.roleId)
            })),
            createdBy: 'YTMT-STUDIO'
        });
        imported++;
    }

    console.log('YTMT-STUDIO yapılandırması MongoDB üzerine aktarıldı.');
    console.log('Sunucu:', config.guildName || guildId);
    console.log('Oto rol:', autoRoleIds.length);
    console.log('Mesaj rol sistemi:', imported);
    console.log('Yeni mesaj taslakları bot tarafından otomatik gönderilecek.');
}

main()
    .catch(error => {
        console.error('[STUDIO IMPORT ERROR]', error.message);
        process.exitCode = 1;
    })
    .finally(async () => {
        await mongoose.disconnect().catch(() => {});
    });
