// 사용: T=<토큰> node full.js <건물 후기 쪽 주소...>  → 각 쪽에서 '전체 보기'를 눌러 사이트가 받는 /api/review 응답(후기 전문)을 stdout에 JSON 한 줄씩
const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch(); const c = await b.newContext({ userAgent: 'Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/131 Mobile Safari/537.36', viewport:{width:400,height:900} });
  await c.addCookies([{ name: 'ZIPPOOM_AUTH_TOKEN', value: process.env.T, domain: 'zippoom.com', path: '/' }]);
  await c.addInitScript(t => { try { localStorage.setItem('AUTH_TOKEN', JSON.stringify({sessionToken: t, refreshToken: ''})); } catch(e){} }, process.env.T);
  const p = await c.newPage();
  for (const u of process.argv.slice(2)) {
    let got = null;
    const h = async r => { if (r.url().includes('/api/review?')) { try { got = await r.json(); } catch(e){} } };
    p.on('response', h);
    try {
      await p.goto(u, { waitUntil: 'networkidle', timeout: 45000 });
      const el = p.locator('text=/전체 보기\\(총/').first();
      if (await el.count()) { await el.click(); for (let i=0;i<20 && !got;i++) await p.waitForTimeout(300); }
    } catch(e) { console.error('ERR', u.slice(-20), String(e).slice(0,80)); }
    p.off('response', h);
    console.log(JSON.stringify({ url: u, data: got }));
    await p.waitForTimeout(2000);
  }
  await b.close();
})();
