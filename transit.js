// 사용: node transit.js pts.json <목적지 lat,lng> <도착 시각 epoch(현지시각을 UTC로 본 값)> out.json
// 구글 지도 길찾기(대중교통, 도착 시각 기준)를 실제 브라우저로 열어 첫 추천 경로의 시간·탈것을 읽는다. 개인 규모: 매물마다 4초 간격.
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const pts = JSON.parse(fs.readFileSync(process.argv[2])); const dest = process.argv[3], ts = process.argv[4], outp = process.argv[5] || 'transit.json';
  const px = process.env.HTTPS_PROXY || process.env.https_proxy;
  const b = await chromium.launch(px ? { proxy: { server: px } } : {});
  const c = await b.newContext({ locale: 'ko-KR', ignoreHTTPSErrors: true, viewport: { width: 1200, height: 900 } });
  const p = await c.newPage(); const res = {};
  for (const [id, lat, lng] of pts) {
    try {
      await p.goto(`https://www.google.com/maps/dir/${lat},${lng}/${dest}/data=!4m6!4m5!2m3!6e1!7e2!8j${ts}!3e3?hl=ko`, { waitUntil: 'domcontentloaded', timeout: 60000 });
      await p.waitForSelector('[data-trip-index]', { timeout: 20000 });
      await p.waitForTimeout(1500);
      res[id] = await p.evaluate(() => { const e = document.querySelector('[data-trip-index]');
        return { t: e.innerText.replace(/\s*\n\s*/g, ' | ').slice(0, 200), modes: [...e.querySelectorAll('img[alt],span[aria-label]')].map(x => x.getAttribute('alt') || x.getAttribute('aria-label')) }; });
    } catch (err) { res[id] = { err: err.message.slice(0, 100) }; }
    
    await p.waitForTimeout(4000);
  }
  fs.writeFileSync(outp, JSON.stringify(res));
  await b.close();
})().catch(e => { console.log('ERR', e.message); process.exit(1); });
