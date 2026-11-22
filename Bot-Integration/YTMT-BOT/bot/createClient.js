const {
    Client,
    GatewayIntentBits,
    Partials,
    Collection
} = require('discord.js');

function normalizeCommandKey(value) {
    return String(value || '')
        .trim()
        .replace(/^y!/i, '')
        .toLocaleLowerCase('tr-TR');
}

function createClient() {
    const client = new Client({
        intents: [
            GatewayIntentBits.Guilds,
            GatewayIntentBits.GuildMessages,
            GatewayIntentBits.GuildMembers,
            GatewayIntentBits.MessageContent,
            GatewayIntentBits.DirectMessages,
            GatewayIntentBits.GuildPresences,
            GatewayIntentBits.GuildMessageReactions
        ],
        partials: [Partials.Channel, Partials.Message, Partials.Reaction]
    });

    client.commands = new Collection();
    client.commandAliases = new Map();
    client.resolveCommand = name => {
        const key = normalizeCommandKey(name);
        const canonicalName = client.commandAliases.get(key) || key;
        return client.commands.get(canonicalName) || null;
    };

    return { client, normalizeCommandKey };
}

module.exports = { createClient, normalizeCommandKey };
