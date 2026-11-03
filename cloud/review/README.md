# 인물별 말투·말맛 검수 — 끝남 (2026-11-02)

- 작업서: cloud/review-plan.md. 결과: translation/retrans/review-*.tsv 144개, 모두 1975줄(같은 ID가 여러 파일에 있으면 이름순 뒤 파일이 이김).
- 끝난 묶음: 인물 묶음 전부 + 서술 narration-00-01 ~ narration-26-26 전부.
- 적용 순서(이름순): review-<인물>·review-narration-* → **review-zz-uncle.tsv**(숙부 높임 정리) → **review-zzz-final.tsv**(표기 통일·말줄임표, 맨 마지막).
- 사용자 결정(11-02): 진실이 밝혀진 장면(순서 9,824줄) 뒤로는 서술·주인공 대사 모두 제르베스를 높이지 않고 은은한 분노. 뉘우치는 갈래(24250~24252)는 다시 높임. 호칭은 누가 말하든 "삼촌"(final.py가 바꿈).
  단 뉘우치는 갈래(24250)는 존댓말. 용어: 검은 베일(the Shroud)·장막(the veil)·재상·황금회·베르니스·즈가리야서·벨라도나·세네갈리아 세네갈(translate-guide.md 끝 표).
- 남은 일: PC에서 scripts\cloud-apply.bat로 게임에 넣기(사용자). 보류: 주인공→에릭 반말 10여 줄, 주인공→바르바라 서술 쪽 반말 4줄(5253·5254·5309·5310).

## 다시 돌릴 때
- 이 폴더 파일을 스크래치 S로 복사(rv/에 brief*.md jobs.json assign.json out-*.tsv pick-*.tsv), `cd S && python3 -I jobs.py`.
- 묶음: `python3 -I apply.py <묶음> [write]`. 숙부 정리: uncle.py로 rv/in-uncle-*.txt 생성 → `uncle_apply.py <n> [write]`.
- 마무리: **review-zzz-final.tsv를 지운 뒤** `python3 -I final.py`(지우지 않으면 자기 결과를 읽어 0줄로 덮어씀). 다른 검수를 고친 뒤엔 꼭 다시 돌린다.
- 주의: Sonnet 검수자가 고친 문장을 옆 줄 ID에 붙이는 실수가 묶음 5개에서 있었음 → apply의 BAD(para)·CHECK 줄을 반드시 영어와 맞대어 본다.
