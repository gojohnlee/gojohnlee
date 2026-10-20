# gojohnlee 게임 모드

## RimWorld

| 모드 | 경로 | 대상 버전 |
|---|---|---|
| Hello RimWorld (템플릿) | `rimworld/HelloRimWorld` | 1.7 |

### 빌드
```bash
./scripts/setup-dotnet.sh   # .NET 10 SDK가 없을 때
dotnet build rimworld/HelloRimWorld/Source/HelloRimWorld/HelloRimWorld.csproj -c Release
```
결과 DLL은 `rimworld/HelloRimWorld/1.7/Assemblies/`에 생성됩니다.
push할 때마다 GitHub Actions가 빌드하고, 바로 넣을 수 있는 모드 폴더를 아티팩트로 올립니다.

### 설치 (PC)
1. Actions 탭 → 최신 "RimWorld mod build" 실행 → `HelloRimWorld` 아티팩트 다운로드
2. 압축을 풀어 `HelloRimWorld` 폴더를 `RimWorld/Mods/`에 넣기
3. 게임에서 **Harmony** 모드를 먼저 켜고, 그 아래에 Hello RimWorld 활성화
4. 게임을 시작하거나 불러오면 "Hello RimWorld 모드가 작동 중입니다!" 메시지가 뜨면 성공

### 새 모드 만들기
```bash
./scripts/new-rimworld-mod.sh MyMod "모드 설명"
```
`rimworld/MyMod/`가 만들어지고, CI가 자동으로 함께 빌드합니다.

PC에서 확인할 항목: [docs/HOME_CHECKLIST.md](docs/HOME_CHECKLIST.md)
