# YTMT BOT API entegrasyonu

Bu klasördeki `ytmt-stats-metrics.cjs`, YTMT BOT'un mevcut Express `/api/stats` yanıtına güvenli performans alanları eklemek için hazırlanmıştır.

1. Dosyayı YTMT BOT deposunda `integrations/ytmt-stats-metrics.cjs` yoluna kopyala.
2. `server.js` içinde, Express `app` ve Discord.js `client` tanımlandıktan sonra içe aktar:

```js
const { buildStatsPayload } = require('./integrations/ytmt-stats-metrics.cjs');
```

3. Mevcut `/api/stats` endpoint'ini ikinci kez tanımlama. Var olan handler'da JSON döndürdüğün satırı aşağıdaki şekilde güncelle:

```js
res.json({
  ...existingStats,
  ...buildStatsPayload(client)
});
```

`existingStats` mevcut endpoint'inin hazırladığı nesnenin adı değilse, kendi değişken adını kullan. Var olan yanıtın yoksa doğrudan `res.json(buildStatsPayload(client))` kullanabilirsin.

Bu dosya sadece Node.js bellek/CPU bilgisi, container bellek değerleri, Discord durumu/ping ve runtime metaverisi verir. Token, environment variable veya veritabanı bilgisi döndürmez.

Not: cgroup RAM limitinin Render ortamında `max` veya erişilemez olması durumunda container yüzdesi boş olabilir. Node RSS ve V8 heap ölçümleri yine ayrı ayrı döner.