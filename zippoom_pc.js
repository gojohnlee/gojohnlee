// 회사 PC 1회용 집품 수집기(사용자 11-25). 집품.bat → zippoom_pc.ps1이 실행한다.
// 집품 사이트맵에서 안양·의왕·군포 건물 후기 쪽 중 아직 안 받은 것(zippoom_cache.json에 없는 것)을 10초 간격으로 열어 지번을 읽고,
// 대상 지번(매물 있는 건물)이면 '전체 보기'를 눌러 사이트가 받는 /api/review 응답(후기 전문)을 잡는다.
// 결과는 바탕화면\원룸\zippoom_result.json에 한 쪽마다 저장(중간에 꺼도 다시 실행하면 이어서). 토큰은 환경변수 T로만 받고 어디에도 쓰지 않는다.
const { chromium } = require('playwright-core');
const fs = require('fs'), path = require('path');
const OUT = process.env.OUT, T = process.env.T;
const TARGETS = ["안양시 동안구 호계동 970-6", "안양시 동안구 관양동 1447-8", "의왕시 포일동 675-2", "군포시 금정동 41-4", "군포시 금정동 42-6", "군포시 산본동 207-7", "군포시 산본동 49-9", "군포시 산본동 75-2", "안양시 동안구 관양동 1454-2", "안양시 동안구 관양동 1454-7", "안양시 동안구 관양동 1490-21", "안양시 동안구 관양동 1490-24", "안양시 동안구 관양동 1717-4", "안양시 동안구 관양동 1725-3", "안양시 동안구 호계동 1069-5", "안양시 동안구 호계동 1079", "안양시 동안구 호계동 1082-6", "안양시 동안구 호계동 1095-4", "안양시 동안구 호계동 1098-1", "안양시 동안구 호계동 1102-7", "안양시 동안구 호계동 1110-2", "안양시 동안구 호계동 904-10", "안양시 동안구 호계동 917-3", "안양시 동안구 호계동 924-10", "안양시 동안구 호계동 924-17", "안양시 동안구 호계동 924-24", "안양시 동안구 호계동 924-9", "안양시 동안구 호계동 925-48", "안양시 동안구 호계동 966-20", "안양시 동안구 호계동 968-1", "안양시 동안구 호계동 996-3", "안양시 만안구 안양동 195-25", "안양시 만안구 안양동 363-12", "안양시 만안구 안양동 387-9", "안양시 만안구 안양동 402-20", "안양시 만안구 안양동 406-34", "안양시 만안구 안양동 431-1", "안양시 만안구 안양동 453-5", "안양시 만안구 안양동 491-8", "안양시 만안구 안양동 492-13", "안양시 만안구 안양동 494-25", "안양시 만안구 안양동 500-4", "안양시 만안구 안양동 517-4", "안양시 만안구 안양동 523-18", "안양시 만안구 안양동 554-1", "안양시 만안구 안양동 557-21", "안양시 만안구 안양동 564-12", "안양시 만안구 안양동 622-130", "안양시 만안구 안양동 622-236", "안양시 만안구 안양동 627-153", "안양시 만안구 안양동 633-8", "의왕시 내손동 803", "의왕시 내손동 803-13", "의왕시 내손동 810-2", "의왕시 내손동 827-3", "의왕시 내손동 827-6", "의왕시 포일동 660-3", "의왕시 포일동 662-2", "의왕시 포일동 671-5"];
const FIRST = TARGETS.slice(0, 3);
const norm = a => (a || '').replace(/^경기(도)?\s*/, '').replace(/\s+/g, ' ').trim();
const dec = u => { try { return decodeURIComponent(u); } catch (e) { return u; } };
const sleep = ms => new Promise(r => setTimeout(r, ms));
const cached = new Set(Object.keys(JSON.parse(fs.readFileSync(path.join(__dirname, 'zippoom_cache.json'), 'utf8'))).map(dec));
const resF = path.join(OUT, 'zippoom_result.json');
const R = fs.existsSync(resF) ? JSON.parse(fs.readFileSync(resF, 'utf8')) : { started: new Date().toISOString(), pages: {} };
const save = () => fs.writeFileSync(resF, JSON.stringify(R));
const dongs = [...new Set(TARGETS.map(a => a.split(' ').slice(-2, -1)[0]))];

(async () => {
  let b;
  for (const ch of ['msedge', 'chrome', undefined]) { try { b = await chromium.launch({ channel: ch }); break; } catch (e) {} }
  if (!b) { console.log('Edge·Chrome을 찾지 못했습니다.'); process.exit(1); }
  const c = await b.newContext({ userAgent: 'Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/131 Mobile Safari/537.36', viewport: { width: 400, height: 900 } });
  await c.addCookies([{ name: 'ZIPPOOM_AUTH_TOKEN', value: T, domain: 'zippoom.com', path: '/' }]);
  await c.addInitScript(t => { try { localStorage.setItem('AUTH_TOKEN', JSON.stringify({ sessionToken: t, refreshToken: '' })); } catch (e) {} }, T);

  // 사이트맵(색인이면 하위 사이트맵까지)
  let locs = [];
  const getLocs = async u => { const r = await c.request.get(u); if (!r.ok()) throw new Error('사이트맵 ' + r.status()); return [...(await r.text()).matchAll(/<loc>([^<]+)<\/loc>/g)].map(m => m[1]); };
  const top = await getLocs(process.env.SM || 'https://zippoom.com/sitemap/%EB%A6%AC%EB%B7%B0/index.xml');
  for (const u of top) {
    if (/\.xml$/.test(u)) { await sleep(3000); try { locs.push(...await getLocs(u)); } catch (e) { console.log('하위 사이트맵 실패', e.message); } }
    else locs.push(u);
  }
  const seg = u => dec(u).split('/').slice(-2, -1)[0] || '';
  let todo = locs.filter(u => /안양|의왕|군포/.test(seg(u)) && !cached.has(dec(u)) && (!R.pages[u] || R.pages[u].err || R.pages[u].status >= 400))
                 .filter(u => dongs.some(d => seg(u).split('-').includes(d)));
  const pri = u => /-(호계동|관양동|포일동)-/.test(seg(u)) ? 0 : 1;
  todo.sort((x, y) => pri(x) - pri(y));
  R.sitemapRegion = locs.filter(u => /안양|의왕|군포/.test(seg(u))).length;
  console.log(`사이트맵 지역 쪽 ${R.sitemapRegion}곳, 이번에 열 쪽 ${todo.length}곳 (약 ${Math.ceil(todo.length * 15 / 60)}분)`);

  const p = await c.newPage();

  // 11-25: 사이트맵에 빠진 건물이 있음(포일동 671-5) → 동별 순위 쪽(/지역별-부동산/…/순위, 검색엔진에 올라온 공개 쪽)에서 후기 쪽 주소를 더 모은다. robots.txt가 막으면 건너뜀.
  let robotsOK = true;
  try {
    const rt = await (await c.request.get((process.env.BASE || 'https://zippoom.com') + '/robots.txt')).text();
    R.robotsTxt = rt.slice(0, 4000);
    // 무리(User-agent 줄 묶음)별로 읽고, 사용자 요청으로 움직이는 'Claude-User' 무리 규칙을 따른다(없으면 '*'). 집품은 Claude-User에 /지역별-부동산·/리뷰를 허용.
    const groups = []; let cur = null, lastUA = false;
    for (const line of rt.split(/\r?\n/)) {
      const m = line.match(/^\s*(User-agent|Allow|Disallow)\s*:\s*([^#]*)/i); if (!m) continue;
      const k = m[1].toLowerCase(), v = m[2].trim();
      if (k === 'user-agent') { if (!lastUA) { cur = { ua: [], rules: [] }; groups.push(cur); } cur.ua.push(v.toLowerCase()); lastUA = true; }
      else { lastUA = false; if (cur && v) cur.rules.push({ allow: k === 'allow', path: dec(v).replace(/\*/g, '').replace(/\$$/, '') }); }
    }
    const g = groups.find(x => x.ua.includes('claude-user')) || groups.find(x => x.ua.includes('*')) || { rules: [] };
    const rules = g.rules; R.robotsGroup = g.ua;
    // 가장 긴 규칙이 이김(같으면 Allow) — 구글 robots 해석과 같음
    const okPath = pth => { let best = null; for (const r of rules) if (pth.startsWith(r.path) && (!best || r.path.length > best.path.length || (r.path.length === best.path.length && r.allow))) best = r; return !best || best.allow; };
    robotsOK = okPath('/지역별-부동산/경기-안양시-동안구-관양동/빌라/순위');
    R.robotsRankAllowed = robotsOK;
  } catch (e) {}
  R.rank = R.rank || {};
  const extra = [];
  if (robotsOK) {
    const dongPaths = [...new Set(TARGETS.map(a => { const w = a.split(' '); w.pop(); return '경기-' + w.join('-'); }))];
    dongPaths.sort((x, y) => (/호계동|관양동|포일동/.test(x) ? 0 : 1) - (/호계동|관양동|포일동/.test(y) ? 0 : 1));
    for (const dp of dongPaths) for (const ty of ['빌라', '오피스텔']) {
      const ru = `${process.env.BASE || 'https://zippoom.com'}/지역별-부동산/${dp}/${ty}/순위`;
      const info = {};
      try {
        const resp = await p.goto(ru, { waitUntil: 'domcontentloaded', timeout: 60000 });
        await p.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
        info.status = resp ? resp.status() : 0;
        for (let k = 0; k < 15; k++) {
          await p.mouse.wheel(0, 4000); await p.waitForTimeout(700);
          const more = p.locator('text=/더\\s?보기/').first();
          if (await more.count()) { try { await more.click({ timeout: 3000 }); await p.waitForTimeout(1500); } catch (e) {} }
        }
        const html = await p.content();
        const hrefs = await p.$$eval('a[href]', as => as.map(a => a.href));
        const found = new Set();
        for (const h of hrefs) if (dec(h).includes('/리뷰/')) found.add(h.split('?')[0]);
        for (const m of dec(html).matchAll(/\/리뷰\/[^"'\s<>\\?#]+\/[A-Za-z0-9_-]+/g)) found.add((process.env.BASE || 'https://zippoom.com') + m[0]);
        info.links = found.size;
        for (const h of found) if (/안양|의왕|군포/.test(seg(h)) && !cached.has(dec(h)) && !(R.pages[h] && !R.pages[h].err)) extra.push(h);
      } catch (e) { info.err = String(e).replace(/\x1b\[[0-9;]*m/g, '').split('\n')[0].slice(0, 120); }
      R.rank[ru] = info; save();
      console.log(`순위 쪽 ${dp.split('-').pop()} ${ty}: ${info.err || ('후기 쪽 ' + (info.links || 0) + '곳')}`);
      await sleep(+process.env.GAP || 10000);
    }
  } else console.log('robots.txt가 순위 쪽을 막아 건너뜀');
  const seen = new Set(todo.map(dec));
  const add = [...new Set(extra.map(dec))].filter(h => !seen.has(h)).map(h => encodeURI(h));
  todo = add.concat(todo);
  console.log(`순위 쪽에서 새 후기 쪽 ${add.length}곳 추가 → 열 쪽 ${todo.length}곳 (약 ${Math.ceil(todo.length * 15 / 60)}분)`);
  let blocked = 0, i = 0;
  for (const u of todo) {
    i++;
    let got = null;
    const h = async r => { if (r.url().includes('/api/review?')) { try { got = await r.json(); } catch (e) {} } };
    p.on('response', h);
    const rec = { t: new Date().toISOString() };
    try {
      const resp = await p.goto(u, { waitUntil: 'domcontentloaded', timeout: 60000 });
      await p.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
      rec.status = resp ? resp.status() : 0;
      if (rec.status >= 400) { blocked++; }
      else {
        blocked = 0;
        const nd = await p.evaluate(() => { const e = document.getElementById('__NEXT_DATA__'); return e ? e.textContent : null; });
        const pp = nd ? (JSON.parse(nd).props || {}).pageProps || {} : {};
        const d = pp.dashboard || {};
        rec.addr = d.oldAddress || null; rec.total = d.totalReviewCount;
        rec.reviews = (pp.loadedReview || []).map(r => { const o = {}; for (const k of ['floor', 'prosComment', 'consComment', 'prosKeyword', 'consKeyword', 'createdTs', 'isLivingVerified']) o[k] = r[k]; return o; });
        if (TARGETS.includes(norm(rec.addr))) {
          rec.target = true;
          const el = p.locator('text=/전체 보기\\(총/').first();
          if (await el.count()) { await el.click(); for (let k = 0; k < 30 && !got; k++) await p.waitForTimeout(300); }
          rec.full = got;
        }
      }
    } catch (e) { rec.err = String(e).replace(/\x1b\[[0-9;]*m/g, '').split('\n')[0].slice(0, 120); }
    p.off('response', h);
    R.pages[u] = rec; save();
    console.log(`[${i}/${todo.length}] ${seg(u).slice(0, 30)} → ${rec.addr || rec.err || rec.status}${rec.target ? '  ★대상' + (rec.full ? ' 전문 받음' : ' 전문 못 받음') : ''}`);
    if (blocked >= 3) { console.log('집품이 연속 3번 막음(403 등) → 멈춥니다. 받은 만큼은 저장됐습니다.'); R.stopped = 'blocked'; break; }
    await sleep(+process.env.GAP || 10000);
  }
  R.finished = new Date().toISOString(); save();
  const found = new Set(Object.values(R.pages).filter(r => r.target).map(r => norm(r.addr)));
  console.log(`\n끝. 대상 ${TARGETS.length}곳 중 집품 쪽 찾음 ${found.size}곳. 먼저 볼 세 곳: ` + FIRST.map(a => a.split(' ').slice(-2).join(' ') + (found.has(a) ? ' 찾음' : ' 없음')).join(', '));
  await b.close();
})().catch(e => { console.log('오류:', String(e).slice(0, 200)); process.exit(1); });
