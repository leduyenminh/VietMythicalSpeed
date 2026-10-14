import type { Config } from '../config.ts';
import type { FetchFn } from './providers.ts';

/** Sends plain-text email (password reset codes). */
export interface Mailer {
  send(to: string, subject: string, text: string): Promise<void>;
}

/**
 * EMAIL_PROVIDER: resend | postmark | sendgrid (HTTP APIs, key in EMAIL_API_KEY, sender in EMAIL_FROM),
 * or console (development: prints the mail to the server log). Returns null when email isn't configured.
 */
export function createMailer(config: Config, fetchImpl: FetchFn, log: (line: string) => void = console.log): Mailer | null {
  const { provider, from, apiKey } = config.email;
  const post = async (url: string, headers: Record<string, string>, body: unknown) => {
    const res = await fetchImpl(url, { method: 'POST', headers: { 'content-type': 'application/json', ...headers }, body: JSON.stringify(body) });
    if (!res.ok) throw new Error(`${provider} returned ${res.status}: ${(await res.text()).slice(0, 200)}`);
  };
  switch (provider) {
    case 'console':
      return { send: async (to, subject, text) => log(`[mail] to ${to} | ${subject}\n${text}`) };
    case 'resend':
      return { send: (to, subject, text) => post('https://api.resend.com/emails', { authorization: `Bearer ${apiKey}` }, { from, to: [to], subject, text }) };
    case 'postmark':
      return {
        send: (to, subject, text) =>
          post('https://api.postmarkapp.com/email', { 'x-postmark-server-token': apiKey, accept: 'application/json' }, { From: from, To: to, Subject: subject, TextBody: text, MessageStream: 'outbound' }),
      };
    case 'sendgrid':
      return {
        send: (to, subject, text) =>
          post('https://api.sendgrid.com/v3/mail/send', { authorization: `Bearer ${apiKey}` }, {
            personalizations: [{ to: [{ email: to }] }],
            from: { email: from },
            subject,
            content: [{ type: 'text/plain', value: text }],
          }),
      };
    default:
      return null;
  }
}
