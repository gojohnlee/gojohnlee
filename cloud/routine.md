# 클라우드 재번역 한 덩어리 절차 (Routine 세션이 그대로 따른다)

덩어리를 다 끝날 때까지 쉬지 않고 이어서 한다(번역은 덩어리마다 새 서브에이전트라 이 세션 대화는 거의 안 커짐). 사용 한도·오류로 끊기면 매시 예약이 다시 깨운다(돌고 있는 동안 온 예약은 끝난 뒤 들어오고, 잠금 확인으로 바로 끝남). 사용자에게 묻지 않는다. 출력은 짧게(토큰 최소). 공개 저장소 gojohnlee/gojohnlee에는 게임 글을 올리지 않는다.
작업 폴더 R=/home/user/refs (valheim-refs의 necromancer 브랜치, 이미 clone됨).

1. 잠금 없음: 번역하는 세션은 이것 하나뿐이고 깨우는 예약은 돌던 일이 끝난 뒤에 들어온다. 이미 이 대화에서 덩어리를 이어 하는 중이면(앞 턴이 끊긴 것) 그냥 2부터 다시.
2. (반복 시작) `python3 cloud/chunk.py next`
   - `ALLDONE`이면: status.md 커밋·푸시, 이 세션을 깨우는 예약을 모두 끈다(list_triggers로 이 세션에 묶인 것을 찾아 update_trigger enabled=false). 끝.
3. 번역: Agent 도구로 model "sonnet" 또는 ChatGPT의 "Sol" 서브에이전트 하나에 맡긴다(아래 지시문 그대로). 직접 번역하지 않는다(비용).
   > 작업 폴더 /home/user/refs/cloud. translate-guide.md를 읽고 따른다. 이름표 names-ko.tsv. 입력 work/chunk.txt.
   > [완료]가 아닌 줄을 모두 한국어로 옮겨 work/out.tsv에 쓴다(UTF-8, 한 줄에 'ID<TAB>문장', 단락 수는 주어진 그대로 ' / '로 나눔).
   > 다른 일은 하지 않는다. 끝나면 "OK"만 답한다.
4. `python3 cloud/chunk.py apply` (한 줄 요약이 나옴).
5. `git add -A translation cloud && git commit -qm "재번역 <apply 요약의 d/a>" && git push -q origin necromancer`.
   push 거절이면 `git pull -q --rebase origin necromancer` 후 다시 push(최대 3번). 2로 돌아감.
7. 끝. 최종 답은 마지막 apply 요약 한 줄.

사용 한도·오류로 중간에 끊기면 그대로 둔다: 15분마다 오는 예약이 다시 깨운다. cloud/work에 반쯤 된 덩어리가 있으면 next가 새로 만든다.
