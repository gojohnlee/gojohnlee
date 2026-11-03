#!/bin/bash
# commit.sh "<message>"
cd /home/user/valheim-refs || exit 1
git add translation/retrans/review-*.tsv && git commit -q -m "$1

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_019gWVoM9PMm9tNPThfvLbf1" || exit 1
for d in 2 4 8 16; do
  git pull -q --rebase origin necromancer && git push -q origin HEAD:necromancer && { echo PUSHED $(git log --oneline -1); exit 0; }
  sleep $d
done; echo PUSHFAIL
