const mongoose = require('mongoose');
const { Schema } = mongoose;

const mappingSchema = new Schema({
    emoji: { type: String, required: true, trim: true, maxlength: 80 },
    roleId: { type: String, required: true, trim: true }
}, { _id: false });

const reactionRoleConfigSchema = new Schema({
    guildId: { type: String, required: true, index: true },
    channelId: { type: String, required: true },
    messageId: { type: String, default: null, index: true },
    mode: { type: String, enum: ['existing-message', 'new-message'], default: 'existing-message' },
    messageContent: { type: String, default: '', maxlength: 2000 },
    status: { type: String, enum: ['pending', 'processing', 'active', 'failed'], default: 'active' },
    enabled: { type: Boolean, default: true },
    mappings: { type: [mappingSchema], default: [] },
    createdBy: { type: String, default: null }
}, { timestamps: true });

reactionRoleConfigSchema.index({ guildId: 1, messageId: 1 });

const ReactionRoleConfig = mongoose.models.ReactionRoleConfig
    || mongoose.model('ReactionRoleConfig', reactionRoleConfigSchema);

module.exports = { ReactionRoleConfig };
