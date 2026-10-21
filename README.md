# Valheim Helper (발헤임 일꾼 NPC) — `valheim` 브랜치

이 브랜치는 발헤임 모드 전용입니다. 림월드 모드(철기병 등)는 `rimworld` 브랜치에 있습니다.

- 모드 코드: `valheim/ValheimHelper` (Core = 게임 무관 로직·테스트, Plugin = BepInEx 플러그인)
- 빌드: `./scripts/valheim-build.sh` (게임 DLL 필요), Core 테스트: `dotnet test valheim/ValheimHelper/ValheimHelper.slnx`
- 게임 시험(Windows PC): `scripts/valheim-test.ps1`
- 진행 상황: `docs/valheim/progress.txt`, 무인 루프 상태: `docs/valheim/status.txt`
