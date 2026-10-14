import fs from 'node:fs';
import { createApp } from './app.ts';
import { loadConfig } from './config.ts';
import { Store } from './db.ts';
import { loadGameData } from './game/data.ts';
import { pollGoogleRefunds } from './liveops.ts';
import { googlePlayConfigured } from './payments/google.ts';
import { steamPaymentsConfigured } from './payments/steam.ts';
import { createServers } from './server.ts';

const config = loadConfig();
const data = loadGameData(config.gameDataDir);
const store = new Store(config.databasePath);
const servers = createServers(config, createApp({ config, store, data }));

servers.api.listen(config.port, () => {
  const scheme = config.tlsCertFile ? 'https' : 'http';
  console.log(`[vms] ${scheme} listening on :${config.port} (public ${config.publicUrl}); ${data.navigators.length} navigators, ${data.banners.length} banners`);
  console.log(`[vms] Google Play Games ${config.googleClientId ? 'enabled' : 'not configured'}; Steam tickets ${config.steamPublisherKey ? 'enabled' : 'not configured'}`);
  console.log(`[vms] minimum client version ${config.minClientVersion || 'not enforced'}; HTTPS ${config.requireHttps ? 'required' : 'optional'}`);
  console.log(`[vms] purchases: Google Play ${googlePlayConfigured(config) ? 'on' : 'off'}, Steam ${steamPaymentsConfigured(config) ? 'on' : 'off'}${config.paymentsFake ? ', FAKE STORE ON' : ''}; email ${config.email.provider || 'off'}; admin API ${config.adminToken ? 'on' : 'off'}`);
});
servers.redirect?.listen(config.httpRedirectPort, () => console.log(`[vms] http :${config.httpRedirectPort} redirects to ${config.publicUrl}`));

// Refunds and chargebacks on Google Play: take the gold back and tell the player by mail.
if (googlePlayConfigured(config) && config.googleVoidedPollMinutes > 0) {
  const poll = () =>
    pollGoogleRefunds(config, store, data, fetch)
      .then((n) => n > 0 && console.log(`[payments] revoked ${n} refunded Google Play purchase(s)`))
      .catch((e) => console.error('[payments] refund check failed:', e));
  setTimeout(poll, 30_000).unref();
  setInterval(poll, config.googleVoidedPollMinutes * 60_000).unref();
}

if (config.tlsCertFile) {
  // Certificate renewals (certbot, acme.sh) replace the files; pick them up without a restart.
  fs.watchFile(config.tlsCertFile, { interval: 60_000 }, servers.reloadTls);
  process.on('SIGHUP', servers.reloadTls);
}

for (const signal of ['SIGINT', 'SIGTERM'] as const) {
  process.on(signal, () => {
    if (config.tlsCertFile) fs.unwatchFile(config.tlsCertFile);
    void servers.close().then(() => {
      store.db.close();
      process.exit(0);
    });
  });
}
