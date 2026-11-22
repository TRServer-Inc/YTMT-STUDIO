const mongoose = require('mongoose');
const { Schema } = mongoose;

const hgbbSchema = new Schema({
    guildId: { type: String, required: true, unique: true, index: true },
    channelId: { type: String, required: true }
});

const linkEngelSchema = new Schema({
    guildId: { type: String, required: true, unique: true, index: true },
    durum: { type: Boolean, default: false }
});

const guildConfigSchema = new Schema({
    guildId: { type: String, required: true, unique: true, index: true },
    autoRoleId: { type: String, default: null }, // Legacy single-role setting.
    autoRoleIds: { type: [String], default: [] },
    logChannelId: { type: String, default: null },
    welcomeEnabled: { type: Boolean, default: true },
    welcomeChannelId: { type: String, default: null },
    welcomeMessage: { type: String, default: 'Hoş geldin {user}! Sunucumuza katıldın.', maxlength: 2000 },
    antiSpamEnabled: { type: Boolean, default: false },
    capsFilterEnabled: { type: Boolean, default: true },
    profanityFilterEnabled: { type: Boolean, default: true },
    levelingEnabled: { type: Boolean, default: true },
    xpCooldownSeconds: { type: Number, default: 60, min: 10, max: 3600 },
    updatedBy: { type: String, default: null }
}, { timestamps: true });

const Hgbb = mongoose.models.Hgbb || mongoose.model('Hgbb', hgbbSchema);
const LinkEngel = mongoose.models.LinkEngel || mongoose.model('LinkEngel', linkEngelSchema);
const GuildConfig = mongoose.models.GuildConfig || mongoose.model('GuildConfig', guildConfigSchema);

module.exports = { Hgbb, LinkEngel, GuildConfig };
