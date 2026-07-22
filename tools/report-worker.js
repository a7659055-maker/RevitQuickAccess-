/**
 * Cloudflare Worker — приёмник отчётов об ошибках Revit Quick Access.
 *
 * Зачем он нужен: плагин с открытым исходным кодом не должен содержать никаких секретов.
 * Поэтому плагин знает только публичный URL этого воркера, а сам секрет (токен Telegram,
 * вебхук Discord и т.п.) хранится в переменных окружения воркера и наружу не попадает.
 *
 * --- Развёртывание (5 минут, бесплатно) ---
 * 1. dash.cloudflare.com → Workers & Pages → Create → Worker → вставить этот файл → Deploy
 * 2. Settings → Variables → добавить секреты (см. ниже) → Save and deploy
 * 3. Скопировать URL воркера (вида https://rqa-report.<логин>.workers.dev)
 * 4. Указать его в RevitQuickAccess_settings.txt:   reportEndpoint=https://...
 *    (или в UpdateDefaults.ReportEndpoint при сборке)
 *
 * --- Переменные (задать нужные, лишние можно не создавать) ---
 *   TELEGRAM_TOKEN   — токен бота от @BotFather
 *   TELEGRAM_CHAT_ID — твой chat id (узнать: напиши боту, затем открой
 *                      https://api.telegram.org/bot<TOKEN>/getUpdates )
 *   DISCORD_WEBHOOK  — URL вебхука канала (альтернатива Telegram)
 */

export default {
  async fetch(request, env) {
    if (request.method === 'OPTIONS') return cors(new Response(null, { status: 204 }));
    if (request.method !== 'POST') return cors(new Response('POST only', { status: 405 }));

    let data;
    try {
      data = await request.json();
    } catch {
      return cors(new Response('bad json', { status: 400 }));
    }

    // простая защита от мусора
    const report = String(data.report || '').slice(0, 15000);
    if (!report) return cors(new Response('empty report', { status: 400 }));

    const kind = data.kind === 'crash' ? '💥 КРАШ' : '🐞 Баг';
    const head = `${kind} · Revit Quick Access ${data.version || '?'}\n${data.revit || ''}\n${data.os || ''}`;
    const text = `${head}\n\n${report}`;

    const sent = [];

    if (env.TELEGRAM_TOKEN && env.TELEGRAM_CHAT_ID) {
      // Telegram ограничивает сообщение 4096 символами — режем на части
      for (const chunk of split(text, 3900)) {
        await fetch(`https://api.telegram.org/bot${env.TELEGRAM_TOKEN}/sendMessage`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ chat_id: env.TELEGRAM_CHAT_ID, text: chunk, disable_web_page_preview: true }),
        });
      }
      sent.push('telegram');
    }

    if (env.DISCORD_WEBHOOK) {
      for (const chunk of split(text, 1900)) {
        await fetch(env.DISCORD_WEBHOOK, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ content: '```\n' + chunk + '\n```' }),
        });
      }
      sent.push('discord');
    }

    if (sent.length === 0) return cors(new Response('no destination configured', { status: 500 }));
    return cors(new Response(JSON.stringify({ ok: true, sent }), {
      headers: { 'content-type': 'application/json' },
    }));
  },
};

function split(s, size) {
  const out = [];
  for (let i = 0; i < s.length; i += size) out.push(s.slice(i, i + size));
  return out;
}

function cors(resp) {
  resp.headers.set('Access-Control-Allow-Origin', '*');
  resp.headers.set('Access-Control-Allow-Headers', 'content-type');
  return resp;
}
