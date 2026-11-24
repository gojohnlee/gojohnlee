// 사용: node daangn.js addrs.txt out.json  (한 줄에 '경기도 ...동 지번' 하나) → 당근부동산 '살아본 후기' 주소 조회
const {chromium}=require('playwright');const fs=require('fs');(async()=>{const b=await chromium.launch();const out={};
for(const a of fs.readFileSync(process.argv[2],'utf8').trim().split('\n').filter(Boolean)){const p=await b.newPage();out[a]=null;
p.on('response',async r=>{if(r.url().includes('/graphql')){try{const j=await r.json();if(j.data&&j.data.livedReviewsByAddress)out[a]=(j.data.livedReviewsByAddress.edges||[]).map(e=>e.node.content||'')}catch(e){}}});
try{await p.goto('https://realty.daangn.com/?address_lived_review='+encodeURIComponent(a),{timeout:40000});await p.waitForTimeout(7000)}catch(e){}await p.close()}
fs.writeFileSync(process.argv[3],JSON.stringify(out,null,1));await b.close()})();
