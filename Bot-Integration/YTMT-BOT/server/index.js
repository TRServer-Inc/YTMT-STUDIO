const express = require('express');
const logger = require('../utils/logger.js');
const registerLiveEvents = require('./liveEvents.js');
const registerMiddleware = require('./middleware.js');
const { registerStatsRoutes } = require('./metrics.js');
const registerTerminalRoute = require('./routes/terminal.js');
const registerStudioRoutes = require('./routes/studio.js');

const app = express();
let discordClient = null;

const liveEvents = registerLiveEvents(app);
registerMiddleware(app, liveEvents.publishLiveEvent);
app.setDiscordClient = client => {
    discordClient = client;
};

registerStatsRoutes(
    app,
    () => discordClient,
    liveEvents.getTotalDiscordCommands
);
registerTerminalRoute(app);
registerStudioRoutes(app, () => discordClient);

app.use((error, req, res, next) => {
    logger.write('errors', 'ERROR',
        `Express request failed: ${req.method} ${req.path}`, error);
    if (res.headersSent) return next(error);
    res.status(500).json({
        error: 'Sunucu isteği işlerken beklenmeyen bir hata oluştu.'
    });
});

const PORT = process.env.PORT || 10000;
const server = app.listen(PORT, () => {
    logger.write('render', 'SUCCESS', `Web service listening on port ${PORT}.`);
    console.log(`[SUNUCU] Web servisi ${PORT} portunda başarıyla başlatıldı.`);
});

server.on('error', error => {
    logger.write('errors', 'ERROR', 'Web server error.', error);
    console.error('[SUNUCU HATA]', error);
});

module.exports = app;
